using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;
using ShiftGo.BitFlow.Overtime;

namespace ShiftGo.BitFlow.Tests
{
    public sealed class OvertimeCoreTests
    {
        [Test]
        public void SizePresetsMatchTheEightProblemPlan()
        {
            for (int i = 1; i <= 3; i++) Assert.AreEqual(7, OvertimeConfig.SizeFor(i).Width);
            for (int i = 4; i <= 6; i++) Assert.AreEqual(9, OvertimeConfig.SizeFor(i).Width);
            Assert.AreEqual(11, OvertimeConfig.SizeFor(7).Width);
            Assert.AreEqual(13, OvertimeConfig.SizeFor(8).Width);

            Assert.AreEqual(8, OvertimeConfig.MaxProblems);
            Assert.AreEqual(5, new OvertimeState().version);
            Assert.AreEqual("MERGE", OvertimeConfig.DisplayName(1));
            Assert.AreEqual("FINAL CASCADE", OvertimeConfig.DisplayName(8));
            Assert.AreEqual("OT-PROBLEM_008.json", OvertimeConfig.FileName(8));
            Assert.IsTrue(OvertimeConfig.TryParseFileName("OT-PROBLEM_008.json", out int index));
            Assert.AreEqual(8, index);
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_009.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_012.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_1.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_0001.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("LEVEL_01.json", out _));
        }

        [Test]
        public void CatalogDefinesEightLayoutsWithMatchingSizes()
        {
            Assert.AreEqual(OvertimeConfig.MaxProblems, OvertimeCatalog.Count);

            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                OvertimeLayout layout = OvertimeCatalog.Build(index);
                OvertimeSize size = OvertimeConfig.SizeFor(index);

                Assert.AreEqual(size.Width, layout.Width, "第 " + index + " 题宽度与尺寸档不一致");
                Assert.AreEqual(size.Height, layout.Height, "第 " + index + " 题高度与尺寸档不一致");
                Assert.AreEqual(layout.StepCount, layout.Pieces.Count, "第 " + index + " 题空位与手牌数量不一致");
                Assert.AreEqual(layout.StepCount, OvertimeConfig.TargetStepsFor(index), "第 " + index + " 题目标件数与尺寸档不一致");
                Assert.Greater(layout.Emitters.Count, 0, "第 " + index + " 题没有发射器");
                Assert.Greater(layout.Receivers.Count, 0, "第 " + index + " 题没有接收器");
            }

