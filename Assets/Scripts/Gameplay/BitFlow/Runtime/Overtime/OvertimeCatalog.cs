using System;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 手牌在本题标准解里承担的职责。编号同时是目录里的紧凑写法（见 <see cref="OvertimeCatalog"/>）。
    /// </summary>
    public enum OvertimePieceRole
    {
        NotGate = 0,
        FilterZero = 1,
        FilterOne = 2,
        Splitter = 3,
    }

    /// <summary>一条直线路段：从上一段终点出发，沿 <see cref="Direction"/> 走 <see cref="Length"/> 格。</summary>
    public sealed class OvertimeRouteSegment
    {
        public readonly Dir Direction;
        public readonly int Length;

        public OvertimeRouteSegment(Dir direction, int length)
        {
            Direction = direction;
            Length = length;
        }
    }

    /// <summary>
    /// 一道 OVERTIME 的手工蓝图：固定棋盘尺寸、折线路线、每个转角的标准解元件，以及发牌顺序。
    /// 路线上的转角数就是本题需要的元件数；<see cref="HandOrder"/> 只改变发到手牌的顺序，
    /// 不影响标准解和目标值。
    /// </summary>
    public sealed class OvertimeBlueprint
    {
        public int Index;
        public int Width;
        public int Height;
        public GridPoint EmitterCoord;
        public Dir EmitterFacing;
        public int StartValue;
        public OvertimeRouteSegment[] Route;
        public OvertimePieceRole[] SlotRoles;
        public int[] HandOrder;

        public int SlotCount
        {
            get { return SlotRoles != null ? SlotRoles.Length : 0; }
        }
    }

    /// <summary>
    /// 12 道固定 OVERTIME 题目。全部由 <see cref="OvertimeSolver"/> 验证过唯一解，
    /// 尺寸按 7x6 → 9x8 → 11x8 → 13x9 逐档变长，路线和发牌顺序均为手工挑选。
    /// </summary>
    public static class OvertimeCatalog
    {
        public const int Count = 12;
        public const int FirstProblem = 1;

        static readonly OvertimeBlueprint[] Blueprints =
        {
            // 01 · 7x6 · 两次右转，先滤 0 再取反
            Define(1, 7, 6, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Up, 2), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.NotGate),
                Hand(1, 0)),

            // 02 · 7x6 · 先分叉再滤 0，认识分叉器
            Define(2, 7, 6, 1, 4, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Down, 2), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.Splitter, OvertimePieceRole.FilterZero),
                Hand(0, 1)),

            // 03 · 7x6 · 3 个转角的短 Z
            Define(3, 7, 6, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Up, 2), Seg(Dir.Right, 2), Seg(Dir.Down, 1)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.Splitter, OvertimePieceRole.NotGate),
                Hand(2, 1, 0)),

            // 04 · 9x8 · 大 U 掉头，滤 1 收尾
            Define(4, 9, 8, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 4), Seg(Dir.Up, 4), Seg(Dir.Left, 4), Seg(Dir.Up, 1)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.NotGate, OvertimePieceRole.FilterOne),
                Hand(1, 2, 0)),

            // 05 · 9x8 · S 形，两个分叉器连续改道
            Define(5, 9, 8, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Up, 4), Seg(Dir.Right, 2), Seg(Dir.Down, 4), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.Splitter, OvertimePieceRole.Splitter, OvertimePieceRole.FilterZero),
                Hand(3, 1, 0, 2)),

            // 06 · 9x8 · 回折到接收器背后，取反后滤 1
            Define(6, 9, 8, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Up, 4), Seg(Dir.Right, 3), Seg(Dir.Down, 4), Seg(Dir.Right, 1)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.FilterZero, OvertimePieceRole.NotGate, OvertimePieceRole.FilterOne),
                Hand(2, 3, 0, 1)),

            // 07 · 11x8 · 宽 U，先滤 1 再两次取反
            Define(7, 11, 8, 1, 0, Dir.Right, 0,
                Route(Seg(Dir.Right, 6), Seg(Dir.Up, 5), Seg(Dir.Left, 2), Seg(Dir.Up, 1)),
                Roles(OvertimePieceRole.NotGate, OvertimePieceRole.FilterOne, OvertimePieceRole.NotGate),
                Hand(1, 0, 2)),

            // 08 · 11x8 · 加宽 S，两个滤 1 夹住两次取反
            Define(8, 11, 8, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Up, 4), Seg(Dir.Right, 3), Seg(Dir.Down, 4), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.NotGate, OvertimePieceRole.FilterOne, OvertimePieceRole.FilterOne, OvertimePieceRole.NotGate),
                Hand(2, 0, 3, 1)),

            // 09 · 11x8 · 阶梯上升，分叉器改道后取反
            Define(9, 11, 8, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 2), Seg(Dir.Up, 2), Seg(Dir.Right, 2), Seg(Dir.Up, 2), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.Splitter, OvertimePieceRole.FilterZero, OvertimePieceRole.NotGate),
                Hand(1, 3, 0, 2)),

            // 10 · 13x9 · 钩形，先滤 0，分叉器把位流抬上去
            Define(10, 13, 9, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 4), Seg(Dir.Up, 2), Seg(Dir.Left, 2), Seg(Dir.Up, 2), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.FilterZero, OvertimePieceRole.Splitter, OvertimePieceRole.NotGate),
                Hand(3, 2, 0, 1)),

            // 11 · 13x9 · 长 Z，两个分叉器连续下降
            Define(11, 13, 9, 1, 2, Dir.Right, 0,
                Route(Seg(Dir.Right, 4), Seg(Dir.Up, 5), Seg(Dir.Right, 4), Seg(Dir.Down, 5), Seg(Dir.Right, 2)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.Splitter, OvertimePieceRole.Splitter, OvertimePieceRole.FilterZero),
                Hand(1, 0, 3, 2)),

            // 12 · 13x9 · 最终长 S，三连滤 0 后取反
            Define(12, 13, 9, 1, 1, Dir.Right, 0,
                Route(Seg(Dir.Right, 4), Seg(Dir.Up, 3), Seg(Dir.Right, 2), Seg(Dir.Down, 3), Seg(Dir.Right, 4)),
                Roles(OvertimePieceRole.FilterZero, OvertimePieceRole.FilterZero, OvertimePieceRole.FilterZero, OvertimePieceRole.NotGate),
                Hand(3, 0, 2, 1)),
        };

        public static bool TryGet(int index, out OvertimeBlueprint blueprint)
        {
            int offset = index - FirstProblem;
            if (offset < 0 || offset >= Blueprints.Length)
            {
                blueprint = null;
                return false;
            }

            blueprint = Blueprints[offset];
            return true;
        }

        public static OvertimeBlueprint Get(int index)
        {
            OvertimeBlueprint blueprint;
            if (!TryGet(index, out blueprint))
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "OVERTIME 题号超出 1.." + Count);
            }

            return blueprint;
        }

        /// <summary>按题号生成布局；调用方拿到的 <see cref="OvertimeLayout"/> 已经是完整的标准解形状。</summary>
        public static OvertimeLayout Build(int index)
        {
            return OvertimeLayoutBuilder.Build(Get(index));
        }

        static OvertimeBlueprint Define(
            int index, int width, int height, int emitterX, int emitterY, Dir facing, int startValue,
            OvertimeRouteSegment[] route, OvertimePieceRole[] roles, int[] handOrder)
        {
            return new OvertimeBlueprint
            {
                Index = index,
                Width = width,
                Height = height,
                EmitterCoord = new GridPoint(emitterX, emitterY),
                EmitterFacing = facing,
                StartValue = startValue,
                Route = route,
                SlotRoles = roles,
                HandOrder = handOrder,
            };
        }

        static OvertimeRouteSegment Seg(Dir direction, int length)
        {
            return new OvertimeRouteSegment(direction, length);
        }

        static OvertimeRouteSegment[] Route(params OvertimeRouteSegment[] segments)
        {
            return segments;
        }

        static OvertimePieceRole[] Roles(params OvertimePieceRole[] roles)
        {
            return roles;
        }

        static int[] Hand(params int[] order)
        {
            return order;
        }
    }
}
