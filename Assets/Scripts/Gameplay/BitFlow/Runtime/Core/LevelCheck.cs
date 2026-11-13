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
            return Run(definition, definition != null && definition.HasSupplies, out success);
        }

        /// <summary>
        /// 同上，但由调用方告诉这里「这一关有没有给玩家手牌」。
        ///
        /// 为什么要单独传：<c>LevelSO.ToDefinition()</c> 只转 placements + 墙，<b>不转 supplies</b>
        /// （手牌是玩家局的素材，不是逻辑关卡的一部分），所以从关卡资产来的 definition 里
        /// <c>HasSupplies</c> 永远是 false。报告措辞要区分「题还没解」和「数据错了」就得知道这件事，
        /// 而从资产来的调用方手里正好有 <c>LevelSO.HasSupplies</c>。
        /// </summary>
        public static string Run(LevelDefinition definition, bool hasSupplies, out bool success)
        {
            success = false;
            if (definition == null) return "关卡定义是空的";

            BitFlowSimulation simulation = new BitFlowSimulation(definition);
            // 上限必须与模拟器一致才有意义：模拟器自己会在 SafetyTickLimit（默认 5000）停下，
            // 而 W*H*8 在「多发射器 + 长路径 + 延迟器」的关卡里远远不够 ——
            // 那时自检会在还没结算时就下结论，游戏内试跑却能跑完，作者看到的就是「能过却说不能过」。
            int safety = Math.Max(SafetyTicks(definition), simulation.SafetyTickLimit);
            int ticks = 0;
            while (!simulation.IsFinished && ticks < safety)
            {
                simulation.Step();
                ticks++;
            }

            LevelResult result = simulation.BuildResult();
            success = result.IsSuccess;
            return Describe(definition, hasSupplies, result, ticks, safety);
        }

        static string Describe(LevelDefinition definition, bool hasSupplies, LevelResult result, int ticks, int safety)
        {
            StringBuilder builder = new StringBuilder();

            // 有手牌的关，预设布局跑不通才是对的（规则见 RunForSave：跑通了说明手牌是摆设）。
            // 这里用 ❌ 会把正常的关卡设计报成事故，作者会以为导入坏了 ——
            // 措辞必须能一眼区分「题还没解」和「数据错了」。
            bool puzzle = hasSupplies && !result.IsSuccess;

            if (result.IsSuccess) builder.Append("✅ 通关 ");
            else if (puzzle) builder.Append("📝 预设布局未通关 ");
            else builder.Append("❌ 未通过 ");

            builder.Append(result.TickCount).Append(" ticks");
            if (puzzle) builder.Append("（有手牌的关，这是预期：玩家要自己用牌解开）");

            // 「跑到上限还没结算」不等于关卡数据错了，措辞要分开。
            if (!result.IsSuccess && ticks >= safety)
            {
                builder.Append("（跑到 ").Append(safety)
                    .Append(" tick 上限还没结算，可能有个位卡住了；这是自检上限，不是关卡数据错误）");
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

        // ------------------------------------------------------------------ 存档前自检

        /// <summary>
        /// 存档前的「必要条件」自检：只查那些<b>无论玩家怎么动都救不回来</b>的错。
        ///
        /// 为什么不是"跑一遍模拟能过就存"：
        /// - 没有手牌的关卡（演示关）就该能直接跑通 —— 和以前的规则一致；
        /// - 有手牌的关卡，作者摆的预设布局必须**跑不通**。预设布局自己就能通关，
        ///   说明玩家一张牌都不用出，那手牌就是摆设 —— 这是"有限元件解谜"最常见的设计事故，
        ///   而且模拟器一眼就能看出来，不该等玩家进关才发现；
        /// - 空位必须够放下所有牌：4×4 的板发 20 张牌，一定有几张永远放不下。
        ///
        /// 真正的"这关一定有人能解"要等人去玩，所以这里只叫必要条件；
        /// 完整验证留给工坊的 PLAYTEST AS PLAYER（phase 2）。
        ///
        /// 返回空表 = 可以存。每条都是能直接念给作者听的中文。
        /// </summary>
        /// <summary>存档前自检里一条意见的严重程度。</summary>
        public enum SaveIssueSeverity
        {
            /// <summary>一定要改，否则存下来也是坏关（结构缺失 / 发牌发错种类 / 空位不够）。</summary>
            Blocking,

            /// <summary>设计上的提醒，作者确认后可以照存（目前只有「手牌是摆设」）。</summary>
            Advisory,
        }

        /// <summary>一条自检意见：严重程度 + 能直接念给作者听的中文。</summary>
        public sealed class SaveIssue
        {
            public SaveIssueSeverity Severity;
            public string Message;
        }

        public static List<string> RunForSave(LevelDefinition definition)
        {
            List<string> problems = new List<string>();
            foreach (SaveIssue issue in RunForSaveDetailed(definition))
            {
                if (issue.Severity == SaveIssueSeverity.Blocking) problems.Add(issue.Message);
            }
            return problems;
        }

        /// <summary>
        /// 同一次自检，但把「一定要改」和「提醒一下」分开（见 <see cref="SaveIssueSeverity"/>）。
        ///
        /// 工坊 DONE 用这一版：Blocking 拒绝保存，Advisory 只提示、再点一次 DONE 就照存 ——
        /// 「手牌是摆设」是设计口味问题（比如机器本身自洽、作者只想再给玩家一张 B 卡），
        /// 不该让作者连关都存不下来；外部编辑器一直就是这么处理的。
        /// </summary>
        public static List<SaveIssue> RunForSaveDetailed(LevelDefinition definition)
        {
            List<SaveIssue> problems = new List<SaveIssue>();
            if (definition == null)
            {
                problems.Add(Blocking("关卡定义是空的"));
                return problems;
            }

            int receivers = 0;
            int emitters = 0;
            foreach (ElementDefinition element in definition.Elements)
            {
                if (element is ReceiverDefinition) receivers++;
                else if (element is EmitterDefinition) emitters++;
            }

            if (receivers == 0) problems.Add(Blocking("这一关没有接收器，没法判定通关"));
            if (emitters == 0) problems.Add(Blocking("这一关没有发射器，场上永远不会有位"));

            // 这条规则本身不看有没有接收器：没有接收器时 Run() 必然失败，
            // 拿它当"预设布局跑不通"的证据没有意义，留给上面那两条去报。
            string report = Run(definition, out bool solved);

            if (!definition.HasSupplies)
            {
                if (receivers > 0 && emitters > 0 && !solved)
                {
                    problems.Add(Blocking("没有手牌，但预设布局跑不通 —— 要么补件，要么发牌：" + FirstLine(report)));
                }
            }
            else
            {
                if (solved)
                {
                    // 设计口味问题，不是数据错误：工坊会提示，作者再点一次 DONE 就照存。
                    problems.Add(Advisory("预设布局自己就能通关 —— 玩家一张牌都不用出，手牌是摆设"));
                }

                foreach (LevelSupplyEntry supply in definition.Supplies)
                {
                    if (supply == null) continue;

                    if (!SupplyKinds.IsSupplyKind(supply.Kind))
                    {
                        problems.Add(Blocking("手牌里有" + supply.Kind + "：" +
                            "发射器 / 接收器定的是这一关的题，只能由作者给定，不能发给玩家"));
                    }
                }

                int occupied = definition.Elements.Count;
                int empty = definition.Width * definition.Height - occupied;
                int total = 0;
                foreach (LevelSupplyEntry supply in definition.Supplies)
                {
                    if (supply != null) total += Math.Max(0, supply.Count);
                }

                if (empty < total)
                {
                    problems.Add(Blocking("空位只有 " + empty + " 格，却发了 " + total + " 张牌 —— 一定有几张放不下"));
                }
            }

            return problems;
        }

        static SaveIssue Blocking(string message)
        {
            return new SaveIssue { Severity = SaveIssueSeverity.Blocking, Message = message };
        }

        static SaveIssue Advisory(string message)
        {
            return new SaveIssue { Severity = SaveIssueSeverity.Advisory, Message = message };
        }

        /// <summary>报告的第一行（"✅ 通关 12 ticks" 这种），拼进问题描述里给作者一句上下文。</summary>
        static string FirstLine(string report)
        {
            if (string.IsNullOrEmpty(report)) return string.Empty;

            int index = report.IndexOf('\n');
            return index <= 0 ? report : report.Substring(0, index);
        }
    }
}