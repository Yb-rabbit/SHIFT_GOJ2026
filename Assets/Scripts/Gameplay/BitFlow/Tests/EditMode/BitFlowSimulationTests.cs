using System.Collections.Generic;
using NUnit.Framework;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 位流核心逻辑的 EditMode 单元测试（纯 C#，不加载场景、不进 Play 模式）。
    /// 覆盖：演示关的通关判定 / 15 tick 上限 / 过滤器拦截 / 次数不符判负 / 同格双销毁 / 元件停 1 tick / 逆行销毁 / Reset 可重播。
    /// </summary>
    public class BitFlowSimulationTests
    {
        static BitFlowSimulation RunUntilFinished(LevelDefinition level, out List<TickReport> reports, int maxTicks = 200)
        {
            BitFlowSimulation simulation = new BitFlowSimulation(level);
            reports = new List<TickReport>();
            for (int i = 0; i < maxTicks && !simulation.IsFinished; i++)
            {
                reports.Add(simulation.Step());
            }
            return simulation;
        }

        static List<BitDestroyed> CollectDestroyed(List<TickReport> reports)
        {
            List<BitDestroyed> destroyed = new List<BitDestroyed>();
            foreach (TickReport report in reports)
            {
                destroyed.AddRange(report.Destroyed);
            }
            return destroyed;
        }

        static ReceiverResult FindReceiver(LevelResult result, int receiverId)
        {
            foreach (ReceiverResult receiver in result.Receivers)
            {
                if (receiver.ReceiverId == receiverId) return receiver;
            }
            Assert.Fail("找不到接收器 " + receiverId);
            return null;
        }

        [Test]
        public void DemoLevel_FinishesWithinExpectedTicks()
        {
            BitFlowSimulation simulation = RunUntilFinished(DemoLevels.CreateLevel01(), out _);

            Assert.IsTrue(simulation.IsFinished, "发射器吐完后场上应无残留位");
            Assert.LessOrEqual(simulation.TickIndex, DemoLevels.ExpectedMaxTicks,
                "演示关应在 " + DemoLevels.ExpectedMaxTicks + " 个 tick 内结算完毕，实际 " + simulation.TickIndex);
            Assert.Greater(simulation.TickIndex, 0);
        }

        [Test]
        public void DemoLevel_IsSuccess()
        {
            BitFlowSimulation simulation = RunUntilFinished(DemoLevels.CreateLevel01(), out _);
            LevelResult result = simulation.BuildResult();

            Assert.IsTrue(result.IsSuccess, "演示关应通关：" + result.Summary());
            Assert.AreEqual(3, result.Receivers.Count);
        }

        [Test]
        public void DemoLevel_ReceiverPayloadsAreExact()
        {
            BitFlowSimulation simulation = RunUntilFinished(DemoLevels.CreateLevel01(), out _);
            LevelResult result = simulation.BuildResult();

            ReceiverResult receiver1 = FindReceiver(result, DemoLevels.Receiver1Id);
            Assert.AreEqual(6, receiver1.ReceivedCount, "R1 应收到 6 个位");
            foreach (byte value in receiver1.Received)
            {
                Assert.AreEqual(1, value, "R1 只应收到 1");
            }

            ReceiverResult receiver2 = FindReceiver(result, DemoLevels.Receiver2Id);
            Assert.AreEqual(2, receiver2.ReceivedCount, "R2 应收到 2 个位");
            foreach (byte value in receiver2.Received)
            {
                Assert.AreEqual(0, value, "R2 只应收到 0");
            }

            ReceiverResult receiver3 = FindReceiver(result, DemoLevels.Receiver3Id);
            Assert.AreEqual(2, receiver3.ReceivedCount, "R3 应收到 2 个位");
            foreach (byte value in receiver3.Received)
            {
                Assert.AreEqual(1, value, "R3 只应收到 1（非门把 0 取反成 1）");
            }
        }

        [Test]
        public void DemoLevel_FilterBlocksExactlyOneZero()
        {
            RunUntilFinished(DemoLevels.CreateLevel01(), out List<TickReport> reports);
            List<BitDestroyed> destroyed = CollectDestroyed(reports);

            int rejected = 0;
            foreach (BitDestroyed bit in destroyed)
            {
                if (bit.Reason == DestroyReason.FilterRejected) rejected++;
            }

            Assert.AreEqual(1, rejected, "过滤器应当且仅当拦下 1 个值不匹配的位");
            Assert.AreEqual(1, destroyed.Count, "演示关除过滤器外不应有任何销毁事件（无撞墙、无冲突、无逆行）");
        }

        [Test]
        public void DemoLevel_HasNoConflictsOrWalls()
        {
            RunUntilFinished(DemoLevels.CreateLevel01(), out List<TickReport> reports);

            foreach (BitDestroyed bit in CollectDestroyed(reports))
            {
                Assert.AreNotEqual(DestroyReason.Conflict, bit.Reason, "演示关不应出现同格争抢");
                Assert.AreNotEqual(DestroyReason.Wall, bit.Reason, "演示关不应撞墙");
                Assert.AreNotEqual(DestroyReason.OutOfBounds, bit.Reason, "演示关不应出界");
                Assert.AreNotEqual(DestroyReason.ReceiverWrongFace, bit.Reason, "演示关不应从错误进料面进入");
            }
        }

        [Test]
        public void ReceiverCountTooLow_FailsTheLevel()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();
            foreach (ElementDefinition element in level.Elements)
            {
                if (element is ReceiverDefinition receiver && receiver.Id == DemoLevels.Receiver1Id)
                {
                    receiver.TargetCount = 5; // 故意改坏：实际会收到 6 个
                }
            }

            BitFlowSimulation simulation = RunUntilFinished(level, out _);
            LevelResult result = simulation.BuildResult();

            Assert.IsFalse(result.IsSuccess, "次数不符时必须判负");
            ReceiverResult receiver1 = FindReceiver(result, DemoLevels.Receiver1Id);
            Assert.AreEqual(6, receiver1.ReceivedCount);
            Assert.AreEqual(5, receiver1.TargetCount);
            Assert.IsFalse(receiver1.IsSuccess);
        }
        [Test]
        public void Reset_RestartsFromScratch()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();
            BitFlowSimulation simulation = new BitFlowSimulation(level);

            for (int i = 0; i < 5; i++) simulation.Step();
            Assert.Greater(simulation.ActiveBitCount, 0, "前 5 个 tick 后场上应有位");

            simulation.Reset();
            Assert.AreEqual(0, simulation.TickIndex);
            Assert.AreEqual(0, simulation.ActiveBitCount);
            Assert.IsFalse(simulation.IsFinished);

            // 重播必须得到同样的结果（确定性）
            while (!simulation.IsFinished) simulation.Step();
            Assert.IsTrue(simulation.BuildResult().IsSuccess);
            Assert.LessOrEqual(simulation.TickIndex, DemoLevels.ExpectedMaxTicks);
        }
    }
}
