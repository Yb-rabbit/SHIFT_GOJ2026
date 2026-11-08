namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 全局输入闸门：黑幕演出、选关面板打开、换关的瞬间把玩家输入挡在外面。
    ///
    /// 只做一件事 —— 谁要挡输入谁就 <see cref="Acquire"/>，放开时 <see cref="Release"/>。
    /// 手牌盘、悬停提示、右键菜单都只读这一个开关，彼此不需要认识。
    ///
    /// 注意：手牌盘不能靠 <c>enabled = false</c> 来挡 —— 它在 Start 里订阅了
    /// <see cref="LevelFlow.LevelLoaded"/>，关掉组件会连带把新关卡的刷新一起停掉。
    ///
    /// 用引用计数而不是单个 bool：选关面板关掉的同一帧黑幕已经盖上来，
    /// 两边各持一把锁，谁先放谁后放都不会把对方的锁解掉。
    /// </summary>
    public static class InputGate
    {
        static int holders;

        /// <summary>是否正在挡输入。</summary>
        public static bool Locked => holders > 0;

        /// <summary>挡住输入（可与其它模块叠加）。</summary>
        public static void Acquire()
        {
            holders++;
        }

        /// <summary>放开自己那一道锁。多放不报错，但会把计数兜到 0。</summary>
        public static void Release()
        {
            holders = holders > 0 ? holders - 1 : 0;
        }

        /// <summary>兜底：所有锁一次性清掉（退出演出、场景重载时用）。</summary>
        public static void ReleaseAll()
        {
            holders = 0;
        }
    }
}
