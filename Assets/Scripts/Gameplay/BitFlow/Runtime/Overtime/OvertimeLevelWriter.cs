using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// OVERTIME 的落盘 shim。没有复用 LevelJsonLibrary.TryWriteNew：
    /// 那个方法的 99 上限和自动编号不符合 OT-PROBLEM_### 的命名，而且它会先写后验。
    /// 这里按固定题号写，写前验 JSON、写后再读一遍，失败立即回滚。
    /// </summary>
    public static class OvertimeLevelWriter
    {
        public static string PathFor(int index)
        {
            return Path.Combine(LevelJsonLibrary.Folder, OvertimeConfig.FileName(index));
        }

        public static bool TrySave(LevelDefinition definition, int index, out string path, out string error)
        {
            path = null;
            error = null;

            if (definition == null || !OvertimeConfig.IsValidIndex(index))
            {
                error = OvertimeFactory.FailureMessage;
                return false;
            }

            if (definition.DisplayName != OvertimeConfig.DisplayName(index))
                definition.DisplayName = OvertimeConfig.DisplayName(index);

            LevelJsonLibrary.EnsureFolder();
            path = PathFor(index);

            if (LevelJsonLibrary.FileExists(path))
            {
                error = "OVERTIME 题号 " + index.ToString("00") + " 已经有文件了";
                return false;
            }

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out _);
            foreach (LevelJsonEntry entry in entries)
            {
                if (string.Equals(entry.DisplayName, definition.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "已有同名关卡：" + definition.DisplayName;
                    return false;
                }
            }

            LevelDocument document = LevelDocumentWriter.FromDefinition(definition, new List<string>());
            string json = JsonUtility.ToJson(document, true);
            if (string.IsNullOrEmpty(json) || json == "{}")
            {
                error = "关卡序列化结果为空";
                return false;
            }

            if (!LevelJsonLibrary.TryParseJson(json, out LevelSO probe, out List<string> problems))
            {
                error = problems.Count > 0 ? problems[0] : "关卡 JSON 自检失败";
                return false;
            }
            LevelJsonLibrary.DestroyLevel(probe);

            try
            {
                LevelJsonLibrary.WriteAllText(path, json);
            }
            catch (Exception exception)
            {
                error = "写不进 OVERTIME 目录：" + exception.Message;
                return false;
            }

            if (!LevelJsonLibrary.TryLoad(path, out LevelSO loaded, out List<string> loadProblems))
            {
                LevelJsonLibrary.TryDelete(path, out _);
                error = loadProblems.Count > 0 ? loadProblems[0] : "写完读不回来";
                return false;
            }
            LevelJsonLibrary.DestroyLevel(loaded);

            return true;
        }
    }
}
