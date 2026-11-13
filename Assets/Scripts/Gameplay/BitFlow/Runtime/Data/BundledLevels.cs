using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 打包进来的「作者关卡」：首次启动时用<b>和玩家导入完全相同的那份写盘代码</b>
    /// （<see cref="LevelJsonLibrary.WriteAllText"/>）放进同一个关卡目录。
    ///
    /// 为什么要这一层：运行时只有 <see cref="LevelJsonLibrary"/> 一个入口，它只认
    /// <c>persistentDataPath/LevelJson</c> 里的普通文件（浏览器里那张表由
    /// <see cref="BrowserFileSystem"/> 落到 PlayerPrefs / IndexedDB）。所以「预放入」不该另开一条读路径 ——
    /// 只要把它们变成同一个目录里的同样文件，选关面板、工坊、编辑、删除、导出就全都自动一致。
    ///
    /// 覆盖规则（清单记着「上次播种时的内容」，见 <see cref="ManifestKey"/>）：
    /// <list type="bullet">
    /// <item>本地没有、清单里也没有 → 播种；</item>
    /// <item>本地没有、清单里有 → 玩家自己删的，<b>不复活</b>；</item>
    /// <item>本地内容 == 包内内容 → 已经是最新，什么都不做；</item>
    /// <item>本地内容 == 上次播种的内容（没人动过） → 跟着新版本覆盖；</item>
    /// <item>本地内容被改过（或玩家自己导入过同名关） → <b>玩家赢</b>，一个字节都不动。</item>
    /// </list>
    ///
    /// 一句话：作者改版能到老玩家手里，但玩家的改动和删除永远优先。
    /// </summary>
    public static class BundledLevels
    {
        /// <summary>关卡包在 Resources 里的路径（编辑器侧由 <c>LevelSeedBundler</c> 生成）。</summary>
        public const string ResourceFolder = "BitFlow/Levels";

        /// <summary>播种清单的键：文件名 → 上次播种时的内容摘要（测试与排障会读它）。</summary>
        public const string ManifestKey = "BitFlow.Seeds.Manifest";

        /// <summary>这次进程里是不是已经播过种。失败也算「试过了」，免得每次开面板刷一屏日志。</summary>
        static bool installed;

        /// <summary>测试用：关掉自动播种（<see cref="LevelJsonLibrary.EnsureFolder"/> 里那一句）。</summary>
        static bool suppressed;

        /// <summary>
        /// 播一次种，返回本次真正写入的文件数。<b>整个流程不抛异常</b>：
        /// 内置关卡出问题最多是「列表里少几关」，不能让「打开选关面板」跟着炸。
        /// </summary>
        public static int InstallIfNeeded()
        {
            if (suppressed || installed) return 0;

            installed = true;

            try
            {
                return Install(LoadSeeds());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 内置关卡播种失败：" + exception.Message);
                return 0;
            }
        }

        /// <summary>
        /// Resources 里的关卡包 → 「文件名 → 原文」，按文件名排序（<c>LoadAll</c> 的顺序不保证稳定，
        /// 而清单要能逐字节比较，顺序必须定下来）。<c>TextAsset.name</c> 不带后缀，这里补齐 <c>.json</c>。
        /// </summary>
        static List<KeyValuePair<string, string>> LoadSeeds()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            Array.Sort(assets, (left, right) => string.CompareOrdinal(left.name, right.name));

            List<KeyValuePair<string, string>> seeds = new List<KeyValuePair<string, string>>(assets.Length);
            foreach (TextAsset asset in assets)
            {
                if (asset == null || string.IsNullOrEmpty(asset.name)) continue;
                seeds.Add(new KeyValuePair<string, string>(asset.name + ".json", asset.text));
            }

            return seeds;
        }

        /// <summary>
        /// 把一批种子（文件名 → 原文）播进关卡目录，返回本次真正写入的文件数。
        ///
        /// 生产路径是 <see cref="InstallIfNeeded"/>（种子来自 Resources）；测试直接调这个喂假数据，
        /// 于是「覆盖 / 不覆盖 / 不复活」这些规则可以在不碰真实打包内容的情况下逐条钉死。
        /// </summary>
        public static int Install(IEnumerable<KeyValuePair<string, string>> seeds)
        {
            if (seeds == null) return 0;

            Manifest previous = LoadManifest();
            Dictionary<string, string> seeded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in previous.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name)) continue;
                seeded[entry.name] = entry.hash ?? string.Empty;
            }

            // 新清单只留「这一版包里还有」的名字：从包里删掉的关卡不再保留墓碑，
            // 哪天又放回来就当一关新关卡重新播种。
            Manifest next = new Manifest();
            int written = 0;

            foreach (KeyValuePair<string, string> seed in seeds)
            {
                string text = seed.Value;
                if (string.IsNullOrEmpty(seed.Key) || text == null) continue;

                string fileName = System.IO.Path.GetFileName(seed.Key);
                if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";
                if (IsReservedName(fileName))
                {
                    Debug.LogWarning("[BitFlow] 内置关卡包里有不该出现的文件名，跳过：" + fileName);
                    continue;
                }

                next.entries.Add(new Entry { name = fileName, hash = HashOf(text) });

                string path = System.IO.Path.Combine(LevelJsonLibrary.Folder, fileName);

                if (!LevelJsonLibrary.FileExists(path))
                {
                    // 清单里有这个名字 = 玩家把它删了：删了就是不要了，不复活
                    if (seeded.ContainsKey(fileName)) continue;
                    if (Write(path, text)) written++;
                    continue;
                }

                string current;
                try
                {
                    current = LevelJsonLibrary.ReadAllText(path);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[BitFlow] 内置关卡读不了，先跳过：" + fileName + "，" + exception.Message);
                    continue;
                }

                // 已经是包里这份内容：什么都不用做
                if (string.Equals(current, text, StringComparison.Ordinal)) continue;

                // 本地仍是上次播种的那一份（没人动过）→ 跟着新版本更新
                if (seeded.TryGetValue(fileName, out string planted)
                    && string.Equals(current, planted, StringComparison.Ordinal))
                {
                    if (Write(path, text)) written++;
                    continue;
                }

                // 剩下的都是玩家自己的东西（改过的关、或者他自己导入的同名关）：玩家赢
                Debug.Log("[BitFlow] 内置关卡保留了玩家自己的版本：" + fileName);
            }

            if (next.entries.Count > 0 || previous.entries.Count > 0) SaveManifest(next);
            return written;
        }

        // ------------------------------------------------------------------ 明细

        /// <summary>写盘失败只记一条日志：少一关内置关卡不该影响整个列表。</summary>
        static bool Write(string path, string text)
        {
            try
            {
                LevelJsonLibrary.WriteAllText(path, text);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 内置关卡写不进关卡目录："
                    + System.IO.Path.GetFileName(path) + "，" + exception.Message);
                return false;
            }
        }

        /// <summary>和 <see cref="LevelJsonLibrary.Scan"/> 一样的保留名规则：这类文件放进列表里也看不见。</summary>
        static bool IsReservedName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            if (fileName.StartsWith("_", StringComparison.Ordinal)) return true;
            return string.Equals(fileName, "catalog.json", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ 清单

        /// <summary>
        /// 播种清单：名字 + 上次播种时的内容摘要。
        /// 只记「我们放进去的是什么」，不记玩家后来改成什么 —— 比对时才能一眼看出是谁的版本。
        /// </summary>
        [Serializable]
        sealed class Manifest
        {
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        sealed class Entry
        {
            public string name;
            public string hash;
        }

        static Manifest LoadManifest()
        {
            string raw = PlayerPrefs.GetString(ManifestKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return new Manifest();

            try
            {
                Manifest parsed = JsonUtility.FromJson<Manifest>(raw);
                if (parsed == null) return new Manifest();

                // JsonUtility 走 FormatterServices，不走构造函数：字段可能是 null
                if (parsed.entries == null) parsed.entries = new List<Entry>();
                return parsed;
            }
            catch (Exception exception)
            {
                // 清单坏了就当没播过种：宁可多播一次（本地已有文件时玩家仍然赢），也不要每次开面板都抛
                Debug.LogWarning("[BitFlow] 内置关卡清单损坏，按「没播过种」处理：" + exception.Message);
                return new Manifest();
            }
        }

        static void SaveManifest(Manifest manifest)
        {
            try
            {
                PlayerPrefs.SetString(ManifestKey, JsonUtility.ToJson(manifest));
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 内置关卡清单写不进去，下次启动会重新判断：" + exception.Message);
            }
        }

        /// <summary>
        /// 内容摘要（FNV-1a 32 位）。用途只有一个：判断「本地这份是不是我上次放的那份」。
        /// 特意不用 <c>System.Security.Cryptography</c>：WebGL / IL2CPP 上没必要、也更容易出岔子。
        /// </summary>
        static string HashOf(string text)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }

            return hash.ToString("x8");
        }

        // ------------------------------------------------------------------ 测试钩子

        /// <summary>
        /// 测试用：<c>true</c> = 关掉自动播种（现有测试断言的是「目录里只有我写的这几关」，
        /// 不能被包里的作者关卡搅乱，也不该往开发机的真实关卡目录里塞东西）。
        /// </summary>
        public static void SetSuppressedForTests(bool value)
        {
            suppressed = value;
            installed = false;
        }

        /// <summary>测试用：清掉清单并忘掉「这个进程已经播过种」，让每个用例从零开始。</summary>
        public static void ClearForTests()
        {
            PlayerPrefs.DeleteKey(ManifestKey);
            PlayerPrefs.Save();
            installed = false;
        }
    }
}
