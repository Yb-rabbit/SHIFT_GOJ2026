using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 游戏内编辑出来的自定义关卡存档（JSON）。
    /// 刻意只写 Application.persistentDataPath：Package/Assets 里的 LevelSO 是美术与关卡的唯一真源，
    /// 运行时绝不能改动它们，否则编辑器里会莫名其妙多出 diffs。
    /// </summary>
    public static class LevelDraftSerializer
    {
        public const string DefaultFileName = "custom_level.json";
        const string FolderName = "ShiftGoLevels";

        /// <summary>默认存档路径（Win/Mac/Linux 在 persistentDataPath/ShiftGoLevels 下）。</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FolderName, DefaultFileName);

        /// <summary>把草稿写成 JSON；失败（IO 异常）返回 false，不抛出去打断游戏。</summary>
        public static bool Save(LevelDraft draft, string path = null)
        {
            if (draft == null) return false;

            try
            {
                string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
                string folder = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                File.WriteAllText(target, JsonUtility.ToJson(ToDto(draft), true));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 自定义关卡保存失败: " + exception.Message);
                return false;
            }
        }

        /// <summary>读回草稿；文件不存在或内容坏掉都返回 null（调用方自己提示）。</summary>
        public static LevelDraft Load(string path = null)
        {
            try
            {
                string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
                if (!File.Exists(target)) return null;

                DraftDto dto = JsonUtility.FromJson<DraftDto>(File.ReadAllText(target));
                return FromDto(dto);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 自定义关卡读取失败: " + exception.Message);
                return null;
            }
        }

        public static bool Exists(string path = null)
        {
            return File.Exists(string.IsNullOrEmpty(path) ? DefaultPath : path);
        }

        static DraftDto ToDto(LevelDraft draft)
        {
            DraftDto dto = new DraftDto
            {
                displayName = draft.DisplayName,
                width = draft.Width,
                height = draft.Height,
                tickInterval = draft.TickInterval,
                stockEnabled = draft.StockEnabled,
            };

            foreach (EditableElement element in draft.Cells)
            {
                dto.cells.Add(new CellDto
                {
                    kind = (int)element.Kind,
                    x = element.Coord.X,
                    y = element.Coord.Y,
                    primaryDir = (int)element.PrimaryDir,
                    secondaryDir = (int)element.SecondaryDir,
                    sequence = element.Sequence,
                    count = element.Count,
                    passValue = element.PassValue,
                    typeKey = element.TypeKey,
                    locked = draft.IsLocked(element.Coord),
                });
            }

            // 手牌也要进存档：否则读回来的有限元件关会退化成"无限涂刷"
            if (draft.StockEnabled)
            {
                foreach (KeyValuePair<string, int> pair in draft.Stock)
                {
                    dto.stock.Add(new StockDto { typeKey = pair.Key, count = pair.Value });
                }
            }
            return dto;
        }

        static LevelDraft FromDto(DraftDto dto)
        {
            if (dto == null) return null;

            LevelDraft draft = new LevelDraft(dto.width, dto.height, dto.tickInterval)
            {
                DisplayName = string.IsNullOrEmpty(dto.displayName) ? "CUSTOM LEVEL" : dto.displayName,
            };

            // 先登记手牌：SetStock 会把草稿切到"有限手牌"模式，之后放置才按牌数校验
            if (dto.stockEnabled && dto.stock != null)
            {
                foreach (StockDto stack in dto.stock)
                {
                    if (stack == null || string.IsNullOrEmpty(stack.typeKey)) continue;
                    draft.SetStock(stack.typeKey, stack.count);
                }
            }

            if (dto.cells == null) return draft;

            foreach (CellDto cell in dto.cells)
            {
                if (cell == null) continue;

                // 存档可能是旧版本或手改过的，越界/非法枚举一律丢弃，避免把整关读崩
                if (!Enum.IsDefined(typeof(CellKind), cell.kind)) continue;

                EditableElement element = EditableElement.CreateDefault((CellKind)cell.kind, new GridPoint(cell.x, cell.y));
                element.PrimaryDir = ToDir(cell.primaryDir, element.PrimaryDir);
                element.SecondaryDir = ToDir(cell.secondaryDir, element.SecondaryDir);
                element.Sequence = string.IsNullOrEmpty(cell.sequence) ? element.Sequence : cell.sequence;
                element.Count = cell.count < 0 ? 0 : cell.count;
                element.PassValue = cell.passValue == 0 ? (byte)0 : (byte)1;
                // 存档里带回来类型 Key，玩家局内拾回时才能把牌还回正确的那一摞
                element.TypeKey = string.IsNullOrEmpty(cell.typeKey) ? string.Empty : cell.typeKey;

                draft.Place(element);
                if (cell.locked) draft.SetLocked(element.Coord, true);
            }

            // 读档不是玩家的一步：撤销栈从空开始，第一次 Ctrl+Z 不会把存档内容撤掉
            draft.ClearHistory();
            return draft;
        }

        static Dir ToDir(int raw, Dir fallback)
        {
            if (!Enum.IsDefined(typeof(Dir), raw)) return fallback;
            return (Dir)raw;
        }

        [Serializable]
        public sealed class DraftDto
        {
            public string displayName;
            public int width;
            public int height;
            public float tickInterval;
            public bool stockEnabled;
            public List<CellDto> cells = new List<CellDto>();
            public List<StockDto> stock = new List<StockDto>();
        }

        /// <summary>一摞手牌：类型 Key + 剩余张数（旧存档没有这一段 → stockEnabled 为 false，按作者模式处理）。</summary>
        [Serializable]
        public sealed class StockDto
        {
            public string typeKey;
            public int count;
        }

        [Serializable]
        public sealed class CellDto
        {
            public int kind;
            public int x;
            public int y;
            public int primaryDir;
            public int secondaryDir;
            public string sequence;
            public int count;
            public int passValue;

            /// <summary>来源类型资产的 Key（旧存档没有这个字段 → 空字符串，拾回时按「无归属」处理）。</summary>
            public string typeKey;

            /// <summary>是否是关卡预设件（玩家搬不动的那些；旧存档没有 → false，读回来当自由件）。</summary>
            public bool locked;
        }
    }
}
