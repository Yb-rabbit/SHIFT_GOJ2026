using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;
using ShiftGo.BitFlow.View;

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

        /// <summary>
        /// <see cref="LevelJsonLibrary.TryWriteNew"/> 的前缀。用测试专用的名字，
        /// 免得跟玩家真的 IMPORT_/EXPORT_ 文件抢号、也不会误删它们。
        /// </summary>
        const string TestPrefix = "TestWrite_";

        readonly List<LevelSO> loadedLevels = new List<LevelSO>();
        readonly List<string> writtenFiles = new List<string>();

        /// <summary>写在关卡目录**之外**的临时文件（模拟「玩家从下载目录挑一个 json」）。</summary>
        readonly List<string> externalFiles = new List<string>();

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

            foreach (string file in externalFiles)
            {
                if (File.Exists(file)) File.Delete(file);
            }
            externalFiles.Clear();
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

        // ------------------------------------------------------------------ 导入 / 导出

        /// <summary>
        /// <see cref="LevelJsonLibrary.TryWriteNew"/> 的两条硬要求：<b>原文照抄</b>（不重新序列化）、
        /// <b>永不覆盖</b>（第二次调用必须换个号）。玩家导入别人的关卡时，这两条决定了他
        /// 看到的东西和收到的东西是不是同一份、以及会不会把自己的旧关卡冲掉。
        /// </summary>
        [Test]
        public void TryWriteNew_WritesTextVerbatim_AndNeverReusesAName()
        {
            // 故意写成「不规范」的样子：缩进、键顺序、行尾注释风格的空白都要原样留下
            string json = "{\"displayName\":\"HANDWRITTEN\",   \"width\":4,\n    \"height\":4}";

            LevelJsonLibrary.EnsureFolder();

            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string first, out string error),
                "写不进关卡目录：" + error);
            writtenFiles.Add(first);

            Assert.IsTrue(File.Exists(first), "写完应该有文件：" + first);
            Assert.AreEqual(json, File.ReadAllText(first), "落盘的必须是原文，不能是重新序列化的结果");
            Assert.IsTrue(Path.GetFileName(first).StartsWith(TestPrefix, System.StringComparison.Ordinal),
                "文件名要带上前缀：" + Path.GetFileName(first));

            Assert.IsTrue(LevelJsonLibrary.TryWriteNew(TestPrefix, json, out string second, out string error2),
                "第二次写应该换个文件名成功：" + error2);
            writtenFiles.Add(second);

            Assert.AreNotEqual(first, second, "同名文件绝不能被覆盖");
            Assert.AreEqual(json, File.ReadAllText(first), "第二次写入不该动到第一个文件");
            Assert.AreEqual(json, File.ReadAllText(second));
        }

        [Test]
        public void TryWriteNew_WithoutContent_FailsInsteadOfWritingEmptyFile()
        {
            bool ok = LevelJsonLibrary.TryWriteNew(TestPrefix, null, out string path, out string error);

            Assert.IsFalse(ok, "没有内容就不该写文件");
            Assert.IsNull(path);
            Assert.IsFalse(string.IsNullOrEmpty(error), "失败要说原因");
        }

        /// <summary>
        /// 剪贴板导入用的判定：<see cref="LevelJsonLibrary.TryParseJson"/> 不落盘，
        /// 但必须和「从文件读」得出同一关 —— 两边共用 <c>Parse</c>，这条测试钉住这一点。
        /// </summary>
        [Test]
        public void TryParseJson_RoundTripsAnExportedLevel()
        {
            LevelSO source = LoadAssetOrFail();
            string json = LevelJsonSerializer.ToJson(source);

            Assert.IsTrue(LevelJsonLibrary.TryParseJson(json, out LevelSO parsed, out List<string> problems),
                "导出的 JSON 应该能被剪贴板导入接受：" + string.Join("；", problems));
            loadedLevels.Add(parsed);

            LevelDefinition expected = source.ToDefinition();
            LevelDefinition actual = parsed.ToDefinition();

            Assert.AreEqual(expected.Width, actual.Width);
            Assert.AreEqual(expected.Height, actual.Height);
            Assert.AreEqual(expected.Elements.Count, actual.Elements.Count, "元件总数必须一致（含墙）");
            Assert.AreEqual(source.displayName, parsed.displayName);
        }

        [Test]
        public void TryParseJson_RejectsGarbageWithoutProducingALevel()
        {
            Assert.IsFalse(LevelJsonLibrary.TryParseJson("not a level at all", out LevelSO garbage, out List<string> problems),
                "读不通的文本不该被当成关卡");
            Assert.IsNull(garbage, "失败时不该交出半成品");
            Assert.IsNotEmpty(problems, "失败要说明原因，否则面板上只有一行「NOT A LEVEL」");

            Assert.IsFalse(LevelJsonLibrary.TryParseJson(string.Empty, out LevelSO empty, out List<string> emptyProblems),
                "空文本不该被当成关卡");
            Assert.IsNull(empty);
            Assert.IsNotEmpty(emptyProblems);
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

        // ------------------------------------------------------------------ 外部 JSON 导入

        [Test]
        public void TryImportFile_CopiesExternalJsonIntoLibrary()
        {
            string external = WriteExternalJson("Outside.json", LevelJsonSerializer.ToJson(LoadAssetOrFail()));

            Assert.AreEqual(ImportOutcome.Imported,
                LevelJsonLibrary.TryImportFile(external, false, out string imported, out string error),
                "外部 json 应该能导进来：" + error);

            Assert.IsNotNull(imported);
            writtenFiles.Add(imported);

            Assert.IsTrue(LevelJsonLibrary.IsInsideLibrary(imported),
                "导入的落点必须在正式关卡目录里：" + imported);
            Assert.AreEqual(Path.GetFileName(external), Path.GetFileName(imported),
                "导入保留原文件名（玩家改回来的文件能对上号）");
            Assert.AreEqual(File.ReadAllText(external), File.ReadAllText(imported),
                "导入是原样搬运，不该重新序列化改掉作者的排版");

            // 列表里立刻就能看见它（面板 IMPORT 之后就是靠这个重扫）
            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);
            bool listed = false;
            foreach (LevelJsonEntry entry in entries)
            {
                if (entry.Path == imported) listed = true;
            }
            Assert.IsTrue(listed, "导入完的关卡应该出现在扫描结果里：" + string.Join("；", problems));
        }

        [Test]
        public void TryImportFile_RejectsBrokenJson_WithoutWritingAnything()
        {
            string external = WriteExternalJson("Broken.json", "这不是关卡，只是一段文字");

            Assert.AreEqual(ImportOutcome.Invalid,
                LevelJsonLibrary.TryImportFile(external, false, out string imported, out string error),
                "读不通的东西不该被导进来");
            Assert.IsNull(imported);
            Assert.IsNotEmpty(error, "失败要说清原因（面板只报一句，细节在 Console）");
            Assert.IsFalse(File.Exists(Path.Combine(LevelJsonLibrary.Folder, "Broken.json")),
                "验货没过就不该落盘");
        }

        [Test]
        public void TryImportFile_RejectsReservedFileName_SoTheListNeverLies()
        {
            // 下划线开头 = 模板/草稿，Scan 不会列出它。导入必须明确拒绝，
            // 而不是复制进目录后回一句「成功」、列表里却永远找不到。
            string folder = Path.Combine(Path.GetTempPath(), "ShiftGoLevelImportTests");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string underscore = Path.Combine(folder, "_Hidden.json");
            File.WriteAllText(underscore, LevelJsonSerializer.ToJson(LoadAssetOrFail()));
            externalFiles.Add(underscore);

            Assert.AreEqual(ImportOutcome.Invalid,
                LevelJsonLibrary.TryImportFile(underscore, false, out string hiddenPath, out string hiddenError),
                "下划线开头的文件不该被导入");
            Assert.IsNull(hiddenPath);
            StringAssert.Contains("下划线", hiddenError);
            Assert.IsFalse(File.Exists(Path.Combine(LevelJsonLibrary.Folder, "_Hidden.json")),
                "被拒绝的文件一个字节都不该落盘");

            string catalog = Path.Combine(folder, "catalog.json");
            File.WriteAllText(catalog, LevelJsonSerializer.ToJson(LoadAssetOrFail()));
            externalFiles.Add(catalog);

            Assert.AreEqual(ImportOutcome.Invalid,
                LevelJsonLibrary.TryImportFile(catalog, false, out string catalogPath, out string catalogError),
                "catalog.json 是目录索引，不该被当成关卡导入");
            Assert.IsNull(catalogPath);
            StringAssert.Contains("catalog.json", catalogError);
        }

        [Test]
        public void TryImportFile_RepeatedImportIsUnchangedInsteadOfPilingUp()
        {
            string external = WriteExternalJson("Repeat.json", LevelJsonSerializer.ToJson(LoadAssetOrFail()));

            Assert.AreEqual(ImportOutcome.Imported,
                LevelJsonLibrary.TryImportFile(external, false, out string first, out string firstError), firstError);
            writtenFiles.Add(first);

            Assert.AreEqual(ImportOutcome.Unchanged,
                LevelJsonLibrary.TryImportFile(external, false, out string second, out string secondError), secondError);

            Assert.AreEqual(first, second, "同一份文件重复导入应该指向同一个文件，而不是长出 _02");
            Assert.IsFalse(File.Exists(Path.Combine(LevelJsonLibrary.Folder, "Repeat_02.json")));
        }

        [Test]
        public void TryImportFile_SameNameDifferentContent_IsNameTakenAndKeepsOriginal()
        {
            const string fileName = FilePrefix + "Collision.json";
            string destination = Path.Combine(LevelJsonLibrary.Folder, fileName);

            LevelJsonLibrary.EnsureFolder();
            const string original = "{\"schemaVersion\": 2, \"displayName\": \"ORIGINAL\"}";
            File.WriteAllText(destination, original);
            writtenFiles.Add(destination);

            // 同名文件在目录外，内容与已有关卡不同
            string externalFolder = Path.Combine(Path.GetTempPath(), "ShiftGoLevelImportTests");
            if (!Directory.Exists(externalFolder)) Directory.CreateDirectory(externalFolder);

            string external = Path.Combine(externalFolder, fileName);
            File.WriteAllText(external, LevelJsonSerializer.ToJson(LoadAssetOrFail()));
            externalFiles.Add(external);

            try
            {
                Assert.AreEqual(ImportOutcome.NameTaken,
                    LevelJsonLibrary.TryImportFile(external, false, out string refused, out string error),
                    "同名不同内容必须拒绝，不能悄悄覆盖");
                Assert.AreEqual(destination, refused);
                Assert.IsNotEmpty(error);
                Assert.AreEqual(original, File.ReadAllText(destination),
                    "拒绝之后原文件的字节必须一点没变");
            }
            finally
            {
                if (File.Exists(destination)) File.Delete(destination);
            }

            // 显式 overwrite=true 才允许替换
            try
            {
                Assert.AreEqual(ImportOutcome.Imported,
                    LevelJsonLibrary.TryImportFile(external, true, out string overwritten, out string overwriteError),
                    overwriteError);
                writtenFiles.Add(overwritten);
                Assert.AreEqual(File.ReadAllText(external), File.ReadAllText(overwritten));
            }
            finally
            {
                if (File.Exists(destination)) File.Delete(destination);
            }
        }

        [Test]
        public void ImportFromDropFolders_DoesNotOverwriteDifferentContentWithSameName()
        {
            const string fileName = FilePrefix + "InboxCollision.json";
            LevelJsonLibrary.EnsureFolder();

            string destination = Path.Combine(LevelJsonLibrary.Folder, fileName);
            string inbox = Path.Combine(LevelJsonLibrary.IncomingFolder, fileName);
            const string original = "{\"schemaVersion\": 2, \"displayName\": \"ORIGINAL\"}";

            File.WriteAllText(destination, original);
            File.WriteAllText(inbox, LevelJsonSerializer.ToJson(LoadAssetOrFail()));
            writtenFiles.Add(destination);

            try
            {
                int imported = LevelJsonLibrary.ImportFromDropFolders(out List<string> problems, out _);

                Assert.AreEqual(0, imported, "同名不同内容不该被算作导入成功");
                Assert.AreEqual(original, File.ReadAllText(destination),
                    "IMPORT · FROM INBOX 不能悄悄覆盖已有同名关卡");
                Assert.IsTrue(problems.Exists(problem => problem.Contains(fileName)),
                    "拒绝覆盖要在 problems 里点名文件：" + string.Join("；", problems));
            }
            finally
            {
                if (File.Exists(inbox)) File.Delete(inbox);
                if (File.Exists(destination)) File.Delete(destination);
            }
        }

        [Test]
        public void ImportFromDropFolders_PicksUpInboxFiles()
        {
            LevelJsonLibrary.EnsureFolder();
            Assert.Contains(LevelJsonLibrary.IncomingFolder, LevelJsonLibrary.DropFolders(),
                "收件箱必须是投放目录之一，否则 IMPORT · FROM INBOX 永远导不出东西");

            string inbox = Path.Combine(LevelJsonLibrary.IncomingFolder, FilePrefix + "Inbox.json");
            File.WriteAllText(inbox, LevelJsonSerializer.ToJson(LoadAssetOrFail()));

            string destination = Path.Combine(LevelJsonLibrary.Folder, FilePrefix + "Inbox.json");
            try
            {
                int imported = LevelJsonLibrary.ImportFromDropFolders(out List<string> problems);

                Assert.GreaterOrEqual(imported, 1, "收件箱里的关卡要能被导入：" + string.Join("；", problems));
                Assert.IsTrue(File.Exists(destination), "导入之后正式目录要有一份：" + destination);
                writtenFiles.Add(destination);

                // 收件箱里的原件留着：玩家自己决定删不删，导入不是「搬走」
                Assert.IsTrue(File.Exists(inbox), "导入不该吃掉收件箱里那份");
            }
            finally
            {
                if (File.Exists(inbox)) File.Delete(inbox);
                if (File.Exists(destination)) File.Delete(destination);
            }
        }

        [Test]
        public void WorkshopNameRules_AreAsciiSafeAndFilesystemFriendly()
        {
            Assert.AreEqual("MY PUZZLE 01", BitFlowWorkshop.SanitizeDisplayName("  my   puzzle  /01 中文  "),
                "只保留 ASCII、折叠空格、去首尾；非法字符被丢掉");
            Assert.AreEqual("MY_PUZZLE_01.json", BitFlowWorkshop.FileNameOf("my puzzle 01"),
                "文件名把空格换成下划线，且只由清理后的名字生成");
            Assert.AreEqual("12345678901234567890", BitFlowWorkshop.SanitizeDisplayName("1234567890123456789012345"),
                "超过 20 字符要截断");
            Assert.AreEqual(string.Empty, BitFlowWorkshop.SanitizeDisplayName("中文名"),
                "全是非法字符时返回空串，由调用方报 NAME REQUIRED");
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

        /// <summary>
        /// 写到关卡目录**之外**（系统临时目录），模拟「玩家从下载目录 / U 盘挑一个 json」。
        /// 只有目录外的东西才会真的走 <see cref="LevelJsonLibrary.TryImportFile"/> 的复制分支 ——
        /// 目录内的文件会被当成「已经导入过」直接放行。
        /// </summary>
        string WriteExternalJson(string fileName, string json)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ShiftGoLevelImportTests");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, FilePrefix + fileName);
            File.WriteAllText(path, json);
            externalFiles.Add(path);
            return path;
        }
    }
}
