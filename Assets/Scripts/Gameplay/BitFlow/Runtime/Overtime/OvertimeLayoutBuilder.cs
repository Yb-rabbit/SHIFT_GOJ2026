using System;
using System.Collections.Generic;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 构造一条确定性的“蛇形走廊”：
    ///
    ///   E -> [转角] ^ [转角] -> [转角] v [转角] -> R
    ///
    /// 走廊之外的格子全部填成固定墙。这样玩家只有转角格可放件，
    /// 而每个转角都必须把位转向下一段，因此“所有件都必须用上”是题面本身保证的，
    /// 求解器只需要枚举转角格的排列 + 旋转，不必搜索整个棋盘。
    /// </summary>
    public static class OvertimeLayoutBuilder
    {
        const string NotKey = "Type_NotGate_N1";
        const string SplitterKey = "Type_Splitter_L1";
        const string Filter0Key = "Type_Filter_F0";
        const string Filter1Key = "Type_Filter_F1";

        public static OvertimeLayout Build(OvertimeProfile profile, OvertimeRng rng)
        {
            return Build(profile, rng, profile.TargetSteps);
        }

        public static OvertimeLayout Build(OvertimeProfile profile, OvertimeRng rng, int targetSteps)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int width = profile.Width;
            int height = profile.Height;
            int steps = targetSteps >= 6 ? 6 : targetSteps >= 4 ? 4 : 2;

            int y0 = 1;
            int y1 = Math.Max(y0 + 2, height - 2);

            List<int> turnXs = ChooseTurnColumns(width, steps, rng);
            if (turnXs.Count == 0) throw new InvalidOperationException("OVERTIME 网格太小，放不下走廊");

            OvertimeLayout layout = new OvertimeLayout
            {
                Width = width,
                Height = height,
                EmitterCoord = new GridPoint(1, y0),
                EmitterFacing = Dir.Right,
            };

            HashSet<GridPoint> path = new HashSet<GridPoint>();
            GridPoint cursor = layout.EmitterCoord;
            int y = y0;

            foreach (int turnX in turnXs)
            {
                while (cursor.X < turnX)
                {
                    cursor = new GridPoint(cursor.X + 1, cursor.Y);
                    path.Add(cursor);
                }

                Dir vertical = y == y0 ? Dir.Up : Dir.Down;
                AddTurn(layout, path, cursor, Dir.Right, vertical);

                int targetY = y == y0 ? y1 : y0;
                while (cursor.Y != targetY)
                {
                    cursor = new GridPoint(cursor.X, cursor.Y + (vertical == Dir.Up ? 1 : -1));
                    path.Add(cursor);
                }

                AddTurn(layout, path, cursor, vertical, Dir.Right);
                y = targetY;
            }

            layout.ReceiverCoord = new GridPoint(cursor.X + 1, cursor.Y);
            if (layout.ReceiverCoord.X >= width)
                throw new InvalidOperationException("OVERTIME 走廊没有给接收器留下位置");

            for (int yy = 0; yy < height; yy++)
            {
                for (int xx = 0; xx < width; xx++)
                {
                    GridPoint coord = new GridPoint(xx, yy);
                    if (coord == layout.EmitterCoord || coord == layout.ReceiverCoord) continue;
                    if (path.Contains(coord)) continue;
                    layout.Walls.Add(coord);
                }
            }

            AssignPieces(layout, rng);
            return layout;
        }

        static void AddTurn(OvertimeLayout layout, HashSet<GridPoint> path, GridPoint coord, Dir inDir, Dir outDir)
        {
            if (path.Add(coord)) layout.PathCells.Add(coord);
            layout.Slots.Add(new OvertimeSlot(coord, inDir, outDir));
        }

        static List<int> ChooseTurnColumns(int width, int steps, OvertimeRng rng)
        {
            List<int> result = new List<int>();
            if (width < 7) return result;

            if (steps < 4)
            {
                int max = width - 3;
                int x = 2 + rng.Next(Math.Max(1, max - 1));
                if (x > max) x = max;
                result.Add(x);
                return result;
            }

            if (steps < 6)
            {
                int maxFirst = width - 4;
                int first = 2 + rng.Next(Math.Max(1, maxFirst - 1));
                if (first > maxFirst) first = maxFirst;

                int minSecond = first + 1;
                int maxSecond = width - 3;
                int second = minSecond + rng.Next(Math.Max(1, maxSecond - minSecond + 1));
                if (second > maxSecond) second = maxSecond;

                result.Add(first);
                result.Add(second);
                return result;
            }

            // 6 件 = 3 次上下折返，最后仍需一个右转把位送进接收器。
            int maxA = width - 5;
            int a = 2 + rng.Next(Math.Max(1, maxA - 1));
            if (a > maxA) a = maxA;

            int minB = a + 1;
            int maxB = width - 4;
            int b = minB + rng.Next(Math.Max(1, maxB - minB + 1));
            if (b > maxB) b = maxB;

            int minC = b + 1;
            int maxC = width - 3;
            int c = minC + rng.Next(Math.Max(1, maxC - minC + 1));
            if (c > maxC) c = maxC;

            result.Add(a);
            result.Add(b);
            result.Add(c);
            return result;
        }

        static void AssignPieces(OvertimeLayout layout, OvertimeRng rng)
        {
            layout.StartValue = rng.Next(2);
            int value = layout.StartValue;
            bool uniform = layout.Slots.Count >= 4;
            int family = uniform ? rng.Next(3) : -1;

            foreach (OvertimeSlot slot in layout.Slots)
            {
                int roll = uniform ? (family == 0 ? 0 : family == 1 ? 50 : 80) : rng.Next(100);

                if (roll < 45)
                {
                    layout.Pieces.Add(new OvertimePiece
                    {
                        TypeKey = NotKey,
                        Kind = CellKind.NotGate,
                        SolutionRotation = OvertimePiece.RotationForOutput(slot.OutDir),
                    });
                    value = value == 1 ? 0 : 1;
                }
                else if (roll < 70)
                {
                    bool passOne = value == 1;
                    layout.Pieces.Add(new OvertimePiece
                    {
                        TypeKey = passOne ? Filter1Key : Filter0Key,
                        Kind = CellKind.Filter,
                        PassValue = (byte)(passOne ? 1 : 0),
                        SolutionRotation = OvertimePiece.RotationForOutput(slot.OutDir),
                    });
                }
                else
                {
                    if (!OvertimePiece.TrySplitterRotation((byte)value, slot.OutDir, out int rotation))
                        rotation = OvertimePiece.RotationForOutput(slot.OutDir);

                    layout.Pieces.Add(new OvertimePiece
                    {
                        TypeKey = SplitterKey,
                        Kind = CellKind.Splitter,
                        SolutionRotation = rotation,
                    });
                }
            }

            layout.TargetValue = value;
            int last = layout.Slots.Count - 1;
            layout.ReceiverFeedFace = layout.Slots[last].OutDir.Opposite();
        }
    }
}
