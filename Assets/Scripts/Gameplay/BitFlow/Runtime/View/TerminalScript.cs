using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftGo.BitFlow.View
{
    /// <summary>一行台词是谁说的。颜色由 <see cref="TerminalPalette"/> 按它取，台词本身不存颜色。</summary>
    public enum TerminalSpeaker
    {
        /// <summary>系统旁白：连接、握手、警告。</summary>
        Sys,

        /// <summary>发射器那侧的声音。</summary>
        Emit,

        /// <summary>接收器那侧的声音。</summary>
        Recv,

        /// <summary>没有名字的声音（玩家自己 / 未知源）。</summary>
        Null
    }

    /// <summary>
    /// 一句台词。刻意保持成<b>可序列化的普通类</b>而不是 struct：
    /// Unity 的 List&lt;struct&gt; 在 Inspector 里编辑体验很差（每次改都要整项复制）。
    /// </summary>
    [Serializable]
    public sealed class TerminalLine
    {
        public TerminalSpeaker speaker = TerminalSpeaker.Sys;

        [TextArea(1, 3)]
        [Tooltip("台词正文。像素字体没有中文字形，只能英文 + 常用符号。")]
        public string text = string.Empty;

        [Tooltip("这句打完后再停多久（秒）。0 = 用剧本上的默认停顿")]
        [Min(0f)]
        public float hold = 0f;
    }

    /// <summary>
    /// 一段终端剧本。既可以做成资产（右键 Create → BitFlow → Terminal Script）由作者手改，
    /// 也可以完全由代码生成（<see cref="CreateDefaultIntro"/>）—— 开场那段就是后者，
    /// 这样仓库里不画任何 .asset 也能跑。
    ///
    /// 为什么是 ScriptableObject 而不是纯文本文件：台词要带说话人、停顿这些结构化字段，
    /// 纯文本得再写一套解析器，而 tsv 之类的格式在 Inspector 里没法调。
    /// </summary>
    [CreateAssetMenu(menuName = "BitFlow/Terminal Script", fileName = "TerminalScript")]
    public sealed class TerminalScript : ScriptableObject
    {
        [Tooltip("顶栏左侧那行标题。留空则不显示顶栏")]
        [SerializeField] string channelTitle = "INTRO_00 // CHANNEL 00";

        [Tooltip("每行打完后的默认停顿（秒）。单行可以用 TerminalLine.hold 覆盖")]
        [SerializeField, Min(0f)] float defaultHold = 0.30f;

        [Tooltip("打字速度：每秒出多少个字符")]
        [SerializeField, Min(1f)] float charsPerSecond = 30f;

        [SerializeField] List<TerminalLine> lines = new List<TerminalLine>();

        public string ChannelTitle => channelTitle;
        public float DefaultHold => Mathf.Max(0f, defaultHold);
        public float CharsPerSecond => Mathf.Max(1f, charsPerSecond);
        public IReadOnlyList<TerminalLine> Lines => lines;

        /// <summary>某一行实际该停多久（行内覆盖优先）。</summary>
        public float HoldOf(int index)
        {
            if (index < 0 || index >= lines.Count) return DefaultHold;

            TerminalLine line = lines[index];
            return line != null && line.hold > 0f ? line.hold : DefaultHold;
        }

        /// <summary>第 index 行的正文；越界返回空串，调用方不必每次都判空。</summary>
        public string TextOf(int index)
        {
            if (index < 0 || index >= lines.Count) return string.Empty;

            TerminalLine line = lines[index];
            return line != null ? line.text ?? string.Empty : string.Empty;
        }

        /// <summary>第 index 行的说话人；越界返回 <see cref="TerminalSpeaker.Sys"/>。</summary>
        public TerminalSpeaker SpeakerOf(int index)
        {
            if (index < 0 || index >= lines.Count) return TerminalSpeaker.Sys;

            TerminalLine line = lines[index];
            return line != null ? line.speaker : TerminalSpeaker.Sys;
        }

        /// <summary>
        /// 开场那段。台词全英文、只用像素字体里有的字符 —— 中文会 fallback 到系统字体，
        /// 编辑器里看着正常，打包后是一屏方块。
        ///
        /// 逗号一律不用：位图字体里 <c>,</c> 的字形和 <c>.</c> 几乎一样，出现在句尾会被读成句号，
        /// 干脆用 <c>.</c> 和 <c>...</c> 代替。
        /// </summary>
        public static TerminalScript CreateDefaultIntro()
        {
            TerminalScript script = CreateInstance<TerminalScript>();
            script.name = "TerminalScript_Intro";

            // 代码造的实例不该被存进场景 / 资产库（退出 Play 后也不该在内存里留影子）
            script.hideFlags = HideFlags.DontSave;
            script.channelTitle = "INTRO_00 // CHANNEL 00";
            script.defaultHold = 0.30f;
            script.charsPerSecond = 30f;

            script.lines = new List<TerminalLine>
            {
                Line(TerminalSpeaker.Sys,  "LINK ESTABLISHED.", 0.45f),
                Line(TerminalSpeaker.Sys,  "CHANNEL 00 HANDSHAKE OK."),
                Line(TerminalSpeaker.Sys,  "UNKNOWN SOURCE DETECTED. AWAITING REPLY ......"),
                Line(TerminalSpeaker.Emit, "YOU SHOULD NOT BE HERE.", 0.55f),
                Line(TerminalSpeaker.Emit, "THERE IS NO EXIT. ONLY THE LOOP."),
                Line(TerminalSpeaker.Sys,  "UPLINK ATTEMPT 1 ......... FAIL"),
                Line(TerminalSpeaker.Sys,  "UPLINK ATTEMPT 2 ......... FAIL"),
                Line(TerminalSpeaker.Recv, "... I HEARD THAT.", 0.40f),
                Line(TerminalSpeaker.Recv, "BUT WHOSE VOICE DID YOU SEND ME?"),
                Line(TerminalSpeaker.Sys,  "WARNING: RECEIVER OFFLINE.", 0.85f),
                Line(TerminalSpeaker.Null, "DON'T STOP.", 0.70f)
            };

            return script;
        }

        public static TerminalLine Line(TerminalSpeaker speaker, string text, float hold = 0f)
        {
            return new TerminalLine { speaker = speaker, text = text, hold = hold };
        }

        /// <summary>
        /// 用代码拼一份剧本。给测试用（测试没法改 private 的 <c>[SerializeField]</c> 字段），
        /// 也给将来"按关卡临时拼一段台词"留的口子。
        ///
        /// 参数都会夹到合法区间，调用方不必自己防负数速度 / 负停顿。
        /// </summary>
        public static TerminalScript Create(string channelTitle, IEnumerable<TerminalLine> content,
            float charsPerSecond = 30f, float defaultHold = 0.30f)
        {
            TerminalScript script = CreateInstance<TerminalScript>();
            script.name = "TerminalScript";

            script.channelTitle = channelTitle ?? string.Empty;
            script.charsPerSecond = Mathf.Max(1f, charsPerSecond);
            script.defaultHold = Mathf.Max(0f, defaultHold);

            script.lines = new List<TerminalLine>();
            if (content != null) script.lines.AddRange(content);

            return script;
        }

    }

    /// <summary>
    /// 终端面板的配色与尺寸。抽成静态类是为了让 <see cref="TerminalView"/>、测试和
    /// 编辑器里的视觉原型（<c>Assets/Editor/PREVIEW_TerminalStyle.cs</c>）对上同一组数；
    /// 原型删掉之后这里就是唯一的一份。
    ///
    /// 字号只取 <see cref="PixelFont"/> 的三档整数倍：位图字体在非整数倍字号上会糊。
    /// 32 号 = 每字符 20px，20 号 = 12.5px，16 号 = 10px。
    /// </summary>
    public static class TerminalPalette
    {
        public const int BodySize = PixelFont.Large;
        public const int SmallSize = PixelFont.Body;

        public const float PanelWidth = 1220f;
        public const float PanelHeight = 620f;
        public const float PanelOffsetY = -205f;

        /// <summary>一行的高度。32 号字给 44 留出足够空隙，滚起来不挤。</summary>
        public const float LineHeight = 44f;

        /// <summary>正文左边距。</summary>
        public const float TextLeft = 48f;

        /// <summary>正文顶部起点（相对面板内容区）。</summary>
        public const float TextTop = 62f;

        /// <summary>说话人栏宽度（字符数）。6 = <c>SYSTEM</c> 的长度，等宽字体靠补空格对齐。</summary>
        public const int SpeakerCols = 6;

        /// <summary>
        /// 同时最多显示多少行，超出的往上滚。
        /// 11 是算出来的：正文从顶栏下 62px 起，11 × 44 = 484，占到底部 546，
        /// 面板 620 剩 74 给底部那行按键提示（14 + 26 + 34 的间距）。
        /// 内置开场正好 11 行，一屏放完不用滚。
        /// </summary>
        public const int MaxVisibleLines = 11;

        public static readonly Color PanelBg = new Color(0.055f, 0.070f, 0.100f, 0.62f);
        public static readonly Color PanelEdge = new Color(0.118f, 0.227f, 0.290f, 1.00f);
        public static readonly Color BodyColor = new Color(0.851f, 0.922f, 0.969f, 1.00f);
        public static readonly Color MutedColor = new Color(0.353f, 0.420f, 0.478f, 1.00f);
        public static readonly Color SysColor = new Color(0.486f, 0.576f, 0.651f, 1.00f);
        public static readonly Color EmitColor = new Color(0.357f, 0.851f, 1.000f, 1.00f);
        public static readonly Color RecvColor = new Color(0.298f, 0.902f, 0.549f, 1.00f);
        public static readonly Color NullColor = new Color(1.000f, 0.769f, 0.420f, 1.00f);

        /// <summary>断开 / 故障用。整块面板里只有"信号中断"那一行会用到它。</summary>
        public static readonly Color AlarmColor = new Color(1.000f, 0.318f, 0.298f, 1.00f);

        /// <summary>面板后面压的一层黑：让 3D 场景退成背景，文字才压得住。</summary>
        public static readonly Color ScrimColor = new Color(0f, 0f, 0f, 0.42f);

        public static Color SpeakerColor(TerminalSpeaker speaker)
        {
            switch (speaker)
            {
                case TerminalSpeaker.Emit: return EmitColor;
                case TerminalSpeaker.Recv: return RecvColor;
                case TerminalSpeaker.Null: return NullColor;
                default: return SysColor;
            }
        }

        /// <summary>
        /// 说话人在面板上的名字。空说话人返回空串 —— 靠后面的 <c>PadRight</c> 补空格占位，
        /// 正文才不会跟着往左跑。
        /// </summary>
        public static string SpeakerLabel(TerminalSpeaker speaker)
        {
            switch (speaker)
            {
                case TerminalSpeaker.Emit: return "EMIT";
                case TerminalSpeaker.Recv: return "RECV";
                case TerminalSpeaker.Null: return string.Empty;
                default: return "SYS";
            }
        }
    }
}

