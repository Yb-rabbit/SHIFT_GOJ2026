using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 内置关卡包（<c>Assets/Resources/BitFlow/Levels</c>）与作者源（<c>LevelJson/</c> + <c>incoming/</c>）的对账。
    ///
    /// 为什么要有这层测试：包内 JSON 是「生成物但必须提交」，最容易出的两种事故都不会当场报错 ——
    /// 源里加了关忘了跑「生成内置关卡包」（包是旧的）、或者包里躺着一份源里已经删掉的文件
    /// （要等下次打包才清掉，而在那之前它已经跟着这一版进包了）。两种都只在玩家那边才发现，这里提前拦。
    ///
    /// 用工程相对路径而不是 <c>AssetDatabase</c>：这层只关心文件系统，EditMode 测试的 CWD 就是工程根。
    /// </summary>
    public class BundledLevelsSourceTests
    {
        const string BundledFolder = "Assets/Resources/BitFlow/Levels";

        /// <summary>和 <c>LevelSeedSources.Folders</c> 保持一致：根目录优先，exported/ 不参与。</summary>
        static readonly string[] SourceFolders = { "LevelJson", "LevelJson/incoming" };

        static readonly string[] NotLevels = { "catalog.json", "typeCatalog.json" };

        [Test]
        public void EveryBundledLevel_ComesFromAnAuthorSourceFile()
        {
            List<string> bundled = JsonNames(BundledFolder);
            Assert.IsNotEmpty(bundled, "内置关卡包是空的 —— 先跑 SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包");

            HashSet<string> sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in SourceFolders)
            {
                foreach (string name in JsonNames(folder)) sources.Add(name);
            }

            List<string> orphans = new List<string>();
            foreach (string name in bundled)
            {
                if (!sources.Contains(name)) orphans.Add(name);
            }

            Assert.IsEmpty(orphans,
                "这些文件在包里，但 LevelJson/ 与 incoming/ 里都没有同名源 —— 改源也到不了它们，"
                + "下次打包还会被清掉。要么把源补回来，要么删掉包里的副本：\n" + string.Join("\n", orphans));
        }

        [Test]
        public void EveryBundledLevel_ParsesWithTheRuntimeParser()
        {
            List<string> bundled = JsonNames(BundledFolder);
            Assert.IsNotEmpty(bundled, "内置关卡包是空的 —— 先跑 SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包");

            List<string> broken = new List<string>();
            foreach (string name in bundled)
            {
                // 和出包、进关、导入用的是同一个 Parse —— 这里通过 = 玩家读到的是同一份判定
                string json = File.ReadAllText(Path.Combine(BundledFolder, name));
                if (!LevelJsonLibrary.TryParseJson(json, out LevelSO probe, out List<string> problems))
                {
                    broken.Add(name + "：" + string.Join("；", problems));
                    continue;
                }

                LevelJsonLibrary.DestroyLevel(probe);
            }

            Assert.IsEmpty(broken,
                "包里有读不通的关卡 —— 玩家进这一关会直接停在面板上，而出包时不会给任何提示：\n"
                + string.Join("\n", broken));
        }

        static List<string> JsonNames(string folder)
        {
            List<string> names = new List<string>();
            string absolute = Path.GetFullPath(folder);
            if (!Directory.Exists(absolute)) return names;

            foreach (string file in Directory.GetFiles(absolute, "*.json"))
            {
                string name = Path.GetFileName(file);

                // 下划线开头 = 模板 / 草稿；保留名 = 顺序表 / 类型快照
                if (name.StartsWith("_", StringComparison.Ordinal)) continue;

                bool reserved = false;
                foreach (string skip in NotLevels)
                {
                    if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase))
                    {
                        reserved = true;
                        break;
                    }
                }

                if (!reserved) names.Add(name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
