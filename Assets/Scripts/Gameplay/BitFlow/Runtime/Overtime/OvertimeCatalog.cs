using System;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 手牌在本题标准解里承担的职责。编号同时是目录里的紧凑写法。
    /// </summary>
    public enum OvertimePieceRole
    {
        NotGate = 0,
        FilterZero = 1,
        FilterOne = 2,
        Splitter = 3,

        /// <summary>只改时间不改路由的延迟器。碰撞题用它把一条位流整体错开，避开/制造同格争抢。</summary>
        DelayGate = 4,
    }

    /// <summary>
    /// 8 道固定 OVERTIME 题目。题面全部由 <see cref="OvertimeCollisionCatalog"/> 提供，
    /// 旧走廊蓝图已删除；调用方统一用 <see cref="Build"/> 拿 <see cref="OvertimeLayout"/>。
    /// </summary>
    public static class OvertimeCatalog
    {
        public const int Count = OvertimeCollisionCatalog.Count;
        public const int FirstProblem = 1;

        /// <summary>旧走廊蓝图入口已废弃；保留签名供旧调用方编译，但永远返回 false。</summary>
        public static bool TryGet(int index, out object blueprint)
        {
            blueprint = null;
            return false;
        }

        /// <summary>按题号生成布局；调用方拿到的 <see cref="OvertimeLayout"/> 已经是完整的标准解形状。</summary>
        public static OvertimeLayout Build(int index)
        {
            if (OvertimeCollisionCatalog.TryBuild(index, out OvertimeLayout layout)) return layout;
            throw new ArgumentOutOfRangeException(nameof(index), index, "OVERTIME 题号超出 1.." + Count);
        }
    }
}
