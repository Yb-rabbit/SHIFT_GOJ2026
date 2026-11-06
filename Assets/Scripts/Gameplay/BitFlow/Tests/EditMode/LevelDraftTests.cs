using System.IO;
using NUnit.Framework;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 游戏内编辑器（BUILD MODE）的数据层测试：
    /// 草稿 ↔ 定义的双向转换、放置/删除/旋转的边界行为、id 分配的稳定性，以及 JSON 存档往返。
    /// 全部纯数据驱动，不需要进 Play 模式。
    /// </summary>
    public class LevelDraftTests
    {
        static LevelDraft DemoDraft()
        {
            return LevelDraft.FromDefinition(DemoLevels.CreateLevel01());
        }

        static GridPoint FirstEmptyCell(LevelDraft draft)
        {
            for (int y = 0; y < draft.Height; y++)
            {
                for (int x = 0; x < draft.Width; x++)
                {
                    GridPoint cell = new GridPoint(x, y);
                    if (draft.GetAt(cell) == null) return cell;
                }
            }
            Assert.Fail("演示关卡里应该有空格子");
            return default;
        }

        [Test]
        public void FromDefinition_RoundTripsEveryElement()
        {
            LevelDefinition source = DemoLevels.CreateLevel01();
            LevelDraft draft = LevelDraft.FromDefinition(source);

            Assert.AreEqual(source.Elements.Count, draft.Count, "草稿应一格不落地载入现有关卡");
            Assert.AreEqual(source.Width, draft.Width);
            Assert.AreEqual(source.Height, draft.Height);

            LevelDefinition rebuilt = draft.ToDefinition();
            Assert.AreEqual(source.Elements.Count, rebuilt.Elements.Count);

            var lookup = rebuilt.BuildLookup();
            foreach (ElementDefinition element in source.Elements)
            {
                Assert.IsTrue(lookup.TryGetValue(element.Coord, out ElementDefinition copy),
                    "重建后的关卡缺少 " + element.Coord + " 的元件");
                Assert.AreEqual(element.Kind, copy.Kind, "元件类型被改掉了：" + element.Coord);
            }
        }

        [Test]
        public void FromDefinition_KeepsEmitterAndReceiverConfig()
        {
            LevelDraft draft = DemoDraft();

            foreach (ElementDefinition element in DemoLevels.CreateLevel01().Elements)
            {
                EditableElement copy = draft.GetAt(element.Coord);
                Assert.IsNotNull(copy);

                if (element is EmitterDefinition emitter)
                {
                    Assert.AreEqual(emitter.Facing, copy.PrimaryDir, "发射器朝向丢失");
                    Assert.AreEqual(emitter.Sequence, copy.Sequence, "发射器序列丢失");
                    Assert.AreEqual(emitter.RepeatCount, copy.Count, "发射器轮数丢失");
                }
                else if (element is ReceiverDefinition receiver)
                {
                    Assert.AreEqual(receiver.FeedFace, copy.PrimaryDir, "接收器进料面丢失");
                    Assert.AreEqual(receiver.TargetSequence, copy.Sequence, "接收器目标序列丢失");
                    Assert.AreEqual(receiver.TargetCount, copy.Count, "接收器目标个数丢失");
                }
                else if (element is SplitterDefinition splitter)
                {
                    Assert.AreEqual(splitter.OneArm, copy.PrimaryDir, "分流器 OneArm 丢失");
                    Assert.AreEqual(splitter.ZeroArm, copy.SecondaryDir, "分流器 ZeroArm 丢失");
                }
            }
        }
        [Test]
        public void PlaceDefault_ReplacesExistingAndRejectsOutOfBounds()
        {
            LevelDraft draft = new LevelDraft(3, 3);
            GridPoint cell = new GridPoint(1, 1);

            Assert.IsNotNull(draft.PlaceDefault(CellKind.Wall, cell));
            Assert.AreEqual(1, draft.CountOf(CellKind.Wall));

            // 同一格再放一个不同类型的元件＝整体替换，不是叠两个
            Assert.IsNotNull(draft.PlaceDefault(CellKind.NotGate, cell));
            Assert.AreEqual(1, draft.Count, "一格最多一个元件");
            Assert.AreEqual(0, draft.CountOf(CellKind.Wall));
            Assert.AreEqual(1, draft.CountOf(CellKind.NotGate));

            // 越界一律拒绝
            Assert.IsNull(draft.PlaceDefault(CellKind.Wall, new GridPoint(-1, 0)));
            Assert.IsNull(draft.PlaceDefault(CellKind.Wall, new GridPoint(3, 0)));
            Assert.IsFalse(draft.Place(new EditableElement { Kind = CellKind.Wall, Coord = new GridPoint(0, 5) }));
            Assert.AreEqual(1, draft.Count);

            Assert.IsTrue(draft.Remove(cell));
            Assert.AreEqual(0, draft.Count);
            Assert.IsFalse(draft.Remove(cell), "空格再删应返回 false");
        }

        [Test]
        public void Rotate_TurnsBothArmsClockwise()
        {
            LevelDraft draft = new LevelDraft(3, 3);
            GridPoint cell = new GridPoint(1, 1);

            EditableElement splitter = draft.PlaceDefault(CellKind.Splitter, cell);
            Assert.AreEqual(Dir.Up, splitter.PrimaryDir);
            Assert.AreEqual(Dir.Right, splitter.SecondaryDir);

            Assert.IsTrue(draft.Rotate(cell));
            splitter = draft.GetAt(cell);
            Assert.AreEqual(Dir.Right, splitter.PrimaryDir, "OneArm 应顺时针转 90°");
            Assert.AreEqual(Dir.Down, splitter.SecondaryDir, "ZeroArm 应与 OneArm 保持 90°");

            Assert.IsFalse(draft.Rotate(new GridPoint(0, 0)), "空格不能旋转");
        }

        [Test]
        public void ToDefinition_AssignsIdsByRowMajorAndIsStable()
        {
            LevelDraft draft = new LevelDraft(4, 3);
            draft.PlaceDefault(CellKind.Wall, new GridPoint(2, 2));
            draft.PlaceDefault(CellKind.Emitter, new GridPoint(0, 1));
            draft.PlaceDefault(CellKind.Receiver, new GridPoint(3, 0));

            LevelDefinition first = draft.ToDefinition();
            LevelDefinition second = draft.ToDefinition();

            Assert.AreEqual(3, first.Elements.Count);
            Assert.AreEqual("(3,0)", first.Elements[0].Coord.ToString(), "应先按 y 再按 x 排序");
            Assert.AreEqual("(0,1)", first.Elements[1].Coord.ToString());
            Assert.AreEqual("(2,2)", first.Elements[2].Coord.ToString());

            for (int i = 0; i < first.Elements.Count; i++)
            {
                Assert.AreEqual(i + 1, first.Elements[i].Id, "id 应从 1 开始连续分配");
                Assert.AreEqual(first.Elements[i].Id, second.Elements[i].Id, "重建时 id 必须保持稳定");
                Assert.AreEqual(first.Elements[i].Coord, second.Elements[i].Coord);
            }
        }

        [Test]
        public void EditedDraft_IsSimulatable()
        {
            LevelDraft draft = DemoDraft();
            GridPoint empty = FirstEmptyCell(draft);
            draft.PlaceDefault(CellKind.Filter, empty);
            draft.Rotate(empty);

            BitFlowSimulation simulation = new BitFlowSimulation(draft.ToDefinition());

            // 编辑出来的关卡必须能被模拟器正常吃下（不要求一定通关，只要跑得动、算得出结果）
            for (int i = 0; i < 64 && !simulation.IsFinished; i++) simulation.Step();

            LevelResult result = simulation.BuildResult();
            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(result.Summary()), "结算摘要不应为空");
        }

        [Test]
        public void Serializer_RoundTripsDraftThroughDisk()
        {
            LevelDraft draft = DemoDraft();
            draft.DisplayName = "ROUND TRIP";
            draft.PlaceDefault(CellKind.Filter, FirstEmptyCell(draft));

            string path = Path.Combine(Path.GetTempPath(), "shiftgo_draft_test.json");
            if (File.Exists(path)) File.Delete(path);

            try
            {
                Assert.IsTrue(LevelDraftSerializer.Save(draft, path), "保存应成功");
                Assert.IsTrue(LevelDraftSerializer.Exists(path));

                LevelDraft loaded = LevelDraftSerializer.Load(path);
                Assert.IsNotNull(loaded, "读回失败");
                Assert.AreEqual(draft.Count, loaded.Count, "元件数量应一致");
                Assert.AreEqual(draft.DisplayName, loaded.DisplayName);
                Assert.AreEqual(draft.Width, loaded.Width);
                Assert.AreEqual(draft.Height, loaded.Height);

                LevelDefinition original = draft.ToDefinition();
                LevelDefinition restored = loaded.ToDefinition();
                Assert.AreEqual(original.Elements.Count, restored.Elements.Count);
                for (int i = 0; i < original.Elements.Count; i++)
                {
                    Assert.AreEqual(original.Elements[i].Kind, restored.Elements[i].Kind);
                    Assert.AreEqual(original.Elements[i].Coord, restored.Elements[i].Coord);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void Load_MissingFileReturnsNull()
        {
            string path = Path.Combine(Path.GetTempPath(), "shiftgo_missing_" + System.Guid.NewGuid().ToString("N") + ".json");
            Assert.IsNull(LevelDraftSerializer.Load(path), "文件不存在时应返回 null，而不是抛异常");
        }
    }
}
