namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 元件说明文本（悬停详情 / 提示框）的唯一拼装口。
    ///
    /// 为什么不能直接贴 <c>ElementTypeSO.Describe()</c>：类型资产说的是**预设值** ——
    /// 发射器「1011 x1」、延迟器「HOLD 1 TICK」、朝向还是资产的基准朝向。
    /// 关卡里同一件元件往往被格子参数覆盖过（sequence / count / face / pass / delay），
    /// 还会被整体旋转，把资产的说明原样写进 <see cref="ElementDefinition.Hint"/>，
    /// 详情 UI 就会出现「写着延迟 1 tick、实际跑 3 tick」「写着 FACE →、实际朝下」这类对不上。
    ///
    /// 所以规则只有一条：**参数行一律按这一件的真实定义重算**，
    /// 只有标题行（"EMITTER E1" 这种带 label 的名字）沿用类型资产 / 类型目录给的文本。
    /// 没改过参数、没转过向的老关卡，标题和各字段的写法都和以前一样
    /// （唯一一处换算：发射器那行从资产的「轮数」换成同义的总位数，和自定义关卡的写法统一）。
    /// </summary>
    public static class ElementHint
    {
        /// <summary>没有类型资产可借标题时的整段说明（标题退回种类名）。</summary>
        public static string Describe(ElementDefinition element)
        {
            return Describe(element, string.Empty);
        }

        /// <summary>
        /// 用 <paramref name="presetText"/> 的第一行当标题（类型资产的 <c>Describe()</c>、
        /// 类型目录里的 hint），参数行按实际定义重算。预设文本为空时标题退回种类名。
        /// </summary>
        public static string Describe(ElementDefinition element, string presetText)
        {
            if (element == null) return string.Empty;

            string title = FirstLine(presetText);

            // 标题可能是 <see cref="EditableElement.Summary"/> 那种中文（kind 模式 + 类型目录里
            // 没有同类资产时的兜底）。悬停提示用的是像素字体，中文打包后全是方块，
            // 这种标题宁可退回种类名。
            if (!IsAscii(title)) title = KindName(element);

            string parameters = Parameters(element);
            return string.IsNullOrEmpty(parameters) ? title : title + "\n" + parameters;
        }

        /// <summary>
        /// 参数行（全 ASCII 单词 + 箭头）：像素字体没有中文字形。墙没有可调参数，返回空串。
        /// </summary>
        public static string Parameters(ElementDefinition element)
        {
            switch (element)
            {
                case EmitterDefinition emitter:
                    // 这个数字是「总位数」（= 序列循环填充到几位），不是资产上的「轮数」：
                    // 详情要和模拟器真正发出去的位数一致，别让玩家按轮数去猜。
                    return emitter.Sequence + " x" + emitter.EmitCount + "  FACE " + emitter.Facing.ToMark();

                case ReceiverDefinition receiver:
                    string mouth = receiver.AcceptAnyFace ? "ANY FACE" : "IN " + receiver.FeedFace.ToMark();
                    return "NEED " + receiver.TargetSequence + " x" + receiver.TargetCount + "  " + mouth;

                case SplitterDefinition splitter:
                    return "1 " + splitter.OneArm.ToMark() + "   0 " + splitter.ZeroArm.ToMark();

                case NotGateDefinition notGate:
                    return "INVERT  OUT " + notGate.OutputDir.ToMark();

                case FilterDefinition filter:
                    return "PASS " + (filter.PassValue == 0 ? 0 : 1) + "  OUT " + filter.OutputDir.ToMark();

                case DelayGateDefinition delayGate:
                    return "HOLD " + delayGate.SafeDelayTicks + " TICK  THROUGH";

                default:
                    return string.Empty;
            }
        }

        /// <summary>种类名。和类型资产的标题、<c>LevelBuilder</c> 以前的兜底写法是同一套。</summary>
        public static string KindName(ElementDefinition element)
        {
            if (element == null) return string.Empty;

            switch (element.Kind)
            {
                case CellKind.Emitter: return "EMITTER";
                case CellKind.Receiver: return "RECEIVER";
                case CellKind.Splitter: return "SPLITTER";
                case CellKind.NotGate: return "NOT";
                case CellKind.Filter: return "FILTER";
                case CellKind.DelayGate: return "DELAY";
                default: return "WALL";
            }
        }

        /// <summary>
        /// 取第一行。预设说明是「标题 + 参数」两行（类型资产为了卡片好看是这么写的），
        /// 第二行是资产自己的参数，早就可能被格子覆盖掉，不能直接留用。
        /// </summary>
        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            int end = text.IndexOfAny(LineBreaks);
            string line = end < 0 ? text : text.Substring(0, end);
            return line.Trim();
        }

        static readonly char[] LineBreaks = { '\r', '\n' };

        /// <summary>能不能安全地画在像素字体上（ASCII 可见字符 + 空格；箭头是 BMP 非 ASCII，只出现在参数行）。</summary>
        static bool IsAscii(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < ' ' || c > '~') return false;
            }

            return true;
        }
    }
}
