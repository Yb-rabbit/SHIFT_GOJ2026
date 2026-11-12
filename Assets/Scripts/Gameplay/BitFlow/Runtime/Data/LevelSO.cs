using System;
using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 关卡资产：网格尺寸 + 元件摆放表。运行时只读，转换成纯逻辑的 <see cref="LevelDefinition"/>
    /// 交给 BitFlowSimulation，任何运行时进度都不会写回这个资产（避免 Editor 里资产被改脏）。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Level", fileName = "Level_01")]
    public sealed class LevelSO : ScriptableObject
    {
        [Header("关卡信息")]
        [Tooltip("关卡名。会显示在黑幕标题、HUD 左上角和选关面板里，也是通关记录的 key（改名 = 旧记录失效）")]
        public string displayName = "NEW LEVEL";

        [Tooltip("网格列数（x 方向）")]
        [Min(1)] public int width = 9;

        [Tooltip("网格行数（y 方向）")]
        [Min(1)] public int height = 8;

        [Header("节奏")]
        [Tooltip("1 倍速时一个 tick 的时长（秒）")]
        [Min(0.05f)] public float tickInterval = 0.5f;

        [Header("元件摆放")]
        public List<ElementPlacement> placements = new List<ElementPlacement>();

        [Header("墙层")]
        [Tooltip("画墙用的类型资产。留空 = wallMap 不生效（老关卡一个字节都不变）")]
        public WallTypeSO wallType;

        [Tooltip("用 # 画墙、. 或空格留空。每行对应一个 y（从下往上，第一行是 y=0），行内第一个字符是 x=0。\n" +
                 "例：\n" +
                 "#.......#\n" +
                 ".........\n" +
                 "....###..")]
        [TextArea(4, 12)]
        public string wallMap = string.Empty;

        [Header("玩家手牌")]
        [Tooltip("留空 = 不限量（作者拿来摆草图的演示关）。填了才是有限元件的解谜关：" +
                 "placements 给的是场景，supplies 给的是玩家能做的事")]
        public List<ElementSupply> supplies = new List<ElementSupply>();

        [Header("规则")]
        [Tooltip("允许玩家进关后自己搬元件（沙盒关）。默认关掉 = 玩家只能玩作者摆好的样子。\n" +
                 "对应 JSON 里的 allowBuild（schema 2 起）")]
        public bool allowBuild;

        /// <summary>有没有给玩家手牌。false 时这个关卡只是演示/作者的沙盒。</summary>
        public bool HasSupplies => supplies != null && supplies.Count > 0;

        /// <summary>
        /// 转成纯逻辑关卡。Id 按摆放顺序从 1 递增分配，
        /// 保证同一份资产每次转换得到的 Id 完全一致（UI 进度条与视图映射都靠它）。
        /// </summary>
        public LevelDefinition ToDefinition()
        {
            LevelDefinition definition = new LevelDefinition
            {
                DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName,
                Width = Mathf.Max(1, width),
                Height = Mathf.Max(1, height),
                TickInterval = Mathf.Max(0.05f, tickInterval),
            };

            int nextId = 1;
            foreach (ElementPlacement placement in placements)
            {
                if (placement == null || placement.type == null) continue;

                ElementDefinition element = placement.type.CreateDefinition(
                    new GridPoint(placement.coord.x, placement.coord.y),
                    placement.rotationSteps,
                    nextId);

                if (element == null) continue;

                element.Label = placement.type.label;
                element.TypeKey = placement.type.Key;

                // 和 CreatePlayerDraft 用同一个字段：作者锁着的那一件，玩家局里就该锁着
                element.PlayerEditable = placement.playerEditable;

                // 关卡设计权：这一格在编辑器里填的序列 / 次数，覆盖类型资产的默认值。
                // 没填过的格子这里是个空操作，所以老关卡行为完全不变。
                placement.ApplyOverrides(element);

                // 说明文本必须按「覆盖 + 旋转之后」的这份定义来拼：
                // 直接贴 type.Describe() 会把预设值（1011 x1 / HOLD 1 TICK / 基准朝向）当成实际值，
                // 详情 UI 就和场上跑的对不上了。标题行（"EMITTER E1"）仍然借资产的。
                element.Hint = ElementHint.Describe(element, placement.type.Describe());

                definition.Elements.Add(element);
                nextId++;
            }

            // 墙层追加在 placements 之后。这个顺序是刻意的：
            // 把墙从 placements 迁进 wallMap 之后，Elements 的数量和「元件在前、墙在后」
            // 的格局都不变，Id 不会整体错位，视图映射和进度条也就不受影响。
            AppendWalls((coord, id) =>
            {
                ElementDefinition wall = wallType.CreateDefinition(new GridPoint(coord.x, coord.y), 0, id);
                if (wall == null) return false;

                wall.Label = wallType.label;
                wall.Hint = wallType.Describe();
                wall.TypeKey = wallType.Key;
                definition.Elements.Add(wall);
                return true;
            }, nextId);

            return definition;
        }

        /// <summary>
        /// 把 <see cref="wallMap"/> 展开成坐标。规则（都写在 Tooltip 里，这里重复一遍免得改代码时忘了）：
        /// - 一行 = 一个 y，从下往上数，第一行是 y=0；行内第 i 个字符是 x=i；
        /// - '#' 是墙，其余字符（'.'、空格、Tab）都是空；
        /// - 短行等于右侧留空，超出 width/height 的字符直接忽略（作者改网格尺寸时不用同步改图）；
        /// - 同一格只算一次。
        ///
        /// 之所以用字符网格而不是数组字段：手写关卡时一眼就能看出形状，
        /// 比 18 条每条 8 个字段的 placement 好维护得多。
        /// </summary>
        public IEnumerable<Vector2Int> ParseWalls()
        {
            if (string.IsNullOrEmpty(wallMap) || wallType == null) yield break;

            int maxX = Mathf.Max(1, width);
            int maxY = Mathf.Max(1, height);

            string[] lines = SplitWallLines();

            HashSet<Vector2Int> seen = new HashSet<Vector2Int>();

            for (int y = 0; y < lines.Length && y < maxY; y++)
            {
                string line = lines[y];
                int limit = Mathf.Min(line.Length, maxX);

                for (int x = 0; x < limit; x++)
                {
                    if (line[x] != '#') continue;

                    Vector2Int coord = new Vector2Int(x, y);
                    if (seen.Add(coord)) yield return coord;
                }
            }
        }

        /// <summary>这一格在墙层里吗。只看 wallMap，不看 placements。</summary>
        public bool IsWallInMap(Vector2Int coord)
        {
            if (coord.x < 0 || coord.y < 0 || string.IsNullOrEmpty(wallMap)) return false;

            string[] lines = SplitWallLines();
            if (coord.y >= lines.Length) return false;

            string line = lines[coord.y];
            return coord.x < line.Length && line[coord.x] == '#';
        }

        /// <summary>
        /// 在墙层里画 / 擦一格，返回"文本是否真的变了"（调用方据此决定要不要记 Undo）。
        ///
        /// 会自动把缺的行 / 短的行补齐到 width（用 '.' 补，肉眼能数清列），
        /// 所以作者手写的图不必每行都对齐 —— 少打几个点不会让后面的墙整体错位。
        /// </summary>
        public bool SetWall(Vector2Int coord, bool isWall)
        {
            int maxX = Mathf.Max(1, width);
            int maxY = Mathf.Max(1, height);

            if (coord.x < 0 || coord.y < 0 || coord.x >= maxX || coord.y >= maxY) return false;

            List<string> lines = new List<string>(SplitWallLines());

            while (lines.Count <= coord.y) lines.Add(string.Empty);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length < maxX) lines[i] = lines[i].PadRight(maxX, '.');
            }

            char[] chars = lines[coord.y].ToCharArray();
            char target = isWall ? '#' : '.';
            if (chars[coord.x] == target) return false;

            chars[coord.x] = target;
            lines[coord.y] = new string(chars);

            // 尾部整行都没墙的地位于网格之外，留着只会让文本越改越长
            while (lines.Count > 0 && IsBlankWallLine(lines[lines.Count - 1])) lines.RemoveAt(lines.Count - 1);

            wallMap = string.Join("\n", lines);
            return true;
        }

        string[] SplitWallLines()
        {
            return (wallMap ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }

        static bool IsBlankWallLine(string line)
        {
            foreach (char c in line)
            {
                if (c != '.' && c != ' ' && c != '\t') return false;
            }
            return true;
        }

        /// <summary>这一格是不是已经被 placements 占了。占了的格 wallMap 要跳过：placements 优先。</summary>
        public bool HasPlacementAt(Vector2Int coord)
        {
            return FindPlacement(coord) != null;
        }

        /// <summary>
        /// 遍历 wallMap 里还空着的墙格，交给 <paramref name="add"/> 自己决定怎么落地
        /// （ToDefinition 用逻辑定义、CreatePlayerDraft 用可编辑草稿）。
        /// </summary>
        void AppendWalls(Func<Vector2Int, int, bool> add, int firstId)
        {
            if (wallType == null) return;

            int id = firstId;
            foreach (Vector2Int coord in ParseWalls())
            {
                // placements 优先：作者在网格里手滑画到元件身上时，结果应该是"这格已有东西"，
                // 而不是凭空多出一面墙把元件盖掉
                if (HasPlacementAt(coord)) continue;

                if (add(coord, id)) id++;
            }
        }

        /// <summary>
        /// 生成玩家局内草稿：placements 里 locked 的成为预设件（搬不动），其余成为可自由改动的件；
        /// supplies 变成有限手牌。这是「有限元件解谜」的唯一入口 —— 玩家所有操作都发生在这份草稿上，
        /// 想验证解法就 ToDefinition() 丢给模拟器，跟作者模式共用同一套渲染/模拟管线。
        /// </summary>
        public LevelDraft CreatePlayerDraft()
        {
            LevelDraft draft = new LevelDraft(width, height, tickInterval)
            {
                DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName,
            };

            foreach (ElementPlacement placement in placements)
            {
                if (placement == null || placement.type == null) continue;

                EditableElement element = placement.type.CreateEditable(
                    new GridPoint(placement.coord.x, placement.coord.y), placement.rotationSteps);
                if (element == null) continue;

                // 和 ToDefinition() 用同一份覆盖逻辑，保证作者视角与玩家视角参数一致
                placement.ApplyOverrides(element);

                draft.Place(element, !placement.playerEditable);
            }

            // 墙层的墙在玩家局里永远是「关卡预设件」：搬不动、拾不回，和以前写在 placements 里一样
            AppendWalls((coord, _) =>
            {
                EditableElement wall = wallType.CreateEditable(new GridPoint(coord.x, coord.y));
                if (wall == null) return false;

                draft.Place(wall, true);
                return true;
            }, 0);

            foreach (ElementSupply supply in supplies)
            {
                if (supply == null || supply.type == null) continue;

                // 累加而不是覆盖：库存按类型 Key 记账，同一个类型写两条就是「再多给几张」。
                // 覆盖会让作者手滑加两次时玩家少一张牌（Level_05 就踩过这个坑）。
                // kind 一并登记：类型资产没有 kind 字段，得由注册表问一次，
                // 否则写回存档时这一摞会被兜底成「墙」（自检也就认不出被发错的发射器）。
                draft.AddStock(supply.type.Key, LevelTypeRegistrySO.KindOf(supply.type), supply.count);
            }

            // 关卡初始化不算玩家的一步：第一次 Ctrl+Z 不该把预设件撤掉
            draft.ClearHistory();
            return draft;
        }

        /// <summary>按坐标找摆放项（LevelValidator / 编辑器辅助用）。</summary>
        public ElementPlacement FindPlacement(Vector2Int coord)
        {
            foreach (ElementPlacement placement in placements)
            {
                if (placement != null && placement.coord == coord) return placement;
            }
            return null;
        }
    }
}
