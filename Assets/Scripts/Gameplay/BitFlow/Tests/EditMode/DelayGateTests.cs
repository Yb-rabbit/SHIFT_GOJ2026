using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 延迟器（D）的 EditMode 覆盖：
    /// 延迟拍数（1~4）、四面进入都从对面穿出、FIFO 保序、队列容量与结算条件，
    /// 以及「不旋转」「JSON 往返不丢 delay」这两条和编辑器/存档相关的约定。
    /// 全部用纯逻辑构造关卡，不加载场景 —— 和 <see cref="BitFlowSimulationTests"/> 同一套写法。
    /// </summary>
    public class DelayGateTests
    {
        const int GateId = 10;

        static LevelDefinition BuildStraightLevel(Dir facing, int delayTicks, int count, string sequence)
        {
            LevelDefinition level = new LevelDefinition
            {
                Width = 9,
                Height = 9,
                TickInterval = 0.1f,
            };

            // 发射器 → 延迟器 → 接收器 排成一条直线，延迟器在正中间
            GridPoint gate = new GridPoint(4, 4);
            GridPoint step = facing.ToOffset();
            GridPoint upstream = new GridPoint(gate.X - step.X, gate.Y - step.Y);
            GridPoint downstream = new GridPoint(gate.X + step.X, gate.Y + step.Y);

            // 再往外挪一格，避免位生成在元件格里（发射器机身不可穿过）
            GridPoint emitterCoord = new GridPoint(upstream.X - step.X, upstream.Y - step.Y);
            GridPoint receiverCoord = new GridPoint(downstream.X + step.X, downstream.Y + step.Y);

            level.Elements.Add(new EmitterDefinition
            {
                Id = 1,
                Coord = emitterCoord,
                Facing = facing,
                Sequence = sequence,
                EmitCount = count,
            });

            level.Elements.Add(new DelayGateDefinition
            {
                Id = GateId,
                Coord = gate,
                DelayTicks = delayTicks,
            });

            level.Elements.Add(new ReceiverDefinition
            {
                Id = 2,
                Coord = receiverCoord,
                AcceptAnyFace = true,
                TargetSequence = sequence,
                TargetCount = count,
            });

            return level;
        }

        static int FirstDeliveryTick(LevelDefinition level, out List<TickReport> reports, int maxTicks = 60)
        {
            BitFlowSimulation simulation = new BitFlowSimulation(level);
            reports = new List<TickReport>();

            for (int i = 0; i < maxTicks && !simulation.IsFinished; i++)
            {
                TickReport report = simulation.Step();
                reports.Add(report);
                if (report.Deliveries.Count > 0) return report.Tick;
            }

            return -1;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void DelayTicks_ReleaseAndDeliveryHappenOnExpectedTick(int delayTicks)
        {
            LevelDefinition level = BuildStraightLevel(Dir.Right, delayTicks, 1, "1");

            int deliveredAt = FirstDeliveryTick(level, out List<TickReport> reports);

            Assert.AreEqual(1, reports[0].DelayAbsorbed.Count, "第 1 tick 应把位收进延迟器");

            int releasedAt = -1;
            foreach (TickReport report in reports)
            {
                if (report.DelayReleased.Count > 0)
                {
                    releasedAt = report.Tick;
                    break;
                }
            }

            // 进队在第 1 tick，ReleaseTick = 1 + delay + 1 → 放行是第 delay+2 个 tick；
            // 放行同一 tick 迈一步，再下一 tick 进接收器。
            Assert.AreEqual(delayTicks + 2, releasedAt, "放行 tick 不对（delay=N 要等到第 N+2 个 tick）");
            Assert.AreEqual(delayTicks + 3, deliveredAt, "位到达接收器的 tick 不对（应比放行晚 1 tick）");
        }

        [Test]
        public void DelayOne_IsExactlyOneTickSlowerThanPlainElement()
        {
            // delay=N 的语义 = 比直通多停 N 拍：delay=1 也必须比普通元件晚 1 tick，
            // 否则玩家会觉得"填了 1 跟没填一样"。把延迟器换成非门（只改值、不改时序）做基准对照。
            LevelDefinition plain = BuildStraightLevel(Dir.Right, 1, 1, "1");

            for (int i = 0; i < plain.Elements.Count; i++)
            {
                if (plain.Elements[i] is DelayGateDefinition gate)
                {
                    plain.Elements[i] = new NotGateDefinition
                    {
                        Id = gate.Id,
                        Coord = gate.Coord,
                        OutputDir = Dir.Right,
                    };
                    break;
                }
            }

            int plainTick = FirstDeliveryTick(plain, out _);
            int delayTick = FirstDeliveryTick(BuildStraightLevel(Dir.Right, 1, 1, "1"), out _);

            Assert.AreEqual(plainTick + 1, delayTick, "delay=1 应当比普通元件晚 1 tick（多停 1 拍）");
        }

        [TestCase(Dir.Up)]
        [TestCase(Dir.Right)]
        [TestCase(Dir.Down)]
        [TestCase(Dir.Left)]
        public void DelayGate_ExitsOppositeTheEntryFace_ForAllFourSides(Dir facing)
        {
            LevelDefinition level = BuildStraightLevel(facing, 1, 1, "1");

            int deliveredAt = FirstDeliveryTick(level, out List<TickReport> reports);

            Assert.Greater(deliveredAt, 0, "位应当从对面穿出并抵达接收器");

            foreach (TickReport report in reports)
            {
                Assert.AreEqual(0, report.Destroyed.Count,
                    "四面进入都应直行穿出，不该撞墙 / 出界 / 逆行销毁");
            }
        }

        [Test]
        public void Queue_PreservesFifoOrderAcrossManyBits()
        {
            LevelDefinition level = BuildStraightLevel(Dir.Right, 2, 6, "101101");

            BitFlowSimulation simulation = new BitFlowSimulation(level);
            while (!simulation.IsFinished) simulation.Step();

            LevelResult result = simulation.BuildResult();
            Assert.IsTrue(result.IsSuccess, "延迟器应原样保序输出：" + result.Summary());
            Assert.AreEqual(6, result.Receivers[0].ReceivedCount);
            Assert.AreEqual("1,0,1,1,0,1", string.Join(",", result.Receivers[0].Received));
        }

        [Test]
        public void Queue_CanHoldAtLeastFiveBits()
        {
            // 容量是设计约定：关卡里常见 5 连发，容量必须 ≥ 5，否则会莫名丢位
            Assert.GreaterOrEqual(DelayGateDefinition.Capacity, 5, "延迟器队列容量至少要能压住 5 个位");
        }

        [Test]
        public void DelayGate_DoesNotFinishWhileQueueNotEmpty()
        {
            LevelDefinition level = BuildStraightLevel(Dir.Right, 4, 1, "1");
            BitFlowSimulation simulation = new BitFlowSimulation(level);

            simulation.Step(); // 位进队
            Assert.IsFalse(simulation.IsFinished, "队列里还有位时不能结算");

            for (int i = 0; i < 10 && !simulation.IsFinished; i++) simulation.Step();
            Assert.IsTrue(simulation.IsFinished, "队列放完、场上无位后应当结算");
        }

        [Test]
        public void Draft_DelayGateCannotRotate_AndKeepsDelayTicks()
        {
            EditableElement element = EditableElement.CreateDefault(CellKind.DelayGate, new GridPoint(1, 1));
            Assert.IsFalse(element.SupportsRotation, "延迟器四面等价，不应参与旋转");
            Assert.AreEqual(1, element.DelayTicks);

            element.DelayTicks = 3;

            LevelDraft draft = new LevelDraft(4, 4);
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(element));
            Assert.IsFalse(draft.Rotate(new GridPoint(1, 1)), "延迟器旋转应当被拒绝");

            DelayGateDefinition definition = null;
            foreach (ElementDefinition candidate in draft.ToDefinition().Elements)
            {
                if (candidate is DelayGateDefinition gate) definition = gate;
            }

            Assert.IsNotNull(definition);
            Assert.AreEqual(3, definition.DelayTicks, "延迟拍数不能在放置/旋转过程中丢失");
        }

        [Test]
        public void JsonRoundTrip_KeepsDelayTicks()
        {
            LevelDefinition original = BuildStraightLevel(Dir.Right, 3, 1, "1");

            LevelDocument document = LevelDocumentWriter.FromDefinition(original);
            string json = JsonUtility.ToJson(document);
            LevelDocument parsed = JsonUtility.FromJson<LevelDocument>(json);

            List<string> problems = new List<string>();
            LevelDefinition rebuilt = LevelDocumentBuilder.Build(parsed, problems);

            Assert.IsEmpty(problems, "纯 kind 模式下 JSON 往返不该有问题：" + string.Join("；", problems));

            DelayGateDefinition gate = null;
            foreach (ElementDefinition element in rebuilt.Elements)
            {
                if (element is DelayGateDefinition candidate) gate = candidate;
            }

            Assert.IsNotNull(gate, "往返后延迟器消失了");
            Assert.AreEqual(3, gate.DelayTicks, "delay 字段没有写进 JSON 或没读回来");
        }
    }
}
