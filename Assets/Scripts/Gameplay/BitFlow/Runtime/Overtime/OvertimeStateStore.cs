using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// <c>overtime_state.json</c> 的落盘 / 重建。
    ///
    /// 放在 <see cref="Application.persistentDataPath"/> 根目录，而不是 LevelJson 目录：
    /// LevelJson 会被选关面板当成关卡目录扫描，状态文件放进去会被误报成坏关卡。
    /// 状态文件丢失时用目录里的 OT-PROBLEM_###.json 重建，不重复写题。
    /// </summary>
    public static class OvertimeStateStore
    {
        public static string StatePath => Path.Combine(Application.persistentDataPath, OvertimeConfig.StateFileName);

        public static OvertimeState LoadOrRebuild(out bool rebuilt)
        {
            rebuilt = false;

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
                string folder = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string temporary = StatePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(state, true));
                if (File.Exists(StatePath)) File.Delete(StatePath);
                File.Move(temporary, StatePath);
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
                if (!File.Exists(StatePath)) return null;

                string json = File.ReadAllText(StatePath);
                if (string.IsNullOrEmpty(json)) return null;

                OvertimeState state = JsonUtility.FromJson<OvertimeState>(json);
                if (state == null) return null;
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
                    displayName = entry.DisplayName,
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
    }
}
