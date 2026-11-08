using NUnit.Framework;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 终端演出的节奏逻辑。只测 <see cref="TerminalPlayer"/> 这个纯状态机 ——
    /// 它的全部输入是「剧本 + dt + 按键」，不涉及任何 GameObject，
    /// 所以这一批能在 EditMode 里毫秒级跑完，不用起 Play。
    ///
    /// 真正容易错的是三件事，测试就盯这三件：
    /// 1. 打字速度换算（每秒字符 × dt，向下取整到字符）
    /// 2. 一行打满之后是「停顿」而不是立刻跳行（跳行会让整段演出像倒豆子）
    /// 3. 跳过是两段式（打字中→打满整行；停顿中→下一行），不能一键冲到结尾
    /// </summary>
    public class TerminalPlayerTests
    {
        static TerminalScript Script(float charsPerSecond, float defaultHold, params TerminalLine[] lines)
        {
            return TerminalScript.Create("TEST // CHANNEL 00", lines, charsPerSecond, defaultHold);
        }

        static TerminalLine Line(string text, float hold = 0f)
        {
            return TerminalScript.Line(TerminalSpeaker.Sys, text, hold);
        }

        [Test]
        public void Begin_StartsTheFirstLineAndRevealsNothing()
        {
            TerminalPlayer player = new TerminalPlayer(Script(20f, 0.3f, Line("ABCDE")));

            int started = -1;
            player.LineStarted += index => started = index;

            player.Begin();

            Assert.AreEqual(TerminalPlayer.Phase.Typing, player.CurrentPhase);
            Assert.AreEqual(0, player.LineIndex);
            Assert.AreEqual(0, started, "Begin 应该触发第 0 行的 LineStarted");
            Assert.AreEqual(0, player.RevealedChars, "刚开场一个字都不该露出来");
            Assert.IsFalse(player.IsFinished);
        }

        [Test]
        public void Tick_RevealsCharactersAtTheConfiguredSpeed()
        {
            // 每秒 20 字 × 0.25 秒 = 5 字，向下取整
            TerminalPlayer player = new TerminalPlayer(Script(20f, 0.3f, Line("ABCDEFGHIJ")));
            player.Begin();

            player.Tick(0.25f);
            Assert.AreEqual(5, player.RevealedChars);

            player.Tick(0.10f);   // 再 2 字
            Assert.AreEqual(7, player.RevealedChars);

            Assert.AreEqual(TerminalPlayer.Phase.Typing, player.CurrentPhase, "还没打满，不该提前进停顿");
        }

        [Test]
        public void Typing_CompletesLineThenHolds_BeforeMovingOn()
        {
            TerminalPlayer player = new TerminalPlayer(Script(10f, 0.5f, Line("ABC"), Line("XYZ")));
            player.Begin();

            int completed = -1;
            player.LineCompleted += index => completed = index;

            // 3 字 ÷ 10 字/秒 = 0.3 秒打满
            player.Tick(0.29f);
            Assert.AreEqual(TerminalPlayer.Phase.Typing, player.CurrentPhase);

            player.Tick(0.02f);
            Assert.AreEqual(TerminalPlayer.Phase.Holding, player.CurrentPhase, "打满后应停在停顿里，而不是直接跳下一行");
            Assert.AreEqual(0, completed);
            Assert.AreEqual(3, player.RevealedChars);

            // 停顿 0.5 秒：先差一点，再越过
            player.Tick(0.49f);
            Assert.AreEqual(0, player.LineIndex, "停顿没走完不该换行");

            player.Tick(0.02f);
            Assert.AreEqual(1, player.LineIndex);
            Assert.AreEqual(TerminalPlayer.Phase.Typing, player.CurrentPhase);
            Assert.AreEqual(0, player.RevealedChars, "新的一行从头开始吐字");
        }

        [Test]
        public void RequestAdvance_WhileTyping_ShowsTheWholeLine()
        {
            TerminalPlayer player = new TerminalPlayer(Script(10f, 0.5f, Line("ABCDEFGH")));
            player.Begin();
            player.Tick(0.1f);   // 只露出 1 个字符

            bool consumed = player.RequestAdvance();

            Assert.IsTrue(consumed);
            Assert.AreEqual(TerminalPlayer.Phase.Holding, player.CurrentPhase);
            Assert.AreEqual(8, player.RevealedChars, "跳过应该一次性把整行显示完");
            Assert.AreEqual(0, player.LineIndex, "但不该顺带跳到下一行");
        }

        [Test]
        public void RequestAdvance_WhileHolding_StartsTheNextLine()
        {
            TerminalPlayer player = new TerminalPlayer(Script(10f, 5f, Line("ABC"), Line("XYZ")));
            player.Begin();

            player.RequestAdvance();     // 打字中 → 打满整行
            Assert.AreEqual(TerminalPlayer.Phase.Holding, player.CurrentPhase);

            player.RequestAdvance();     // 停顿中 → 立刻下一行（不必等 5 秒）
            Assert.AreEqual(1, player.LineIndex);
            Assert.AreEqual(TerminalPlayer.Phase.Typing, player.CurrentPhase);
        }

        [Test]
        public void LastLine_FiresFinishedExactlyOnce()
        {
            TerminalPlayer player = new TerminalPlayer(Script(10f, 0f, Line("AB"), Line("CD")));

            int finishedCount = 0;
            player.Finished += () => finishedCount++;

            player.Begin();

            player.RequestAdvance();     // 第 0 行打满
            player.RequestAdvance();     // 停顿 0 → 第 1 行
            player.RequestAdvance();     // 第 1 行打满
            player.RequestAdvance();     // 最后一行的停顿 → 播完

            Assert.AreEqual(TerminalPlayer.Phase.Done, player.CurrentPhase);
            Assert.IsTrue(player.IsFinished);
            Assert.AreEqual(1, finishedCount);

            player.RequestAdvance();
            player.Tick(10f);
            Assert.AreEqual(1, finishedCount, "播完之后再戳也不该重复触发 Finished");
        }

        [Test]
        public void EmptyScript_FinishesOnBegin()
        {
            TerminalPlayer player = new TerminalPlayer(Script(20f, 0.3f));

            bool finished = false;
            player.Finished += () => finished = true;

            player.Begin();

            Assert.IsTrue(finished, "空剧本应该当场结束，而不是留一个永远不动的 Idle");
            Assert.IsTrue(player.IsFinished);
            Assert.AreEqual(-1, player.LineIndex);
        }

        [Test]
        public void HoldOf_UsesLineOverride_AndFallsBackToDefault()
        {
            TerminalScript script = Script(20f, 0.4f, Line("A", 1.2f), Line("B"));

            Assert.AreEqual(1.2f, script.HoldOf(0), 0.0001f);
            Assert.AreEqual(0.4f, script.HoldOf(1), 0.0001f, "没写 hold 的行用剧本默认停顿");
            Assert.AreEqual(0.4f, script.HoldOf(99), 0.0001f, "越界也返回默认值，调用方不必判空");
        }

        [Test]
        public void DefaultIntro_IsAllEnglishWithSpeakerLabelsTheFontHas()
        {
            TerminalScript intro = TerminalScript.CreateDefaultIntro();

            Assert.Greater(intro.Lines.Count, 0);
            Assert.AreEqual(11, intro.Lines.Count, "开场台词是 11 行；改剧本时同步这条预期，或直接改数字");

            foreach (TerminalLine line in intro.Lines)
            {
                Assert.IsFalse(string.IsNullOrEmpty(line.text), "不许有空台词行");

                // 中文 = 像素字体没有字形 = 打包后一屏方块
                foreach (char c in line.text)
                {
                    Assert.Less((int)c, 0x2E80,
                        "台词里出现非 ASCII 字符 '" + c + "'：" + line.text + "（像素字体没有中文字形）");
                }

                string label = TerminalPalette.SpeakerLabel(line.speaker);
                Assert.LessOrEqual(label.Length, TerminalPalette.SpeakerCols,
                    "说话人标签超过 " + TerminalPalette.SpeakerCols + " 个字符会顶掉正文：" + label);
            }
        }
    }
}
