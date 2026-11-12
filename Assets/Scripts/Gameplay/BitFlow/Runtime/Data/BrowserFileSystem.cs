using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// WebGL 下的「关卡目录」替身：把 <c>persistentDataPath/LevelJson</c> 里那几个
    /// <c>.json</c> 存进 <see cref="PlayerPrefs"/>（浏览器里由 Unity 落到 IndexedDB）。
    ///
    /// 为什么要这一层：浏览器沙箱里没有可写的本地目录，<c>System.IO.File</c> /
    /// <c>Directory</c> 全是 stub，一碰就抛 <c>NotSupportedException</c>。
    /// 而关卡库的读写逻辑（<see cref="LevelJsonLibrary"/>、工坊）本来只依赖
    /// 「路径 → 文本」这几个动作，换一层实现就能原样跑在浏览器里。
    ///
    /// <b>桌面端根本不参与</b>：<see cref="IsActive"/> 为 false，调用方仍旧直连 <c>System.IO</c>，
    /// 行为一个字节都不变。
    ///
    /// 键名约定：只拿路径里的<b>文件名</b>做身份（<c>LEVEL_01.json</c>），
    /// 目录层级不落库 —— 关卡目录本来就是平的，这也让虚拟路径变了（换机器 / 换 hash）
    /// 之后存档仍然认得出来。
    /// </summary>
    public static class BrowserFileSystem
    {
        /// <summary>当前平台是否该走浏览器存储。</summary>
        public static bool IsActive
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return forceActive;
#endif
            }
        }

        /// <summary>
        /// 测试用开关：让编辑器里也把读写当成「浏览器环境」走 PlayerPrefs。
        ///
        /// 存在的理由很实际 —— 真正的 WebGL 路径只能在浏览器里验证，
        /// 而「建关 → 存 → 重进还在」这条链的逻辑本身跟平台无关，
        /// 用这个开关就能在 EditMode 测试里完整跑一遍。
        /// 只允许测试代码打开，运行时代码永远不碰它。
        /// </summary>
        static bool forceActive;

        /// <summary>仅供测试：打开/关闭「假装自己是浏览器」。运行时代码不应调用它。</summary>
        public static void SetForcedForTests(bool value)
        {
            forceActive = value;
        }

        /// <summary>已存文件名的索引（<c>{"names":["LEVEL_01.json"]}</c>）。</summary>
        const string IndexKey = "BitFlow.Fs.Index";

        /// <summary>单个文件内容的键前缀，后面接文件名。</summary>
        const string FileKeyPrefix = "BitFlow.Fs.File.";

        /// <summary>索引的内存缓存：每次读写都反序列化一遍太浪费，PlayerPrefs 本身够快。</summary>
        static Index index;

        [Serializable]
        sealed class Index
        {
            public List<string> names = new List<string>();
        }

        static Index Load()
        {
            if (index != null) return index;

            string raw = PlayerPrefs.GetString(IndexKey, string.Empty);
            if (!string.IsNullOrEmpty(raw))
            {
                try { index = JsonUtility.FromJson<Index>(raw); }
                catch (Exception) { index = null; }
            }

            // JsonUtility 走 FormatterServices，不走构造函数：names 可能是 null
            if (index == null) index = new Index();
            if (index.names == null) index.names = new List<string>();
            return index;
        }

        static void Store(Index value)
        {
            index = value;
            PlayerPrefs.SetString(IndexKey, JsonUtility.ToJson(value));
            PlayerPrefs.Save();
        }

        /// <summary>路径 → 存储键。目录层级丢掉，只留文件名。</summary>
        static string NameOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string name = System.IO.Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        /// <summary>浏览器里「关卡目录」永远存在，不让上层报「还没有关卡目录」。</summary>
        public static bool DirectoryExists(string directory)
        {
            return true;
        }

        /// <summary>没有真实目录可建，空操作。</summary>
        public static void CreateDirectory(string directory)
        {
        }

        public static bool Exists(string path)
        {
            string name = NameOf(path);
            return !string.IsNullOrEmpty(name) && PlayerPrefs.HasKey(FileKeyPrefix + name);
        }

        /// <summary>读文本。<b>文件不存在时抛 <see cref="System.IO.FileNotFoundException"/></b>，
        /// 保持和 <c>File.ReadAllText</c> 一样的语义，上层的 try/catch 不用改。</summary>
        public static string ReadAllText(string path)
        {
            string name = NameOf(path);
            string key = FileKeyPrefix + name;
            if (string.IsNullOrEmpty(name) || !PlayerPrefs.HasKey(key))
            {
                throw new System.IO.FileNotFoundException("浏览器存储里没有这个关卡文件", path);
            }

            return PlayerPrefs.GetString(key, string.Empty);
        }

        public static void WriteAllText(string path, string text)
        {
            string name = NameOf(path);
            if (string.IsNullOrEmpty(name)) throw new System.IO.IOException("关卡文件名为空：" + path);

            PlayerPrefs.SetString(FileKeyPrefix + name, text);

            Index loaded = Load();
            if (!loaded.names.Contains(name)) loaded.names.Add(name);
            Store(loaded);
        }

        public static void Delete(string path)
        {
            string name = NameOf(path);
            if (string.IsNullOrEmpty(name)) return;

            PlayerPrefs.DeleteKey(FileKeyPrefix + name);

            Index loaded = Load();
            if (loaded.names.Remove(name)) Store(loaded);
            else PlayerPrefs.Save();
        }

        /// <summary>
        /// 列目录。只支持关卡库实际会用的两种写法（<c>*.json</c> / <c>*</c>），
        /// 返回绝对路径形式的虚拟路径，好让上层继续用 <c>Path.GetFileName</c>。
        /// </summary>
        public static string[] GetFiles(string folder, string pattern)
        {
            bool jsonOnly = !string.IsNullOrEmpty(pattern)
                && pattern.IndexOf(".json", StringComparison.OrdinalIgnoreCase) >= 0;

            Index loaded = Load();
            List<string> result = new List<string>();
            foreach (string name in loaded.names)
            {
                if (jsonOnly && !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(System.IO.Path.Combine(folder, name));
            }

            return result.ToArray();
        }

        /// <summary>仅供测试：清空全部关卡（也用于 RESET）。</summary>
        public static void ClearAll()
        {
            Index loaded = Load();
            foreach (string name in loaded.names)
            {
                PlayerPrefs.DeleteKey(FileKeyPrefix + name);
            }

            Store(new Index());
        }

        /// <summary>
        /// 仅供测试：丢掉内存里的索引缓存，下次从 PlayerPrefs 重新解析一遍 ——
        /// 模拟「关掉页面再打开」。持久化真出了问题时，这个测试会立刻红。
        /// </summary>
        public static void ReloadIndexForTests()
        {
            index = null;
        }
    }
}
