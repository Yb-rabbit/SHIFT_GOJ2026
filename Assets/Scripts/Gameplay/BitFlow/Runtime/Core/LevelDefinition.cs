using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 一关的完整数据（纯 C#，不依赖 ScriptableObject，因此可以在 EditMode 测试里直接构造）。
    /// 实际关卡数据由 <c>LevelSO</c> 转换成这个对象后交给 BitFlowSimulation。
    /// </summary>
    public sealed class LevelDefinition
    {
        public string DisplayName = "Level 01";

        public int Width = 9;
        public int Height = 8;

        /// <summary>一个 tick 的墙钟时长（秒），由难度/倍速在 UI 层再缩放。</summary>
        public float TickInterval = 0.5f;

        public readonly List<ElementDefinition> Elements = new List<ElementDefinition>();

        public bool InBounds(GridPoint p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        public IEnumerable<ElementDefinition> ElementsOfKind(CellKind kind)
        {
            foreach (ElementDefinition element in Elements)
            {
                if (element.Kind == kind) yield return element;
            }
        }

        /// <summary>建立 坐标 → 元件 的查表（模拟器内部用，也方便测试做断言）。</summary>
        public Dictionary<GridPoint, ElementDefinition> BuildLookup()
        {
            Dictionary<GridPoint, ElementDefinition> lookup = new Dictionary<GridPoint, ElementDefinition>();
            foreach (ElementDefinition element in Elements)
            {
                lookup[element.Coord] = element;
            }
            return lookup;
        }
    }
}
