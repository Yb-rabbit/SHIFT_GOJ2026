using System;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 工坊尺寸预设。OVERTIME 的 12 题固定分成四档，题号决定尺寸，
    /// 测试和玩家都能预期。
    /// </summary>
    public enum OvertimeSizePreset
    {
        Compact = 0,
        Standard = 1,
        Wide = 2,
        Deep = 3,
    }

    /// <summary>网格尺寸值对象（纯数据，不依赖 UnityEngine）。</summary>
    public readonly struct OvertimeSize
    {
        public readonly int Width;
        public readonly int Height;

        public OvertimeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public override string ToString() => Width + "x" + Height;
    }

    /// <summary>
    /// OVERTIME 的固定口径：12 题上限、文件名格式、题号到尺寸的映射。
    /// 所有“魔法数字”只放在这里，生成器 / 写盘 / UI 都从同一份读。
    /// </summary>
    public static class OvertimeConfig
    {
        public const int MaxProblems = 12;
        public const int FirstProblem = 1;
        public const int LastProblem = MaxProblems;

        public const string FilePrefix = "OT-PROBLEM_";
        public const string StateFileName = "overtime_state.json";
        public const string LegacyFolderName = "overtime_legacy";

        /// <summary>状态文件 / 题库版本。v2 起改为 12 道手工蓝图，旧 v1 存档与关卡会先归档。</summary>
        public const int StateVersion = 2;

        /// <summary>OVERTIME 关卡的统一 tick 间隔（秒）。比主线稍快，终端题的节奏更紧。</summary>
        public const float TickInterval = 0.35f;

        public static bool IsValidIndex(int index) => index >= FirstProblem && index <= LastProblem;

        /// <summary>题号 → 尺寸档。7x6 / 9x8 / 11x8 / 13x9。</summary>
        public static OvertimeSizePreset PresetFor(int index)
        {
            if (index <= 3) return OvertimeSizePreset.Compact;
            if (index <= 6) return OvertimeSizePreset.Standard;
            if (index <= 9) return OvertimeSizePreset.Wide;
            return OvertimeSizePreset.Deep;
        }

        public static OvertimeSize SizeFor(int index)
        {
            switch (PresetFor(index))
            {
                case OvertimeSizePreset.Compact: return new OvertimeSize(7, 6);
                case OvertimeSizePreset.Standard: return new OvertimeSize(9, 8);
                case OvertimeSizePreset.Wide: return new OvertimeSize(11, 8);
                default: return new OvertimeSize(13, 9);
            }
        }

        /// <summary>目标解题件数。与 <see cref="OvertimeCatalog"/> 每题的转角数一致。</summary>
        public static int TargetStepsFor(int index)
        {
            switch (index)
            {
                case 1:
                case 2: return 2;
                case 3:
                case 4:
                case 7: return 3;
                default: return 4;
            }
        }

        /// <summary>终端显示名：<c>OT-PROBLEM 01</c>。</summary>
        public static string DisplayName(int index) => "OT-PROBLEM " + index.ToString("00");

        /// <summary>磁盘文件名：<c>OT-PROBLEM_001.json</c>（题号固定三位）。</summary>
        public static string FileName(int index) => FilePrefix + index.ToString("000") + ".json";

        /// <summary>
        /// 从文件名解析题号；只认 <see cref="FileName"/> 生成的官方名字。
        /// 12 题是固定集合，<c>OT-PROBLEM_1.json</c> / <c>OT-PROBLEM_0001.json</c>
        /// 这类手改名不能混进题库，否则 Reconcile 会补出一条指向不存在文件的记录。
        /// </summary>
        public static bool TryParseFileName(string fileName, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(fileName)) return false;

            string name = System.IO.Path.GetFileName(fileName);
            if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            string number = name.Substring(FilePrefix.Length, name.Length - FilePrefix.Length - 5);
            if (!int.TryParse(number, out index)) return false;
            if (!IsValidIndex(index)) return false;

            if (!string.Equals(name, FileName(index), StringComparison.OrdinalIgnoreCase))
            {
                index = 0;
                return false;
            }

            return true;
        }
    }
}
