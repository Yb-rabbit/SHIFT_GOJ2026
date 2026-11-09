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

        /// <summary>
        /// 本关发给玩家的手牌（作者在工坊里按类型填的数字）。
        /// 空表 = 这一关没有手牌（演示关，或工坊存出来的「自己会跑」的机器），
        /// 语义和 <c>LevelSO.supplies</c> 完全一致。
        /// </summary>
        public readonly List<LevelSupplyEntry> Supplies = new List<LevelSupplyEntry>();

        /// <summary>
        /// 进关后允许玩家自己搬元件（对应 JSON 的 <c>allowBuild</c>）。
        /// 有手牌的关卡必须打开：否则玩家看得见卡、却一个都放不下去。
        /// </summary>
        public bool AllowBuild;

        /// <summary>有没有给玩家手牌。</summary>
        public bool HasSupplies => Supplies.Count > 0;

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

    /// <summary>
    /// 一条手牌配置：给某一种元件几张牌。
    ///
    /// <see cref="TypeKey"/> 是权威（对应 <c>ElementTypeSO.Key</c>），玩家拾回的牌靠它还回正确的牌堆；
    /// <see cref="Kind"/> 只是「类型解析不出来时的兜底」（工坊写 JSON 时会同时写这两个字段，
    /// 手工改坏 type 以后至少还知道该给哪一种元件）。
    /// </summary>
    public sealed class LevelSupplyEntry
    {
        public string TypeKey = string.Empty;
        public CellKind Kind = CellKind.Wall;

        /// <summary>给几张（必须 ≥ 1，0 张等于不给，写档案时会直接跳过）。</summary>
        public int Count = 1;
    }
}
