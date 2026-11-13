using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 浏览器（WebGL）存储链路测试。
    ///
    /// 真正的 WebGL 只能在浏览器里跑，但「关卡存在哪、怎么列出来、怎么删」这套逻辑
    /// 跟平台无关 —— <see cref="BrowserFileSystem.SetForcedForTests"/> 把编辑器也切到
    /// PlayerPrefs 后端，就能在这里把「建关 → 存 → 重进还在」整条链钉住，
    /// 不必每改一次都打一次包去点浏览器。
    ///
    /// 测试只碰 PlayerPrefs 里 <c>BitFlow.Fs.*</c> / <c>BitFlow.Save.*</c> 这些键，
    /// 不会写玩家真实的关卡目录。
    /// </summary>
    public class BrowserLevelStoreTests
    {
        const string LevelAssetPath = "Assets/Data/Levels/Level_01.asset";

        /// <summary>测试专用的文件名前缀，避免和真的 IMPORT_ / EXPORT_ 抢号。</summary>
        const string TestPrefix = "TestBrowser_";

        readonly List<LevelSO> loadedLevels = new List<LevelSO>();

        /// <summary>进测试之前是不是已经在浏览器模式（正常编辑器里是 false）。</summary>
        bool previousForced;

        [SetUp]
        public void SetUp()
        {
            previousForced = BrowserFileSystem.IsActive;
            BrowserFileSystem.SetForcedForTests(true);

            // 注册表是缓存单例：别的测试可能刷过，这里强制重新从 Resources 取一次
            LevelTypeRegistrySO.ClearCache();
            BrowserFileSystem.ClearAll();

            // 现有断言按「目录里只有我写的这几关」计数：内置关卡不许来搅局
            BundledLevels.SetSuppressedForTests(true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (LevelSO level in loadedLevels)
            {
                if (level != null) LevelJsonLibrary.DestroyLevel(level);
            }
            loadedLevels.Clear();

            BrowserFileSystem.ClearAll();
            BundledLevels.SetSuppressedForTests(false);
            BrowserFileSystem.SetForcedForTests(previousForced);
        }

        // ------------------------------------------------------------------ 目录语义

        [Test]
        public void BrowserMode_EnsureFolderIsNoOp_AndScanIsClean()
        {
            Assert.IsFalse(LevelJsonLibrary.HasLocalFiles, "强制浏览器模式下不该声称有本地文件");

            // WebGL 下这件事以前会直接抛 NotSupportedException（Directory.Exists 是 stub）
            Assert.DoesNotThrow(() => LevelJsonLibrary.EnsureFolder());

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);

            Assert.AreEqual(0, entries.Count, "还没存过关卡，列表应该是空的");
            Assert.AreEqual(0, problems.Count, "空存储不该被报成错误：" + string.Join("；", problems));
        }

        [Test]
        public void BrowserMode_DropFoldersAndShareFolderAreUnavailable()
        {
            Assert.AreEqual(0, LevelJsonLibrary.DropFolders().Count, "浏览器里没有玩家能丢文件的目录");

            Assert.IsFalse(LevelJsonLibrary.EnsureShareFolder(out string shareError), "浏览器没有分享目录");
            Assert.IsFalse(string.IsNullOrEmpty(shareError), "不可用时要给出原因，别让 UI 无话可说");

            string json = LevelJsonSerializer.ToJson(LoadAssetOrFail());
            Assert.IsFalse(LevelJsonLibrary.TryWriteShare(TestPrefix, json, out _, out string writeError));
            Assert.IsFalse(string.IsNullOrEmpty(writeError));
        }

        // ------------------------------------------------------------------ 建关 → 存 → 读回

        [Test]
        public void BrowserMode_SavedLevelShowsUpInScanAndLoadsBack()
        {
            LevelSO source = LoadAssetOrFail();
            string json = LevelJsonSerializer.ToJson(source);

            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string path, out string error),
                "浏览器模式下写关卡应该成功：" + error);
            StringAssert.Contains(TestPrefix + "01.json", path);

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);
            Assert.AreEqual(0, problems.Count, string.Join("；", problems));
            Assert.AreEqual(1, entries.Count, "存进去的关卡必须出现在选关列表里");

            LevelJsonEntry entry = entries[0];
            Assert.AreEqual(source.displayName, entry.DisplayName);
            Assert.AreEqual(source.width, entry.Width);
            Assert.AreEqual(source.height, entry.Height);
            Assert.IsTrue(LevelJsonLibrary.IsInsideLibrary(entry.Path),
                "浏览器存储里的关卡也必须算「关卡库成员」，否则工坊不让人编辑");

            Assert.IsTrue(LevelJsonLibrary.TryLoad(entry.Path, out LevelSO loaded, out List<string> loadProblems),
                "存进去的关卡要能读回来：" + string.Join("；", loadProblems));
            loadedLevels.Add(loaded);

            LevelDefinition expected = source.ToDefinition();
            LevelDefinition actual = loaded.ToDefinition();

            Assert.AreEqual(expected.Width, actual.Width);
            Assert.AreEqual(expected.Height, actual.Height);
            Assert.AreEqual(expected.Elements.Count, actual.Elements.Count, "元件总数必须一致（含墙）");
        }

        [Test]
        public void BrowserMode_WriteNewNeverOverwritesAnExistingName()
        {
            string json = LevelJsonSerializer.ToJson(LoadAssetOrFail());

            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string first, out _));
            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string second, out _));

            Assert.AreNotEqual(first, second, "同名写入必须顺延到下一个号，不能覆盖旧关卡");
            Assert.AreEqual(2, LevelJsonLibrary.Scan(out _).Count);
        }

        [Test]
        public void BrowserMode_DeleteRemovesTheLevelForGood()
        {
            string json = LevelJsonSerializer.ToJson(LoadAssetOrFail());
            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string path, out _));

            Assert.IsTrue(LevelJsonLibrary.TryDelete(path, out string error), error);

            Assert.AreEqual(0, LevelJsonLibrary.Scan(out _).Count, "删掉之后不该还在列表里");
            Assert.IsFalse(LevelJsonLibrary.TryLoad(path, out _, out _), "删掉之后不该还能读到");
        }

        [Test]
        public void BrowserMode_LevelIndexSurvivesAReload()
        {
            // 「重进游戏还在」的关键：索引本身落的是 PlayerPrefs，不是内存里的一张表。
            // 丢掉内存缓存再读一次，模拟重新打开页面。
            string json = LevelJsonSerializer.ToJson(LoadAssetOrFail());
            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string path, out _));

            BrowserFileSystem.ReloadIndexForTests();

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);
            Assert.AreEqual(0, problems.Count, string.Join("；", problems));
            Assert.AreEqual(1, entries.Count, "重新加载后关卡必须还在");
            Assert.AreEqual(path, entries[0].Path);
        }

        // ------------------------------------------------------------------ 存档 / 草稿

        [Test]
        public void BrowserMode_SaveStoreWritesToPlayerPrefs()
        {
            const string key = "BitFlow.Save.save_editor.json";   // 编辑器档；打包后是 save.json

            SaveData data = SaveStore.Data;
            int original = data.audioVolumePercent;

            try
            {
                data.audioVolumePercent = 42;
                SaveStore.Save();

                Assert.IsTrue(PlayerPrefs.HasKey(key), "WebGL 下存档必须落 PlayerPrefs（浏览器里就是 IndexedDB）");
                StringAssert.Contains("42", PlayerPrefs.GetString(key, string.Empty));
            }
            finally
            {
                // 内存和存储都还原，别给后面的测试留一份 42% 的音量
                data.audioVolumePercent = original;
                SaveStore.Save();
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void BrowserMode_DraftExistsDoesNotThrow()
        {
            // 以前这里直接调 File.Exists 而 WebGL 的 System.IO 是 stub，一碰就抛
            Assert.DoesNotThrow(() => LevelDraftSerializer.Exists());
            Assert.IsFalse(LevelDraftSerializer.Exists(), "还没存过草稿，应该是 false");
        }

        // ------------------------------------------------------------------ helpers

        static LevelSO LoadAssetOrFail()
        {
            LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(level, "缺少关卡资产：" + LevelAssetPath);
            return level;
        }
    }
}
