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

        /// <summary>世界坐标 → 网格坐标（游戏内编辑器用鼠标点格子时走这条路径）。</summary>
        public static GridPoint WorldToCell(Vector3 world)
        {
            return new GridPoint(
                Mathf.RoundToInt(world.x / CellSize),
                Mathf.RoundToInt(world.z / CellSize));
        }

        /// <summary>整个网格的世界中心（用来摆摄像机）。</summary>
        public static Vector3 GridCenter(int width, int height, float height0 = 0f)
        {
            return new Vector3((width - 1) * 0.5f * CellSize, height0, (height - 1) * 0.5f * CellSize);
        }

        /// <summary>玩家关卡里最宽的板（9×8）。机位就是按它调校好的：不超过这个宽度一律不推远。</summary>
        public const int CameraReferenceWidth = 9;

        /// <summary>板每宽出一格，最终机位再多推远多少。0.0275 由「13×9 刚好够用 = 1.105」反推。</summary>
        const float CameraPullBackPerColumn = 0.0275f;

        /// <summary>推远上限。再远板就缩成画面中央一小块，反而更难读。</summary>
        public const float MaxCameraPullBack = 1.15f;

        /// <summary>
        /// 板比参考板宽时，最终机位要乘以的「距离」倍数（只放大距离，俯角不变）。
        ///
        /// 按 1 倍摆位时，13×9 这类工坊大板的卡排底部会跑到屏幕下沿之外、网格前排两角也贴边 ——
        /// 玩家第一眼会以为最底下那一行被卡片挡住了。宽度 ≤ <see cref="CameraReferenceWidth"/> 的板
        /// （9×8 / 7×6 等全部玩家关卡）恒等于 1，画面逐像素不变。
        /// </summary>
        public static float CameraPullBack(int width)
        {
            int extra = Mathf.Max(0, width - CameraReferenceWidth);
            return Mathf.Clamp(1f + extra * CameraPullBackPerColumn, 1f, MaxCameraPullBack);
        }
    }
}
