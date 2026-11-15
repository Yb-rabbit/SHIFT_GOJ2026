using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// <c>overtime_state.sav</c> 的落盘 / 重建。
    ///
    /// 放在 <see cref="Application.persistentDataPath"/> 根目录，而不是 LevelJson 目录：
    /// LevelJson 会被选关面板当成关卡目录扫描，状态文件放进去会被误报成坏关卡。
    /// 状态文件丢失时用目录里的 OT-PROBLEM_###.json 重建，不重复写题。
    /// </summary>
    public static class OvertimeStateStore
    {
        public static string StatePath => Path.Combine(Application.persistentDataPath, OvertimeConfig.StateFileName);

        /// <summary>v2 及以前的状态文件名。读到它就搬到 .sav，然后按旧版本归档。</summary>
        static string LegacyStatePath => Path.Combine(Application.persistentDataPath, "overtime_state.json");

        static void MigrateLegacyStateFile()
        {
            try
            {
                if (LevelJsonLibrary.FileExists(StatePath) || !LevelJsonLibrary.FileExists(LegacyStatePath)) return;

                string json = LevelJsonLibrary.ReadAllText(LegacyStatePath);
                LevelJsonLibrary.WriteAllText(StatePath, json);
                LevelJsonLibrary.DeleteFile(LegacyStatePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] OVERTIME 旧状态迁移失败：" + exception.Message);
            }
        }

        public static OvertimeState LoadOrRebuild(out bool rebuilt)
        {
            rebuilt = false;
            MigrateLegacyStateFile();

            // 浏览器旧版本可能没有状态文件、但 OT-PROBLEM_###.json 还在；
            // 也可能状态已经是 v3 而前三题还是旧的单发射器走廊题。
            // 先归档旧形状，避免它们占住碰撞题的题号。
            if (PruneStaleCollisionLevels()) rebuilt = true;

            OvertimeState state = ReadState();
            if (state == null)
            {
                state = new OvertimeState { baseSeed = OvertimeSeed.CreateBaseSeed() };
                rebuilt = true;
            }

            state.Normalize();
            if (ReconcileWithFiles(state)) rebuilt = true;
            if (rebuilt) Save(state);
            return state;
        }

        public static OvertimeState LoadOrRebuild()
        {
            return LoadOrRebuild(out _);
        }

        public static void Save(OvertimeState state)
        {
            if (state == null) return;

            state.Normalize();

            try
            {
                // 走 LevelJsonLibrary 的存储后端：桌面端是 File.WriteAllText，
                // WebGL 端自动落到 BrowserFileSystem（PlayerPrefs / IndexedDB）。
                // 这里不再临时文件 + Move：浏览器沙箱没有 Move，重命名也不是原子操作。
                LevelJsonLibrary.WriteAllText(StatePath, JsonUtility.ToJson(state, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] OVERTIME 状态写不进去：" + exception.Message);
            }
        }

        static OvertimeState ReadState()
        {
            try
            {
                if (!LevelJsonLibrary.FileExists(StatePath)) return null;

                string json = LevelJsonLibrary.ReadAllText(StatePath);
                if (string.IsNullOrEmpty(json)) return null;

                OvertimeState state = JsonUtility.FromJson<OvertimeState>(json);
                if (state == null) return null;

                // v1 是 30 题随机生成；v2 起是手工蓝图，v5 收缩为 8 道碰撞题。旧题号和旧文件不能直接沿用，
                // 先整体挪进 overtime_legacy，再按空状态重建，避免旧布局占住新题号。
                if (state.version < OvertimeConfig.StateVersion)
                {
                    ArchiveLegacy(state.version);
                    return null;
                }

                state.Normalize();
                return state;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] OVERTIME 状态读不了，按空状态重建：" + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// 把旧版本状态和 OT-PROBLEM_*.json 挪到 LevelJson/overtime_legacy/。
        /// 只搬不删，玩家想找旧题还能找回来。
        /// </summary>
        static void ArchiveLegacy(int version)
        {
            int safeVersion = Math.Max(1, version);
            string legacyFolder = Path.Combine(LevelJsonLibrary.Folder, OvertimeConfig.LegacyFolderName);
            if (LevelJsonLibrary.HasLocalFiles) Directory.CreateDirectory(legacyFolder);

            MoveToLegacy(StatePath, legacyFolder, safeVersion);

            // Scan 在桌面与浏览器里走同一份实现；不能用 Directory.GetFiles，
            // 浏览器沙箱里那只是 stub，会直接抛 NotSupportedException。
            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out _);
            foreach (LevelJsonEntry entry in entries)
            {
                // 宽松解析：v4 时代的 9-12 题也必须跟着状态一起归档，
                // 否则它们会以 MY LEVEL 的身份重新出现在选关列表里。
                if (!TryParseArchivedFileName(entry.Path, out _)) continue;
                MoveToLegacy(entry.Path, legacyFolder, safeVersion);
            }

            Debug.LogWarning("[BitFlow] OVERTIME v" + version + " 旧数据已归档到 " + legacyFolder + "，重建 " + OvertimeConfig.MaxProblems + " 题。");
        }

        /// <summary>
        /// 归档单个文件。桌面端真移动；浏览器端没有目录/改名，改成"读出来 → 写回带版本前缀的新键 → 删旧键"，
        /// 避免和仍在使用的 OT-PROBLEM_###.json 在扁平存储里撞名。
        /// </summary>
        static void MoveToLegacy(string source, string legacyFolder, int version)
        {
            if (!LevelJsonLibrary.FileExists(source)) return;

            string fileName = Path.GetFileName(source);
            string destination = LevelJsonLibrary.HasLocalFiles
                ? Path.Combine(legacyFolder, fileName)
                : Path.Combine(legacyFolder, "v" + version + "_" + Path.GetFileNameWithoutExtension(fileName) + ".bak");

            if (LevelJsonLibrary.HasLocalFiles)
            {
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(source, destination);
                return;
            }

            string json = LevelJsonLibrary.ReadAllText(source);
            LevelJsonLibrary.WriteAllText(destination, json);
            LevelJsonLibrary.DeleteFile(source);
        }

        /// <summary>
        /// 状态文件缺失时（尤其是旧 WebGL 版本），前三题可能还是单发射器走廊题。
        /// 这些文件会被 Reconcile 当成有效题捡回来，必须先把它们归档，v3 的碰撞题才有题号。
        /// 只处理 1-3 题，且只认"发射器少于 2"的旧形状；v3 新题（2 发射器）会原样保留。
        /// </summary>
        static bool PruneStaleCollisionLevels()
        {
            bool changed = false;
            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out _);
            string legacyFolder = Path.Combine(LevelJsonLibrary.Folder, OvertimeConfig.LegacyFolderName);

            foreach (LevelJsonEntry entry in entries)
            {
                if (!TryParseArchivedFileName(entry.Path, out int index)) continue;

                // 1-8 之外全部是旧题库（v4 的 9-12 题），直接归档，不参与当前选关列表。
                if (index > OvertimeCollisionCatalog.Count)
                {
                    if (LevelJsonLibrary.HasLocalFiles) Directory.CreateDirectory(legacyFolder);
                    MoveToLegacy(entry.Path, legacyFolder, OvertimeConfig.StateVersion);
                    changed = true;
                    continue;
                }

                if (!LevelJsonLibrary.TryLoad(entry.Path, out LevelSO level, out _)) continue;

                int emitters = 0;
                LevelDefinition definition = level.ToDefinition();
                foreach (ElementDefinition element in definition.Elements)
                {
                    if (element is EmitterDefinition) emitters++;
                }
                LevelJsonLibrary.DestroyLevel(level);

                if (emitters >= 2) continue;
                if (LevelJsonLibrary.HasLocalFiles) Directory.CreateDirectory(legacyFolder);
                MoveToLegacy(entry.Path, legacyFolder, OvertimeConfig.StateVersion);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 用实际文件校正状态：
        /// - 记录还在但文件被删 → 移除记录，题号可以重新生成；
        /// - 文件还在但记录丢了 → 补记录，nextProblem 按 max+1 走，绝不覆盖已有文件。
        /// </summary>
        static bool ReconcileWithFiles(OvertimeState state)
        {
            bool changed = false;

            if (state.problems == null) state.problems = new List<OvertimeProblemRecord>();

            // 先删掉已经不存在的文件记录。只认官方路径，避免把别的 json 当 OVERTIME。
            for (int i = state.problems.Count - 1; i >= 0; i--)
            {
                OvertimeProblemRecord record = state.problems[i];
                string path = Path.Combine(LevelJsonLibrary.Folder, OvertimeConfig.FileName(record.index));
                if (!LevelJsonLibrary.FileExists(path))
                {
                    state.problems.RemoveAt(i);
                    changed = true;
                }
            }

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out _);
            foreach (LevelJsonEntry entry in entries)
            {
                if (!OvertimeConfig.TryParseFileName(entry.Path, out int index)) continue;
                if (state.Find(index) != null) continue;

                OvertimeSize size = OvertimeConfig.SizeFor(index);
                state.problems.Add(new OvertimeProblemRecord
                {
                    index = index,
                    // 旧文件里可能还写着 "OT-PROBLEM 03"；补记录时统一换成蓝图题名。
                    displayName = OvertimeConfig.DisplayName(index),
                    fileName = OvertimeConfig.FileName(index),
                    width = entry.Width > 0 ? entry.Width : size.Width,
                    height = entry.Height > 0 ? entry.Height : size.Height,
                    seed = OvertimeSeed.ForProblem(state.baseSeed, index),
                    targetSteps = OvertimeConfig.TargetStepsFor(index),
                    ticks = 0,
                    createdAt = string.Empty,
                });
                changed = true;
            }

            int beforeNext = state.nextProblem;
            state.Normalize();
            if (beforeNext != state.nextProblem) changed = true;
            return changed;
        }

        /// <summary>
        /// 归档用宽松解析：只认 OT-PROBLEM_###.json 的形状，但题号允许 1..999。
        /// 新题库只接受 1..8；这里放宽是为了把 v4 的 9-12 题也一并扫进 legacy。
        /// </summary>
        static bool TryParseArchivedFileName(string fileName, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(fileName)) return false;

            string name = Path.GetFileName(fileName);
            if (!name.StartsWith(OvertimeConfig.FilePrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            string number = name.Substring(
                OvertimeConfig.FilePrefix.Length,
                name.Length - OvertimeConfig.FilePrefix.Length - 5);
            if (!int.TryParse(number, out index)) return false;
            return index >= 1 && index <= 999;
        }
    }
}