            for (int index = 1; index <= OvertimeCollisionCatalog.Count; index++)
            {
                OvertimeLayout layout = OvertimeCatalog.Build(index);
                Assert.IsTrue(layout.IsCollisionProblem, "第 " + index + " 题应当是碰撞题");
                Assert.GreaterOrEqual(layout.Emitters.Count, 2, "碰撞题至少要有两条位流");
            }
        }

        [Test]
        public void CatalogHasNoDuplicateMechanisms()
        {
            Dictionary<string, int> seen = new Dictionary<string, int>();
            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                string key = MechanismKey(OvertimeCatalog.Build(index));
                Assert.IsFalse(seen.TryGetValue(key, out int duplicate),
                    "第 " + index + " 题与第 " + duplicate + " 题机制重复：" + key);
                seen[key] = index;
            }

            Assert.AreEqual(OvertimeConfig.MaxProblems, seen.Count);
        }

        [Test]
        public void CatalogHasNoExactDuplicateLayouts()
        {
            Dictionary<string, int> seen = new Dictionary<string, int>();
            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                string key = ExactLayoutKey(OvertimeCatalog.Build(index));
                Assert.IsFalse(seen.TryGetValue(key, out int duplicate),
                    "第 " + index + " 题与第 " + duplicate + " 题布局完全重复");
                seen[key] = index;
            }

            Assert.AreEqual(OvertimeConfig.MaxProblems, seen.Count);
        }

        [Test]
        public void SolutionsReferenceCoversEveryProblem()
        {
            string markdown = OvertimeSolutionReference.BuildMarkdown();
            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                Assert.IsTrue(markdown.Contains(OvertimeConfig.DisplayName(index)),
                    "解决方案文档缺少第 " + index + " 题题名");
                Assert.IsTrue(markdown.Contains(OvertimeConfig.FileName(index)),
                    "解决方案文档缺少第 " + index + " 题文件名");
            }

            Assert.IsTrue(markdown.Contains("FINAL CASCADE"));
        }

        [Test]
        public void SeedDerivationIsStablePerProblem()
        {
            int first = OvertimeSeed.ForProblem(12345, 7);
            int second = OvertimeSeed.ForProblem(12345, 7);
            int other = OvertimeSeed.ForProblem(12345, 8);

            Assert.AreEqual(first, second);
            Assert.AreNotEqual(first, other);
        }

        [Test]
        [Timeout(60000)]
        public void EveryOvertimeProblemHasAUniqueSolution()
        {
            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                bool generated = OvertimeFactory.TryGenerate(index, 20261115, 0,
                    out OvertimeGenerationResult result, out string error);

                Assert.IsTrue(generated, "Problem " + index + " failed: " + error);
                Assert.IsTrue(result.Solve.IsUnique, "Problem " + index + " is not unique");
                Assert.AreEqual(OvertimeConfig.SizeFor(index).Width, result.Level.Width);
                Assert.AreEqual(OvertimeConfig.SizeFor(index).Height, result.Level.Height);
                Assert.AreEqual(result.Layout.StepCount, TotalSupplies(result.Level));
                Assert.AreEqual(OvertimeConfig.TargetStepsFor(index), result.Layout.StepCount);
                Assert.AreEqual(OvertimeConfig.DisplayName(index), result.Level.DisplayName);
                Assert.AreEqual(OvertimeCatalog.Build(index).TargetValue, result.Layout.TargetValue);
            }
        }

        [Test]
        public void EveryTurnPieceHasAtLeastOneValidRotation()
        {
            OvertimeLayout layout = OvertimeCatalog.Build(8);

            Assert.GreaterOrEqual(layout.Slots.Count, 2);
            Assert.AreEqual(layout.Slots.Count, layout.Pieces.Count);

            for (int i = 0; i < layout.Slots.Count; i++)
            {
                OvertimePiece piece = layout.Pieces[i];
                Assert.GreaterOrEqual(piece.SolutionRotation, 0);
                Assert.Less(piece.SolutionRotation, piece.RotationCount);
            }
        }

        [Test]
        public void EveryBlueprintSolvesAtItsTargetValue()
        {
            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                OvertimeLayout layout = OvertimeCatalog.Build(index);
                OvertimeSolveResult solve = OvertimeSolver.Solve(layout, 2, 30000);

                Assert.IsTrue(solve.IsUnique, "第 " + index + " 题不是唯一解");

                LevelDefinition solved = OvertimeLevelAssembler.BuildSolvedLevel(
                    layout, solve.SolutionPieces, solve.SolutionRotations);

                ReceiverDefinition receiver = null;
                foreach (ElementDefinition element in solved.Elements)
                {
                    receiver = element as ReceiverDefinition;
                    if (receiver != null) break;
                }

                Assert.IsNotNull(receiver, "第 " + index + " 题没有接收器");
                Assert.AreEqual(layout.Receivers[0].TargetSequence, receiver.TargetSequence,
                    "第 " + index + " 题接收序列必须保留前导 0");

                BitFlowSimulation simulation = new BitFlowSimulation(solved);
                while (!simulation.IsFinished) simulation.Step();
                Assert.IsTrue(simulation.BuildResult().IsSuccess, "第 " + index + " 题标准解没有送达目标值");
            }
        }

        [Test]
        public void SolvedLayoutPassesTheSameSimulationAsTheGame()
        {
            bool generated = OvertimeFactory.TryGenerate(7, 20261115, 0,
                out OvertimeGenerationResult result, out string error);
            Assert.IsTrue(generated, error);

            OvertimeLayout layout = result.Layout;
            OvertimeSolveResult solve = result.Solve;
            Assert.IsTrue(solve.IsUnique);

            LevelDefinition solved = OvertimeLevelAssembler.BuildSolvedLevel(
                layout, solve.SolutionPieces, solve.SolutionRotations);

            BitFlowSimulation simulation = new BitFlowSimulation(solved);
            while (!simulation.IsFinished) simulation.Step();
            Assert.IsTrue(simulation.BuildResult().IsSuccess);
        }

        [Test]
        public void CollisionSamples_MatchTheirIntendedStoryboard()
        {
            for (int index = 1; index <= OvertimeCollisionCatalog.Count; index++)
            {
                OvertimeLayout layout = OvertimeCatalog.Build(index);
                Assert.IsTrue(layout.IsCollisionProblem, "第 " + index + " 题应当是碰撞题");
                Assert.Greater(layout.Emitters.Count, 1, "碰撞题至少要有两条位流");
                Assert.AreEqual(layout.Slots.Count, layout.IntendedPlacements.Count);

                LevelDefinition solved = BuildIntendedSolution(layout);
                List<BitDestroyed> destroyed = RunToEnd(solved, out LevelResult result);
                Assert.IsTrue(result.IsSuccess, "第 " + index + " 题标准解没有通关");

                int conflicts = 0;
                foreach (BitDestroyed bit in destroyed)
                {
                    if (bit.Reason == DestroyReason.Conflict) conflicts++;
                }

                Assert.AreEqual(layout.ExpectedConflictPairs * 2, conflicts,
                    "第 " + index + " 题标准解应产生 " + layout.ExpectedConflictPairs + " 次同格争抢");
            }
        }

        [Test]
        public void CollisionPlayerLevelsKeepEveryEmitterAndReceiver()
        {
            for (int index = 1; index <= OvertimeCollisionCatalog.Count; index++)
            {
                bool generated = OvertimeFactory.TryGenerate(index, 20261115, 0,
                    out OvertimeGenerationResult result, out string error);
                Assert.IsTrue(generated, "第 " + index + " 题生成失败：" + error);

                int emitters = 0;
                int receivers = 0;
                foreach (ElementDefinition element in result.Level.Elements)
                {
                    if (element is EmitterDefinition) emitters++;
                    else if (element is ReceiverDefinition) receivers++;
                }

                Assert.AreEqual(result.Layout.Emitters.Count, emitters);
                Assert.AreEqual(result.Layout.Receivers.Count, receivers);
                Assert.AreEqual(result.Layout.StepCount, TotalSupplies(result.Level));
            }
        }

        [Test]
        public void BrowserState_RebuildArchivesStaleCollisionTrial()
        {
            BrowserFileSystem.SetForcedForTests(true);
            try
            {
                BrowserFileSystem.ClearAll();
                BrowserFileSystem.ReloadIndexForTests();

                // 造一份 v2 形状的第 1 题：单发射器走廊题。
                LevelDefinition stale = new LevelDefinition
                {
                    DisplayName = OvertimeConfig.DisplayName(1),
                    Width = 7,
                    Height = 6,
                    TickInterval = OvertimeConfig.TickInterval,
                };
                stale.Elements.Add(new EmitterDefinition
                {
                    Id = 1,
                    Coord = new GridPoint(0, 2),
                    Facing = Dir.Right,
                    Sequence = "1",
                    EmitCount = 4,
                });
                stale.Elements.Add(new ReceiverDefinition
                {
                    Id = 2,
                    Coord = new GridPoint(6, 2),
                    FeedFace = Dir.Left,
                    TargetSequence = "1",
                    TargetCount = 4,
                });
                LevelDocument document = LevelDocumentWriter.FromDefinition(stale, new List<string>());
                string json = UnityEngine.JsonUtility.ToJson(document, true);
                string path = System.IO.Path.Combine(LevelJsonLibrary.Folder, OvertimeConfig.FileName(1));
                LevelJsonLibrary.WriteAllText(path, json);

                OvertimeState state = OvertimeStateStore.LoadOrRebuild(out bool rebuilt);

                Assert.IsTrue(rebuilt);
                Assert.IsFalse(LevelJsonLibrary.FileExists(path),
                    "旧的单发射器题应被归档，不能占住 v3 第 1 题");
                Assert.IsNull(state.Find(1));
                Assert.AreEqual(OvertimeConfig.FirstProblem, state.nextProblem);
            }
            finally
            {
                BrowserFileSystem.ClearAll();
                BrowserFileSystem.SetForcedForTests(false);
                BrowserFileSystem.ReloadIndexForTests();
            }
        }

        [Test]
        public void VersionFiveArchivesLegacyProblemNineThroughTwelve()
        {
            BrowserFileSystem.SetForcedForTests(true);
            try
            {
                BrowserFileSystem.ClearAll();
                BrowserFileSystem.ReloadIndexForTests();

                OvertimeState legacy = new OvertimeState
                {
                    version = OvertimeConfig.StateVersion - 1,
                    baseSeed = 0x0BADF00D,
                    nextProblem = 13,
                    introSeen = true,
                };
                string stateJson = UnityEngine.JsonUtility.ToJson(legacy, true);
                LevelJsonLibrary.WriteAllText(OvertimeStateStore.StatePath, stateJson);

                LevelDefinition stale = OvertimeLevelAssembler.BuildPlayerLevel(OvertimeCatalog.Build(8), 8);
                stale.DisplayName = "LEGACY TWELVE";
                LevelDocument document = LevelDocumentWriter.FromDefinition(stale, new List<string>());
                string path = System.IO.Path.Combine(LevelJsonLibrary.Folder, OvertimeConfig.FileName(12));
                LevelJsonLibrary.WriteAllText(path, UnityEngine.JsonUtility.ToJson(document, true));

                OvertimeState state = OvertimeStateStore.LoadOrRebuild(out bool rebuilt);

                Assert.IsTrue(rebuilt);
                Assert.IsFalse(LevelJsonLibrary.FileExists(path),
                    "v4 的第 12 题应被归档，不能在 v5 选关列表里冒充 MY LEVEL");
                Assert.IsNull(state.Find(12));
                Assert.AreEqual(OvertimeConfig.FirstProblem, state.nextProblem);
            }
            finally
            {
                BrowserFileSystem.ClearAll();
                BrowserFileSystem.SetForcedForTests(false);
                BrowserFileSystem.ReloadIndexForTests();
            }
        }

        [Test]
        public void StateStore_PersistsInBrowserBackend()
        {
            BrowserFileSystem.SetForcedForTests(true);
            try
            {
                BrowserFileSystem.ClearAll();
                BrowserFileSystem.ReloadIndexForTests();

                OvertimeState state = new OvertimeState
                {
                    baseSeed = 0x12345678,
                    introSeen = true,
                    minTicks = 7,
                };
                state.Normalize();
                OvertimeStateStore.Save(state);

                List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out _);
                Assert.IsFalse(entries.Exists(entry => entry.Path.EndsWith(OvertimeConfig.StateFileName)),
                    "状态侧车不能被当成关卡扫进选关列表");

                // 模拟关掉页面再打开：丢掉内存索引，从 PlayerPrefs 重新读。
                BrowserFileSystem.ReloadIndexForTests();
                OvertimeState loaded = OvertimeStateStore.LoadOrRebuild(out bool rebuilt);

                Assert.IsFalse(rebuilt, "状态文件存在时不应重建");
                Assert.AreEqual(state.baseSeed, loaded.baseSeed);
                Assert.AreEqual(7, loaded.minTicks);
                Assert.IsTrue(loaded.introSeen);
            }
            finally
            {
                BrowserFileSystem.ClearAll();
                BrowserFileSystem.SetForcedForTests(false);
                BrowserFileSystem.ReloadIndexForTests();
            }
        }

        [Test]
        public void StateNormalizeStopsAtEightProblems()
        {
            OvertimeState state = new OvertimeState { baseSeed = 1 };
            for (int index = 1; index <= OvertimeConfig.MaxProblems; index++)
            {
                state.problems.Add(new OvertimeProblemRecord { index = index });
            }

            state.Normalize();

            Assert.AreEqual(OvertimeConfig.MaxProblems + 1, state.nextProblem);
            Assert.IsFalse(state.CanGenerate);
            Assert.AreEqual(OvertimeConfig.MaxProblems, state.GeneratedCount);
        }

        /// <summary>机制指纹：只保留"发几条流、收几路、用哪些件/朝向"，不把尺寸和长度算进去。</summary>
        static string MechanismKey(OvertimeLayout layout)
        {
            List<string> pieces = new List<string>();
            foreach (OvertimePiece piece in layout.Pieces)
            {
                pieces.Add(piece.Kind + ":" + piece.SolutionRotation + ":" + piece.PassValue + ":" + piece.DelayTicks);
            }
            pieces.Sort(StringComparer.Ordinal);

            return layout.Emitters.Count + "->" + layout.Receivers.Count + "|" + string.Join(",", pieces);
        }

        /// <summary>完整布局指纹：尺寸、发射器、接收器、走廊与空位都参与比较。</summary>
        static string ExactLayoutKey(OvertimeLayout layout)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(layout.Width).Append('x').Append(layout.Height).Append('|');

            foreach (OvertimeEmitterSpec emitter in layout.Emitters)
            {
                builder.Append("E").Append(emitter.Coord.X).Append(',').Append(emitter.Coord.Y)
                    .Append(':').Append(emitter.Facing).Append(':').Append(emitter.Sequence)
                    .Append(':').Append(emitter.EmitCount).Append(';');
            }

            foreach (OvertimeReceiverSpec receiver in layout.Receivers)
            {
                builder.Append("R").Append(receiver.Coord.X).Append(',').Append(receiver.Coord.Y)
                    .Append(':').Append(receiver.FeedFace).Append(':').Append(receiver.TargetSequence)
                    .Append(':').Append(receiver.TargetCount).Append(';');
            }

            foreach (GridPoint path in layout.PathCells)
            {
                builder.Append("P").Append(path.X).Append(',').Append(path.Y).Append(';');
            }

            for (int i = 0; i < layout.Slots.Count; i++)
            {
                OvertimePiece piece = layout.Pieces[i];
                builder.Append("S").Append(layout.Slots[i].Coord.X).Append(',').Append(layout.Slots[i].Coord.Y)
                    .Append(':').Append(piece.Kind).Append(':').Append(piece.SolutionRotation).Append(';');
            }

            return builder.ToString();
        }

        static LevelDefinition BuildIntendedSolution(OvertimeLayout layout)
        {
            int[] slotPieces = new int[layout.Slots.Count];
            int[] slotRotations = new int[layout.Slots.Count];
            for (int i = 0; i < slotPieces.Length; i++) slotPieces[i] = -1;

            foreach (OvertimePlacement placement in layout.IntendedPlacements)
            {
                slotPieces[placement.SlotIndex] = placement.PieceIndex;
                slotRotations[placement.SlotIndex] = placement.Rotation;
            }

            return OvertimeLevelAssembler.BuildSolvedLevel(layout, slotPieces, slotRotations);
        }

        static List<BitDestroyed> RunToEnd(LevelDefinition level, out LevelResult result)
        {
            BitFlowSimulation simulation = new BitFlowSimulation(level);
            List<BitDestroyed> destroyed = new List<BitDestroyed>();
            int safety = System.Math.Max(LevelCheck.SafetyTicks(level), simulation.SafetyTickLimit);
            int ticks = 0;

            while (!simulation.IsFinished && ticks < safety)
            {
                destroyed.AddRange(simulation.Step().Destroyed);
                ticks++;
            }

            result = simulation.BuildResult();
            return destroyed;
        }

        static int TotalSupplies(LevelDefinition level)
        {
            int total = 0;
            foreach (LevelSupplyEntry supply in level.Supplies)
            {
                if (supply != null) total += supply.Count;
            }
            return total;
        }
    }
}
