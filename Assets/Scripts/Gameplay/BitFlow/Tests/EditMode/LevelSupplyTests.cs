using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 工坊「改参数 / 发手牌」的数据层测试。
    ///
    /// 覆盖三件容易悄悄坏掉的事：
    /// 1. 手牌写进关卡时用的是「发出去几张」而不是「还剩几张」（作者一边摆一边发，剩余量会被扣掉）；
    /// 2. 发出来的手牌必须让 <c>allowBuild</c> 打开，否则玩家看得见卡、放不下去；
    /// 3. 草稿 → 定义 → JSON 文档 → 再读回来，张数 / 序列 / 收料面一个都不能变。
    /// 全部纯 C#，不需要进 Play 模式。
    /// </summary>
    public class LevelSupplyTests
    {
        static GridPoint FreeCell(int x = 2, int y = 2)
        {
            return new GridPoint(x, y);
        }

        [Test]
        public void ToDefinition_WritesSuppliesAndTurnsOnAllowBuild()
        {
            LevelDraft draft = new LevelDraft(9, 8);
            draft.SetStock("emitter_a", CellKind.Emitter, 3);

            LevelDefinition definition = draft.ToDefinition();

            Assert.AreEqual(1, definition.Supplies.Count, "发过牌就该写进 supplies");
            Assert.AreEqual("emitter_a", definition.Supplies[0].TypeKey);
            Assert.AreEqual(CellKind.Emitter, definition.Supplies[0].Kind, "kind 是给类型解析失败时兜底的");
            Assert.AreEqual(3, definition.Supplies[0].Count);
            Assert.IsTrue(definition.AllowBuild, "有手牌的关卡必须允许玩家动手");
        }

        [Test]
        public void ToDefinition_UsesGrantedCountNotRemaining()
        {
            LevelDraft draft = new LevelDraft(9, 8);
            draft.SetStock("splitter_a", CellKind.Splitter, 2);

            EditableElement element = EditableElement.CreateDefault(CellKind.Splitter, FreeCell());
            element.TypeKey = "splitter_a";

            Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(element), "手牌够的时候应该放得下");
            Assert.AreEqual(1, draft.Remaining("splitter_a"), "放下之后剩余 1 张");

            LevelDefinition definition = draft.ToDefinition();

            Assert.AreEqual(1, definition.Supplies.Count);
            Assert.AreEqual(2, definition.Supplies[0].Count, "存档要写发出去的张数，不是剩余量");
        }

        [Test]
        public void EditStock_AccumulatesAndUndoRestoresPreviousHand()
        {
            LevelDraft draft = new LevelDraft(9, 8);

            draft.EditStock("not_a", CellKind.NotGate, 1);
            draft.EditStock("not_a", CellKind.NotGate, 4);

            Assert.AreEqual(4, draft.Remaining("not_a"), "EditStock 是覆盖式登记：填几就是几");
            Assert.AreEqual(4, draft.Granted("not_a"));

            Assert.IsTrue(draft.Undo(), "发牌也要能撤回");
            Assert.AreEqual(1, draft.Remaining("not_a"));
        }

        [Test]
        public void EditClearHand_ReturnsToUnlimitedMode()
        {
            LevelDraft draft = new LevelDraft(9, 8);
            draft.SetStock("filter_a", CellKind.Filter, 2);

            draft.EditClearHand();

            Assert.IsFalse(draft.StockEnabled, "撤光手牌就该退回不限量的作者模式");
            Assert.AreEqual(-1, draft.Remaining("filter_a"));

            LevelDefinition definition = draft.ToDefinition();
            Assert.AreEqual(0, definition.Supplies.Count);
            Assert.IsFalse(definition.AllowBuild);
        }

        [Test]
        public void AddStock_RecordsSupplyKind()
        {
            LevelDraft draft = new LevelDraft(9, 8);
            draft.AddStock("not_a", CellKind.NotGate, 2);

            Assert.AreEqual(2, draft.Remaining("not_a"));
            Assert.AreEqual(CellKind.NotGate, draft.KindOfStock("not_a"), "AddStock 要记住这一摞是哪种元件");

            LevelDefinition definition = draft.ToDefinition();
            Assert.AreEqual(CellKind.NotGate, definition.Supplies[0].Kind);
            Assert.AreEqual(2, definition.Supplies[0].Count, "加牌还是加牌，不能变成覆盖");
        }

        [Test]
        public void Edit_ChangesSequenceAndUndoRestoresIt()
        {
            LevelDraft draft = new LevelDraft(9, 8);

            EditableElement element = EditableElement.CreateDefault(CellKind.Emitter, FreeCell());
            draft.Place(element);

            Assert.IsTrue(draft.Edit(element.Coord, e =>
            {
                e.Sequence = "101";
                e.Count = 6;
            }));

            EmitterDefinition changed = (EmitterDefinition)draft.GetAt(element.Coord).ToDefinition(1);
            Assert.AreEqual("101", changed.Sequence);
            Assert.AreEqual(6, changed.EmitCount);

            Assert.IsTrue(draft.Undo(), "改参数要能 Ctrl+Z");

            EmitterDefinition restored = (EmitterDefinition)draft.GetAt(element.Coord).ToDefinition(1);
            Assert.AreEqual("1", restored.Sequence, "撤销应该回到改之前那位序列");
            Assert.AreEqual(1, restored.EmitCount);
        }

        [Test]
        public void Edit_RefusesEmptyCell()
        {
            LevelDraft draft = new LevelDraft(9, 8);

            Assert.IsFalse(draft.Edit(FreeCell(), e => e.Count = 5), "空格子上没有东西可改");
            Assert.IsFalse(draft.CanUndo, "失败的改动不该压撤销栈");
        }

        [Test]
        public void AcceptAnyFace_RoundTripsThroughDraft()
        {
            LevelDraft draft = new LevelDraft(9, 8);

            EditableElement receiver = EditableElement.CreateDefault(CellKind.Receiver, FreeCell());
            draft.Place(receiver);

            Assert.IsTrue(receiver.AcceptAnyFace, "新建的接收器默认四面可收");

            draft.Edit(receiver.Coord, e => e.AcceptAnyFace = false);

            ReceiverDefinition strict = (ReceiverDefinition)draft.GetAt(receiver.Coord).ToDefinition(1);
            Assert.IsFalse(strict.AcceptAnyFace);
            Assert.IsFalse(strict.AcceptsEntry(Dir.Up), "严格模式下只认进料面");
            Assert.IsTrue(strict.AcceptsEntry(Dir.Down), "进料面朝下，从上面撞进来才算数");

            EditableElement copy = EditableElement.FromDefinition(strict);
            Assert.IsFalse(copy.AcceptAnyFace, "进编辑器时接收条件不能丢");
        }

        [Test]
        public void WriterAndBuilder_RoundTripSupplies()
        {
            LevelDraft draft = new LevelDraft(9, 8);
            draft.SetStock("emitter_a", CellKind.Emitter, 2);
            draft.SetStock("wall_a", CellKind.Wall, 5);

            LevelDefinition source = draft.ToDefinition();
            LevelDocument document = LevelDocumentWriter.FromDefinition(source, null);

            Assert.AreEqual(2, document.supplies.Count, "两条手牌都要写进文档");
            Assert.IsTrue(document.allowBuild);

            List<string> problems = new List<string>();
            LevelDefinition rebuilt = LevelDocumentBuilder.Build(document, problems);

            Assert.IsEmpty(problems, "这份文档应该读得干干净净");
            Assert.AreEqual(2, rebuilt.Supplies.Count);
            Assert.IsTrue(rebuilt.AllowBuild);

            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (LevelSupplyEntry supply in rebuilt.Supplies) counts[supply.TypeKey] = supply.Count;

            Assert.AreEqual(2, counts["emitter_a"]);
            Assert.AreEqual(5, counts["wall_a"]);
        }

        [Test]
        public void Builder_DefaultsCountToOneAndTurnsOnAllowBuild()
        {
            LevelDocument document = new LevelDocument
            {
                width = 9,
                height = 8,
                displayName = "HAND TEST",
            };

            // count 不写（-1）= 1 张；allowBuild 也不写，但有手牌就必须能动手
            document.supplies.Add(new LevelDocumentSupply { type = "emitter_a", kind = "Emitter" });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, problems);

            Assert.AreEqual(1, definition.Supplies.Count);
            Assert.AreEqual(1, definition.Supplies[0].Count, "count 缺省应该按 1 张算");
            Assert.AreEqual(CellKind.Emitter, definition.Supplies[0].Kind);
            Assert.IsTrue(definition.AllowBuild, "手写 JSON 忘了写 allowBuild 时也要能玩");
        }

        /// <summary>
        /// 关卡资产那条路（<see cref="LevelSO.CreatePlayerDraft"/>）登记手牌时必须带上种类。
        ///
        /// 不带的话 <see cref="LevelDraft.KindOfStock"/> 兜底成「墙」：存档里 <c>kind</c> 写错还是小事，
        /// 自检里「发射器 / 接收器不能发牌」那条会跟着漏检 —— 作者手滑把发射器发给玩家，关卡照样存得下去。
        /// </summary>
        [Test]
        public void PlayerDraft_RemembersSupplyKindFromTypeAsset()
        {
            LevelTypeRegistrySO.ClearCache();
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            Assert.IsNotNull(registry, "缺少类型注册表 Resources/" + LevelTypeRegistrySO.ResourcePath);

            ElementTypeSO notGate = registry.Find("Type_NotGate_N1");
            Assert.IsNotNull(notGate, "缺少类型资产 Type_NotGate_N1");

            LevelSO level = ScriptableObject.CreateInstance<LevelSO>();
            try
            {
                level.displayName = "HAND KIND TEST";
                level.allowBuild = true;
                level.supplies.Add(new ElementSupply { type = notGate, count = 2 });

                LevelDraft draft = level.CreatePlayerDraft();

                Assert.AreEqual(CellKind.NotGate, draft.KindOfStock(notGate.Key), "手牌种类要按类型资产登记");

                LevelDefinition definition = draft.ToDefinition();
                Assert.AreEqual(1, definition.Supplies.Count);
                Assert.AreEqual(CellKind.NotGate, definition.Supplies[0].Kind, "存档里的 kind 不能退化成墙");
                Assert.AreEqual(2, definition.Supplies[0].Count);
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// BUILD MODE（作者无限涂刷）只能挂到<b>没有手牌</b>的沙盒关。
        ///
        /// 一旦挂到手牌关头上，玩家不只能凭空补件（有限手牌立刻失效），
        /// 手牌盘还会整排让位给作者面板 —— 点不动、放不下，表现就是「手牌没法用」。
        /// </summary>
        [Test]
        public void AuthorBuildMode_IsWithheldFromHandLevels()
        {
            LevelSO sandbox = ScriptableObject.CreateInstance<LevelSO>();
            LevelSO handLevel = ScriptableObject.CreateInstance<LevelSO>();

            try
            {
                sandbox.displayName = "SANDBOX";
                sandbox.allowBuild = true;
                Assert.IsTrue(LevelFlow.AllowsAuthorBuildMode(sandbox), "没有手牌的沙盒关沿用老行为");

                handLevel.displayName = "HAND LEVEL";
                handLevel.allowBuild = true;
                handLevel.supplies.Add(new ElementSupply { count = 1 });
                Assert.IsFalse(LevelFlow.AllowsAuthorBuildMode(handLevel),
                    "有手牌的关卡不能挂 BUILD MODE：手牌盘会被整排压掉");

                handLevel.allowBuild = false;
                Assert.IsFalse(LevelFlow.AllowsAuthorBuildMode(handLevel), "没写 allowBuild 就没有作者编辑器");
                Assert.IsFalse(LevelFlow.AllowsAuthorBuildMode(null));
            }
            finally
            {
                Object.DestroyImmediate(sandbox);
                Object.DestroyImmediate(handLevel);
            }
        }

        [Test]
        public void Builder_SkipsZeroCountSupply()
        {
            LevelDocument document = new LevelDocument { width = 9, height = 8 };
            document.supplies.Add(new LevelDocumentSupply { type = "emitter_a", count = 0 });

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, problems);

            Assert.AreEqual(0, definition.Supplies.Count, "0 张等于不给这张牌");
            Assert.IsNotEmpty(problems, "跳过要留下一条问题，方便作者排查");
        }

        /// <summary>
        /// 同一「种类」下的多个变体（D1 / D3）在手牌里是两摞独立的牌。
        ///
        /// 以前库存按种类合并，作者发了 D3 之后卡排只显示一张 D1、放下去的也是 D1 ——
        /// 延迟 1 tick 的件被当成延迟 3 tick 的用，关卡解法直接变了。
        /// 所以库存字典的键必须是类型 Key，写盘时也要两条 supplies。
        /// </summary>
        [Test]
        public void SameKindVariants_KeepSeparateStacksInHand()
        {
            LevelTypeRegistrySO.ClearCache();
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            Assert.IsNotNull(registry, "缺少类型注册表 Resources/" + LevelTypeRegistrySO.ResourcePath);

            ElementTypeSO delayOne = registry.Find("Type_DelayGate_D1");
            ElementTypeSO delayThree = registry.Find("Type_DelayGate_D3");
            Assert.IsNotNull(delayOne, "缺少类型资产 Type_DelayGate_D1");
            Assert.IsNotNull(delayThree, "缺少类型资产 Type_DelayGate_D3");

            LevelSO level = ScriptableObject.CreateInstance<LevelSO>();
            try
            {
                level.displayName = "VARIANT HAND TEST";
                level.allowBuild = true;
                level.supplies.Add(new ElementSupply { type = delayOne, count = 2 });
                level.supplies.Add(new ElementSupply { type = delayThree, count = 1 });

                LevelDraft draft = level.CreatePlayerDraft();

                Assert.AreEqual(2, draft.Remaining(delayOne.Key), "D1 是独立的一摞");
                Assert.AreEqual(1, draft.Remaining(delayThree.Key), "D3 不能被并进 D1");

                EditableElement placed = delayThree.CreateEditable(FreeCell());
                Assert.IsNotNull(placed);
                Assert.AreEqual(PlaceResult.Ok, draft.TryPlace(placed), "手里有 D3 就该放得下");
                Assert.AreEqual(0, draft.Remaining(delayThree.Key), "放下的是 D3，扣的也该是 D3");
                Assert.AreEqual(2, draft.Remaining(delayOne.Key), "D1 一张都没动");
                Assert.AreEqual(3, placed.DelayTicks, "落下之后得是 3 tick，不是 D1 的 1 tick");

                LevelDefinition definition = draft.ToDefinition();
                Assert.AreEqual(2, definition.Supplies.Count, "两个变体要各写一条 supplies");
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }
    }
}
