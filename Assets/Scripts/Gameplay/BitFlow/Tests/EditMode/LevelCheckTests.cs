using System.Collections.Generic;
using NUnit.Framework;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 存档前自检（<see cref="LevelCheck.RunForSaveDetailed"/>）的测试。
    ///
    /// 这套规则只查「必要条件」：结构齐不齐、有手牌的关是不是手牌摆设、空位够不够放下所有牌。
    /// 它存在的理由：工坊以前只要求"预设布局能跑通"，于是「玩家一张牌都不用出」的关也能存进去，
    /// 手牌系统等于白做 —— 这种事故模拟器一眼就能看出来，不该等玩家进关才发现。
    ///
    /// 严重程度区分：Blocking 一定拒绝保存；Advisory（目前只有「手牌是摆设」）只提示一次，
    /// 作者再点一次 DONE 就照存 —— 所以它必须<b>不</b>出现在 <see cref="LevelCheck.RunForSave"/> 里。
    /// </summary>
    public class LevelCheckTests
    {
        static string Join(List<string> problems)
        {
            return string.Join("；", problems);
        }

        static string JoinAdvisories(List<LevelCheck.SaveIssue> issues)
        {
            List<string> messages = new List<string>();
            foreach (LevelCheck.SaveIssue issue in issues)
            {
                if (issue.Severity == LevelCheck.SaveIssueSeverity.Advisory) messages.Add(issue.Message);
            }
            return Join(messages);
        }

        [Test]
        public void DemoSolvableLevel_WithoutSupplies_Passes()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();

            // Run 返回的是报告文本，是否通关在 out 参数里
            LevelCheck.Run(level, out bool solved);
            Assert.IsTrue(solved, "演示关本身应该能跑通");
            Assert.IsEmpty(LevelCheck.RunForSave(level), "没有手牌的演示关应该能直接存");
        }

        [Test]
        public void Supplies_OnAnAlreadySolvableLayout_AreOnlyAnAdvisory()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();
            level.Supplies.Add(new LevelSupplyEntry
            {
                TypeKey = "Type_Splitter_L1",
                Kind = CellKind.Splitter,
                Count = 1,
            });

            // 只提示、不挡保存：作者可能就是想给玩家一张 B 卡
            Assert.IsEmpty(LevelCheck.RunForSave(level), "「手牌是摆设」是设计提醒，不该把整个关卡死");

            StringAssert.Contains("手牌是摆设", JoinAdvisories(LevelCheck.RunForSaveDetailed(level)),
                "预设布局自己就能通关时，玩家一张牌都不用出");
        }

        [Test]
        public void EmitterOrReceiverCards_AreRejected()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();
            level.Supplies.Add(new LevelSupplyEntry
            {
                TypeKey = "Type_Emitter",
                Kind = CellKind.Emitter,
                Count = 1,
            });

            StringAssert.Contains("手牌里有", Join(LevelCheck.RunForSave(level)),
                "发射器 / 接收器定的是这一关的题，不能发给玩家");
        }

        [Test]
        public void MoreCardsThanEmptyCells_IsRejected()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();
            level.Supplies.Add(new LevelSupplyEntry
            {
                TypeKey = "Type_Splitter_L1",
                Kind = CellKind.Splitter,
                Count = 500,
            });

            StringAssert.Contains("空位", Join(LevelCheck.RunForSave(level)),
                "空位不够时一定有几张牌永远放不下");
        }

        [Test]
        public void LevelWithoutReceiver_IsRejected()
        {
            LevelDefinition level = new LevelDefinition { Width = 3, Height = 3 };
            level.Elements.Add(new EmitterDefinition
            {
                Id = 1,
                Coord = new GridPoint(0, 0),
                Facing = Dir.Right,
                Sequence = "1",
                EmitCount = 1,
            });

            StringAssert.Contains("没有接收器", Join(LevelCheck.RunForSave(level)));
        }
    }
}
