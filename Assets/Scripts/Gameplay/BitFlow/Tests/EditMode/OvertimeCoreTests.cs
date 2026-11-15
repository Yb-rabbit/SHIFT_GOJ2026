using NUnit.Framework;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Overtime;

namespace ShiftGo.BitFlow.Tests
{
    public sealed class OvertimeCoreTests
    {
        [Test]
        public void SizePresetsMatchTheTwelveProblemPlan()
        {
            for (int i = 1; i <= 3; i++) Assert.AreEqual(7, OvertimeConfig.SizeFor(i).Width);
            for (int i = 4; i <= 6; i++) Assert.AreEqual(9, OvertimeConfig.SizeFor(i).Width);
            for (int i = 7; i <= 9; i++) Assert.AreEqual(11, OvertimeConfig.SizeFor(i).Width);
            for (int i = 10; i <= 12; i++) Assert.AreEqual(13, OvertimeConfig.SizeFor(i).Width);

            Assert.AreEqual(12, OvertimeConfig.MaxProblems);
            Assert.AreEqual(2, new OvertimeState().version);
            Assert.AreEqual("OT-PROBLEM 01", OvertimeConfig.DisplayName(1));
            Assert.AreEqual("OT-PROBLEM_012.json", OvertimeConfig.FileName(12));
            Assert.IsTrue(OvertimeConfig.TryParseFileName("OT-PROBLEM_012.json", out int index));
            Assert.AreEqual(12, index);
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_013.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_1.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("OT-PROBLEM_0001.json", out _));
            Assert.IsFalse(OvertimeConfig.TryParseFileName("LEVEL_01.json", out _));
        }

        [Test]
        public void CatalogDefinesTwelveBlueprintsWithMatchingSizes()
        {
            Assert.AreEqual(OvertimeConfig.MaxProblems, OvertimeCatalog.Count);

            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                OvertimeBlueprint blueprint = OvertimeCatalog.Get(index);
                OvertimeSize size = OvertimeConfig.SizeFor(index);

                Assert.AreEqual(index, blueprint.Index, "蓝图题号不连续");
                Assert.AreEqual(size.Width, blueprint.Width, "第 " + index + " 题宽度与尺寸档不一致");
                Assert.AreEqual(size.Height, blueprint.Height, "第 " + index + " 题高度与尺寸档不一致");
                Assert.AreEqual(blueprint.Route.Length - 1, blueprint.SlotCount, "第 " + index + " 题转角数与路线不一致");
                Assert.AreEqual(blueprint.SlotCount, OvertimeConfig.TargetStepsFor(index), "第 " + index + " 题目标件数与尺寸档不一致");
            }
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
            OvertimeLayout layout = OvertimeCatalog.Build(9);

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
                Assert.AreEqual(layout.TargetValue.ToString(), receiver.TargetSequence);

                BitFlowSimulation simulation = new BitFlowSimulation(solved);
                while (!simulation.IsFinished) simulation.Step();
                Assert.IsTrue(simulation.BuildResult().IsSuccess, "第 " + index + " 题标准解没有送达目标值");
            }
        }

        [Test]
        public void SolvedLayoutPassesTheSameSimulationAsTheGame()
        {
            bool generated = OvertimeFactory.TryGenerate(9, 20261115, 0,
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
        public void StateNormalizeStopsAtTwelveProblems()
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
