using NUnit.Framework;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Overtime;

namespace ShiftGo.BitFlow.Tests
{
    public sealed class OvertimeCoreTests
    {
        [Test]
        public void SizePresetsMatchTheThirtyProblemPlan()
        {
            for (int i = 1; i <= 6; i++) Assert.AreEqual(7, OvertimeConfig.SizeFor(i).Width);
            for (int i = 7; i <= 14; i++) Assert.AreEqual(9, OvertimeConfig.SizeFor(i).Width);
            for (int i = 15; i <= 24; i++) Assert.AreEqual(11, OvertimeConfig.SizeFor(i).Width);
            for (int i = 25; i <= 30; i++) Assert.AreEqual(13, OvertimeConfig.SizeFor(i).Width);

            Assert.AreEqual("OT-PROBLEM 01", OvertimeConfig.DisplayName(1));
            Assert.AreEqual("OT-PROBLEM_030.json", OvertimeConfig.FileName(30));
            Assert.IsTrue(OvertimeConfig.TryParseFileName("OT-PROBLEM_030.json", out int index));
            Assert.AreEqual(30, index);
            Assert.IsFalse(OvertimeConfig.TryParseFileName("LEVEL_01.json", out _));
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
        [Timeout(30000)]
        public void EveryOvertimeProblemHasAUniqueSolution()
        {
            for (int index = 1; index <= OvertimeConfig.MaxProblems; index++)
            {
                bool generated = OvertimeFactory.TryGenerate(index, 20261115, 0,
                    out OvertimeGenerationResult result, out string error);

                Assert.IsTrue(generated, "Problem " + index + " failed: " + error);
                Assert.IsTrue(result.Solve.IsUnique, "Problem " + index + " is not unique");
                Assert.AreEqual(OvertimeConfig.SizeFor(index).Width, result.Level.Width);
                Assert.AreEqual(OvertimeConfig.SizeFor(index).Height, result.Level.Height);
                Assert.AreEqual(result.Layout.StepCount, TotalSupplies(result.Level));
                Assert.AreEqual(OvertimeConfig.DisplayName(index), result.Level.DisplayName);
            }
        }

        [Test]
        public void EveryTurnPieceHasAtLeastOneValidRotation()
        {
            OvertimeLayout layout = OvertimeLayoutBuilder.Build(
                OvertimeProfile.For(7, 20261115, 0), new OvertimeRng(99));

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
        public void StateNormalizeStopsAtThirtyProblems()
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
