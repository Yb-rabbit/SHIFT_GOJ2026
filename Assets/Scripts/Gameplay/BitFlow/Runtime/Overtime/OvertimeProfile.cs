using System;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 一题的生成参数。题号决定尺寸与目标件数，seed 由状态里的 baseSeed 派生。
    /// </summary>
    public sealed class OvertimeProfile
    {
        public int Index;
        public int Width;
        public int Height;
        public int Seed;
        public int TargetSteps;
        public int MinTicks;
        public OvertimeSizePreset Preset;

        public static OvertimeProfile For(int index, int baseSeed, int minTicks)
        {
            if (!OvertimeConfig.IsValidIndex(index))
                throw new ArgumentOutOfRangeException(nameof(index));

            OvertimeSize size = OvertimeConfig.SizeFor(index);
            return new OvertimeProfile
            {
                Index = index,
                Width = size.Width,
                Height = size.Height,
                Seed = OvertimeSeed.ForProblem(baseSeed, index),
                TargetSteps = OvertimeConfig.TargetStepsFor(index),
                MinTicks = Math.Max(0, minTicks),
                Preset = OvertimeConfig.PresetFor(index),
            };
        }

        public OvertimeProfile WithTargetSteps(int steps)
        {
            return new OvertimeProfile
            {
                Index = Index,
                Width = Width,
                Height = Height,
                Seed = Seed,
                TargetSteps = Math.Max(2, steps),
                MinTicks = MinTicks,
                Preset = Preset,
            };
        }
    }
}
