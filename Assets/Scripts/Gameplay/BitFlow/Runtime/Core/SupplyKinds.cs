namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 「这一种元件能不能当手牌发给玩家」的唯一判定。
    ///
    /// 为什么要有这条线：手牌是玩家<b>改</b>关卡的手段，不是<b>定义</b>关卡的手段。
    /// - 发射器决定这一关往场上发什么位（以及发多少），接收器决定通关条件 ——
    ///   让玩家自己摆，等于把出题权交给玩家，关卡立刻失去意义；
    /// - 墙 / 非门 / 过滤器 / 分流器都只是"把已有的位怎么走"，玩家怎么摆都是解题，不会改题。
    ///
    /// 所以工坊的调色板、发牌面板、开档自检三处共用这一份判定，
    /// 免得以后加元件时三个地方各记一遍、还记拧了。
    /// </summary>
    public static class SupplyKinds
    {
        /// <summary>这一种元件能发成手牌吗。</summary>
        public static bool IsSupplyKind(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Wall:
                case CellKind.Splitter:
                case CellKind.NotGate:
                case CellKind.Filter:
                case CellKind.DelayGate:
                    return true;

                default:
                    // Emitter / Receiver
                    return false;
            }
        }
    }
}
