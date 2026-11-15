using System;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// OVERTIME 终端的台词生成。保持 ASCII 大写（像素字体没有中文字形），
    /// 并把“状态 → 屏幕文字”的映射从 UI 生命周期里拆出来，方便测试和改文案。
    /// </summary>
    public static class OvertimeTerminalScripts
    {
        public const string IntroTitle = "OVERTIME // FIRST CONNECTION";
        public const string IdleTitle = "OVERTIME // NIGHT SHIFT";
        public const string ReadyTitle = "OVERTIME // PROBLEM READY";
        public const string ErrorTitle = "OVERTIME // SIGNAL LOST";

        public static string IntroBody()
        {
            return "SYSTEM  NIGHT SHIFT PROTOCOL\n" +
                   "CHANNEL " + OvertimeConfig.MaxProblems + " // OFF THE CLOCK\n\n" +
                   "YOU ARE OUTSIDE THE CATALOG NOW.\n" +
                   "EVERY PROBLEM IS HAND-CHECKED FOR\n" +
                   "ONE VALID SOLUTION BEFORE IT IS WRITTEN.\n\n" +
                   "THERE ARE " + OvertimeConfig.MaxProblems + " SLOTS. THEN THE TERMINAL GOES QUIET.\n\n" +
                   "PRESS ENTER TO CONNECT.";
        }

        public static string IdleBody(OvertimeState state)
        {
            bool canGenerate = state != null && state.CanGenerate;
            string next = canGenerate
                ? OvertimeConfig.DisplayName(state.nextProblem) + "  " + OvertimeConfig.SizeFor(state.nextProblem)
                : "NO PROBLEMS LEFT";

            return "SYSTEM  OVERTIME TERMINAL ONLINE\n" +
                   "CHANNEL " + OvertimeConfig.MaxProblems + " // OFF THE CLOCK\n\n" +
                   "PROBLEMS  " + (state != null ? state.GeneratedCount : 0) + " / " + OvertimeConfig.MaxProblems + "\n" +
                   "SEED      0x" + (state != null ? state.baseSeed.ToString("X8") : "00000000") + "\n\n" +
                   "NEXT      " + next + "\n\n" +
                   "THE CLOCK IS OFF. THE WORK IS NOT.";
        }

        public static string ConstructingBody(int index)
        {
            return "SYSTEM  ALLOCATING UNIQUE PROBLEM...\n\n" +
                   "TARGET  " + OvertimeConfig.DisplayName(index) + "  " + OvertimeConfig.SizeFor(index) + "\n" +
                   "RULE    ONE VALID SOLUTION. NO GUESSWORK.\n\n" +
                   "PLEASE WAIT.";
        }

        public static string BriefingBody(OvertimeGenerationResult result, long elapsedMs)
        {
            return "SYSTEM  PROBLEM CONSTRUCTED\n" +
                   "CHANNEL " + OvertimeConfig.MaxProblems + " // NIGHT SHIFT\n\n" +
                   "GRID      " + result.Level.Width + "x" + result.Level.Height + "\n" +
                   "STEPS     " + result.Layout.StepCount + "\n" +
                   "SEED      0x" + result.Profile.Seed.ToString("X8") + "\n" +
                   "CHECK     UNIQUE SOLUTION VERIFIED\n" +
                   "BUILD     " + elapsedMs + " MS\n\n" +
                   "THE BOARD IS WAITING.";
        }

        public static string ErrorBody(string message)
        {
            return "SYSTEM  CONSTRUCTION FAILED\n\n" +
                   (string.IsNullOrEmpty(message) ? OvertimeFactory.FailureMessage : message) + "\n\n" +
                   "NO FILE WAS WRITTEN.";
        }
    }
}
