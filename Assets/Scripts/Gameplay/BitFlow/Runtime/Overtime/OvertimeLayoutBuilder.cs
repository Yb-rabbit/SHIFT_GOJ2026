using System;
using System.Collections.Generic;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 把 <see cref="OvertimeBlueprint"/> 展开成可玩的走廊：
    ///
    ///   E -> [转角] ^ [转角] -> [转角] v [转角] -> R
    ///
    /// 走廊之外的格子全部填成固定墙。玩家只有转角格可放件，每个转角都必须把位转向下一段，
    /// 因此“所有件都必须用上”是题面本身保证的，求解器只需要枚举转角格的排列 + 旋转。
    /// </summary>
    public static class OvertimeLayoutBuilder
    {
        const string NotKey = "Type_NotGate_N1";
        const string SplitterKey = "Type_Splitter_L1";
        const string Filter0Key = "Type_Filter_F0";
        const string Filter1Key = "Type_Filter_F1";

        public static OvertimeLayout Build(OvertimeBlueprint blueprint)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            if (blueprint.Width < 5 || blueprint.Height < 5)
                throw new InvalidOperationException("OVERTIME 网格太小");
            if (blueprint.Route == null || blueprint.Route.Length < 2)
                throw new InvalidOperationException("OVERTIME 路线至少需要一段出发和一次折返");
            if (blueprint.StartValue != 0 && blueprint.StartValue != 1)
                throw new InvalidOperationException("OVERTIME 起始位只能是 0 或 1");

            int slotCount = blueprint.Route.Length - 1;
            if (blueprint.SlotRoles == null || blueprint.SlotRoles.Length != slotCount)
                throw new InvalidOperationException("OVERTIME 蓝图转角数与元件数对不上");

            int[] handOrder = ResolveHandOrder(blueprint.HandOrder, slotCount);

            OvertimeLayout layout = new OvertimeLayout
            {
                Width = blueprint.Width,
                Height = blueprint.Height,
                StartValue = blueprint.StartValue,
                EmitterCoord = blueprint.EmitterCoord,
                EmitterFacing = blueprint.EmitterFacing,
            };

            if (!Inside(layout, layout.EmitterCoord))
                throw new InvalidOperationException("OVERTIME 发射器在棋盘外");

            HashSet<GridPoint> path = new HashSet<GridPoint>();
            GridPoint cursor = layout.EmitterCoord;
            Dir previous = blueprint.EmitterFacing;

            for (int i = 0; i < blueprint.Route.Length; i++)
            {
                OvertimeRouteSegment segment = blueprint.Route[i];
                if (segment == null || segment.Length < 1)
                    throw new InvalidOperationException("OVERTIME 路线存在空路段");

                if (i > 0)
                {
                    // 上一段的终点就是这一段的转角；它已经在 path 里，不重复计入。
                    layout.Slots.Add(new OvertimeSlot(cursor, previous, segment.Direction));
                }

                GridPoint offset = DirUtil.ToOffset(segment.Direction);
                for (int step = 0; step < segment.Length; step++)
                {
                    cursor = cursor + offset;
                    if (!Inside(layout, cursor))
                        throw new InvalidOperationException("OVERTIME 路线走出棋盘");
                    if (!path.Add(cursor))
                        throw new InvalidOperationException("OVERTIME 路线自交");
                    layout.PathCells.Add(cursor);
                }

                previous = segment.Direction;
            }

            GridPoint receiver = cursor + DirUtil.ToOffset(blueprint.Route[blueprint.Route.Length - 1].Direction);
            if (!Inside(layout, receiver))
                throw new InvalidOperationException("OVERTIME 接收器在棋盘外");
            if (receiver == layout.EmitterCoord || path.Contains(receiver))
                throw new InvalidOperationException("OVERTIME 接收器与走廊重叠");
            layout.ReceiverCoord = receiver;

            for (int y = 0; y < layout.Height; y++)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    GridPoint coord = new GridPoint(x, y);
                    if (coord == layout.EmitterCoord || coord == layout.ReceiverCoord) continue;
                    if (path.Contains(coord)) continue;
                    layout.Walls.Add(coord);
                }
            }

            int[] inputValues = new int[slotCount];
            int value = blueprint.StartValue;

            for (int i = 0; i < slotCount; i++)
            {
                OvertimeSlot slot = layout.Slots[i];
                OvertimePieceRole role = blueprint.SlotRoles[i];
                inputValues[i] = value;

                switch (role)
                {
                    case OvertimePieceRole.NotGate:
                        value = value == 1 ? 0 : 1;
                        break;
                    case OvertimePieceRole.FilterZero:
                        if (value != 0)
                            throw new InvalidOperationException("OVERTIME 标准解在滤 0 处被挡住");
                        break;
                    case OvertimePieceRole.FilterOne:
                        if (value != 1)
                            throw new InvalidOperationException("OVERTIME 标准解在滤 1 处被挡住");
                        break;
                    case OvertimePieceRole.Splitter:
                        if (!OvertimePiece.TrySplitterRotation((byte)value, slot.OutDir, out _))
                            throw new InvalidOperationException("OVERTIME 分叉器无法转向标准解出口");
                        break;
                    default:
                        throw new InvalidOperationException("OVERTIME 蓝图含未知元件");
                }
            }

            layout.TargetValue = value;

            for (int hand = 0; hand < handOrder.Length; hand++)
            {
                int slotIndex = handOrder[hand];
                OvertimeSlot slot = layout.Slots[slotIndex];
                layout.Pieces.Add(CreatePiece(blueprint.SlotRoles[slotIndex], inputValues[slotIndex], slot.OutDir));
            }

            int last = layout.Slots.Count - 1;
            layout.ReceiverFeedFace = layout.Slots[last].OutDir.Opposite();
            return layout;
        }

        static OvertimePiece CreatePiece(OvertimePieceRole role, int inputValue, Dir outDir)
        {
            switch (role)
            {
                case OvertimePieceRole.NotGate:
                    return new OvertimePiece
                    {
                        TypeKey = NotKey,
                        Kind = CellKind.NotGate,
                        SolutionRotation = OvertimePiece.RotationForOutput(outDir),
                    };

                case OvertimePieceRole.FilterZero:
                case OvertimePieceRole.FilterOne:
                {
                    byte pass = role == OvertimePieceRole.FilterOne ? (byte)1 : (byte)0;
                    return new OvertimePiece
                    {
                        TypeKey = pass == 1 ? Filter1Key : Filter0Key,
                        Kind = CellKind.Filter,
                        PassValue = pass,
                        SolutionRotation = OvertimePiece.RotationForOutput(outDir),
                    };
                }

                case OvertimePieceRole.Splitter:
                {
                    int rotation;
                    if (!OvertimePiece.TrySplitterRotation((byte)inputValue, outDir, out rotation))
                        throw new InvalidOperationException("OVERTIME 分叉器无法转向标准解出口");
                    return new OvertimePiece
                    {
                        TypeKey = SplitterKey,
                        Kind = CellKind.Splitter,
                        SolutionRotation = rotation,
                    };
                }

                default:
                    throw new InvalidOperationException("OVERTIME 蓝图含未知元件");
            }
        }

        static int[] ResolveHandOrder(int[] handOrder, int slotCount)
        {
            if (handOrder == null)
            {
                int[] identity = new int[slotCount];
                for (int i = 0; i < slotCount; i++) identity[i] = i;
                return identity;
            }

            if (handOrder.Length != slotCount)
                throw new InvalidOperationException("OVERTIME 发牌顺序长度与转角数对不上");

            bool[] seen = new bool[slotCount];
            for (int i = 0; i < handOrder.Length; i++)
            {
                int index = handOrder[i];
                if (index < 0 || index >= slotCount || seen[index])
                    throw new InvalidOperationException("OVERTIME 发牌顺序不是完整排列");
                seen[index] = true;
            }

            return handOrder;
        }

        static bool Inside(OvertimeLayout layout, GridPoint coord)
        {
            return coord.X >= 0 && coord.X < layout.Width && coord.Y >= 0 && coord.Y < layout.Height;
        }
    }
}
