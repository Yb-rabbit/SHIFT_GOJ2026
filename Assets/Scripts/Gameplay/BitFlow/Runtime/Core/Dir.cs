using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>四个正交方向，顺序固定为 上 → 右 → 下 → 左，方便做旋转运算。</summary>
    public enum Dir
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3,
    }

    public static class DirUtil
    {
        /// <summary>方向对应的网格步进量。</summary>
        public static GridPoint ToOffset(this Dir dir)
        {
            switch (dir)
            {
                case Dir.Up: return new GridPoint(0, 1);
                case Dir.Right: return new GridPoint(1, 0);
                case Dir.Down: return new GridPoint(0, -1);
                default: return new GridPoint(-1, 0);
            }
        }

        public static Dir Opposite(this Dir dir) => (Dir)(((int)dir + 2) & 3);

        public static Dir RotateClockwise(this Dir dir) => (Dir)(((int)dir + 1) & 3);

        public static Dir RotateCounterClockwise(this Dir dir) => (Dir)(((int)dir + 3) & 3);

        public static bool IsVertical(this Dir dir) => dir == Dir.Up || dir == Dir.Down;

        /// <summary>中文朝向名（提示框与游戏内编辑器共用）。</summary>
        public static string ToChineseName(this Dir dir)
        {
            switch (dir)
            {
                case Dir.Up: return "上";
                case Dir.Right: return "右";
                case Dir.Down: return "下";
                default: return "左";
            }
        }

        public static bool Contains(this IEnumerable<Dir> dirs, Dir dir)
        {
            foreach (Dir candidate in dirs)
            {
                if (candidate == dir) return true;
            }
            return false;
        }
    }
}
