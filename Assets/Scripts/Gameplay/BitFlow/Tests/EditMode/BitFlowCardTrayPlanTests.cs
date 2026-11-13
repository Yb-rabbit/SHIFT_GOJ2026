using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 工坊卡排“大类 → 变体”两级选择的排序规则。
    ///
    /// 这里守的是<b>哪些种类会被折叠</b>这条作者约定：
    /// 延迟器 / 过滤器（卡面一样、只能靠角标分）折成一张“D*4”，
    /// 发射器 / 接收器 / 非门 / 墙 / 分流器保持一张一张平铺。
    ///
    /// 折叠状态本身存在 <c>BitFlowCardTray.foldedKind</c> 里（交互层），
    /// 但“这一屏画哪些卡”是完全纯数据的 <see cref="BitFlowCardTray.PlanWorkshopCards"/>，
    /// 所以不用起场景也能验。
    /// </summary>
    public class BitFlowCardTrayPlanTests
    {
        readonly List<ElementTypeSO> created = new List<ElementTypeSO>();

        [TearDown]
        public void TearDown()
        {
            foreach (ElementTypeSO type in created)
            {
                if (type != null) Object.DestroyImmediate(type);
            }
            created.Clear();
        }

        T New<T>(string key, string label) where T : ElementTypeSO
        {
            T type = ScriptableObject.CreateInstance<T>();
            type.name = key;
            type.label = label;
            created.Add(type);
            return type;
        }

        DelayGateTypeSO Delay(int ticks, string label)
        {
            DelayGateTypeSO type = New<DelayGateTypeSO>("Type_DelayGate_" + label, label);
            type.delayTicks = ticks;
            return type;
        }

        FilterTypeSO Filter(string label, Dir output)
        {
            FilterTypeSO type = New<FilterTypeSO>("Type_Filter_" + label, label);
            type.outputDir = output;
            return type;
        }

        EmitterTypeSO Emitter(string label, string sequence)
        {
            EmitterTypeSO type = New<EmitterTypeSO>("Type_Emitter_" + label, label);
            type.sequence = sequence;
            return type;
        }

        [Test]
        public void FoldsVariants_OnlyDelayAndFilter()
        {
            Assert.IsTrue(BitFlowCardTray.FoldsVariants(CellKind.DelayGate), "延迟器 D1~D4 卡面一样，必须折");
            Assert.IsTrue(BitFlowCardTray.FoldsVariants(CellKind.Filter), "过滤器 F0/F1 卡面一样，必须折");

            Assert.IsFalse(BitFlowCardTray.FoldsVariants(CellKind.Emitter), "发射器按作者要求保持平铺");
            Assert.IsFalse(BitFlowCardTray.FoldsVariants(CellKind.Receiver), "接收器按作者要求保持平铺");
            Assert.IsFalse(BitFlowCardTray.FoldsVariants(CellKind.NotGate));
            Assert.IsFalse(BitFlowCardTray.FoldsVariants(CellKind.Wall));
            Assert.IsFalse(BitFlowCardTray.FoldsVariants(CellKind.Splitter));
        }

        [Test]
        public void Plan_Unfolded_TurnsDelayVariantsIntoOneGroupCard()
        {
            List<ElementTypeSO> palette = new List<ElementTypeSO>
            {
                Delay(1, "D1"), Delay(2, "D2"), Delay(3, "D3"), Delay(4, "D4"),
            };

            List<BitFlowCardTray.CardPlan> plans = BitFlowCardTray.PlanWorkshopCards(palette, null);

            Assert.AreEqual(1, plans.Count, "一排只该有一张延迟器卡片");
            Assert.IsTrue(plans[0].IsGroup);
            Assert.AreEqual(CellKind.DelayGate, plans[0].GroupKind);
            Assert.AreEqual("D*4", plans[0].Badge, "角标写“类*变体数”，而且只能是 ASCII（位图字体没有 ×）");
            Assert.AreSame(palette[0], plans[0].Type, "卡面取该组第一张：同一个 Kind 共用一张美术");
        }

        [Test]
        public void Plan_Expanded_ListsEveryVariantWithItsOwnBadge()
        {
            List<ElementTypeSO> palette = new List<ElementTypeSO>
            {
                Delay(1, "D1"), Delay(2, "D2"), Delay(3, "D3"), Delay(4, "D4"),
            };

            List<BitFlowCardTray.CardPlan> plans = BitFlowCardTray.PlanWorkshopCards(palette, CellKind.DelayGate);

            Assert.AreEqual(4, plans.Count);
            for (int i = 0; i < plans.Count; i++)
            {
                Assert.IsFalse(plans[i].IsGroup, "变体卡本身是能拿起来的牌，不是大类卡");
                Assert.AreEqual("D" + (i + 1), plans[i].Badge);
                Assert.AreSame(palette[i], plans[i].Type);
            }
        }

        [Test]
        public void Plan_Unfolded_KeepsEmitterVariantsFlat()
        {
            List<ElementTypeSO> palette = new List<ElementTypeSO>
            {
                Emitter("E1", "1"), Emitter("E2", "01"), Emitter("E3", "11"),
                Emitter("E4", "101"), Emitter("E5", "111"),
            };

            List<BitFlowCardTray.CardPlan> plans = BitFlowCardTray.PlanWorkshopCards(palette, null);

            Assert.AreEqual(5, plans.Count, "发射器不折叠：五张就是五张");
            foreach (BitFlowCardTray.CardPlan plan in plans)
            {
                Assert.IsFalse(plan.IsGroup);
                Assert.IsNotNull(plan.Badge, "同类多张要打角标，否则分不清发的是哪一条序列");
            }
        }

        [Test]
        public void Plan_Unfolded_KeepsSingleVariantKindsBadgeless()
        {
            List<ElementTypeSO> palette = new List<ElementTypeSO>
            {
                Filter("F0", Dir.Right),
                Emitter("E1", "1"),
            };

            List<BitFlowCardTray.CardPlan> plans = BitFlowCardTray.PlanWorkshopCards(palette, null);

            Assert.AreEqual(2, plans.Count);
            Assert.IsNull(plans[0].Badge, "只有一张过滤器时不该打角标");
            Assert.IsNull(plans[1].Badge, "只有一张发射器时不该打角标");
        }

        [Test]
        public void Plan_SkipsToolCards()
        {
            BufferTypeSO buffer = New<BufferTypeSO>("Type_Buffer", "B");
            List<ElementTypeSO> palette = new List<ElementTypeSO> { Delay(1, "D1"), buffer };

            List<BitFlowCardTray.CardPlan> plans = BitFlowCardTray.PlanWorkshopCards(palette, null);

            Assert.AreEqual(1, plans.Count, "BUFFER 借的是墙的形态，摆进卡排会是一张假墙");
            Assert.AreNotSame(buffer, plans[0].Type);
        }

        [Test]
        public void Plan_ExpandedGroupThatIsGone_ReturnsEmptySoCallerFallsBack()
        {
            List<ElementTypeSO> palette = new List<ElementTypeSO> { Emitter("E1", "1") };

            Assert.AreEqual(0, BitFlowCardTray.PlanWorkshopCards(palette, CellKind.DelayGate).Count,
                "调色板里没有延迟器时，展开视图必须返回空表，让调用方退回大类视图");

            Assert.AreEqual(0, BitFlowCardTray.PlanWorkshopCards(palette, CellKind.Emitter).Count,
                "发射器不是折叠组，即使有变体也不该被“展开”");
        }

        [Test]
        public void Plan_OrdersGroupsByRegistryOrder()
        {
            List<ElementTypeSO> palette = new List<ElementTypeSO>
            {
                Emitter("E1", "1"),
                Delay(1, "D1"),
                Delay(2, "D2"),
            };

            List<BitFlowCardTray.CardPlan> plans = BitFlowCardTray.PlanWorkshopCards(palette, null);

            Assert.AreEqual(2, plans.Count);
            Assert.AreSame(palette[0], plans[0].Type, "顺序跟调色板一致：发射器在前就先画它");
            Assert.IsTrue(plans[1].IsGroup);
        }
    }
}
