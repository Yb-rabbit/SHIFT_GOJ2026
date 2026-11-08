using System;
using UnityEngine;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 终端演出的<b>播放状态机</b>：只管「现在该显示到第几行的第几个字」，
    /// 不碰任何 uGUI / GameObject。这样这套节奏逻辑可以在 EditMode 测试里
    /// 用假的 dt 一步一步推，不用起 Play 模式（见 <c>TerminalPlayerTests</c>）。
    ///
    /// 一次演出走三个阶段：
    /// <code>
    /// Typing  ── 一行逐字吐出来
    ///    ↓ 打满
    /// Holding ── 停顿（行内 hold 覆盖 / 剧本 defaultHold），让玩家读完
    ///    ↓ 停顿结束
    /// 下一行 …  最后一行停顿结束 → Done
    /// </code>
    ///
    /// 玩家按键走 <see cref="RequestAdvance"/>，语义和 galgame 一样分两段：
    /// 打字中按 → 立刻显示整行；停顿中按 → 立刻进下一行。
    /// 两段式而不是"一键跳到结尾"：后者容易把还没读的台词一次性冲掉。
    /// </summary>
    public sealed class TerminalPlayer
    {
        public enum Phase
        {
            /// <summary>还没 <see cref="Begin"/>。</summary>
            Idle,

            /// <summary>正在逐字打当前行。</summary>
            Typing,

            /// <summary>当前行已打满，停在停顿里。</summary>
            Holding,

            /// <summary>全剧播完。</summary>
            Done
        }

        readonly TerminalScript script;

        int lineIndex = -1;
        float revealed;
        float holdLeft;

        public TerminalPlayer(TerminalScript script)
        {
            this.script = script;
        }

        public Phase CurrentPhase { get; private set; } = Phase.Idle;

        /// <summary>当前行下标；未开始时是 -1。</summary>
        public int LineIndex => lineIndex;

        public int LineCount => script != null ? script.Lines.Count : 0;

        public bool IsFinished => CurrentPhase == Phase.Done;

        /// <summary>从 Begin 开始累计的演出时间（秒），给顶栏的 <c>T 3.42s</c> 用。</summary>
        public float Elapsed { get; private set; }

        /// <summary>当前行已经露出来的字符数（整数字符，给渲染切片用）。</summary>
        public int RevealedChars
        {
            get
            {
                if (lineIndex < 0) return 0;

                int length = CurrentTextLength;
                return Mathf.Clamp(Mathf.FloorToInt(revealed), 0, length);
            }
        }

        /// <summary>当前行完整长度。</summary>
        public int CurrentTextLength => script != null ? script.TextOf(lineIndex).Length : 0;

        /// <summary>当前行是否已经打满（含停顿阶段）。</summary>
        public bool IsLineComplete => CurrentPhase == Phase.Holding || CurrentPhase == Phase.Done;

        /// <summary>一行开始吐字。</summary>
        public event Action<int> LineStarted;

        /// <summary>一行打满（进入停顿的那一刻）。</summary>
        public event Action<int> LineCompleted;

        /// <summary>全剧播完，只触发一次。</summary>
        public event Action Finished;

        /// <summary>从头开始播。剧本为空时直接进 Done。</summary>
        public void Begin()
        {
            Elapsed = 0f;

            if (LineCount == 0)
            {
                lineIndex = -1;
                revealed = 0f;
                holdLeft = 0f;
                CurrentPhase = Phase.Done;
                Finished?.Invoke();
                return;
            }

            StartLine(0);
        }

        /// <summary>
        /// 推进时间。用<b>非缩放时间</b>（调用方传 <c>Time.unscaledDeltaTime</c>）：
        /// 演出期间就算游戏被暂停、timeScale 被改，台词也该照常往下走。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Done) return;
            if (deltaTime <= 0f) return;

            Elapsed += deltaTime;

            if (CurrentPhase == Phase.Typing)
            {
                AdvanceTyping(deltaTime);
                return;
            }

            holdLeft -= deltaTime;
            if (holdLeft <= 0f) NextLine();
        }

        /// <summary>
        /// 玩家按键。返回 true 表示这次输入被消化了（调用方据此做音效/闪烁）。
        /// 打字中 → 打满整行；停顿中 → 进下一行；已结束 → 什么都不做。
        /// </summary>
        public bool RequestAdvance()
        {
            if (CurrentPhase == Phase.Typing)
            {
                CompleteLine();
                return true;
            }

            if (CurrentPhase == Phase.Holding)
            {
                NextLine();
                return true;
            }

            return false;
        }

        void AdvanceTyping(float deltaTime)
        {
            int length = CurrentTextLength;
            revealed += deltaTime * script.CharsPerSecond;

            if (revealed >= length) CompleteLine();
        }

        void CompleteLine()
        {
            if (CurrentPhase != Phase.Typing) return;

            revealed = CurrentTextLength;
            holdLeft = script.HoldOf(lineIndex);
            CurrentPhase = Phase.Holding;

            LineCompleted?.Invoke(lineIndex);
        }

        void NextLine()
        {
            int next = lineIndex + 1;

            if (next >= LineCount)
            {
                CurrentPhase = Phase.Done;
                Finished?.Invoke();
                return;
            }

            StartLine(next);
        }

        void StartLine(int index)
        {
            lineIndex = index;
            revealed = 0f;
            holdLeft = 0f;
            CurrentPhase = Phase.Typing;

            LineStarted?.Invoke(index);
        }
    }
}
