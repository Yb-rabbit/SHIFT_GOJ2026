using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 运行时 JSON 关卡链路测试：<see cref="LevelTypeRegistrySO"/> 能不能供上类型、
    /// <see cref="LevelJsonLibrary"/> 能不能把磁盘上的 JSON 读成一关、读出来的关能不能真的跑通。
    ///
    /// 这里不走编辑器导入工具（那是 Assembly-CSharp-Editor），断言的就是<b>打包后会跑的那段代码</b>：
    /// 编辑器 Play / EditMode / Player 三者共用同一条路径，测试能把这条路径钉住。
    ///
    /// 测试会往 persistentDataPath/LevelJson 里写临时文件（前缀 <c>TestJson_</c>），TearDown 一律删干净，
    /// 不会碰到玩家自己扔进去的关卡。
    /// </summary>
    public class LevelJsonLibraryTests
    {
        const string LevelAssetPath = "Assets/Data/Levels/Level_01.asset";
        const string FilePrefix = "TestJson_";

        readonly List<LevelSO> loadedLevels = new List<LevelSO>();
        readonly List<string> writtenFiles = new List<string>();

        [SetUp]
        public void SetUp()
        {
            // 注册表是缓存单例：别的测试或菜单可能刷过，这里强制重新从 Resources 取一次
            LevelTypeRegistrySO.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (LevelSO level in loadedLevels)
            {
                if (level != null) LevelJsonLibrary.DestroyLevel(level);
            }
            loadedLevels.Clear();

            foreach (string file in writtenFiles)
            {
                if (File.Exists(file)) File.Delete(file);
            }
            writtenFiles.Clear();
        }

        // ------------------------------------------------------------------ 类型注册表

        [Test]
        public void Registry_SuppliesEveryKindUsedByJson()
        {
            LevelTypeRegistrySO registry = LoadRegistryOrFail();

            Assert.Greater(registry.Count, 0, "注册表里一个类型都没有");

            HashSet<CellKind> kinds = new HashSet<CellKind>();
            foreach (ElementTypeSO type in registry.Types)
            {
                if (type == null) continue;
                kinds.Add(LevelTypeRegistrySO.KindOf(type));
            }

            Assert.IsTrue(kinds.Contains(CellKind.Emitter), "注册表里没有发射器");
            Assert.IsTrue(kinds.Contains(CellKind.Receiver), "注册表里没有接收器");
            Assert.IsTrue(kinds.Contains(CellKind.Splitter), "注册表里没有分流器");
            Assert.IsTrue(kinds.Contains(CellKind.NotGate), "注册表里没有非门");
            Assert.IsTrue(kinds.Contains(CellKind.Filter), "注册表里没有过滤器");

            Assert.IsNotNull(registry.FindWall(null), "注册表里没有墙类型：JSON 的 wallRows 会整层丢掉");
        }

        // ------------------------------------------------------------------ 读文件成关卡

        [Test]
        public void TryLoad_ExportedLevel_ProducesSameDefinitionAsAsset()
        {
            LevelSO source = LoadAssetOrFail();
            string json = LevelJsonSerializer.ToJson(source);
            string path = WriteLevelJson("Exported.json", json);

            Assert.IsTrue(LevelJsonLibrary.TryLoad(path, out LevelSO loaded, out List<string> problems),
                "导出的 JSON 应该能被运行时读进来：" + string.Join("；", problems));
            loadedLevels.Add(loaded);

            LevelDefinition expected = source.ToDefinition();
            LevelDefinition actual = loaded.ToDefinition();

            Assert.AreEqual(expected.Width, actual.Width);
            Assert.AreEqual(expected.Height, actual.Height);
            Assert.AreEqual(expected.Elements.Count, actual.Elements.Count, "元件总数必须一致（含墙）");
            Assert.AreEqual(source.displayName, loaded.displayName);
        }

        [Test]
        public void LoadedJsonLevel_SolvesLikeTheOriginalAsset()
        {
            // 读进来的关卡要能直接喂给模拟核心：这条断言把「JSON → 关卡 → 能玩」串成一根线
            string json = LevelJsonSerializer.ToJson(LoadAssetOrFail());
            string path = WriteLevelJson("Playable.json", json);

            Assert.IsTrue(LevelJsonLibrary.TryLoad(path, out LevelSO loaded, out List<string> problems),
                "读不进关卡：" + string.Join("；", problems));
            loadedLevels.Add(loaded);

            BitFlowSimulation simulation = new BitFlowSimulation(loaded.ToDefinition());
            for (int i = 0; i < 200 && !simulation.IsFinished; i++) simulation.Step();

            Assert.IsTrue(simulation.IsFinished, "发射器吐完后场上应无残留位");
            Assert.LessOrEqual(simulation.TickIndex, DemoLevels.ExpectedMaxTicks,
                "JSON 版的演示关应在 " + DemoLevels.ExpectedMaxTicks + " tick 内结算，实际 " + simulation.TickIndex);

            LevelResult result = simulation.BuildResult();
            Assert.IsTrue(result.IsSuccess, "JSON 版的演示关应通关：" + result.Summary());
        }

        [Test]
        public void TryLoad_UnknownType_ReportsErrorInsteadOfHalfLevel()
        {
            string json = "{\n"
                + "  \"schema\": 1,\n"
                + "  \"displayName\": \"BROKEN\",\n"
                + "  \"width\": 4,\n"
                + "  \"height\": 4,\n"
                + "  \"elements\": [\n"
                + "    { \"x\": 1, \"y\": 1, \"type\": \"NoSuchType_AssertedMissing\" }\n"
                + "  ]\n"
                + "}\n";

            string path = WriteLevelJson("Broken.json", json);

            bool ok = LevelJsonLibrary.TryLoad(path, out LevelSO level, out List<string> problems);
            if (level != null) loadedLevels.Add(level);

            Assert.IsFalse(ok, "断链的 type 不该被当成读成功");
            Assert.IsNull(level, "失败时不该交出半成品关卡");
            Assert.IsNotEmpty(problems, "失败必须说明原因，否则面板上只会看到一行「读不出」");
        }

        [Test]
        public void TryLoad_MissingFile_FailsQuietly()
        {
            string path = Path.Combine(LevelJsonLibrary.Folder, FilePrefix + "NotThere.json");

            bool ok = LevelJsonLibrary.TryLoad(path, out LevelSO level, out List<string> problems);

            Assert.IsFalse(ok);
            Assert.IsNull(level);
            Assert.IsNotEmpty(problems);
        }

        // ------------------------------------------------------------------ 面板列表

        [Test]
        public void Scan_ListsReadableFiles_AndSkipsUnderscoreDrafts()
        {
            string valid = WriteLevelJson("Scannable.json", LevelJsonSerializer.ToJson(LoadAssetOrFail()));
            string fileName = Path.GetFileName(valid);

            // 下划线开头的是草稿/模板：就算内容是坏的，也不该被当成「问题」刷到面板上
            WriteLevelJson("_draft.json", "这不是合法 JSON，但下划线开头就不该被看见");

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);

            LevelJsonEntry entry = null;
            foreach (LevelJsonEntry candidate in entries)
            {
                if (candidate.Path == valid) entry = candidate;
            }

            Assert.IsNotNull(entry, "扫描结果里应包含刚写的关卡文件");
            Assert.IsTrue(entry.Subtitle.StartsWith(fileName, System.StringComparison.Ordinal),
                "副标题要以文件名为开头（面板靠它区分同名关卡），实际：" + entry.Subtitle);
            Assert.Greater(entry.Width, 0);
            Assert.Greater(entry.Height, 0);

            foreach (string problem in problems)
            {
                Assert.IsFalse(problem != null && problem.Contains(fileName),
                    "能读通的关卡不该出现在问题清单里：" + problem);
            }
        }

        [Test]
        public void Scan_ReportsBrokenFile_WithoutThrowing()
        {
            string broken = WriteLevelJson("Garbage.json", "not a level at all");
            string fileName = Path.GetFileName(broken);

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);

            foreach (LevelJsonEntry entry in entries)
            {
                Assert.AreNotEqual(broken, entry.Path, "读不通的文件不该出现在列表里");
            }

            bool mentioned = false;
            foreach (string problem in problems)
            {
                if (problem != null && problem.Contains(fileName)) mentioned = true;
            }
            Assert.IsTrue(mentioned, "问题清单里要点名坏文件：" + string.Join("；", problems));
        }

        // ------------------------------------------------------------------ 小工具

        static LevelTypeRegistrySO LoadRegistryOrFail()
        {
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            Assert.IsNotNull(registry, "缺少 Resources/" + LevelTypeRegistrySO.ResourcePath
                + ".asset —— 先跑菜单 SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表");
            return registry;
        }

        static LevelSO LoadAssetOrFail()
        {
            LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(level, "缺少关卡资产：" + LevelAssetPath);
            return level;
        }

        /// <summary>写到运行时关卡目录里，登记进待删清单（TearDown 负责删）。</summary>
        string WriteLevelJson(string fileName, string json)
        {
            LevelJsonLibrary.EnsureFolder();
            string path = Path.Combine(LevelJsonLibrary.Folder, FilePrefix + fileName);
            File.WriteAllText(path, json);
            writtenFiles.Add(path);
            return path;
        }
    }
}
