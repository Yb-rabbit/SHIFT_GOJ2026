using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 网格 ↔ 世界坐标换算。逻辑网格躺在 XZ 平面上：X 向右、网格 Y 对应世界 +Z（远离镜头）。
    /// 摄像机从 -Z 方向俯视，所以屏幕上的"上"正好是网格的 Y 增大方向。
    /// </summary>
    public static class GridMetrics
    {
        /// <summary>单格边长（世界单位）。取 1 便于调试，缩放整体交给父物体。</summary>
        public const float CellSize = 1f;

        public static Vector3 CellToWorld(GridPoint coord, float height = 0f)
        {
            return new Vector3(coord.X * CellSize, height, coord.Y * CellSize);
        }

        public static Vector3 DirToWorld(Dir dir)
        {
            switch (dir)
            {
                case Dir.Up: return Vector3.forward;
                case Dir.Right: return Vector3.right;
                case Dir.Down: return Vector3.back;
                default: return Vector3.left;
            }
        }

        /// <summary>整个网格的世界中心（用来摆摄像机）。</summary>
        public static Vector3 GridCenter(int width, int height, float height0 = 0f)
        {
            return new Vector3((width - 1) * 0.5f * CellSize, height0, (height - 1) * 0.5f * CellSize);
        }
    }
}
