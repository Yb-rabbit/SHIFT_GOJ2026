using System;
using System.Diagnostics;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    public sealed class OvertimeGenerationResult
    {
        public OvertimeProfile Profile;
        public OvertimeLayout Layout;
        public LevelDefinition Level;
        public OvertimeSolveResult Solve;
        public int Attempts;
        public long ElapsedMs;
        public int Ticks;
    }

    /// <summary>
    /// 组装“布局 → 唯一解证明 → 玩家题面”的入口。
    /// 任何一步失败都不返回半成品，终端只会看到 SIGNAL LOST。
    ///
    /// 当前版本不注入 BUFFER：题面空位刚好等于转角件数，而 LevelCheck 会把工具卡
    /// 也计入手牌总数，额外发一张 BUFFER 会让“空位 &lt; 手牌”直接判坏。
    /// 在求解器能连同 BUFFER 的回收状态一起证明唯一解之前，按计划退回无 BUFFER 版本。
    /// </summary>
    public static class OvertimeFactory
    {
        public const string FailureMessage = "SIGNAL LOST. TRY AGAIN.";
        public const int TimeBudgetMs = 2600;

        public static bool TryGenerate(int index, int baseSeed, int minTicks,
            out OvertimeGenerationResult result, out string error)
        {
            result = null;
            error = null;

            if (!OvertimeConfig.IsValidIndex(index))
            {
                error = FailureMessage;
                return false;
            }

            Stopwatch watch = Stopwatch.StartNew();
            OvertimeProfile profile = OvertimeProfile.For(index, baseSeed, minTicks);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (watch.ElapsedMilliseconds > TimeBudgetMs - 300) break;

                int steps = attempt >= 10 ? 2 : profile.TargetSteps;
                int attemptSeed = unchecked(profile.Seed ^ (int)((uint)attempt * 0x9E3779B9u));
                OvertimeRng rng = new OvertimeRng(attemptSeed);

                OvertimeLayout layout;
                try
                {
                    layout = OvertimeLayoutBuilder.Build(profile, rng, steps);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogWarning("[BitFlow] OVERTIME 布局构造失败：" + exception.Message);
                    continue;
                }

                // 4 件全排列 x 4 朝向 = 6144 个候选；上限只是兜底，正常远到不了。
                OvertimeSolveResult solve = OvertimeSolver.Solve(layout, 2, 150000);
                if (!solve.IsUnique) continue;
                if (solve.Ticks < Math.Max(0, minTicks)) continue;

                LevelDefinition playerLevel = OvertimeLevelAssembler.BuildPlayerLevel(layout, index);
                if (playerLevel.Supplies.Count == 0) continue;

                result = new OvertimeGenerationResult
                {
                    Profile = profile,
                    Layout = layout,
                    Level = playerLevel,
                    Solve = solve,
                    Attempts = attempt + 1,
                    ElapsedMs = watch.ElapsedMilliseconds,
                    Ticks = solve.Ticks,
                };
                return true;
            }

            error = FailureMessage;
            return false;
        }
    }
}
