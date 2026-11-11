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

        /// <summary>已通关的内置关卡名（<see cref="LevelSO.displayName"/>），选关面板打勾用。</summary>
        public List<string> clearedLevels = new List<string>();

        /// <summary>
        /// 已通关的运行时 JSON 关卡，按<b>文件绝对路径</b>记（见 <see cref="SaveStore.JsonLevelKey"/>）。
        ///
        /// 不能和 <see cref="clearedLevels"/> 混用：JSON 是玩家自己复制的文件，显示名可以
        /// 和内置关一模一样（例如从「00  BIT FLOW」导出的副本仍叫这个名），按名字记会让
        /// 没玩过的副本直接继承内置关的 OK。路径才是这份文件的身份。
        /// </summary>
        public List<string> clearedJsonLevels = new List<string>();

        /// <summary>
        /// 设置字段的版本号。老存档没有这个字段时 JsonUtility 会给 0，读取时按 0 补齐默认值
        /// （音频开、80%、提示开）。以后再加设置项就把它 +1，并在 <see cref="SaveStore.Load"/> 里补迁移。
        /// </summary>
        public int settingsVersion = CurrentSettingsVersion;

        /// <summary>音频总开关：1 = 开，0 = 关。用 int 而不是 bool 是为了让"字段缺失"能被 version 判定出来。</summary>
        public int audioOn = 1;

        /// <summary>音频总音量（0 ~ 100）。关掉音频时保留这个值，下次打开还是原音量。</summary>
        public int audioVolumePercent = 80;

        /// <summary>是否显示玩法提示（1 = 开，0 = 关）。帮助系统接进来之前先存着，避免以后再改一次存档结构。</summary>
        public int helpHintOn = 1;

        public const int CurrentSettingsVersion = 1;
    }

    /// <summary>
    /// 存档读写：<c>persistentDataPath/ShiftGo/save.json</c>，用 JsonUtility 存，
    /// 出任何异常都退化成"空存档"而不是抛出去 —— 丢进度能忍，开不了游戏不能忍。
    ///
    /// 编辑器里读写的是同一目录下的 <c>save_editor.json</c>：自测通关不该污染
    /// 打包版真正会读的那份，两份互不干扰（见 <see cref="ActiveFileName"/>）。
    /// </summary>
    public static class SaveStore
    {
        const string FolderName = "ShiftGo";

        /// <summary>打包后玩家机上的存档名。</summary>
        const string FileName = "save.json";

        /// <summary>
        /// 编辑器里另用一份：自测通关不该写进"真实"存档，否则选关面板一次之后就永远打勾，
        /// 还得手动去 LocalLow 里删文件。两份文件互不干扰，打包后只认 <see cref="FileName"/>。
        /// </summary>
        const string EditorFileName = "save_editor.json";

        static string ActiveFileName
        {
            get
            {
#if UNITY_EDITOR
                return EditorFileName;
#else
                return FileName;
#endif
            }
        }

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
        public static string FilePath => Path.Combine(Application.persistentDataPath, FolderName, ActiveFileName);

        /// <summary>打包后的正式存档路径。编辑器里显示它 = 那个"玩家真文档"，只用于提示/清理。</summary>
        public static string PlayerFilePath => Path.Combine(Application.persistentDataPath, FolderName, FileName);

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

        /// <summary>
        /// JSON 关卡的存档键：把路径变成「同一条路径只有一种写法」。
        ///
        /// <see cref="Path.GetFullPath"/> 抹平相对路径和当前工作目录的差异，
        /// 斜杠统一成 <c>/</c>，再去掉结尾的分隔符。大小写不在这里折叠，
        /// 比较时用 <see cref="StringComparison.OrdinalIgnoreCase"/> —— Windows 路径不区分大小写，
        /// 而 Unix 上真的存在只差大小写的两个文件（虽然关在同一个目录里的概率极低）。
        /// </summary>
        public static string JsonLevelKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string full = path;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // 无效路径保留原样：至少不会把玩家已有的勾清掉
            }

            return full.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>这个 JSON 文件是否已经通关（按路径，不按显示名）。</summary>
        public static bool IsJsonCleared(string path)
        {
            string key = JsonLevelKey(path);
            if (key == null) return false;

            foreach (string cleared in Data.clearedJsonLevels)
            {
                if (SameKey(cleared, key)) return true;
            }

            return false;
        }

        /// <summary>标记一个 JSON 文件已通关。</summary>
        public static void MarkJsonCleared(string path)
        {
            string key = JsonLevelKey(path);
            if (key == null || IsJsonCleared(key)) return;

            Data.clearedJsonLevels.Add(key);
            Save();
        }

        /// <summary>
        /// 清掉一个 JSON 文件的通关勾。工坊改完关卡内容后调用：
        /// 题目已经变了，旧的通关记录不能继续算数。
        /// </summary>
        public static void ClearJsonCleared(string path)
        {
            string key = JsonLevelKey(path);
            if (key == null) return;

            int removed = Data.clearedJsonLevels.RemoveAll(cleared => SameKey(cleared, key));
            if (removed > 0) Save();
        }

        /// <summary>
        /// 清空所有通关勾（内置关 + JSON 关），<b>保留</b>已见元件记录。
        /// 选关面板的 RESET PROGRESS 走这里 —— 玩家想重新打一遍，但不该被迫重看开场教学。
        /// </summary>
        public static void ClearClearedLevels()
        {
            if (Data.clearedLevels.Count == 0 && Data.clearedJsonLevels.Count == 0) return;

            Data.clearedLevels.Clear();
            Data.clearedJsonLevels.Clear();
            Save();
        }

        static bool SameKey(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 清空进度：元件"首次出现"和通关打勾一起清，内存与磁盘同时归零。
        /// 直接删文件而不是写一份空档 —— 空档和"没存过档"之后要分两种情况维护，没必要。
        /// 编辑器里清的是 <see cref="EditorFileName"/>；它碰不到打包后的 <see cref="FileName"/>。
        /// </summary>
        public static void ClearAll()
        {
            data = new SaveData();

            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 存档删除失败：" + exception.Message);
            }
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
                if (loaded.clearedJsonLevels == null) loaded.clearedJsonLevels = new List<string>();

                // 老存档（带进度、但不带设置）在这里补齐设置默认值。
                // 不能只靠字段初始化：JsonUtility 走 FormatterServices 建对象，不执行构造函数/字段初始化。
                if (loaded.settingsVersion < SaveData.CurrentSettingsVersion)
                {
                    loaded.settingsVersion = SaveData.CurrentSettingsVersion;
                    loaded.audioOn = 1;
                    loaded.audioVolumePercent = 80;
                    loaded.helpHintOn = 1;
                }

                // 手改坏的存档（例如 250）也夹回合法范围，别让音量按钮从 250% 开始
                if (loaded.audioVolumePercent < 0) loaded.audioVolumePercent = 0;
                if (loaded.audioVolumePercent > 100) loaded.audioVolumePercent = 100;
                return loaded;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 存档读取失败，按空存档继续：" + exception.Message);
                return new SaveData();
            }
        }

        /// <summary>
        /// 立即把当前内存存档写盘。设置类字段（音频等）改完就调它 ——
        /// 设置没有"关卡进度"那种批量标记的时机，等下一次 MarkSeen 才落盘会让重启丢设置。
        /// </summary>
        public static void Save()
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
