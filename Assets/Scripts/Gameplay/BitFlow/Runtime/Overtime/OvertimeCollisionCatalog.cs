using System;
using System.Collections.Generic;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 碰撞题里的一个空位：玩家必须在这里放一件指定的手牌，标准解朝向已经由作者写好。
    /// </summary>
    public sealed class OvertimeCollisionSlot
    {
        public readonly GridPoint Coord;
        public readonly OvertimePieceRole Role;
        public readonly int SolutionRotation;

        public OvertimeCollisionSlot(GridPoint coord, OvertimePieceRole role, int solutionRotation)
        {
            Coord = coord;
            Role = role;
            SolutionRotation = solutionRotation;
        }
    }

    /// <summary>
    /// 一道碰撞题的完整棋盘描述：多发射器、多接收器、固定走廊与必须填满的空位。
    ///
    /// 和旧走廊蓝图不同，这里不靠"每个转角必须转向"来强制唯一解，而是靠
    /// 位流在同一个 tick 撞进同一格时一起销毁这条规则。标准解里哪些位必须被擦掉，
    /// 由 <see cref="ExpectedConflictPairs"/> 记下来，测试会重放标准解验证它。
    /// </summary>
    public sealed class OvertimeCollisionBlueprint
    {
        public int Index;
        public int Width;
        public int Height;
        public OvertimeEmitterSpec[] Emitters;
        public OvertimeReceiverSpec[] Receivers;
        public GridPoint[] PathCells;
        public OvertimeCollisionSlot[] Slots;
        public int[] HandOrder;
        public int ExpectedConflictPairs;
    }

    /// <summary>把碰撞蓝图展开成 <see cref="OvertimeLayout"/>；非走廊格一律填墙，玩家只能看见空位。</summary>
    public static class OvertimeCollisionBuilder
    {
        const string NotKey = "Type_NotGate_N1";
        const string SplitterKey = "Type_Splitter_L1";
        const string Filter0Key = "Type_Filter_F0";
        const string Filter1Key = "Type_Filter_F1";
        const string Delay1Key = "Type_DelayGate_D1";

        public static OvertimeLayout Build(OvertimeCollisionBlueprint blueprint)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            if (blueprint.Width < 5 || blueprint.Height < 5)
                throw new InvalidOperationException("OVERTIME 碰撞题网格太小");
            if (blueprint.Emitters == null || blueprint.Emitters.Length == 0)
                throw new InvalidOperationException("OVERTIME 碰撞题没有发射器");
            if (blueprint.Receivers == null || blueprint.Receivers.Length == 0)
                throw new InvalidOperationException("OVERTIME 碰撞题没有接收器");
            if (blueprint.Slots == null || blueprint.Slots.Length == 0)
                throw new InvalidOperationException("OVERTIME 碰撞题没有空位");
            if (blueprint.PathCells == null || blueprint.PathCells.Length == 0)
                throw new InvalidOperationException("OVERTIME 碰撞题没有走廊");

            OvertimeLayout layout = new OvertimeLayout
            {
                Width = blueprint.Width,
                Height = blueprint.Height,
                ExpectedConflictPairs = Math.Max(0, blueprint.ExpectedConflictPairs),
            };

            for (int i = 0; i < blueprint.Emitters.Length; i++)
            {
                OvertimeEmitterSpec emitter = blueprint.Emitters[i];
                if (emitter == null) throw new InvalidOperationException("OVERTIME 碰撞题含空发射器");
                if (!Inside(layout, emitter.Coord)) throw new InvalidOperationException("OVERTIME 发射器在棋盘外");
                if (emitter.EmitCount < 1) throw new InvalidOperationException("OVERTIME 发射器没有吐位");
                layout.Emitters.Add(emitter);
            }

            for (int i = 0; i < blueprint.Receivers.Length; i++)
            {
                OvertimeReceiverSpec receiver = blueprint.Receivers[i];
                if (receiver == null) throw new InvalidOperationException("OVERTIME 碰撞题含空接收器");
                if (!Inside(layout, receiver.Coord)) throw new InvalidOperationException("OVERTIME 接收器在棋盘外");
                if (receiver.TargetCount < 1) throw new InvalidOperationException("OVERTIME 接收器目标数为 0");
                layout.Receivers.Add(receiver);
            }

            layout.EmitterCoord = layout.Emitters[0].Coord;
            layout.EmitterFacing = layout.Emitters[0].Facing;
            layout.ReceiverCoord = layout.Receivers[0].Coord;
            layout.ReceiverFeedFace = layout.Receivers[0].FeedFace;
            layout.StartValue = FirstBit(layout.Emitters[0].Sequence);
            layout.TargetValue = ParseFirstInt(layout.Receivers[0].TargetSequence);

            HashSet<GridPoint> occupied = new HashSet<GridPoint>();
            foreach (OvertimeEmitterSpec emitter in layout.Emitters)
            {
                if (!occupied.Add(emitter.Coord)) throw new InvalidOperationException("OVERTIME 发射器坐标重复");
            }
            foreach (OvertimeReceiverSpec receiver in layout.Receivers)
            {
                if (!occupied.Add(receiver.Coord)) throw new InvalidOperationException("OVERTIME 接收器坐标重复");
            }
            foreach (GridPoint path in blueprint.PathCells)
            {
                if (!Inside(layout, path)) throw new InvalidOperationException("OVERTIME 走廊走出棋盘");
                if (!occupied.Add(path)) throw new InvalidOperationException("OVERTIME 走廊坐标重复");
                layout.PathCells.Add(path);
            }

            for (int slot = 0; slot < blueprint.Slots.Length; slot++)
            {
                OvertimeCollisionSlot spec = blueprint.Slots[slot];
                if (spec == null) throw new InvalidOperationException("OVERTIME 碰撞题含空空位");
                if (!Inside(layout, spec.Coord)) throw new InvalidOperationException("OVERTIME 空位在棋盘外");
                if (!occupied.Add(spec.Coord)) throw new InvalidOperationException("OVERTIME 空位与走廊/发射器/接收器重叠");

                layout.Slots.Add(new OvertimeSlot(spec.Coord, Dir.Left, SpecOutput(spec)));
                layout.PathCells.Add(spec.Coord);
            }

            int[] handOrder = ResolveHandOrder(blueprint.HandOrder, blueprint.Slots.Length);
            for (int hand = 0; hand < handOrder.Length; hand++)
            {
                int slotIndex = handOrder[hand];
                OvertimeCollisionSlot spec = blueprint.Slots[slotIndex];
                OvertimePiece piece = CreatePiece(spec);
                layout.Pieces.Add(piece);
                layout.IntendedPlacements.Add(new OvertimePlacement(slotIndex, hand, piece.SolutionRotation));
            }

            for (int y = 0; y < layout.Height; y++)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    GridPoint coord = new GridPoint(x, y);
                    if (occupied.Contains(coord)) continue;
                    layout.Walls.Add(coord);
                }
            }

            return layout;
        }

        static OvertimePiece CreatePiece(OvertimeCollisionSlot slot)
        {
            switch (slot.Role)
            {
                case OvertimePieceRole.NotGate:
                    return new OvertimePiece
                    {
                        TypeKey = NotKey,
                        Kind = CellKind.NotGate,
                        SolutionRotation = Normalize(slot.SolutionRotation),
                    };

                case OvertimePieceRole.FilterZero:
                case OvertimePieceRole.FilterOne:
                {
                    byte pass = slot.Role == OvertimePieceRole.FilterOne ? (byte)1 : (byte)0;
                    return new OvertimePiece
                    {
                        TypeKey = pass == 1 ? Filter1Key : Filter0Key,
                        Kind = CellKind.Filter,
                        PassValue = pass,
                        SolutionRotation = Normalize(slot.SolutionRotation),
                    };
                }

                case OvertimePieceRole.Splitter:
                    return new OvertimePiece
                    {
                        TypeKey = SplitterKey,
                        Kind = CellKind.Splitter,
                        SolutionRotation = Normalize(slot.SolutionRotation),
                    };

                case OvertimePieceRole.DelayGate:
                    return new OvertimePiece
                    {
                        TypeKey = Delay1Key,
                        Kind = CellKind.DelayGate,
                        DelayTicks = 1,
                        SolutionRotation = 0,
                    };

                default:
                    throw new InvalidOperationException("OVERTIME 碰撞题含未知元件");
            }
        }

        static Dir SpecOutput(OvertimeCollisionSlot slot)
        {
            switch (slot.Role)
            {
                case OvertimePieceRole.NotGate:
                case OvertimePieceRole.FilterZero:
                case OvertimePieceRole.FilterOne:
                    return OvertimePiece.Rotate(Dir.Right, slot.SolutionRotation);
                case OvertimePieceRole.Splitter:
                    return OvertimePiece.Rotate(Dir.Up, slot.SolutionRotation);
                default:
                    return Dir.Right;
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
                throw new InvalidOperationException("OVERTIME 碰撞题发牌顺序长度与空位数对不上");

            bool[] seen = new bool[slotCount];
            for (int i = 0; i < handOrder.Length; i++)
            {
                int index = handOrder[i];
                if (index < 0 || index >= slotCount || seen[index])
                    throw new InvalidOperationException("OVERTIME 碰撞题发牌顺序不是完整排列");
                seen[index] = true;
            }

            return handOrder;
        }

        static int FirstBit(string sequence)
        {
            if (!string.IsNullOrEmpty(sequence))
            {
                for (int i = sequence.Length - 1; i >= 0; i--)
                {
                    if (sequence[i] == '0') return 0;
                    if (sequence[i] == '1') return 1;
                }
            }
            return 1;
        }

        static int ParseFirstInt(string sequence)
        {
            if (!string.IsNullOrEmpty(sequence) && int.TryParse(sequence, out int value)) return value;
            return 0;
        }

        static int Normalize(int rotation) => ((rotation % 4) + 4) % 4;

        static bool Inside(OvertimeLayout layout, GridPoint coord)
        {
            return coord.X >= 0 && coord.X < layout.Width && coord.Y >= 0 && coord.Y < layout.Height;
        }
    }

    /// <summary>
    /// 12 道题全部改成碰撞样板，用来证明"同格争抢"不是事故，而是可以出题的机制：
    /// 01 ERASE    两条位流并轨，重叠的位一起蒸发，只留下未被擦掉的部分；
    /// 02 CROSSING 两条位流十字交叉，延迟器把其中一条整体错开，避免同 tick 相撞；
    /// 03 INTERLEAVE 探针流隔拍擦除主流的位，接收器只看到剩下的序列。
    ///
    /// 04-12 沿用同一套碰撞语言，逐步加长探针、增加分流器、把"避免碰撞"翻成"主动制造碰撞"，
    /// 让 7x6 → 13x9 的尺寸增长对应机制复杂度，而不是单纯把走廊拉长。
    /// </summary>
    public static class OvertimeCollisionCatalog
    {
        public const int Count = 12;

        static readonly OvertimeCollisionBlueprint[] Blueprints =
        {
            // 01 · ERASE · 上路先向下并轨；只有一拍重叠，擦掉一对后两路剩下的 6 位全部送达。
            Define(1, 7, 6, 1,
                new[]
                {
                    Emitter(0, 4, Dir.Right, "1", 4),
                    Emitter(0, 1, Dir.Right, "1", 4),
                },
                new[] { Receiver(6, 1, Dir.Left, "1", 6) },
                new[]
                {
                    P(1, 4), P(2, 4), P(3, 4), P(4, 3), P(4, 2), P(5, 1),
                    P(1, 1), P(2, 1), P(3, 1),
                },
                new[]
                {
                    Slot(4, 4, OvertimePieceRole.FilterOne, 1),
                    Slot(4, 1, OvertimePieceRole.FilterOne, 0),
                },
                new[] { 0, 1 }),

            // 02 · CROSSING · 十字交叉；没有延迟器时两条流会在交叉格同 tick 全灭。
            Define(2, 7, 6, 0,
                new[]
                {
                    Emitter(2, 2, Dir.Right, "1", 2),
                    Emitter(4, 0, Dir.Up, "1", 2),
                },
                new[]
                {
                    Receiver(6, 2, Dir.Left, "1", 2),
                    Receiver(4, 4, Dir.Down, "1", 2),
                },
                new[]
                {
                    P(4, 2), P(5, 2), P(4, 1), P(4, 3),
                },
                new[]
                {
                    Slot(3, 2, OvertimePieceRole.DelayGate, 0),
                },
                new[] { 0 }),

            // 03 · INTERLEAVE · 探针流隔拍撞掉主流的第 2、6 位，剩下的值倒相后送到接收器。
            Define(3, 7, 6, 2,
                new[]
                {
                    Emitter(0, 2, Dir.Right, "101010", 6),
                    Emitter(4, 4, Dir.Down, "1010", 4),
                },
                new[] { Receiver(6, 2, Dir.Left, "1101", 4) },
                new[]
                {
                    P(1, 2), P(2, 2), P(3, 2), P(5, 2),
                },
                new[]
                {
                    Slot(4, 3, OvertimePieceRole.Splitter, 2),
                    Slot(4, 2, OvertimePieceRole.NotGate, 0),
                },
                new[] { 0, 1 }),

            // 04 · ERASE → INVERT · 两条 4 位 1 流并轨擦掉一对，剩 6 位被非门整体翻成 0。
            Define(4, 9, 8, 1,
                new[]
                {
                    Emitter(0, 5, Dir.Right, "1111", 4),
                    Emitter(0, 2, Dir.Right, "1111", 4),
                },
                new[] { Receiver(8, 2, Dir.Left, "000000", 6) },
                new[]
                {
                    P(1, 5), P(2, 5), P(3, 5), P(4, 4), P(4, 3),
                    P(1, 2), P(2, 2), P(3, 2), P(5, 2), P(6, 2),
                },
                new[]
                {
                    Slot(4, 5, OvertimePieceRole.FilterOne, 1),
                    Slot(4, 2, OvertimePieceRole.FilterOne, 0),
                    Slot(7, 2, OvertimePieceRole.NotGate, 0),
                },
                new[] { 0, 1, 2 }),

            // 05 · CROSSING → INVERT · 延迟器让横流避开竖流；两路各送 2 位，竖流还经过非门。
            Define(5, 9, 8, 0,
                new[]
                {
                    Emitter(0, 4, Dir.Right, "11", 2),
                    Emitter(5, 0, Dir.Up, "11", 2),
                },
                new[]
                {
                    Receiver(8, 4, Dir.Left, "11", 2),
                    Receiver(5, 7, Dir.Down, "00", 2),
                },
                new[]
                {
                    P(1, 4), P(2, 4), P(3, 4), P(5, 4), P(6, 4),
                    P(5, 1), P(5, 2), P(5, 5),
                },
                new[]
                {
                    Slot(4, 4, OvertimePieceRole.DelayGate, 0),
                    Slot(7, 4, OvertimePieceRole.FilterOne, 0),
                    Slot(5, 3, OvertimePieceRole.NotGate, 3),
                    Slot(5, 6, OvertimePieceRole.FilterZero, 3),
                },
                new[] { 0, 1, 2, 3 }),

            // 06 · INTERLEAVE → SPLIT · 探针隔拍撞掉主流两位，非门后分流器把 0 分到两个接收器。
            Define(6, 9, 8, 2,
                new[]
                {
                    Emitter(0, 2, Dir.Right, "101010", 6),
                    Emitter(4, 6, Dir.Down, "1010", 4),
                },
                new[]
                {
                    Receiver(5, 4, Dir.Down, "000", 3),
                    Receiver(7, 2, Dir.Left, "0", 1),
                },
                new[]
                {
                    P(1, 2), P(2, 2), P(3, 2), P(6, 2),
                    P(4, 5), P(4, 4),
                },
                new[]
                {
                    Slot(4, 3, OvertimePieceRole.Splitter, 2),
                    Slot(4, 2, OvertimePieceRole.NotGate, 0),
                    Slot(5, 2, OvertimePieceRole.Splitter, 0),
                    Slot(5, 3, OvertimePieceRole.NotGate, 3),
                },
                new[] { 0, 1, 2, 3 }),

            // 07 · DOUBLE ERASE · 两条 5 位 1 流并轨，连续擦掉两对，6 位取反后送达。
            Define(7, 11, 8, 2,
                new[]
                {
                    Emitter(0, 5, Dir.Right, "11111", 5),
                    Emitter(0, 2, Dir.Right, "11111", 5),
                },
                new[] { Receiver(10, 2, Dir.Left, "000000", 6) },
                new[]
                {
                    P(1, 5), P(2, 5), P(3, 5), P(4, 4), P(4, 3),
                    P(1, 2), P(2, 2), P(3, 2), P(5, 2), P(6, 2),
                    P(7, 2), P(8, 2),
                },
                new[]
                {
                    Slot(4, 5, OvertimePieceRole.FilterOne, 1),
                    Slot(4, 2, OvertimePieceRole.FilterOne, 0),
                    Slot(9, 2, OvertimePieceRole.NotGate, 0),
                },
                new[] { 0, 1, 2 }),

            // 08 · LONG CROSSING · 长距离十字交叉；延迟器错开横流，两路各送 3 位。
            Define(8, 11, 8, 0,
                new[]
                {
                    Emitter(0, 4, Dir.Right, "111", 3),
                    Emitter(6, 0, Dir.Up, "111", 3),
                },
                new[]
                {
                    Receiver(10, 4, Dir.Left, "111", 3),
                    Receiver(6, 7, Dir.Down, "000", 3),
                },
                new[]
                {
                    P(1, 4), P(2, 4), P(3, 4), P(5, 4), P(6, 4),
                    P(7, 4), P(8, 4), P(6, 1), P(6, 2), P(6, 5),
                },
                new[]
                {
                    Slot(4, 4, OvertimePieceRole.DelayGate, 0),
                    Slot(9, 4, OvertimePieceRole.FilterOne, 0),
                    Slot(6, 3, OvertimePieceRole.NotGate, 3),
                    Slot(6, 6, OvertimePieceRole.FilterZero, 3),
                },
                new[] { 0, 1, 2, 3 }),

            // 09 · DOUBLE INTERLEAVE · 两道探针连续改造主流，输出 00000101。
            Define(9, 11, 8, 2,
                new[]
                {
                    Emitter(0, 2, Dir.Right, "10101010", 8),
                    Emitter(4, 6, Dir.Down, "1010", 4),
                    Emitter(8, 6, Dir.Down, "1010", 4),
                },
                new[] { Receiver(10, 2, Dir.Left, "00000101", 8) },
                new[]
                {
                    P(1, 2), P(2, 2), P(3, 2), P(5, 2), P(6, 2), P(7, 2),
                    P(9, 2), P(4, 5), P(4, 4), P(8, 5), P(8, 4),
                },
                new[]
                {
                    Slot(4, 3, OvertimePieceRole.Splitter, 2),
                    Slot(4, 2, OvertimePieceRole.NotGate, 0),
                    Slot(8, 3, OvertimePieceRole.Splitter, 2),
                    Slot(8, 2, OvertimePieceRole.NotGate, 0),
                },
                new[] { 0, 1, 2, 3 }),

            // 10 · DELIBERATE COLLISION · 延迟器主动制造一次并轨碰撞，把 4+4 位裁成 3+3。
            Define(10, 13, 9, 1,
                new[]
                {
                    Emitter(0, 4, Dir.Right, "1111", 4),
                    Emitter(6, 0, Dir.Up, "1111", 4),
                },
                new[]
                {
                    Receiver(12, 4, Dir.Left, "111", 3),
                    Receiver(6, 8, Dir.Down, "000", 3),
                },
                new[]
                {
                    P(1, 4), P(2, 4), P(3, 4), P(5, 4), P(6, 4),
                    P(7, 4), P(8, 4), P(9, 4), P(10, 4),
                    P(6, 1), P(6, 2), P(6, 5), P(6, 6),
                },
                new[]
                {
                    Slot(4, 4, OvertimePieceRole.DelayGate, 0),
                    Slot(11, 4, OvertimePieceRole.FilterOne, 0),
                    Slot(6, 3, OvertimePieceRole.NotGate, 3),
                    Slot(6, 7, OvertimePieceRole.FilterZero, 3),
                },
                new[] { 0, 1, 2, 3 }),

            // 11 · DOUBLE COLLISION · 同为 13x9 的加压版：两条 5 位流连续撞掉两对，各剩 3 位。
            Define(11, 13, 9, 2,
                new[]
                {
                    Emitter(0, 4, Dir.Right, "11111", 5),
                    Emitter(6, 0, Dir.Up, "11111", 5),
                },
                new[]
                {
                    Receiver(12, 4, Dir.Left, "111", 3),
                    Receiver(6, 8, Dir.Down, "000", 3),
                },
                new[]
                {
                    P(1, 4), P(2, 4), P(3, 4), P(5, 4), P(6, 4),
                    P(7, 4), P(8, 4), P(9, 4), P(10, 4),
                    P(6, 1), P(6, 2), P(6, 5), P(6, 6),
                },
                new[]
                {
                    Slot(4, 4, OvertimePieceRole.DelayGate, 0),
                    Slot(11, 4, OvertimePieceRole.FilterOne, 0),
                    Slot(6, 3, OvertimePieceRole.NotGate, 3),
                    Slot(6, 7, OvertimePieceRole.FilterZero, 3),
                },
                new[] { 0, 1, 2, 3 }),

            // 12 · FINAL CASCADE · 12 位主流 + 8 位探针，两次合流/倒相后分流成 2 + 6 个 0。
            Define(12, 13, 9, 4,
                new[]
                {
                    Emitter(0, 2, Dir.Right, "101010101010", 12),
                    Emitter(6, 7, Dir.Down, "10101010", 8),
                },
                new[]
                {
                    Receiver(7, 4, Dir.Down, "00", 2),
                    Receiver(9, 2, Dir.Left, "000000", 6),
                },
                new[]
                {
                    P(1, 2), P(2, 2), P(3, 2), P(4, 2), P(5, 2), P(8, 2),
                    P(6, 6), P(6, 5), P(6, 4),
                },
                new[]
                {
                    Slot(6, 3, OvertimePieceRole.Splitter, 2),
                    Slot(6, 2, OvertimePieceRole.NotGate, 0),
                    Slot(7, 2, OvertimePieceRole.Splitter, 0),
                    Slot(7, 3, OvertimePieceRole.NotGate, 3),
                },
                new[] { 0, 1, 2, 3 }),
        };

        public static bool Contains(int index) => index >= 1 && index <= Count;

        public static bool TryBuild(int index, out OvertimeLayout layout)
        {
            layout = null;
            if (!Contains(index)) return false;
            layout = OvertimeCollisionBuilder.Build(Blueprints[index - 1]);
            return true;
        }

        static OvertimeCollisionBlueprint Define(
            int index, int width, int height, int expectedConflictPairs,
            OvertimeEmitterSpec[] emitters, OvertimeReceiverSpec[] receivers,
            GridPoint[] pathCells, OvertimeCollisionSlot[] slots, int[] handOrder)
        {
            return new OvertimeCollisionBlueprint
            {
                Index = index,
                Width = width,
                Height = height,
                ExpectedConflictPairs = expectedConflictPairs,
                Emitters = emitters,
                Receivers = receivers,
                PathCells = pathCells,
                Slots = slots,
                HandOrder = handOrder,
            };
        }

        static OvertimeEmitterSpec Emitter(int x, int y, Dir facing, string sequence, int count)
        {
            return new OvertimeEmitterSpec(new GridPoint(x, y), facing, sequence, count);
        }

        static OvertimeReceiverSpec Receiver(int x, int y, Dir feedFace, string target, int count)
        {
            return new OvertimeReceiverSpec(new GridPoint(x, y), feedFace, target, count);
        }

        static GridPoint P(int x, int y) => new GridPoint(x, y);

        static OvertimeCollisionSlot Slot(int x, int y, OvertimePieceRole role, int rotation)
        {
            return new OvertimeCollisionSlot(new GridPoint(x, y), role, rotation);
        }
    }
}
