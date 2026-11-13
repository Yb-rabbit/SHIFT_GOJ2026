using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 内置关卡（打包里的作者关卡）播种测试。
    ///
    /// 这里钉的是「谁赢」的规则：作者改版要能到老玩家手里，玩家的改动和删除永远优先。
    /// 用 <see cref="BrowserFileSystem.SetForcedForTests"/> 把存储切到 PlayerPrefs，
    /// 于是测试既不碰开发机真实的关卡目录，也不必打一次 WebGL 包去浏览器里点。
    /// 种子是测试自己喂的（<see cref="BundledLevels.Install"/>），不依赖 Resources 里正好有哪些关。
    /// </summary>
    public class BundledLevelsTests
    {
        /// <summary>测试专用的文件名前缀：万一真被写进真实目录，也好认、好删。</summary>
        const string Prefix = "BundledTest_";

        bool previousForced;

        [SetUp]
        public void SetUp()
        {
            previousForced = BrowserFileSystem.IsActive;
            BrowserFileSystem.SetForcedForTests(true);
            BrowserFileSystem.ClearAll();

            // 默认关掉自动播种：这些用例断言的是「只有我这几关」，不能被包里实际内容搅乱
            BundledLevels.SetSuppressedForTests(true);
            BundledLevels.ClearForTests();

            // 注册表是缓存单例：别的测试可能刷过，这里强制重新从 Resources 取一次
            LevelTypeRegistrySO.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            BrowserFileSystem.ClearAll();
            BundledLevels.ClearForTests();
            BundledLevels.SetSuppressedForTests(false);
            BrowserFileSystem.SetForcedForTests(previousForced);
        }

        // ------------------------------------------------------------------ 播种

        [Test]
        public void FirstInstall_WritesSeedsVerbatim()
        {
            string json = "{\"displayName\":\"AUTHOR ONE\"}";

            Assert.AreEqual(1, BundledLevels.Install(Seed(Prefix + "A.json", json)), "第一次装应该写一个文件");

            string path = PathOf(Prefix + "A.json");
            Assert.IsTrue(LevelJsonLibrary.FileExists(path), "种子应该落在关卡目录里：" + path);
            Assert.AreEqual(json, LevelJsonLibrary.ReadAllText(path), "播种是原文照抄，不许重新序列化");
        }

        [Test]
        public void ReservedNames_AreNeverSeeded()
        {
            List<KeyValuePair<string, string>> seeds = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("_draft.json", "{}"),
                new KeyValuePair<string, string>("catalog.json", "{}")
            };

            Assert.AreEqual(0, BundledLevels.Install(seeds), "下划线草稿和目录索引不该被播种");
            Assert.IsFalse(LevelJsonLibrary.FileExists(PathOf("_draft.json")));
            Assert.IsFalse(LevelJsonLibrary.FileExists(PathOf("catalog.json")));
        }

        // ------------------------------------------------------------------ 谁赢

        [Test]
        public void UpdatedBundle_OverwritesCopyNobodyTouched()
        {
            string name = Prefix + "Follow.json";

            BundledLevels.Install(Seed(name, "{\"v\":1}"));
            Assert.AreEqual(1, BundledLevels.Install(Seed(name, "{\"v\":2}")), "没人动过的关应该跟着新版本更新");

            Assert.AreEqual("{\"v\":2}", LevelJsonLibrary.ReadAllText(PathOf(name)));
        }

        [Test]
        public void UpdatedBundle_NeverOverwritesPlayerEdits()
        {
            string name = Prefix + "Mine.json";

            BundledLevels.Install(Seed(name, "{\"v\":1}"));
            LevelJsonLibrary.WriteAllText(PathOf(name), "{\"v\":\"MY EDIT\"}");

            Assert.AreEqual(0, BundledLevels.Install(Seed(name, "{\"v\":2}")), "玩家改过的关不该被覆盖");
            Assert.AreEqual("{\"v\":\"MY EDIT\"}", LevelJsonLibrary.ReadAllText(PathOf(name)), "玩家的版本永远赢");
        }

        [Test]
        public void DeletedLevel_DoesNotComeBack()
        {
            string name = Prefix + "Gone.json";

            BundledLevels.Install(Seed(name, "{\"v\":1}"));
            string path = PathOf(name);

            LevelJsonLibrary.DeleteFile(path);
            Assert.IsFalse(LevelJsonLibrary.FileExists(path), "测试自己先把它删掉");

            Assert.AreEqual(0, BundledLevels.Install(Seed(name, "{\"v\":1}")), "玩家删掉的关不该复活");
            Assert.IsFalse(LevelJsonLibrary.FileExists(path), "删了就是不要了");
        }

        [Test]
        public void LevelRemovedFromBundle_IsForgotten_SoItCanComeBackLater()
        {
            string kept = Prefix + "Kept.json";
            string dropped = Prefix + "Dropped.json";

            BundledLevels.Install(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(kept, "{\"v\":1}"),
                new KeyValuePair<string, string>(dropped, "{\"v\":1}")
            });
            LevelJsonLibrary.DeleteFile(PathOf(dropped));

            // 这一版包里没有 Dropped 了 → 清单里连墓碑一起忘掉（否则它会「永远不复活」）
            BundledLevels.Install(Seed(kept, "{\"v\":1}"));

            Assert.AreEqual(1, BundledLevels.Install(Seed(dropped, "{\"v\":1}")),
                "从包里删掉又放回来的关卡，应该当新关卡重新播种");
            Assert.IsTrue(LevelJsonLibrary.FileExists(PathOf(dropped)));
        }

        [Test]
        public void BrokenManifest_IsTreatedAsEmpty_AndPlayerStillWins()
        {
            string name = Prefix + "Corrupt.json";
            string path = PathOf(name);
            LevelJsonLibrary.WriteAllText(path, "{\"v\":\"MY FILE\"}");

            // 清单坏了（升级、手改、写到一半断电）：不许抛，也不许把玩家的文件当成「我上次放的」
            PlayerPrefs.SetString(BundledLevels.ManifestKey, "{ this is not json");
            PlayerPrefs.Save();

            int written = 0;
            Assert.DoesNotThrow(() => written = BundledLevels.Install(Seed(name, "{\"v\":2}")));
            Assert.AreEqual(0, written);
            Assert.AreEqual("{\"v\":\"MY FILE\"}", LevelJsonLibrary.ReadAllText(path), "清单坏了也不该动玩家的文件");
        }

        // ------------------------------------------------------------------ 自动播种

        [Test]
        public void InstallIfNeeded_IsSuppressedInTests()
        {
            // SetUp 已经关掉了自动播种：面板 / 测试里 EnsureFolder 不该偷偷改存储
            Assert.AreEqual(0, BundledLevels.InstallIfNeeded());
        }

        [Test]
        public void SeededLevel_ShowsUpInTheSameListAsPlayerLevels()
        {
            string sample = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "LevelJson", "exported", "Level_01.json"));
            if (!File.Exists(sample))
            {
                Assert.Ignore("没有 LevelJson/exported/Level_01.json，跳过：这里要用一份真关卡验证「进同一个列表」");
            }

            string name = Prefix + "Scanned.json";
            Assert.AreEqual(1, BundledLevels.Install(Seed(name, File.ReadAllText(sample))));

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);

            LevelJsonEntry found = null;
            foreach (LevelJsonEntry entry in entries)
            {
                if (entry.Path == PathOf(name)) found = entry;
            }

            Assert.IsNotNull(found, "播种的关卡必须和玩家自己造的关出现在同一份扫描结果里："
                + string.Join("；", problems));
            Assert.Greater(found.Width, 0);
            Assert.Greater(found.Height, 0);
        }

        // ------------------------------------------------------------------ 工具

        static string PathOf(string fileName)
        {
            return Path.Combine(LevelJsonLibrary.Folder, fileName);
        }

        static List<KeyValuePair<string, string>> Seed(string fileName, string json)
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(fileName, json)
            };
        }
    }
}
