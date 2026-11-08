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

        /// <summary>
        /// 朝向的箭头写法（↑ → ↓ ←），给玩家可见的提示用。
        ///
        /// 为什么不复用 <see cref="ToChineseName"/>：玩家侧的 UI 字体换成了像素字体
        /// （<c>View.PixelFont</c>），没有中文字形，打包后中文会变成方块。
        /// 箭头还顺带省地方 —— 一行参数里要塞两个方向，中文字宽是箭头的两倍。
        /// 作者模式（游戏内编辑器）仍然用中文名，那里是开发工具，不走像素字体。
        /// </summary>
        public static string ToMark(this Dir dir)
        {
            switch (dir)
            {
                case Dir.Up: return "↑";
                case Dir.Right: return "→";
                case Dir.Down: return "↓";
                default: return "←";
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
