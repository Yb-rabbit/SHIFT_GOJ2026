using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 玩家进度存档的全部内容。<b>只放跨关卡的东西</b>，和
    /// <see cref="LevelDraftSerializer"/>（自定义关卡的完整快照，另一个文件）互不干扰。
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>已经见过的元件类型 Key（<see cref="ElementTypeSO.Key"/>），黑幕里"首次出现"靠它判定。</summary>
        public List<string> seenElements = new List<string>();

        /// <summary>已通关的关卡名（<see cref="LevelSO.displayName"/>），选关面板打勾用。</summary>
        public List<string> clearedLevels = new List<string>();
    }

    /// <summary>
    /// 存档读写：<c>persistentDataPath/ShiftGo/save.json</c>，用 JsonUtility 存，
    /// 出任何异常都退化成"空存档"而不是抛出去 —— 丢进度能忍，开不了游戏不能忍。
    /// </summary>
    public static class SaveStore
    {
        const string FolderName = "ShiftGo";
        const string FileName = "save.json";

        static SaveData data;

        /// <summary>当前存档（首次访问时从磁盘读）。</summary>
        public static SaveData Data
        {
            get
            {
                if (data == null) data = Load();
                return data;
            }
        }

        /// <summary>存档的完整路径（调试/文档用）。</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, FolderName, FileName);

        public static bool IsSeen(string typeKey)
        {
            return !string.IsNullOrEmpty(typeKey) && Data.seenElements.Contains(typeKey);
        }

        public static bool IsCleared(string levelName)
        {
            return !string.IsNullOrEmpty(levelName) && Data.clearedLevels.Contains(levelName);
        }

        /// <summary>标记元件类型已经出现过；本来就有则一次磁盘都不写。</summary>
        public static void MarkSeen(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey) || Data.seenElements.Contains(typeKey)) return;

            Data.seenElements.Add(typeKey);
            Save();
        }

        /// <summary>批量标记（黑幕里一次性把本关手牌都记下）。</summary>
        public static void MarkSeen(IEnumerable<string> typeKeys)
        {
            if (typeKeys == null) return;

            bool changed = false;
            foreach (string key in typeKeys)
            {
                if (string.IsNullOrEmpty(key) || Data.seenElements.Contains(key)) continue;

                Data.seenElements.Add(key);
                changed = true;
            }

            if (changed) Save();
        }

        public static void MarkCleared(string levelName)
        {
            if (string.IsNullOrEmpty(levelName) || Data.clearedLevels.Contains(levelName)) return;

            Data.clearedLevels.Add(levelName);
            Save();
        }

        static SaveData Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new SaveData();

                SaveData loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                if (loaded == null) return new SaveData();

                // JsonUtility 走 FormatterServices 建对象，不走构造函数：
                // 老存档缺字段时这里会是 null，必须补回来，否则第一次读取就 NullReference
                if (loaded.seenElements == null) loaded.seenElements = new List<string>();
                if (loaded.clearedLevels == null) loaded.clearedLevels = new List<string>();
                return loaded;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 存档读取失败，按空存档继续：" + exception.Message);
                return new SaveData();
            }
        }

        static void Save()
        {
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 存档写入失败：" + exception.Message);
            }
        }
    }
}
