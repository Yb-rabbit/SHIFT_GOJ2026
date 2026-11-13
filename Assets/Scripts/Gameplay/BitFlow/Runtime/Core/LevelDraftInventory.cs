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
    /// BUFFER（把预设件收回手牌）的结果。和 <see cref="PlaceResult"/> 分开是因为它多两种专属失败：
    /// 这格不是锁定的（该用右键）/ 这一件根本不许被收回（发射器、接收器）。
    /// </summary>
    public enum RetractResult
    {
        Ok = 0,
        OutOfBounds = 1,
        EmptyCell = 2,
        NotLocked = 3,
        NotBufferable = 4,
        OutOfStock = 5,
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

        /// <summary>发出去的张数（只增不减）。存档要写这个，不能写 <see cref="stock"/> 的剩余量。</summary>
        readonly Dictionary<string, int> granted = new Dictionary<string, int>();

        /// <summary>每一摞牌是哪种元件。写关卡档案时按它填 <c>kind</c> 兜底。</summary>
        readonly Dictionary<string, CellKind> stockKinds = new Dictionary<string, CellKind>();

        readonly List<DraftSnapshot> history = new List<DraftSnapshot>();

        bool stockEnabled;
        bool restoring;

        /// <summary>是否处于「有限手牌」模式。false = 作者模式（无限涂刷）。</summary>
        public bool StockEnabled => stockEnabled;

        /// <summary>
        /// 作者模式：锁定标记只当「给玩家的标记」，<b>不挡作者自己</b>。
        ///
        /// 工坊里必须打开它：那一局里作者正在做这一关，他要能搬动 / 旋转 / 改参数自己锁过的件，
        /// 否则「把预设件标成 FIXED」这个动作会先把作者的鼠标关在门外。
        /// 玩家局（<c>LevelSO.CreatePlayerDraft</c>）永远从 false 开始，所以锁定对玩家照样是硬的。
        /// </summary>
        public bool AuthorMode;

        /// <summary>关卡预设件数量（玩家搬不动的那些）。</summary>
        public int LockedCount => lockedCells.Count;

        /// <summary>手牌表：类型 Key → 剩余张数（已放下的不算在内）。</summary>
        public IEnumerable<KeyValuePair<string, int>> Stock => stock;

        /// <summary>登记一摞手牌。调到过一次就自动进入有限手牌模式。</summary>
        public void SetStock(string typeKey, int count)
        {
            if (string.IsNullOrEmpty(typeKey)) return;

            // 不知道种类时沿用上一次登记的（多半是关卡资产那条路），实在没有就记成墙 ——
            // 只要 TypeKey 还在，写档案时它就是权威，kind 只是个兜底。
            SetStock(typeKey, KindOfStock(typeKey), count);
        }

        /// <summary>
        /// 登记一摞手牌，并记住它是哪种元件。工坊的「发牌」面板走这条：
        /// 它只认得类型资产，不认得 <see cref="CellKind"/> 之外的运行期信息。
        /// </summary>
        public void SetStock(string typeKey, CellKind kind, int count)
        {
            if (string.IsNullOrEmpty(typeKey)) return;

            stockEnabled = true;

            int value = Math.Max(0, count);
            stock[typeKey] = value;
            granted[typeKey] = value;
            stockKinds[typeKey] = kind;
        }

        /// <summary>把某一摞牌整摞取消（0 张 = 不给）。</summary>
        public void ClearStock(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey)) return;

            stock.Remove(typeKey);
            granted.Remove(typeKey);
            stockKinds.Remove(typeKey);

            // 全撤光了就退回「不限量」的作者模式，免得玩家面对一排 0 张的卡
            if (stock.Count == 0) stockEnabled = false;
        }

        /// <summary>撤销发牌：整张手牌表清空并退回作者模式。</summary>
        public void ClearAllStock()
        {
            stock.Clear();
            granted.Clear();
            stockKinds.Clear();
            stockEnabled = false;
        }

        /// <summary>
        /// 发牌（UI 用）：和格子的改动一样压一步撤销栈。
        /// <paramref name="count"/> ≤ 0 等于「这一摞不给」，会把整摞撤掉。
        /// </summary>
        public void EditStock(string typeKey, CellKind kind, int count)
        {
            if (string.IsNullOrEmpty(typeKey)) return;

            PushHistory();

            if (count <= 0) ClearStock(typeKey);
            else SetStock(typeKey, kind, count);
        }

        /// <summary>撤回所有发牌（UI 的 NO HAND）。本来就是不限量时什么都不做，别白压一格撤销栈。</summary>
        public void EditClearHand()
        {
            if (!stockEnabled && stock.Count == 0 && granted.Count == 0) return;

            PushHistory();
            ClearAllStock();
        }

        /// <summary>某一摞牌发过多少张（存档用；放下的牌不会让它变小）。</summary>
        public int Granted(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey)) return 0;
            return granted.TryGetValue(typeKey, out int value) ? value : 0;
        }

        /// <summary>某一摞牌是哪种元件；没登记过按墙处理。</summary>
        public CellKind KindOfStock(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey)) return CellKind.Wall;
            return stockKinds.TryGetValue(typeKey, out CellKind kind) ? kind : CellKind.Wall;
        }

        /// <summary>发出去的牌总共几张（工坊状态行「HAND x/y」用）。</summary>
        public int StockTotal
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<string, int> pair in granted) total += Math.Max(0, pair.Value);
                return total;
            }
        }

        /// <summary>
        /// 往某一摞牌里「加」张数，而不是覆盖。
        ///
        /// 关卡资产登记手牌必须走这里：库存是按类型 Key 记账的（两摞都用分流器时就该合成一摞），
        /// 所以 supplies 里同一个类型出现两次是「再给几张」，不是「换一个数字」——
        /// 覆盖式登记会让玩家凭空少牌。存档载入仍用 <see cref="SetStock"/>：
        /// 存档里每个 Key 只出现一次，语义就是"这就是全部剩余"。
        /// </summary>
        public void AddStock(string typeKey, int count)
        {
            AddStock(typeKey, KindOfStock(typeKey), count);
        }

        /// <summary>
        /// 同上，并且<b>记下这一摞是哪种元件</b>。
        ///
        /// 关卡资产（<c>LevelSO.CreatePlayerDraft</c>）必须走这条：类型资产本身没有 kind 字段，
        /// 不传进来的话 <see cref="KindOfStock"/> 只能兜底成墙 ——
        /// 存档里的 <c>kind</c> 就会把「非门」写成「墙」，而自检
        /// （<see cref="LevelCheck.RunForSave"/> 的"发射器/接收器不能发牌"那条）也就漏检了。
        /// </summary>
        public void AddStock(string typeKey, CellKind kind, int count)
        {
            if (string.IsNullOrEmpty(typeKey)) return;

            stockEnabled = true;
            int add = Math.Max(0, count);

            int left = stock.TryGetValue(typeKey, out int existing) ? existing : 0;
            stock[typeKey] = left + add;

            int given = granted.TryGetValue(typeKey, out int already) ? already : 0;
            granted[typeKey] = given + add;

            stockKinds[typeKey] = kind;
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

        /// <summary>
        /// 这一格现在<b>挡不挡人</b>。和 <see cref="IsLocked"/> 的区别：作者模式下锁定只是个标记，
        /// 挡住了玩家挡不住作者 —— 工坊里的每一次搬动 / 旋转 / 改参数都走这个判定。
        /// </summary>
        bool Blocked(GridPoint coord)
        {
            return !AuthorMode && lockedCells.Contains(coord);
        }

        public void SetLocked(GridPoint coord, bool locked)
        {
            if (!cells.ContainsKey(coord)) return;
            if (locked) lockedCells.Add(coord);
            else lockedCells.Remove(coord);
        }

        /// <summary>
        /// 改「玩家能不能搬这一格」，并压一步撤销栈。
        ///
        /// 和 <see cref="SetLocked"/> 的区别只有撤销：那个是载入关卡时批量打标记用的（不算玩家的一步），
        /// 这个是作者在工坊里按 FIXED / FREE —— 手滑锁错了必须能 Ctrl+Z 回来。
        /// </summary>
        public void EditLock(GridPoint coord, bool locked)
        {
            if (!cells.ContainsKey(coord)) return;
            if (lockedCells.Contains(coord) == locked) return;

            PushHistory();
            SetLocked(coord, locked);
        }

        /// <summary>
        /// 只读判定：这一张现在能不能落在它自己的 <see cref="EditableElement.Coord"/> 上。
        /// 规则和 <see cref="TryPlace"/> 逐条一致，但**不动任何状态**（不扣牌、不压撤销栈）——
        /// 放置预览要跟着鼠标在每一格上问一遍"能放吗"，绝不能顺手改数据。
        /// </summary>
        public PlaceResult CanPlace(EditableElement element)
        {
            if (element == null || !InBounds(element.Coord)) return PlaceResult.OutOfBounds;

            GridPoint coord = element.Coord;
            if (Blocked(coord)) return PlaceResult.Locked;
            if (cells.ContainsKey(coord)) return PlaceResult.Occupied;

            string typeKey = element.TypeKey;
            if (stockEnabled && !string.IsNullOrEmpty(typeKey) && Remaining(typeKey) <= 0) return PlaceResult.OutOfStock;

            return PlaceResult.Ok;
        }

        /// <summary>
        /// 玩家放一张牌：越界 / 预设格 / 已占用 / 没牌了都会被挡下来，且只有真的放下了才扣牌。
        /// 自由移动是「先 TryPick 拾回、再 TryPlace 放下」，所以这里不做原地替换。
        /// 校验部分复用 <see cref="CanPlace"/>，保证"预览说能放"和"真的放下"永远是同一套规则。
        /// </summary>
        public PlaceResult TryPlace(EditableElement element)
        {
            PlaceResult check = CanPlace(element);
            if (check != PlaceResult.Ok) return check;

            GridPoint coord = element.Coord;
            string typeKey = element.TypeKey;
            int left = Remaining(typeKey);

            PushHistory();
            cells[coord] = element;
            if (stockEnabled && !string.IsNullOrEmpty(typeKey)) stock[typeKey] = left - 1;
            return PlaceResult.Ok;
        }

        /// <summary>拾回格子里的元件并把牌还回去；预设件或空格返回 false。</summary>
        public bool TryPick(GridPoint coord, out EditableElement picked)
        {
            picked = null;

            if (Blocked(coord)) return false;
            if (!cells.TryGetValue(coord, out EditableElement element)) return false;

            int left = Remaining(element.TypeKey);

            PushHistory();
            cells.Remove(coord);
            if (stockEnabled && !string.IsNullOrEmpty(element.TypeKey)) stock[element.TypeKey] = left + 1;

            picked = element;
            return true;
        }

        /// <summary>
        /// BUFFER：把一堵<b>关卡预设件</b>收回手牌，并把这件本身交回去（UI 会直接把它拿在手里）。
        ///
        /// 和 <see cref="TryPick"/> 的三处差别都是故意的：
        /// 1) 只认锁定格 —— 自己放的件右键就能免费拾回，拿工具牌去收它是白花一张；
        /// 2) 只认 <see cref="SupplyKinds.IsSupplyKind"/> 的种类 —— 发射器 / 接收器定义了这道题，谁也不能收走，
        ///    否则玩家等于自己改题目；
        /// 3) 要花掉一张 <paramref name="toolKey"/>（BUFFER 本身），但**先判定再记账**：任何失败都不扣牌。
        ///
        /// 记账方向和放牌相反：收回来的件要往它那一摞里**加一张**（stock 与 granted 同步加），
        /// 所以状态行的 "HAND x/y" 里 y 会变大 —— 手牌总量没变，只是从工具牌换成了元件牌，可以再放回去。
        /// </summary>
        public RetractResult TryRetract(GridPoint coord, string toolKey, out EditableElement picked)
        {
            picked = null;

            if (!InBounds(coord)) return RetractResult.OutOfBounds;
            if (!cells.TryGetValue(coord, out EditableElement element)) return RetractResult.EmptyCell;
            if (!lockedCells.Contains(coord)) return RetractResult.NotLocked;
            if (!SupplyKinds.IsSupplyKind(element.Kind)) return RetractResult.NotBufferable;

            // 没有有限手牌就没有「一张工具牌」可花：作者模式里不该出现能凭空搬走预设件的操作
            if (!stockEnabled || string.IsNullOrEmpty(toolKey)) return RetractResult.OutOfStock;

            int toolLeft = stock.TryGetValue(toolKey, out int left) ? left : 0;
            if (toolLeft <= 0) return RetractResult.OutOfStock;

            PushHistory();

            cells.Remove(coord);
            lockedCells.Remove(coord);

            // 收回来的这一件回到它自己那一摞：TypeKey 是权威，玩家拿到的是「这一件」而不是这种元件的默认值
            string typeKey = element.TypeKey;
            if (!string.IsNullOrEmpty(typeKey) && typeKey != toolKey)
            {
                int have = stock.TryGetValue(typeKey, out int held) ? held : 0;
                stock[typeKey] = have + 1;
                granted[typeKey] = Granted(typeKey) + 1;
                if (!stockKinds.ContainsKey(typeKey)) stockKinds[typeKey] = element.Kind;
            }

            stock[toolKey] = toolLeft - 1;

            picked = element;
            return RetractResult.Ok;
        }

        /// <summary>
        /// 搬动判定：<paramref name="from"/> 格能搬、<paramref name="to"/> 格能落。
        ///
        /// 规则和 <see cref="CanPlace"/> 一致，但有两条不同：
        /// 1) 不看手牌库存 —— 搬动是把已有的件挪个位置，不是又发了一张牌；
        /// 2) 目标就是原格时算 Ok —— 玩家抓起又原地放下，最自然的反应是"什么都没发生"，而不是一句报错。
        ///
        /// 只读：预览要在每一格上问一遍，绝不能顺手改数据。
        /// </summary>
        public PlaceResult CanMove(GridPoint from, GridPoint to)
        {
            if (!InBounds(to)) return PlaceResult.OutOfBounds;
            if (Blocked(from)) return PlaceResult.Locked;
            if (!cells.ContainsKey(from)) return PlaceResult.Occupied;
            if (to.Equals(from)) return PlaceResult.Ok;
            if (Blocked(to)) return PlaceResult.Locked;
            if (cells.ContainsKey(to)) return PlaceResult.Occupied;
            return PlaceResult.Ok;
        }

        /// <summary>
        /// 把 <paramref name="from"/> 格的整件挪到 <paramref name="to"/> 格。
        ///
        /// 挪的是同一个 <see cref="EditableElement"/>：序列 / 张数 / 朝向 / 类型 Key 全都跟着走，
        /// 玩家把门挪个位置不会把它改回默认配置（这正是"拾回再放"那条老路会丢的东西）。
        /// 整次搬动只压一步撤销栈，而且不碰库存 —— 搬一次不该让玩家少一张牌。
        /// </summary>
        public bool TryMove(GridPoint from, GridPoint to)
        {
            if (CanMove(from, to) != PlaceResult.Ok) return false;
            if (to.Equals(from)) return true;

            EditableElement element = cells[from];
            bool wasLocked = lockedCells.Contains(from);

            PushHistory();
            cells.Remove(from);
            element.Coord = to;
            cells[to] = element;

            // 锁必须跟着件走。作者模式下能搬自己锁着的件，如果锁留在原格，
            // 新位置对玩家就变成"可搬"、旧位置变成"不可放"—— 两边都错。
            if (wasLocked)
            {
                lockedCells.Remove(from);
                lockedCells.Add(to);
            }

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

            foreach (KeyValuePair<string, int> pair in granted)
            {
                snapshot.Granted[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<string, CellKind> pair in stockKinds)
            {
                snapshot.StockKinds[pair.Key] = pair.Value;
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

            granted.Clear();
            foreach (KeyValuePair<string, int> pair in snapshot.Granted)
            {
                granted[pair.Key] = pair.Value;
            }

            stockKinds.Clear();
            foreach (KeyValuePair<string, CellKind> pair in snapshot.StockKinds)
            {
                stockKinds[pair.Key] = pair.Value;
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
            public readonly Dictionary<string, int> Granted = new Dictionary<string, int>();
            public readonly Dictionary<string, CellKind> StockKinds = new Dictionary<string, CellKind>();
            public readonly bool StockEnabled;

            public DraftSnapshot(bool stockEnabled)
            {
                StockEnabled = stockEnabled;
            }
        }
    }
}
