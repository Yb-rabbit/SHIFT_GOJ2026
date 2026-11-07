using System.Collections.Generic;
using NUnit.Framework;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 单条规则的定点测试：同格争抢、元件停 1 tick、逆行销毁、进错进料面、非门取反、过滤器拦截。
    /// 每个用例都手工搭一个极小的关卡，只验证一条规则。
    /// </summary>
    public class BitFlowMechanicsTests
    {
        static LevelDefinition CreateEmptyLevel(int width, int height)
        {
            return new LevelDefinition { DisplayName = "test", Width = width, Height = height };
        }

        /// <summary>跑到结算，返回整局所有销毁事件（顺序即发生顺序）。</summary>
        static List<BitDestroyed> RunToEnd(LevelDefinition level, int maxTicks = 200)
        {
            BitFlowSimulation simulation = new BitFlowSimulation(level);
            List<BitDestroyed> destroyed = new List<BitDestroyed>();

            for (int i = 0; i < maxTicks && !simulation.IsFinished; i++)
            {
                destroyed.AddRange(simulation.Step().Destroyed);
            }

            return destroyed;
        }

        static EmitterDefinition AddEmitter(LevelDefinition level, int id, int x, int y, Dir facing, string sequence, int repeat = 1)
        {
            EmitterDefinition emitter = new EmitterDefinition
            {
                Id = id,
                Coord = new GridPoint(x, y),
                Facing = facing,
                Sequence = sequence,
                EmitCount = repeat,
            };
            level.Elements.Add(emitter);
            return emitter;
        }

        [Test]
        public void TwoEmittersTargetingSameCell_AreBothDestroyed()
        {
            LevelDefinition level = CreateEmptyLevel(3, 1);
            AddEmitter(level, 1, 0, 0, Dir.Right, "1");
            AddEmitter(level, 2, 2, 0, Dir.Left, "1");

            BitFlowSimulation simulation = new BitFlowSimulation(level);
            TickReport report = simulation.Step();

            Assert.AreEqual(2, report.Destroyed.Count, "两颗位应一起被销毁");
            foreach (BitDestroyed destroyed in report.Destroyed)
            {
                Assert.AreEqual(DestroyReason.Conflict, destroyed.Reason);
                Assert.AreEqual(new GridPoint(1, 0), destroyed.Coord, "冲突出现在两颗位的共同目标格");
            }
            Assert.AreEqual(0, simulation.ActiveBitCount);
        }

        [Test]
        public void BitStopsExactlyOneTickInsideElement()
        {
            LevelDefinition level = CreateEmptyLevel(5, 5);
            AddEmitter(level, 1, 0, 0, Dir.Right, "1");
            level.Elements.Add(new SplitterDefinition
            {
                Id = 2,
                Coord = new GridPoint(2, 0),
                OneArm = Dir.Up,
                ZeroArm = Dir.Right,
            });

            BitFlowSimulation simulation = new BitFlowSimulation(level);

            // 第 1 tick：位被吐出后前进一格、正好走进分流器格，当场裁决改向上，但本 tick 不再移动
            simulation.Step();
            Assert.AreEqual(1, simulation.ActiveBitCount);
            BitSnapshot bit = simulation.GetBits()[0];
            Assert.AreEqual(new GridPoint(2, 0), bit.Coord, "位应停在被进入的元件格里");
            Assert.AreEqual(Dir.Up, bit.Facing, "值 1 应走 1-臂（上）");

            // 第 2 tick：才真正离开元件格
            simulation.Step();
            Assert.AreEqual(new GridPoint(2, 1), simulation.GetBits()[0].Coord);
        }

        [Test]
        public void EnteringSplitterFromExitFace_IsDestroyed()
        {
            LevelDefinition level = CreateEmptyLevel(3, 5);
            level.Elements.Add(new SplitterDefinition
            {
                Id = 1,
                Coord = new GridPoint(1, 1),
                OneArm = Dir.Up,
                ZeroArm = Dir.Right,
            });
            AddEmitter(level, 2, 1, 3, Dir.Down, "1");

            List<BitDestroyed> destroyed = RunToEnd(level);

            Assert.AreEqual(1, destroyed.Count);
            Assert.AreEqual(DestroyReason.SplitterBackflow, destroyed[0].Reason);
        }

        [Test]
        public void EnteringReceiverFromWrongFace_IsDestroyed()
        {
            LevelDefinition level = CreateEmptyLevel(3, 3);
            level.Elements.Add(new ReceiverDefinition
            {
                Id = 1,
                Coord = new GridPoint(2, 0),
                FeedFace = Dir.Left,
                // 默认是四面可收；这里显式关掉，专门验证"严格模式"下进错面要销毁
                AcceptAnyFace = false,
                TargetSequence = "1",
                TargetCount = 1,
            });
            AddEmitter(level, 2, 2, 2, Dir.Down, "1");

            List<BitDestroyed> destroyed = RunToEnd(level);

            Assert.AreEqual(1, destroyed.Count);
            Assert.AreEqual(DestroyReason.ReceiverWrongFace, destroyed[0].Reason);
        }

        /// <summary>
        /// 默认四面可收：位从"非进料面"撞进来也必须被收下。这是为了"墙围一圈只留一个口"
        /// 这类布局能用 —— 作者不必再为每面单独摆朝向。
        /// </summary>
        [Test]
        public void Receiver_AcceptsFromAnyFaceByDefault()
        {
            LevelDefinition level = CreateEmptyLevel(3, 3);
            ReceiverDefinition receiver = new ReceiverDefinition
            {
                Id = 1,
                Coord = new GridPoint(2, 0),
                FeedFace = Dir.Left,   // 位实际从下方撞进来，和进料面无关
                TargetSequence = "1",
                TargetCount = 1,
            };
            level.Elements.Add(receiver);
            AddEmitter(level, 2, 2, 2, Dir.Down, "1");

            BitFlowSimulation simulation = new BitFlowSimulation(level);
            List<BitDestroyed> destroyed = new List<BitDestroyed>();

            for (int i = 0; i < 50 && !simulation.IsFinished; i++)
            {
                destroyed.AddRange(simulation.Step().Destroyed);
            }

            Assert.AreEqual(0, destroyed.Count, "四面可收时不该有任何位被销毁");
            Assert.IsTrue(receiver.AcceptAnyFace, "默认必须是四面可收");
            Assert.IsTrue(receiver.AcceptsEntry(Dir.Down), "从下方进来也该收");
            Assert.IsTrue(receiver.AcceptsEntry(Dir.Up), "任意面都该收");
        }

        [Test]
        public void NotGate_InvertsValueAndRedirects()
        {
            LevelDefinition level = CreateEmptyLevel(5, 3);
            AddEmitter(level, 1, 0, 0, Dir.Right, "0");
            level.Elements.Add(new NotGateDefinition { Id = 2, Coord = new GridPoint(2, 0), OutputDir = Dir.Up });

            BitFlowSimulation simulation = new BitFlowSimulation(level);
            simulation.Step(); // 吐到 (1,0)
            simulation.Step(); // 进入非门并被取反

            BitSnapshot bit = simulation.GetBits()[0];
            Assert.AreEqual(1, bit.Value, "0 进非门应变成 1");
            Assert.AreEqual(Dir.Up, bit.Facing, "应从非门输出方向离开");
        }

        [Test]
        public void Filter_RejectsMismatchedValue()
        {
            LevelDefinition level = CreateEmptyLevel(5, 1);
            AddEmitter(level, 1, 0, 0, Dir.Right, "0");
            level.Elements.Add(new FilterDefinition
            {
                Id = 2,
                Coord = new GridPoint(2, 0),
                OutputDir = Dir.Right,
                PassValue = 1,
            });

            List<BitDestroyed> destroyed = RunToEnd(level);

            Assert.AreEqual(1, destroyed.Count);
            Assert.AreEqual(DestroyReason.FilterRejected, destroyed[0].Reason);
        }

        [Test]
        public void NotGateOutputBackflow_IsDestroyed()
        {
            LevelDefinition level = CreateEmptyLevel(5, 3);
            level.Elements.Add(new NotGateDefinition { Id = 1, Coord = new GridPoint(2, 0), OutputDir = Dir.Right });
            AddEmitter(level, 2, 4, 0, Dir.Left, "1");

            List<BitDestroyed> destroyed = RunToEnd(level);

            Assert.AreEqual(1, destroyed.Count);
            Assert.AreEqual(DestroyReason.NotGateBackflow, destroyed[0].Reason);
        }
    }
}
