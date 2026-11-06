using System.IO;
using NUnit.Framework;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 玩家模式（有限手牌）的数据层测试：手牌扣减/归还、预设件锁定、撤销栈，
    /// 以及三关教学的「参考解」在模拟器里确实成立。
    ///
    /// 教学关的参考解在这里是手搭的（不读 .asset），所以这套测试同时守住了两件事：
    /// 手牌规则的边界行为没被改坏，以及关卡设计本身的逻辑成立。
    /// </summary>
    public class PlayerDraftTests
    {
        const string SplitterKey = "Type_Splitter_L1";
        const string FilterKey = "Type_Filter_F0";
        const string NotGateKey = "Type_NotGate_N1";

        static bool RunLevel(LevelDraft draft)
        {
            return RunLevel(draft, out _);
        }

        /// <summary>跑到结算，并把关键事件记成文本（断言失败时贴出来，方便看清位是怎么死的）。</summary>
        static bool RunLevel(LevelDraft draft, out string trace)
        {
            BitFlowSimulation simulation = new BitFlowSimulation(draft.ToDefinition());
            System.Text.StringBuilder log = new System.Text.StringBuilder();

            int guard = 0;
            while (!simulation.IsFinished && guard++ < 256)
            {
                TickReport report = simulation.Step();
                foreach (BitSpawned spawned in report.Spawns)
                {
                    log.AppendLine("spawn " + spawned.Coord + " " + spawned.Facing + " v" + spawned.Value);
                }
                foreach (BitMoved moved in report.Moves)
                {
                    if (moved.From.Equals(moved.To)) continue;
                    log.AppendLine("move " + moved.From + "->" + moved.To + " v" + moved.Value);
                }
                foreach (BitDestroyed destroyed in report.Destroyed)
                {
                    log.AppendLine("destroy " + destroyed.Coord + " " + destroyed.Reason);
                }
                foreach (DeliveryEvent delivery in report.Deliveries)
                {
                    log.AppendLine("deliver " + delivery.Value + " #" + delivery.ReceivedIndex + " matched=" + delivery.Matched);
                }
            }

            trace = log.ToString();
            return simulation.BuildResult().IsSuccess;
        }

        static EditableElement Emitter(GridPoint coord, string sequence, int repeatCount)
        {
            EditableElement element = EditableElement.CreateDefault(CellKind.Emitter, coord);
            element.PrimaryDir = Dir.Right;
            element.Sequence = sequence;
            element.Count = repeatCount;
            return element;
        }

        /// <summary>接收器：feedFace 是「位从哪一面进来」＝位前进方向的反面（进料面）。</summary>
        static EditableElement Receiver(GridPoint coord, Dir feedFace, string sequence, int count)
        {
            EditableElement element = EditableElement.CreateDefault(CellKind.Receiver, coord);
            element.PrimaryDir = feedFace;
            element.Sequence = sequence;
            element.Count = count;
            return element;
        }

        /// <summary>作者模式：不限量，放多少都不扣牌。</summary>
        [Test]
        public void AuthorDraft_IsUnlimited()
        {
            LevelDraft draft = new LevelDraft(4, 4);

            Assert.IsFalse(draft.StockEnabled, "没登记手牌时就是作者模式");
            Assert.AreEqual(-1, draft.Remaining(SplitterKey), "作者模式没有牌数上限");

            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(EditableElement.CreateDefault(CellKind.Filter, new GridPoint(1, 1))));
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(EditableElement.CreateDefault(CellKind.Filter, new GridPoint(2, 1))));
            Assert.AreEqual(2, draft.Count);
        }

        [Test]
        public void SetStock_TurnsOnInventoryMode()
        {
            LevelDraft draft = new LevelDraft(4, 4);
            draft.SetStock(SplitterKey, 2);

            Assert.IsTrue(draft.StockEnabled);
            Assert.AreEqual(2, draft.Remaining(SplitterKey));
            Assert.AreEqual(0, draft.Remaining("Type_NotGate_N1"), "没登记的类型就是 0 张");
        }

        [Test]
        public void TryPlace_ConsumesOneCardAndStopsWhenEmpty()
        {
            LevelDraft draft = new LevelDraft(4, 4);
            draft.SetStock(SplitterKey, 1);

            EditableElement first = EditableElement.CreateDefault(CellKind.Splitter, new GridPoint(1, 1));
            first.TypeKey = SplitterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(first));
            Assert.AreEqual(0, draft.Remaining(SplitterKey), "放下一张就要扣一张");

            EditableElement second = EditableElement.CreateDefault(CellKind.Splitter, new GridPoint(2, 1));
            second.TypeKey = SplitterKey;
            Assert.AreEqual(PlaceResult.OutOfStock, draft.TryPlace(second), "牌用完了不能再放");
            Assert.AreEqual(1, draft.Count, "被拒绝的放置不能留下痕迹");
        }

        [Test]
        public void TryPlace_RejectsLockedAndOccupiedCells()
        {
            LevelDraft draft = new LevelDraft(4, 4);
            draft.Place(EditableElement.CreateDefault(CellKind.Wall, new GridPoint(0, 0)), true);

            Assert.IsTrue(draft.IsLocked(new GridPoint(0, 0)));
            Assert.AreEqual(PlaceResult.Locked,
                draft.TryPlace(EditableElement.CreateDefault(CellKind.Filter, new GridPoint(0, 0))),
                "关卡预设件那格玩家放不进去");

            draft.Place(EditableElement.CreateDefault(CellKind.Wall, new GridPoint(1, 1)), false);
            Assert.AreEqual(PlaceResult.Occupied,
                draft.TryPlace(EditableElement.CreateDefault(CellKind.Filter, new GridPoint(1, 1))),
                "自由格已经占了也要先拾回再放");
        }

        [Test]
        public void TryPick_ReturnsCardToItsOwnStack()
        {
            LevelDraft draft = new LevelDraft(4, 4);
            draft.SetStock(SplitterKey, 1);
            draft.SetStock(FilterKey, 2);

            EditableElement splitter = EditableElement.CreateDefault(CellKind.Splitter, new GridPoint(2, 2));
            splitter.TypeKey = SplitterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(splitter));
            Assert.AreEqual(0, draft.Remaining(SplitterKey));

            Assert.IsTrue(draft.TryPick(new GridPoint(2, 2), out EditableElement picked));
            Assert.AreEqual(SplitterKey, picked.TypeKey, "拾回的元件要记得自己来自哪一摞");
            Assert.AreEqual(1, draft.Remaining(SplitterKey), "牌应该还回分流器那摞");
            Assert.AreEqual(2, draft.Remaining(FilterKey), "别的牌堆不受影响");
            Assert.AreEqual(0, draft.Count);

            Assert.IsFalse(draft.TryPick(new GridPoint(2, 2), out _), "空格拾不到东西");
        }

        [Test]
        public void TryPick_RejectsLockedCell()
        {
            LevelDraft draft = new LevelDraft(4, 4);
            draft.Place(EditableElement.CreateDefault(CellKind.Receiver, new GridPoint(3, 3)), true);

            Assert.IsFalse(draft.TryPick(new GridPoint(3, 3), out _), "关卡给的接收器搬不动");
            Assert.AreEqual(1, draft.Count);
        }

        [Test]
        public void Undo_RestoresCellsAndStock()
        {
            LevelDraft draft = new LevelDraft(4, 4);
            draft.SetStock(SplitterKey, 1);
            draft.ClearHistory();

            EditableElement splitter = EditableElement.CreateDefault(CellKind.Splitter, new GridPoint(1, 1));
            splitter.TypeKey = SplitterKey;
            draft.TryPlace(splitter);
            draft.Rotate(new GridPoint(1, 1));

            Assert.AreEqual(Dir.Right, draft.GetAt(new GridPoint(1, 1)).PrimaryDir, "分流器已推上去了");
            Assert.IsTrue(draft.Undo(), "旋转可以撤");
            Assert.AreEqual(Dir.Up, draft.GetAt(new GridPoint(1, 1)).PrimaryDir, "撤销要回到旋转前");

            Assert.IsTrue(draft.Undo(), "放置也可以撤");
            Assert.AreEqual(0, draft.Count);
            Assert.AreEqual(1, draft.Remaining(SplitterKey), "撤销要把牌也还回去");
            Assert.IsFalse(draft.CanUndo, "栈已经空了");
        }

        static EditableElement Splitter(GridPoint coord, Dir oneArm, Dir zeroArm)
        {
            EditableElement element = EditableElement.CreateDefault(CellKind.Splitter, coord);
            element.PrimaryDir = oneArm;
            element.SecondaryDir = zeroArm;
            return element;
        }

        static EditableElement Filter(GridPoint coord, Dir outputDir, byte passValue)
        {
            EditableElement element = EditableElement.CreateDefault(CellKind.Filter, coord);
            element.PrimaryDir = outputDir;
            element.PassValue = passValue;
            return element;
        }

        static EditableElement NotGate(GridPoint coord, Dir outputDir)
        {
            EditableElement element = EditableElement.CreateDefault(CellKind.NotGate, coord);
            element.PrimaryDir = outputDir;
            return element;
        }

        /// <summary>第 1 关（转向）：不给分流器，位一路向右飞出棋盘。</summary>
        [Test]
        public void Tutorial01_TurningNeedsTheSplitter()
        {
            LevelDraft draft = new LevelDraft(3, 3);
            draft.Place(Emitter(new GridPoint(0, 2), "1", 1), true);
            draft.Place(Receiver(new GridPoint(1, 0), Dir.Up, "1", 1), true);

            Assert.IsFalse(RunLevel(draft), "没有分流器时位会一路向右飞出棋盘");

            draft.SetStock(SplitterKey, 1);
            EditableElement splitter = Splitter(new GridPoint(1, 2), Dir.Down, Dir.Right);
            splitter.TypeKey = SplitterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(splitter));

            Assert.IsTrue(RunLevel(draft, out string trace),
                "值 1 走 1-臂（向下），正好落进接收器\n" + trace);
        }

        /// <summary>第 2 关（过滤）：0 会污染接收序列，必须用过滤器挡掉。</summary>
        [Test]
        public void Tutorial02_FilteringDropsTheWrongBit()
        {
            LevelDraft draft = new LevelDraft(4, 1);
            draft.Place(Emitter(new GridPoint(0, 0), "10", 1), true);
            draft.Place(Receiver(new GridPoint(3, 0), Dir.Left, "1", 1), true);

            Assert.IsFalse(RunLevel(draft), "两个位都进去，序列 1 后面跟了个 0");

            draft.SetStock(FilterKey, 1);
            EditableElement filter = Filter(new GridPoint(2, 0), Dir.Right, 1);
            filter.TypeKey = FilterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(filter));

            Assert.IsTrue(RunLevel(draft), "放行值 1 的过滤器只让 1 通过");
        }

        /// <summary>第 3 关（非门）：接收器要 1，发射器只会吐 0。</summary>
        [Test]
        public void Tutorial03_NotGateSuppliesTheInvertedBit()
        {
            LevelDraft draft = new LevelDraft(4, 1);
            draft.Place(Emitter(new GridPoint(0, 0), "0", 1), true);
            draft.Place(Receiver(new GridPoint(3, 0), Dir.Left, "1", 1), true);

            Assert.IsFalse(RunLevel(draft), "0 直接进接收器是错的");

            draft.SetStock(NotGateKey, 1);
            EditableElement notGate = NotGate(new GridPoint(2, 0), Dir.Right);
            notGate.TypeKey = NotGateKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(notGate));

            Assert.IsTrue(RunLevel(draft), "0 穿过非门变成 1，正好对上");
        }

        /// <summary>
        /// 第 4 关（分流 + 旋转 + 过滤）：1 直走右路，0 被转到下路，再经「只放行 0」的过滤器进第二个接收器。
        /// 参考解只用手牌里的分流器和过滤器，且必须按一次 R 把 0 支路掰下来 —— 这一关教的是「元件能转」。
        /// </summary>
        [Test]
        public void Tutorial04_TurningAndFilteringTogether()
        {
            LevelDraft draft = new LevelDraft(9, 6);
            draft.Place(Emitter(new GridPoint(0, 2), "1011", 2), true);
            draft.Place(Receiver(new GridPoint(7, 2), Dir.Left, "1", 6), true);
            draft.Place(Receiver(new GridPoint(7, 0), Dir.Left, "0", 2), true);

            draft.SetStock(SplitterKey, 1);
            draft.SetStock(FilterKey, 1);

            EditableElement splitter = Splitter(new GridPoint(4, 2), Dir.Up, Dir.Right);
            splitter.TypeKey = SplitterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(splitter));
            Assert.IsTrue(draft.Rotate(new GridPoint(4, 2)), "按一次 R：1 臂转到向右");

            EditableElement filter = Filter(new GridPoint(4, 0), Dir.Right, 0);
            filter.TypeKey = FilterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(filter));

            Assert.IsTrue(RunLevel(draft, out string trace), "参考解应当通关\n" + Dump(draft) + trace);
        }

        /// <summary>把草稿里每个元件的朝向打出来，方便对照模拟器的实际走位。</summary>
        static string Dump(LevelDraft draft)
        {
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            foreach (EditableElement element in draft.Cells)
            {
                log.AppendLine(element.Coord + " " + element.Kind + " P=" + element.PrimaryDir +
                    " S=" + element.SecondaryDir + " v=" + element.PassValue + " seq=" + element.Sequence + "x" + element.Count);
            }
            return log.ToString();
        }

        /// <summary>存档要能还原「有限手牌 + 预设件」：否则读档回来预设件就又变成可搬的了。</summary>
        [Test]
        public void Serializer_KeepsLockedCellsAndStock()
        {
            LevelDraft draft = new LevelDraft(3, 3);
            draft.Place(Emitter(new GridPoint(0, 2), "1", 1), true);
            draft.Place(Receiver(new GridPoint(1, 0), Dir.Up, "1", 1), true);

            draft.SetStock(SplitterKey, 2);
            EditableElement splitter = Splitter(new GridPoint(1, 2), Dir.Down, Dir.Right);
            splitter.TypeKey = SplitterKey;
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(splitter));

            string path = Path.Combine(Path.GetTempPath(), "shiftgo_player_draft.json");
            if (File.Exists(path)) File.Delete(path);

            try
            {
                Assert.IsTrue(LevelDraftSerializer.Save(draft, path), "存档应成功");

                LevelDraft loaded = LevelDraftSerializer.Load(path);
                Assert.IsNotNull(loaded, "读档失败");

                Assert.IsTrue(loaded.StockEnabled, "读回来还得是有限手牌模式");
                Assert.AreEqual(1, loaded.Remaining(SplitterKey), "用掉的那张分摊在存档里");
                Assert.IsTrue(loaded.IsLocked(new GridPoint(0, 2)), "发射器还是预设件");
                Assert.IsTrue(loaded.IsLocked(new GridPoint(1, 0)), "接收器还是预设件");
                Assert.IsFalse(loaded.IsLocked(new GridPoint(1, 2)), "玩家自己放的能被拾回");

                Assert.IsFalse(loaded.CanUndo, "读档不该留下可撤销的历史");

                Assert.AreEqual(PlaceResult.Locked, loaded.TryPlace(Filter(new GridPoint(0, 2), Dir.Right, 1)),
                    "读档后预设格依然挡人");
                Assert.IsTrue(loaded.TryPick(new GridPoint(1, 2), out EditableElement picked), "玩家件可以拾回");
                Assert.AreEqual(SplitterKey, picked.TypeKey, "拾回的牌要记得自己属于哪一摞");
                Assert.AreEqual(2, loaded.Remaining(SplitterKey), "拾回后牌数回来");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
