using System;
using System.Collections.Generic;
using System.Text;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 关卡自检：把一份关卡定义丢进 <see cref="BitFlowSimulation"/> 跑到底，返回一句人话报告。
    ///
    /// 关卡是纯数据 —— 改一个发射序列或接收数就可能让整关从能通变成不能通，
    /// 而 Play 一关要等动画、要点鼠标。这里一次跑完拿结论：作者改数据前跑一次、
    /// 改完再跑一次，立刻知道自己动的是对还是错。
    ///
    /// 它和玩家通关走的是同一个模拟器，所以这里通过 ≈ 玩家按同一份数据也能通过。
    /// Editor 侧的「试跑这一关」与批量自检菜单共用这一份实现。
    /// </summary>
    public static class LevelCheck
    {
        /// <summary>安全上限：跑过这么多 tick 还没结算，就判定「有位卡住了」而不是无限循环。</summary>
        public static int SafetyTicks(LevelDefinition definition)
        {
            if (definition == null) return 128;
            return Math.Max(128, definition.Width * definition.Height * 8);
        }

        /// <summary>跑一遍并给出报告。success = 是否通关。</summary>
        public static string Run(LevelDefinition definition, out bool success)
        {
            success = false;
            if (definition == null) return "关卡定义是空的";

            BitFlowSimulation simulation = new BitFlowSimulation(definition);
            int safety = SafetyTicks(definition);
            int ticks = 0;
            while (!simulation.IsFinished && ticks < safety)
            {
                simulation.Step();
                ticks++;
            }

            LevelResult result = simulation.BuildResult();
            success = result.IsSuccess;
            return Describe(definition, result, ticks, safety);
        }

        static string Describe(LevelDefinition definition, LevelResult result, int ticks, int safety)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(result.IsSuccess ? "✅ 通关 " : "❌ 未通过 ");
            builder.Append(result.TickCount).Append(" ticks");

            if (!result.IsSuccess && ticks >= safety)
            {
                builder.Append("（跑到 ").Append(safety).Append(" tick 上限还没结算，可能有个位卡住了）");
            }

            if (result.Receivers.Count == 0)
            {
                builder.Append("\n这一关没有接收器，没法判定通关");
                return builder.ToString();
            }

            foreach (ReceiverResult receiver in result.Receivers)
            {
                builder.Append("\n  R").Append(receiver.ReceiverId).Append("   ");
                builder.Append(receiver.ReceivedCount).Append('/').Append(receiver.TargetCount);

                if (receiver.ReceivedCount > receiver.TargetCount) builder.Append(" 超收");
                else if (receiver.ReceivedCount < receiver.TargetCount) builder.Append(" 少收");

                builder.Append(receiver.IsSuccess ? "  ✓" : "  ✗");
                builder.Append("   实收 ").Append(BitsText(receiver.Received, 24));

                if (receiver.IsSuccess) continue;

                // 指出第一个不匹配的位置：作者一眼能定位是哪一段节奏错了
                ReceiverDefinition source = FindReceiver(definition, receiver.ReceiverId);
                for (int i = 0; i < receiver.Matched.Count; i++)
                {
                    if (receiver.Matched[i]) continue;

                    builder.Append("\n      第 ").Append(i + 1).Append(" 位不匹配：期望 ");
                    builder.Append(source != null ? source.ExpectedValueAt(i) : (byte)1);
                    builder.Append(" 实收 ").Append(receiver.Received[i]);
                    break;
                }
            }

            return builder.ToString();
        }

        /// <summary>把位的取值拼成 "1011" 这种串（超过 maxShow 位就截断）。</summary>
        public static string BitsText(List<byte> bits, int maxShow = 32)
        {
            if (bits == null || bits.Count == 0) return "（空）";

            StringBuilder builder = new StringBuilder(bits.Count + 8);
            int show = Math.Min(bits.Count, maxShow);
            for (int i = 0; i < show; i++) builder.Append(bits[i] == 0 ? '0' : '1');
            if (bits.Count > show) builder.Append("…（共 ").Append(bits.Count).Append(" 位）");
            return builder.ToString();
        }

        static ReceiverDefinition FindReceiver(LevelDefinition definition, int receiverId)
        {
            foreach (ElementDefinition element in definition.Elements)
            {
                if (element is ReceiverDefinition receiver && receiver.Id == receiverId) return receiver;
            }
            return null;
        }
    }
}