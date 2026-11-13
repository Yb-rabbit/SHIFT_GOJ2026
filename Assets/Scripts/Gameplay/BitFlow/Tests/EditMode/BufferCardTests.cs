using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// BUFFER 卡（B）的数据层测试。
    ///
    /// 这张牌和别的牌不一样：它不往板子上放东西，而是把一件<b>关卡预设件</b>收进手牌。
    /// 所以这里守的不是「元件怎么跑」，而是四条边界：
    /// 1. 只有锁定格能收（自己的件用右键，白花一张牌是玩家最不能接受的失败）；
    /// 2. 收走的东西一定得回到手里（少一张牌、多一个件 = 关卡的物资凭空消失）；
    /// 3. 任何失败都不能扣牌（点歪一下就把道具吃掉，等于惩罚手抖）；
    /// 4. 收回来的件必须能再放回去 —— 不然它就只是被删掉了。
    /// </summary>
    public class BufferCardTests
    {
        const string BufferKey = "Type_Buffer";
        const string WallKey = "Type_Wall";

        static readonly GridPoint WallCell = new GridPoint(4, 3);

        /// <summary>一关最小的草稿：一堵锁定的墙 + 一张 BUFFER。</summary>
        static LevelDraft NewDraft(out EditableElement wall, int bufferCards = 1, bool locked = true)
        {
            LevelDraft draft = new LevelDraft(9, 7);

            wall = EditableElement.CreateDefault(CellKind.Wall, WallCell);
            wall.TypeKey = WallKey;
            draft.Place(wall, locked);

            if (bufferCards >= 0) draft.SetStock(BufferKey, CellKind.Wall, bufferCards);

            // 关卡初始化不该算玩家的一步：第一次 Ctrl+Z 必须能撤掉 BUFFER 本身
            draft.ClearHistory();
            return draft;
        }

        [Test]
        public void TryRetract_LockedWall_MovesItBackIntoHandAndSpendsTheCard()
        {
            LevelDraft draft = NewDraft(out EditableElement wall);
            Assert.AreEqual(1, draft.LockedCount, "前置：这堵墙是预设件");

            RetractResult result = draft.TryRetract(WallCell, BufferKey, out EditableElement picked);

            Assert.AreEqual(RetractResult.Ok, result);
            Assert.AreSame(wall, picked, "交回来的必须是板上那一件本身，参数才带得走");
            Assert.IsNull(draft.GetAt(WallCell), "板上那一格应该空了");
            Assert.AreEqual(0, draft.LockedCount, "预设件标记要一起消失，否则它放回去还是锁的");
            Assert.AreEqual(0, draft.Remaining(BufferKey), "BUFFER 用一张少一张");
            Assert.AreEqual(1, draft.Remaining(WallKey), "墙变成手里的一张牌");
        }

        [Test]
        public void TryRetract_CountsTowardGrantedSoSavingKeepsThePiece()
        {
            LevelDraft draft = NewDraft(out _);

            Assert.AreEqual(RetractResult.Ok, draft.TryRetract(WallCell, BufferKey, out _));

            Assert.AreEqual(1, draft.Granted(WallKey), "存档写的是发出过几张：收回来的那件必须算进去");
            Assert.AreEqual(2, draft.StockTotal, "手牌总量 = BUFFER 0 张剩余，但发出去过 2 张（BUFFER + 墙）");
        }

        [Test]
        public void TryRetract_OwnPiece_IsRejectedWithoutSpendingTheCard()
        {
            LevelDraft draft = NewDraft(out _, locked: false);

            RetractResult result = draft.TryRetract(WallCell, BufferKey, out EditableElement picked);

            Assert.AreEqual(RetractResult.NotLocked, result, "自己放的件右键就能拾回，不该吃工具牌");
            Assert.IsNull(picked);
            Assert.AreEqual(1, draft.Remaining(BufferKey), "失败一律不扣牌");
            Assert.IsNotNull(draft.GetAt(WallCell), "失败不能把东西弄没");
        }

        /// <summary>
        /// 卡排的悬停预览每帧都要问"这一格能不能收"。那个判定绝不能有副作用 ——
        /// 否则鼠标扫过网格就会真的把件收走、把工具牌扣掉，玩家只会觉得这张牌"自己乱吃"。
        /// </summary>
        [Test]
        public void CanRetract_IsReadOnly_SoTheHoverPreviewCannotStealPieces()
        {
            LevelDraft draft = NewDraft(out EditableElement wall);

            Assert.AreEqual(RetractResult.Ok, draft.CanRetract(WallCell, BufferKey), "判定结果必须和 TryRetract 一致");

            // 判完什么都没变：件还在、还是锁的、牌一张没少、撤销栈没被压
            Assert.AreSame(wall, draft.GetAt(WallCell), "只看一眼不能把件拿走");
            Assert.AreEqual(1, draft.LockedCount, "锁定标记也不能被预览改掉");
            Assert.AreEqual(1, draft.Remaining(BufferKey), "预览不能扣牌");
            Assert.AreEqual(0, draft.Remaining(WallKey), "预览不能凭空虚增一张牌");
            Assert.IsFalse(draft.CanUndo, "预览不能往撤销栈里塞东西 —— 否则 Ctrl+Z 会先撤掉一次\"什么都没发生\"");

            // 每一种失败码都和 TryRetract 走同一套判定
            Assert.AreEqual(RetractResult.EmptyCell, draft.CanRetract(new GridPoint(0, 0), BufferKey));
            Assert.AreEqual(RetractResult.OutOfBounds, draft.CanRetract(new GridPoint(-1, 0), BufferKey));
            Assert.AreEqual(RetractResult.OutOfStock, draft.CanRetract(WallCell, string.Empty), "没有工具牌就没有可花的");
        }

        [Test]
        public void TryRetract_EmitterOrReceiver_IsRejected()
        {
            LevelDraft draft = new LevelDraft(9, 7);
            EditableElement emitter = EditableElement.CreateDefault(CellKind.Emitter, new GridPoint(0, 3));
            emitter.TypeKey = "Type_Emitter_E1";
            draft.Place(emitter, true);
            draft.SetStock(BufferKey, CellKind.Wall, 1);
            draft.ClearHistory();

            RetractResult result = draft.TryRetract(new GridPoint(0, 3), BufferKey, out _);

            Assert.AreEqual(RetractResult.NotBufferable, result,
                "发射器/接收器定义的是这道题，收走它等于玩家自己改题");
            Assert.AreEqual(1, draft.Remaining(BufferKey), "被拒绝的收回不该扣牌");
            Assert.IsNotNull(draft.GetAt(new GridPoint(0, 3)));
        }

        [Test]
        public void TryRetract_WithoutAnyBufferLeft_DoesNothing()
        {
            LevelDraft empty = NewDraft(out _, bufferCards: 0);

            Assert.AreEqual(RetractResult.OutOfStock, empty.TryRetract(WallCell, BufferKey, out _));
            Assert.IsNotNull(empty.GetAt(WallCell), "没牌就什么都别动");
        }

        [Test]
        public void TryRetract_WithoutAHandAtAll_DoesNothing()
        {
            // 作者模式（不限量）里不该有"凭空搬走预设件"的操作：那是玩家的特权，得用牌换
            LevelDraft authorMode = NewDraft(out _, bufferCards: -1);

            Assert.AreEqual(RetractResult.OutOfStock, authorMode.TryRetract(WallCell, BufferKey, out _));
            Assert.IsNotNull(authorMode.GetAt(WallCell));
        }
        [Test]
        public void TryRetract_EmptyOrOutsideCell_IsReported()
        {
            LevelDraft draft = NewDraft(out _);

            Assert.AreEqual(RetractResult.EmptyCell, draft.TryRetract(new GridPoint(8, 6), BufferKey, out _));
            Assert.AreEqual(RetractResult.OutOfBounds, draft.TryRetract(new GridPoint(9, 0), BufferKey, out _));
            Assert.AreEqual(1, draft.Remaining(BufferKey), "这两种点歪都不该扣牌");
        }

        [Test]
        public void RetractedPiece_CanBePlacedAgainOnAnotherCell()
        {
            LevelDraft draft = NewDraft(out _);
            Assert.AreEqual(RetractResult.Ok, draft.TryRetract(WallCell, BufferKey, out EditableElement picked));

            EditableElement moved = picked.Clone();
            moved.Coord = new GridPoint(2, 5);

            Assert.AreEqual(PlaceResult.Ok, draft.CanPlace(moved), "收回来的墙应该能放到别的空格");
            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(moved));
            Assert.AreEqual(0, draft.Remaining(WallKey), "放下去之后那一摞就空了");
        }

        [Test]
        public void Undo_PutsTheWallBackAndReturnsTheBufferCard()
        {
            LevelDraft draft = NewDraft(out _);
            draft.TryRetract(WallCell, BufferKey, out _);

            Assert.IsTrue(draft.Undo(), "BUFFER 也必须是可撤销的一步");

            Assert.IsNotNull(draft.GetAt(WallCell), "撤销之后墙回到原位");
            Assert.AreEqual(1, draft.LockedCount, "锁定标记也要回来");
            Assert.AreEqual(1, draft.Remaining(BufferKey), "工具牌退回手里");
            Assert.AreEqual(0, draft.Remaining(WallKey), "墙上那一摞牌收回去了");
        }

        [Test]
        public void BufferType_IsAToolThatBorrowsTheWallForm()
        {
            BufferTypeSO buffer = UnityEngine.ScriptableObject.CreateInstance<BufferTypeSO>();
            try
            {
                Assert.IsTrue(buffer.IsTool, "卡排要靠它把 BUFFER 和元件卡分流");
                Assert.AreEqual("Card_Buffer", buffer.CardArt, "工具卡有自己的美术，不能跟着借来的种类走");
                Assert.AreEqual(CellKind.Wall, LevelTypeRegistrySO.KindOf(buffer), "借墙的形态，只为让种类合法");
                Assert.IsTrue(SupplyKinds.IsSupplyKind(CellKind.Wall), "墙是能发给玩家的种类，自检不该拦");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buffer);
            }
        }

        [Test]
        public void Level07_IsWiredAndSolvableWithTheBufferCard()
        {
            const string path = "Assets/Data/Levels/Level_07.asset";
            if (!File.Exists(path)) Assert.Ignore("关卡资产还没生成：" + path);

            LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(path);
            Assert.IsNotNull(level);
            Assert.IsTrue(level.HasSupplies, "教 BUFFER 的关必须发牌");

            bool presetSolved;
            LevelCheck.Run(level.ToDefinition(), level.HasSupplies, out presetSolved);
            Assert.IsFalse(presetSolved, "预设布局必须跑不通 —— 否则这张牌是摆设");

            LevelDraft draft = level.CreatePlayerDraft();

            string bufferKey = null;
            foreach (var pair in draft.Stock)
            {
                if (pair.Value > 0) bufferKey = pair.Key;
            }
            Assert.IsNotNull(bufferKey, "这一关得发一张牌");

            // 挨个试墙：拿掉某一堵之后这一关应该能通关（这就是玩家的参考解）
            bool solved = false;
            for (int x = 0; x < level.width && !solved; x++)
            {
                for (int y = 0; y < level.height; y++)
                {
                    GridPoint coord = new GridPoint(x, y);
                    EditableElement at = draft.GetAt(coord);
                    if (at == null || at.Kind != CellKind.Wall) continue;

                    Assert.AreEqual(RetractResult.Ok, draft.TryRetract(coord, bufferKey, out _));

                    bool afterRetract;
                    LevelCheck.Run(draft.ToDefinition(), true, out afterRetract);
                    solved = afterRetract;

                    // 解开了就收工；这一堵不对就撤回来试下一堵（外层循环靠 solved 退出）
                    if (solved) break;
                    draft.Undo();
                }
            }

            Assert.IsTrue(solved, "拿掉一堵墙之后这一关应该能通关");
        }

        [Test]
        public void Registry_ExposesTheBufferCard()
        {
            LevelTypeRegistrySO registry = Resources.Load<LevelTypeRegistrySO>(LevelTypeRegistrySO.ResourcePath);
            if (registry == null) Assert.Ignore("工程里没有类型注册表");

            ElementTypeSO buffer = registry.Find(BufferKey);
            Assert.IsNotNull(buffer, "BUFFER 必须进注册表，否则打包后 supplies 里的 Type_Buffer 解析不出来");
            Assert.IsTrue(buffer.IsTool);
        }

    }
}
