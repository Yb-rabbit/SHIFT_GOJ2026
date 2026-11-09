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
    /// 纯 C# 关卡数据层（<see cref="LevelDocument"/> → <see cref="LevelDefinition"/>）的测试。
    ///
    /// 这一层存在的意义就是「Unity 之外的编辑器」和 Unity 跑同一份规则，所以这里重点验三件事：
    /// 1. 资产 → 文档 → 关卡 的往返是否<b>逐字段无损</b>（不然外部编辑器改一下关就变味了）；
    /// 2. <c>kind</c> 模式（只写种类 + 参数、不认资产）能不能独立成立；
    /// 3. 坏数据（越界 / 叠格 / 认不出的 type）是不是变成可读的问题条目，而不是异常。
    /// </summary>
    public class LevelDocumentBuilderTests
    {
        const string LevelAssetPath = "Assets/Data/Levels/Level_01.asset";

        /// <summary>外部编辑器读的那份类型快照（菜单「导出类型目录」生成）。</summary>
        const string CatalogFile = "LevelJson/exported/typeCatalog.json";

        LevelTypeCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            LevelTypeRegistrySO.ClearCache();
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            Assert.IsNotNull(registry, "缺少类型注册表 Resources/" + LevelTypeRegistrySO.ResourcePath
                + "：先跑菜单 SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表");
            catalog = LevelTypeCatalogSource.Build(registry);
        }

        // ------------------------------------------------------------------ 类型快照

        [Test]
        public void Catalog_CoversEveryRegisteredType()
        {
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            Assert.AreEqual(registry.Count, catalog.types.Count, "每个注册的类型都要有一份快照");

            foreach (ElementTypeSO type in registry.Types)
            {
                Assert.IsTrue(catalog.TryGet(type.Key, out LevelTypeSpec spec), "快照里缺类型：" + type.Key);
                Assert.AreEqual(LevelTypeRegistrySO.KindOf(type).ToString(), spec.kind, "种类对不上：" + type.Key);
                Assert.AreEqual(type.label, spec.label, "短名对不上：" + type.Key);
            }
        }

        /// <summary>
        /// 仓库里那份 <c>typeCatalog.json</c> 必须和现在工程里的资产一致 ——
        /// 改了类型资产却忘了重新导出时，这条会提醒（否则外部编辑器一直在用旧参数）。
        /// </summary>
        [Test]
        public void ExportedCatalogFile_MatchesLiveTypeAssets()
        {
            string path = Path.GetFullPath(CatalogFile);
            Assert.IsTrue(File.Exists(path), "缺少类型目录：" + path + "（菜单 SHIFT GO/BitFlow/关卡 JSON/导出类型目录）");

            LevelTypeCatalog shipped = JsonUtility.FromJson<LevelTypeCatalog>(File.ReadAllText(path));
            Assert.IsNotNull(shipped, "类型目录读不出来：" + path);
            Assert.AreEqual(catalog.types.Count, shipped.types.Count, "类型数量对不上，重新导出一次类型目录");

            foreach (LevelTypeSpec live in catalog.types)
            {
                Assert.IsTrue(shipped.TryGet(live.key, out LevelTypeSpec saved), "类型目录里缺：" + live.key);

                Assert.AreEqual(live.kind, saved.kind, live.key + " 的重量种类变了");
                Assert.AreEqual(live.dir, saved.dir, live.key + " 的朝向变了");
                Assert.AreEqual(live.dir2, saved.dir2, live.key + " 的零臂变了");
                Assert.AreEqual(live.sequence, saved.sequence, live.key + " 的序列变了");
                Assert.AreEqual(live.count, saved.count, live.key + " 的数量变了");
                Assert.AreEqual(live.acceptAnyFace, saved.acceptAnyFace, live.key + " 的收位面变了");
                Assert.AreEqual(live.pass, saved.pass, live.key + " 的放行值变了");
            }
        }

        // ------------------------------------------------------------------ 往返

        [Test]
        public void Level01_DocumentRoundTrip_KeepsEveryElement()
        {
            LevelSO source = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(source, "缺少关卡资产：" + LevelAssetPath);

            LevelDefinition expected = source.ToDefinition();
            LevelDocument document = LevelDocumentWriter.FromDefinition(expected);

            Assert.IsNotNull(document, "资产应该能写回文档");
            Assert.AreEqual(expected.DisplayName, document.displayName);
            Assert.AreEqual(expected.Width, document.width);
            Assert.AreEqual(expected.Height, document.height);

            List<string> problems = new List<string>();
            LevelDefinition actual = LevelDocumentBuilder.Build(document, catalog, problems);

            Assert.IsEmpty(problems, "往返过程中不该有问题：" + string.Join("；", problems));
            AssertSameLevel(expected, actual);
        }

        /// <summary>
        /// 文档往返之后，引擎跑出来的结果也要一模一样 ——
        /// 这一条是「外部编辑器里测通 = 游戏里能过」的真正凭据。
        /// </summary>
        [Test]
        public void Level01_DocumentRoundTrip_PlaysTheSame()
        {
            LevelSO source = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(source, "缺少关卡资产：" + LevelAssetPath);

            LevelDefinition expected = source.ToDefinition();
            LevelDefinition actual = LevelDocumentBuilder.Build(
                LevelDocumentWriter.FromDefinition(expected), catalog, new List<string>());

            BitFlowSimulation expectedRun = new BitFlowSimulation(expected);
            while (!expectedRun.IsFinished) expectedRun.Step();

            BitFlowSimulation actualRun = new BitFlowSimulation(actual);
            while (!actualRun.IsFinished) actualRun.Step();

            Assert.IsTrue(expectedRun.BuildResult().IsSuccess, "资产版应该能通关");
            Assert.AreEqual(expectedRun.TickIndex, actualRun.TickIndex, "往返之后通关所需 tick 数变了");
            Assert.IsTrue(actualRun.BuildResult().IsSuccess, "文档版也应该能通关：" + actualRun.BuildResult().Summary());
        }

        // ------------------------------------------------------------------ kind 模式

        [Test]
        public void KindOnlyDocument_NeedsNoCatalog()
        {
            LevelDocument document = new LevelDocument
            {
                displayName = "KIND ONLY",
                width = 5,
                height = 4,
                tickInterval = 0.3f,
            };

            document.elements.Add(new LevelDocumentCell
            {
                kind = "Emitter", x = 0, y = 1, dir = "Right", sequence = "11", count = 2,
            });

            document.elements.Add(new LevelDocumentCell
            {
                kind = "Receiver", x = 4, y = 1, dir = "Left", sequence = "1", count = 2,
            });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, problems);

            Assert.IsEmpty(problems, "kind 模式不该需要类型目录：" + string.Join("；", problems));
            Assert.AreEqual(2, definition.Elements.Count);

            BitFlowSimulation run = new BitFlowSimulation(definition);
            while (!run.IsFinished) run.Step();

            Assert.IsTrue(run.BuildResult().IsSuccess, "手写的最小关应该能通关：" + run.BuildResult().Summary());
        }

        [Test]
        public void KindMode_HonoursExplicitDirections()
        {
            LevelDocument document = new LevelDocument { width = 4, height = 4 };

            // 发射器朝上、分流器两臂朝左/下：kind 模式下 dir / dir2 是绝对朝向，不再叠加旋转
            document.elements.Add(new LevelDocumentCell
            {
                kind = "Emitter", x = 1, y = 0, dir = "上", sequence = "1", count = 1,
            });

            document.elements.Add(new LevelDocumentCell
            {
                kind = "Splitter", x = 1, y = 2, dir = "Left", dir2 = "Down",
            });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, problems);
            Assert.IsEmpty(problems);

            foreach (ElementDefinition element in definition.Elements)
            {
                if (element is EmitterDefinition emitter)
                {
                    Assert.AreEqual(Dir.Up, emitter.Facing, "dir=上 应该解析成 Up");
                }
                else if (element is SplitterDefinition splitter)
                {
                    Assert.AreEqual(Dir.Left, splitter.OneArm);
                    Assert.AreEqual(Dir.Down, splitter.ZeroArm);
                }
            }
        }

        // ------------------------------------------------------------------ 坏数据

        [Test]
        public void OutOfBoundsDuplicateAndUnknownKind_AreReported()
        {
            LevelDocument document = new LevelDocument { width = 3, height = 3 };

            document.elements.Add(new LevelDocumentCell { kind = "NotGate", x = 0, y = 0, dir = "Right" });
            document.elements.Add(new LevelDocumentCell { kind = "NotGate", x = 0, y = 0, dir = "Left" });
            document.elements.Add(new LevelDocumentCell { kind = "NotGate", x = 9, y = 9, dir = "Right" });
            document.elements.Add(new LevelDocumentCell { kind = "Nonsense", x = 1, y = 1 });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, problems);

            Assert.AreEqual(1, definition.Elements.Count, "只有第一格该留下");
            Assert.AreEqual(3, problems.Count, "重复 / 越界 / 认不出 各该有一条：" + string.Join("；", problems));
        }

        [Test]
        public void UnknownType_FallsBackToKind()
        {
            LevelDocument document = new LevelDocument { width = 3, height = 3 };
            document.elements.Add(new LevelDocumentCell
            {
                type = "Type_Does_Not_Exist", kind = "Filter", x = 1, y = 1, dir = "Up", pass = 0,
            });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, catalog, problems);

            Assert.AreEqual(1, definition.Elements.Count, "认不出 type 时应该按 kind 落地，而不是丢掉这一格");
            Assert.AreEqual(1, problems.Count, "但要留一条问题给作者看");

            FilterDefinition filter = definition.Elements[0] as FilterDefinition;
            Assert.IsNotNull(filter);
            Assert.AreEqual(Dir.Up, filter.OutputDir);
            Assert.AreEqual(0, filter.PassValue);
        }

        // ------------------------------------------------------------------ 墙层

        [Test]
        public void WallRowsAndWallMap_BothBecomeWalls()
        {
            LevelDocument rows = new LevelDocument { width = 4, height = 3, wallRows = new[] { "#...", "....", "...#" } };
            LevelDocument legacy = new LevelDocument { width = 4, height = 3, wallMap = "#...\n....\n...#" };

            LevelDefinition fromRows = LevelDocumentBuilder.Build(rows, new List<string>());
            LevelDefinition fromMap = LevelDocumentBuilder.Build(legacy, new List<string>());

            Assert.AreEqual(2, CountWalls(fromRows), "wallRows 里两个 # 应该变成两面墙");
            Assert.AreEqual(2, CountWalls(fromMap), "老写法 wallMap 也要能读出来");
            Assert.AreEqual(fromRows.Elements.Count, fromMap.Elements.Count);
        }

        [Test]
        public void WallOverlappingAnElement_IsSkippedWithAProblem()
        {
            LevelDocument document = new LevelDocument { width = 3, height = 3, wallRows = new[] { "...", ".#.", "..." } };
            document.elements.Add(new LevelDocumentCell { kind = "NotGate", x = 1, y = 1, dir = "Right" });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, problems);

            Assert.AreEqual(1, definition.Elements.Count, "同一格上元件优先，不该出现两个元件");
            Assert.AreEqual(CellKind.NotGate, definition.Elements[0].Kind);
            Assert.AreEqual(1, problems.Count, "叠格要提醒作者");
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>
        /// 逐格比对两份关卡。<b>故意不比 Id</b>：Id 只服务 UI 的进度映射，
        /// 这里关心的是「玩家会看到并撞上的东西」有没有变。
        /// </summary>
        static void AssertSameLevel(LevelDefinition expected, LevelDefinition actual)
        {
            Assert.AreEqual(expected.Width, actual.Width);
            Assert.AreEqual(expected.Height, actual.Height);
            Assert.AreEqual(expected.TickInterval, actual.TickInterval, 0.0001f);
            Assert.AreEqual(expected.Elements.Count, actual.Elements.Count, "元件总数（含墙）必须一致");

            Dictionary<GridPoint, ElementDefinition> byCoord = actual.BuildLookup();
            Assert.AreEqual(expected.Elements.Count, byCoord.Count, "不该有两格挤在同一坐标");

            foreach (ElementDefinition left in expected.Elements)
            {
                Assert.IsTrue(byCoord.TryGetValue(left.Coord, out ElementDefinition right),
                    "往返之后丢了 " + left.Coord + " 上的元件");

                Assert.AreEqual(left.Kind, right.Kind, left.Coord + " 的种类变了");

                switch (left)
                {
                    case EmitterDefinition a when right is EmitterDefinition b:
                        Assert.AreEqual(a.Facing, b.Facing, left.Coord + " 的朝向变了");
                        Assert.AreEqual(a.Sequence, b.Sequence, left.Coord + " 的序列变了");
                        Assert.AreEqual(a.EmitCount, b.EmitCount, left.Coord + " 的发射位数变了");
                        break;

                    case ReceiverDefinition a when right is ReceiverDefinition b:
                        Assert.AreEqual(a.FeedFace, b.FeedFace, left.Coord + " 的进料面变了");
                        Assert.AreEqual(a.AcceptAnyFace, b.AcceptAnyFace, left.Coord + " 的收位面变了");
                        Assert.AreEqual(a.TargetSequence, b.TargetSequence, left.Coord + " 的期望序列变了");
                        Assert.AreEqual(a.TargetCount, b.TargetCount, left.Coord + " 的期望个数变了");
                        break;

                    case SplitterDefinition a when right is SplitterDefinition b:
                        Assert.AreEqual(a.OneArm, b.OneArm, left.Coord + " 的 1-臂变了");
                        Assert.AreEqual(a.ZeroArm, b.ZeroArm, left.Coord + " 的 0-臂变了");
                        break;

                    case NotGateDefinition a when right is NotGateDefinition b:
                        Assert.AreEqual(a.OutputDir, b.OutputDir, left.Coord + " 的输出变了");
                        break;

                    case FilterDefinition a when right is FilterDefinition b:
                        Assert.AreEqual(a.OutputDir, b.OutputDir, left.Coord + " 的输出变了");
                        Assert.AreEqual(a.PassValue, b.PassValue, left.Coord + " 的放行值变了");
                        break;
                }
            }
        }

        static int CountWalls(LevelDefinition definition)
        {
            int count = 0;
            foreach (ElementDefinition element in definition.Elements)
            {
                if (element.Kind == CellKind.Wall) count++;
            }
            return count;
        }
    }
}
