using System.Collections.Generic;
using System.Text;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>单个接收器的运行时状态快照（给 UI 显示 "已收 3/6" 用）。</summary>
    public sealed class ReceiverProgress
    {
        public int ReceiverId;
        public int TargetCount;
        public int ReceivedCount;

        /// <summary>到目前为止是否全部匹配（超量也算失败）。</summary>
        public bool IsStillValid = true;

        public bool IsOverflow => ReceivedCount > TargetCount;

        public bool IsComplete => ReceivedCount >= TargetCount;
    }

    /// <summary>一关的结算结果。</summary>
    public sealed class LevelResult
    {
        public string DisplayName = string.Empty;

        public int TickCount;

        /// <summary>所有接收器都恰好收满且每一位都匹配 → 通关。</summary>
        public bool IsSuccess;

        public readonly List<ReceiverResult> Receivers = new List<ReceiverResult>();

        /// <summary>失败/成功的一句话结论，UI 直接显示。</summary>
        public string Summary()
        {
            if (IsSuccess) return "ALL RECEIVERS LOCKED IN " + TickCount + " TICKS";

            StringBuilder builder = new StringBuilder("MISMATCH");
            foreach (ReceiverResult receiver in Receivers)
            {
                builder.Append("  |  R").Append(receiver.ReceiverId).Append(' ');
                builder.Append(receiver.ReceivedCount).Append('/').Append(receiver.TargetCount);
                builder.Append(receiver.IsSuccess ? " ok" : " bad");
            }
            return builder.ToString();
        }
    }

    /// <summary>单个接收器的结算明细。</summary>
    public sealed class ReceiverResult
    {
        public int ReceiverId;
        public int TargetCount;
        public int ReceivedCount;

        public readonly List<byte> Received = new List<byte>();
        public readonly List<bool> Matched = new List<bool>();

        public bool IsSuccess
        {
            get
            {
                if (ReceivedCount != TargetCount) return false;
                for (int i = 0; i < Matched.Count; i++)
                {
                    if (!Matched[i]) return false;
                }
                return true;
            }
        }
    }
}
