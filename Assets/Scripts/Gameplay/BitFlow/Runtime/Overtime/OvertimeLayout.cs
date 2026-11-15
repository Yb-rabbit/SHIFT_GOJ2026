using System;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// OVERTIME 里发到玩家手牌的一件元件。
    ///
    /// 它同时保存“类型资产 Key”和“生成器的标准解朝向”：
    /// - TypeKey 写进 supplies，游戏里按类型资产实例化；
    /// - SolutionRotation 只用于生成器构造标准解，不会写进关卡文件。
    /// </summary>
    public sealed class OvertimePiece
    {
        public string TypeKey;
        public CellKind Kind;
        public byte PassValue = 1;
        public int DelayTicks = 1;
        public int SolutionRotation;

        public bool SupportsRotation => Kind != CellKind.DelayGate;
        public int RotationCount => SupportsRotation ? 4 : 1;

        public ElementDefinition CreateDefinition(GridPoint coord, int id, int rotation)
        {
            int steps = NormalizeSteps(rotation);

            switch (Kind)
            {
                case CellKind.NotGate:
                    return new NotGateDefinition
                    {
                        Id = id,
                        Coord = coord,
                        OutputDir = Rotate(Dir.Right, steps),
                        TypeKey = TypeKey,
                    };

                case CellKind.Filter:
                    return new FilterDefinition
                    {
                        Id = id,
                        Coord = coord,
                        OutputDir = Rotate(Dir.Right, steps),
                        PassValue = PassValue,
                        TypeKey = TypeKey,
                    };

                case CellKind.Splitter:
                    return new SplitterDefinition
                    {
                        Id = id,
                        Coord = coord,
                        OneArm = Rotate(Dir.Up, steps),
                        ZeroArm = Rotate(Dir.Right, steps),
                        TypeKey = TypeKey,
                    };

                case CellKind.DelayGate:
                    return new DelayGateDefinition
                    {
                        Id = id,
                        Coord = coord,
                        DelayTicks = DelayTicks,
                        TypeKey = TypeKey,
                    };

                default:
                    return new WallDefinition
                    {
                        Id = id,
                        Coord = coord,
                        TypeKey = TypeKey,
                    };
            }
        }

        public static int RotationForOutput(Dir output)
        {
            return ((int)output - (int)Dir.Right + 4) & 3;
        }

        public static bool TrySplitterRotation(byte value, Dir output, out int rotation)
        {
            for (int steps = 0; steps < 4; steps++)
            {
                Dir one = Rotate(Dir.Up, steps);
                Dir zero = Rotate(Dir.Right, steps);
                if ((value == 1 && one == output) || (value == 0 && zero == output))
                {
                    rotation = steps;
                    return true;
                }
            }

            rotation = 0;
            return false;
        }

        public static Dir Rotate(Dir dir, int steps)
        {
            int count = ((steps % 4) + 4) % 4;
            Dir result = dir;
            for (int i = 0; i < count; i++) result = result.RotateClockwise();
            return result;
        }

        static int NormalizeSteps(int steps) => ((steps % 4) + 4) % 4;
    }

    /// <summary>标准解里的一个转角格：进入方向与必须朝哪个方向出去。</summary>
    public sealed class OvertimeSlot
    {
        public GridPoint Coord;
        public Dir InDir;
        public Dir OutDir;

        public OvertimeSlot(GridPoint coord, Dir inDir, Dir outDir)
        {
            Coord = coord;
            InDir = inDir;
            OutDir = outDir;
        }
    }

    /// <summary>
    /// 生成器构造出来的一条标准解。它不是给玩家看的，而是给求解器做唯一性证明的基准。
    /// </summary>
    public sealed class OvertimeLayout
    {
        public int Width;
        public int Height;
        public int StartValue;
        public int TargetValue;
        public GridPoint EmitterCoord;
        public Dir EmitterFacing = Dir.Right;
        public GridPoint ReceiverCoord;
        public Dir ReceiverFeedFace;

        public readonly System.Collections.Generic.List<OvertimeSlot> Slots = new System.Collections.Generic.List<OvertimeSlot>();
        public readonly System.Collections.Generic.List<OvertimePiece> Pieces = new System.Collections.Generic.List<OvertimePiece>();
        public readonly System.Collections.Generic.List<GridPoint> PathCells = new System.Collections.Generic.List<GridPoint>();
        public readonly System.Collections.Generic.List<GridPoint> Walls = new System.Collections.Generic.List<GridPoint>();

        public int StepCount => Slots.Count;
    }
}
