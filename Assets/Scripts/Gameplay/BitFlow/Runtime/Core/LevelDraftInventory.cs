using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 玩家手牌放置的结果。UI 要靠它区分提示：没牌了 / 这格是关卡给的 / 这格已占用 / 点歪了。
    /// </summary>
    public enum PlaceResult
    {
        Ok = 0,
        OutOfBounds = 1,
        Locked = 2,
        Occupied = 3,
        OutOfStock = 4,
    }

    /// <summary>
    /// <see cref="LevelDraft"/> 的玩家模式部分：有限手牌 + 关卡预设锁定 + 撤销栈。
    ///
    /// 规则（已确认）：元件放下后还能自由拾回 / 移动 / 旋转，手牌数量是硬约束，但每一次改动都能 Ctrl+Z 撤回
    /// —— 玩家不该因为一次误触就重开关卡，压力应该来自「牌不够用」，而不是「手抖」。
    ///
    /// 库存按类型资产的 Key 记账（不是按 CellKind），所以两摞都用分流器时不会串味，
    /// 拾回的元件也能自动还回它原来那一摞。
    /// </summary>
    public sealed partial class LevelDraft
    {
        /// <summary>撤销栈深度：一次绘制拖动最多几十格，32 步足够覆盖一整关的误操作。</summary>
        const int MaxHistory = 32;

        readonly HashSet<GridPoint> lockedCells = new HashSet<GridPoint>();
        readonly Dictionary<string, int> stock = new Dictionary<string, int>();
        readonly List<DraftSnapshot> history = new List<DraftSnapshot>();

        bool stockEnabled;
        bool restoring;

        /// <summary>是否处于「有限手牌」模式。false = 作者模式（无限涂刷）。</summary>
        public bool StockEnabled => stockEnabled;

        /// <summary>关卡预设件数量（玩家搬不动的那些）。</summary>
        public int LockedCount => lockedCells.Count;

        /// <summary>手牌表：类型 Key → 剩余张数（已放下的不算在内）。</summary>
        public IEnumerable<KeyValuePair<string, int>> Stock => stock;

        /// <summary>登记一摞手牌。调到过一次就自动进入有限手牌模式。</summary>
        public void SetStock(string typeKey, int count)
        {
            if (string.IsNullOrEmpty(typeKey)) return;

            stockEnabled = true;
            stock[typeKey] = Math.Max(0, count);
        }

        /// <summary>某摞牌还剩几张；返回 -1 表示不限量（作者模式，或这个 Key 没有归属）。</summary>
        public int Remaining(string typeKey)
        {
            if (!stockEnabled || string.IsNullOrEmpty(typeKey)) return -1;
            return stock.TryGetValue(typeKey, out int left) ? left : 0;
        }

        public bool IsLocked(GridPoint coord)
        {
            return lockedCells.Contains(coord);
        }

        public void SetLocked(GridPoint coord, bool locked)
        {
            if (!cells.ContainsKey(coord)) return;
            if (locked) lockedCells.Add(coord);
            else lockedCells.Remove(coord);
        }

        /// <summary>
        /// 玩家放一张牌：越界 / 预设格 / 已占用 / 没牌了都会被挡下来，且只有真的放下了才扣牌。
        /// 自由移动是「先 TryPick 拾回、再 TryPlace 放下」，所以这里不做原地替换。
        /// </summary>
        public PlaceResult TryPlace(EditableElement element)
        {
            if (element == null || !InBounds(element.Coord)) return PlaceResult.OutOfBounds;

            GridPoint coord = element.Coord;
            if (lockedCells.Contains(coord)) return PlaceResult.Locked;
            if (cells.ContainsKey(coord)) return PlaceResult.Occupied;

            string typeKey = element.TypeKey;
            int left = Remaining(typeKey);
            if (stockEnabled && !string.IsNullOrEmpty(typeKey) && left <= 0) return PlaceResult.OutOfStock;

            PushHistory();
            cells[coord] = element;
            if (stockEnabled && !string.IsNullOrEmpty(typeKey)) stock[typeKey] = left - 1;
            return PlaceResult.Ok;
        }

        /// <summary>拾回格子里的元件并把牌还回去；预设件或空格返回 false。</summary>
        public bool TryPick(GridPoint coord, out EditableElement picked)
        {
            picked = null;

            if (lockedCells.Contains(coord)) return false;
            if (!cells.TryGetValue(coord, out EditableElement element)) return false;

            int left = Remaining(element.TypeKey);

            PushHistory();
            cells.Remove(coord);
            if (stockEnabled && !string.IsNullOrEmpty(element.TypeKey)) stock[element.TypeKey] = left + 1;

            picked = element;
            return true;
        }

        public bool CanUndo => history.Count > 0;

        /// <summary>撤回上一步（放置 / 拾回 / 旋转 / 删除都算一步）。</summary>
        public bool Undo()
        {
            if (history.Count == 0) return false;

            DraftSnapshot snapshot = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Restore(snapshot);
            return true;
        }

        /// <summary>清空撤销栈（换关 / 重开时用，避免把上一关的操作撤回来）。</summary>
        public void ClearHistory()
        {
            history.Clear();
        }

        /// <summary>载入现有关卡，并把这些坐标标记成玩家动不了的预设件。</summary>
        public static LevelDraft FromDefinition(LevelDefinition level, IEnumerable<GridPoint> lockedCoords)
        {
            LevelDraft draft = FromDefinition(level);
            draft.ClearHistory();

            if (lockedCoords == null) return draft;

            foreach (GridPoint coord in lockedCoords)
            {
                if (draft.cells.ContainsKey(coord)) draft.lockedCells.Add(coord);
            }
            return draft;
        }

        void PushHistory()
        {
            if (restoring) return;

            history.Add(Capture());
            if (history.Count > MaxHistory) history.RemoveAt(0);
        }

        DraftSnapshot Capture()
        {
            DraftSnapshot snapshot = new DraftSnapshot(stockEnabled);

            foreach (KeyValuePair<GridPoint, EditableElement> pair in cells)
            {
                snapshot.Cells[pair.Key] = pair.Value.Clone();
            }

            snapshot.Locked.UnionWith(lockedCells);

            foreach (KeyValuePair<string, int> pair in stock)
            {
                snapshot.Stock[pair.Key] = pair.Value;
            }
            return snapshot;
        }

        void Restore(DraftSnapshot snapshot)
        {
            restoring = true;

            cells.Clear();
            foreach (KeyValuePair<GridPoint, EditableElement> pair in snapshot.Cells)
            {
                // 再克隆一次：撤销之后玩家继续改，不能顺手改到快照里的那份
                cells[pair.Key] = pair.Value.Clone();
            }

            lockedCells.Clear();
            lockedCells.UnionWith(snapshot.Locked);

            stock.Clear();
            foreach (KeyValuePair<string, int> pair in snapshot.Stock)
            {
                stock[pair.Key] = pair.Value;
            }

            stockEnabled = snapshot.StockEnabled;
            restoring = false;
        }

        /// <summary>一步之前的整份状态。格子里的元件必须深拷贝，否则旋转会连带改掉历史。</summary>
        sealed class DraftSnapshot
        {
            public readonly Dictionary<GridPoint, EditableElement> Cells = new Dictionary<GridPoint, EditableElement>();
            public readonly HashSet<GridPoint> Locked = new HashSet<GridPoint>();
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly bool StockEnabled;

            public DraftSnapshot(bool stockEnabled)
            {
                StockEnabled = stockEnabled;
            }
        }
    }
}
