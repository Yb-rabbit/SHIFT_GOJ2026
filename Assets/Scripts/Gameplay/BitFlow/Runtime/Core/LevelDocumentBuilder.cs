using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// <see cref="LevelDocument"/> → <see cref="LevelDefinition"/>（纯 C#、零 UnityEngine）。
    ///
    /// 规则和 Unity 侧的 <c>LevelJsonSerializer.Read</c> + <c>LevelSO.ToDefinition</c> **逐条对齐**：
    /// - 坐标越界 / 重复（先到先得）→ 记一条问题并跳过这一格，其余照常读；
    /// - 元件按 (y, x) 排序后从 1 开始分配 Id，墙层追加在最后（墙的 Id 接在后面，不整体错位）；
    /// - <c>type</c> 模式以 <c>rot</c> 定朝向且忽略 <c>pass</c>；<c>kind</c> 模式以绝对朝向 <c>dir</c>/<c>dir2</c> 为准；
    /// - <c>sequence</c> / <c>count</c> / <c>face</c> 出现即覆盖类型资产的默认值。
    ///
    /// 外部关卡编辑器（Tools/LevelStudio）和 Unity 共用这一份，所以「工具里测通」= 「游戏里能过」。
    /// </summary>
    public static class LevelDocumentBuilder
    {
        /// <summary>把一份文档构建成逻辑关卡。问题（越界/未知类型/叠格…）写进 problems，不会抛异常。</summary>
        public static LevelDefinition Build(LevelDocument document, LevelTypeCatalog catalog, List<string> problems)
        {
            LevelDefinition definition = new LevelDefinition();
            if (document == null)
            {
                Add(problems, "关卡数据是空的");
                return definition;
            }

            int width = Math.Max(1, document.width);
            int height = Math.Max(1, document.height);

            definition.DisplayName = string.IsNullOrEmpty(document.displayName) ? "CUSTOM LEVEL" : document.displayName;
            definition.Width = width;
            definition.Height = height;
            definition.TickInterval = document.tickInterval > 0.01f
                ? Clamp(document.tickInterval, 0.05f, 5f)
                : 0.35f;

            List<PlacedCell> placed = new List<PlacedCell>();
            ReadCells(document, catalog, width, height, placed, problems);

            // 统一按 (y, x) 排序后分配 Id —— 同一份 JSON 无论行序怎么排，构建结果都一样
            placed.Sort(ComparePlaced);

            int nextId = 1;
            HashSet<GridPoint> occupied = new HashSet<GridPoint>();
            foreach (PlacedCell cell in placed)
            {
                ElementDefinition element = cell.Spec.CreateDefinition(new GridPoint(cell.X, cell.Y), cell.RotationSteps, nextId);
                if (element == null) continue;

                element.Label = cell.Spec.label;
                element.Hint = cell.Spec.hint;
                element.TypeKey = string.IsNullOrEmpty(cell.TypeKey) ? cell.Spec.key : cell.TypeKey;

                // JSON 里的 playerEditable 是「玩家能不能搬这一格」的唯一来源。
                // 缺省 false = 预设件，和 LevelJsonSerializer / LevelSO 保持同一套语义。
                element.PlayerEditable = cell.Cell != null && cell.Cell.playerEditable;

                ApplyCellOverrides(element, cell);

                definition.Elements.Add(element);
                occupied.Add(element.Coord);
                nextId++;
            }

            AppendWalls(document, catalog, width, height, definition, occupied, nextId, problems);
            ReadSupplies(document, definition, problems);
            return definition;
        }

        /// <summary>
        /// 读手牌表（作者给玩家几张牌）。规则对齐 <c>LevelJsonSerializer.ReadSupplies</c>：
        /// <c>count</c> 缺省（-1）= 1 张，<c>count=0</c> 等于「不给这张牌」（记一条问题后跳过），
        /// <c>type</c> 原样保留——类型资产只属于 Unity 侧，纯 C# 层不认它，但也不能把它丢掉。
        /// </summary>
        static void ReadSupplies(LevelDocument document, LevelDefinition definition, List<string> problems)
        {
            definition.Supplies.Clear();
            if (document.supplies == null) return;

            for (int i = 0; i < document.supplies.Count; i++)
            {
                LevelDocumentSupply supply = document.supplies[i];
                if (supply == null) continue;

                string where = "supplies[" + i + "]";

                int count = supply.count >= 0 ? supply.count : 1;
                if (count == 0)
                {
                    Add(problems, where + " count=0 等于不给这张牌，已跳过");
                    continue;
                }

                CellKind kind;
                bool hasKind = TryParseKind(supply.kind, out kind);

                if (string.IsNullOrEmpty(supply.type) && !hasKind)
                {
                    Add(problems, where + " 既没有 type 也认不出 kind，已跳过");
                    continue;
                }

                definition.Supplies.Add(new LevelSupplyEntry
                {
                    TypeKey = supply.type ?? string.Empty,
                    Kind = hasKind ? kind : CellKind.Wall,
                    Count = count,
                });
            }

            // 有手牌就必须能动手：手写 JSON 忘了写 allowBuild 时，玩家会盯着一排点不动的卡
            definition.AllowBuild = document.allowBuild || definition.Supplies.Count > 0;
        }

        /// <summary>只有一份文本、没有类型目录时的快捷入口（kind 模式足够描述一整关）。</summary>
        public static LevelDefinition Build(LevelDocument document, List<string> problems)
        {
            return Build(document, null, problems);
        }

        // ------------------------------------------------------------------ 元件

        sealed class PlacedCell
        {
            public LevelTypeSpec Spec;
            public LevelDocumentCell Cell;
            public int X;
            public int Y;
            public int RotationSteps;
            public string TypeKey;

            /// <summary>这一格是不是按 kind 模式落地的（此时 dir/dir2/pass 才是权威朝向/取值）。</summary>
            public bool KindMode;
        }

        static int ComparePlaced(PlacedCell left, PlacedCell right)
        {
            if (left.Y != right.Y) return left.Y.CompareTo(right.Y);
            return left.X.CompareTo(right.X);
        }

        static void ReadCells(LevelDocument document, LevelTypeCatalog catalog, int width, int height,
            List<PlacedCell> placed, List<string> problems)
        {
            if (document.elements == null) return;

            HashSet<GridPoint> used = new HashSet<GridPoint>();
            for (int i = 0; i < document.elements.Count; i++)
            {
                LevelDocumentCell cell = document.elements[i];
                if (cell == null) continue;

                string where = "elements[" + i + "]";
                GridPoint coord = new GridPoint(cell.x, cell.y);

                if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height)
                {
                    Add(problems, where + " 坐标 " + coord + " 越界（本关 " + width + "×" + height + "），已跳过");
                    continue;
                }

                if (!used.Add(coord))
                {
                    Add(problems, where + " 坐标 " + coord + " 与前面的格子重复，已跳过");
                    continue;
                }

                if (!TryResolveSpec(cell, catalog, where, problems, out LevelTypeSpec spec, out int rotationSteps, out bool kindMode))
                {
                    used.Remove(coord);
                    continue;
                }

                placed.Add(new PlacedCell
                {
                    Spec = spec,
                    Cell = cell,
                    X = cell.x,
                    Y = cell.y,
                    RotationSteps = ((rotationSteps % 4) + 4) % 4,
                    TypeKey = cell.type,
                    KindMode = kindMode,
                });
            }
        }

        /// <summary>
        /// 一格 → 类型快照。两种姿势：
        /// <c>type</c> 精确（要走类型目录，找不到就退回 kind）；<c>kind</c> 宽松（用同类快照的默认值，没有就内置默认）。
        /// </summary>
        static bool TryResolveSpec(LevelDocumentCell cell, LevelTypeCatalog catalog, string where,
            List<string> problems, out LevelTypeSpec spec, out int rotationSteps, out bool kindMode)
        {
            spec = null;
            kindMode = true;
            rotationSteps = ((cell.rot % 4) + 4) % 4;

            if (!string.IsNullOrEmpty(cell.type))
            {
                if (catalog != null && catalog.TryGet(cell.type, out LevelTypeSpec found))
                {
                    spec = found;
                    kindMode = false;
                    return true;
                }

                if (!TryParseKind(cell.kind, out CellKind fallbackKind))
                {
                    Add(problems, where + " 找不到类型 " + cell.type + "（缺 typeCatalog.json？也不认识 kind=\"" + cell.kind + "\"）");
                    return false;
                }

                // 退回 kind 模式：这一格写的 dir / pass 就是权威值，不要再叠 rot
                Add(problems, where + " 类型目录里没有 " + cell.type + "，按 kind=" + fallbackKind + " 的默认值处理");
                spec = BuildKindSpec(fallbackKind, catalog);
                rotationSteps = 0;
                return spec != null;
            }

            if (!TryParseKind(cell.kind, out CellKind kind))
            {
                Add(problems, where + " 解析不出元件类型：kind=\"" + cell.kind + "\" type=\"" + cell.type + "\"");
                return false;
            }

            spec = BuildKindSpec(kind, catalog);
            rotationSteps = 0;
            return spec != null;
        }

        /// <summary>
        /// kind 模式的默认值。**参数一律用内置默认**（= <see cref="EditableElement.CreateDefault"/>），
        /// 因为 Unity 侧解析 kind 时也是先造一份这样的草稿，再去找/造等价的类型资产 ——
        /// 拿类型目录里「恰好同类的第一份资产」当默认值会和游戏里跑出两个结果。
        /// 类型目录只借它的短名/提示（纯显示）。
        /// </summary>
        static LevelTypeSpec BuildKindSpec(CellKind kind, LevelTypeCatalog catalog)
        {
            EditableElement defaults = EditableElement.CreateDefault(kind, new GridPoint(0, 0));

            LevelTypeSpec spec = new LevelTypeSpec
            {
                key = string.Empty,
                kind = kind.ToString(),
                label = string.Empty,
                hint = defaults.Summary(),
                dir = defaults.PrimaryDir.ToString(),
                dir2 = defaults.SecondaryDir.ToString(),
                sequence = defaults.Sequence,
                count = defaults.Count,
                acceptAnyFace = true,
                pass = defaults.PassValue,
            };

            LevelTypeSpec named = catalog != null ? catalog.FindByKind(kind) : null;
            if (named != null)
            {
                spec.label = named.label;
                spec.hint = named.hint;
            }

            return spec;
        }

        /// <summary>把这一格自己写的参数盖到生成好的定义上（和 ElementPlacement.ApplyOverrides 同义）。</summary>
        static void ApplyCellOverrides(ElementDefinition element, PlacedCell placed)
        {
            LevelDocumentCell cell = placed.Cell;
            bool kindMode = placed.KindMode;

            switch (element)
            {
                case EmitterDefinition emitter:
                    if (kindMode && TryParseDir(cell.dir, out Dir facing)) emitter.Facing = facing;
                    if (!string.IsNullOrEmpty(cell.sequence)) emitter.Sequence = cell.sequence.Trim();
                    if (cell.count >= 0) emitter.EmitCount = Math.Max(0, cell.count);
                    break;

                case ReceiverDefinition receiver:
                    if (kindMode && TryParseDir(cell.dir, out Dir feed)) receiver.FeedFace = feed;

                    string face = ParseFace(cell.face);
                    if (face == "any") receiver.AcceptAnyFace = true;
                    else if (face == "in") receiver.AcceptAnyFace = false;

                    if (!string.IsNullOrEmpty(cell.sequence)) receiver.TargetSequence = cell.sequence.Trim();
                    if (cell.count >= 0) receiver.TargetCount = Math.Max(0, cell.count);
                    break;

                case SplitterDefinition splitter:
                    if (kindMode)
                    {
                        if (TryParseDir(cell.dir, out Dir one)) splitter.OneArm = one;
                        if (TryParseDir(cell.dir2, out Dir zero)) splitter.ZeroArm = zero;
                    }
                    break;

                case NotGateDefinition notGate:
                    if (kindMode && TryParseDir(cell.dir, out Dir output)) notGate.OutputDir = output;
                    break;

                case FilterDefinition filter:
                    if (kindMode && TryParseDir(cell.dir, out Dir filterOutput)) filter.OutputDir = filterOutput;
                    if (kindMode && cell.pass >= 0) filter.PassValue = (byte)(cell.pass == 0 ? 0 : 1);
                    break;
            }
        }

        // ------------------------------------------------------------------ 墙层

        static void AppendWalls(LevelDocument document, LevelTypeCatalog catalog, int width, int height,
            LevelDefinition definition, HashSet<GridPoint> occupied, int firstId, List<string> problems)
        {
            List<GridPoint> walls = ParseWalls(document, width, height, problems);
            if (walls.Count == 0) return;

            LevelTypeSpec wallSpec = catalog != null ? catalog.FindByKind(CellKind.Wall) : null;
            string label = wallSpec != null ? wallSpec.label : string.Empty;
            string hint = wallSpec != null ? wallSpec.hint : "WALL";
            string typeKey = wallSpec != null ? wallSpec.key : string.Empty;

            int id = firstId;
            int overlapped = 0;
            foreach (GridPoint coord in walls)
            {
                if (occupied.Contains(coord))
                {
                    overlapped++;
                    continue;
                }

                definition.Elements.Add(new WallDefinition
                {
                    Id = id,
                    Coord = coord,
                    Label = label,
                    Hint = hint,
                    TypeKey = typeKey,
                });

                id++;
            }

            if (overlapped > 0)
            {
                Add(problems, "墙层和 elements 有 " + overlapped + " 个格子重叠，那些格子的墙已忽略（元件优先）");
            }
        }

        /// <summary>
        /// 墙层 → 坐标表。一行一个 y（第一行 y=0，从下往上，行序与 wallRows 一致），
        /// 行内第 i 个字符是 x=i；短行右侧留空，超出尺寸的字符忽略，同一格只算一次。
        /// </summary>
        public static List<GridPoint> ParseWalls(LevelDocument document, int width, int height, List<string> problems)
        {
            List<GridPoint> walls = new List<GridPoint>();
            if (document == null) return walls;

            List<string> lines = WallLines(document, height, problems);

            HashSet<GridPoint> seen = new HashSet<GridPoint>();
            for (int y = 0; y < lines.Count && y < height; y++)
            {
                string line = lines[y];
                if (string.IsNullOrEmpty(line)) continue;

                int limit = Math.Min(line.Length, width);
                for (int x = 0; x < limit; x++)
                {
                    if (line[x] != '#') continue;

                    GridPoint coord = new GridPoint(x, y);
                    if (seen.Add(coord)) walls.Add(coord);
                }
            }

            return walls;
        }

        static List<string> WallLines(LevelDocument document, int height, List<string> problems)
        {
            List<string> lines = new List<string>();

            if (document.wallRows != null && document.wallRows.Length > 0)
            {
                if (document.wallRows.Length > height)
                {
                    Add(problems, "wallRows 有 " + document.wallRows.Length + " 行，超过 height=" + height
                        + "，多出来的行会被忽略");
                }

                foreach (string row in document.wallRows) lines.Add(row ?? string.Empty);
                return lines;
            }

            if (string.IsNullOrEmpty(document.wallMap)) return lines;

            string[] split = document.wallMap.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string row in split) lines.Add(row);
            return lines;
        }

        // ------------------------------------------------------------------ 小工具

        /// <summary>朝向解析：Up/Right/Down/Left（不分大小写），也认 ↑→↓← 与 上右下左。</summary>
        public static bool TryParseDir(string text, out Dir dir)
        {
            dir = Dir.Right;
            if (string.IsNullOrEmpty(text)) return false;

            switch (text.Trim())
            {
                case "↑": dir = Dir.Up; return true;
                case "→": dir = Dir.Right; return true;
                case "↓": dir = Dir.Down; return true;
                case "←": dir = Dir.Left; return true;
                case "上": dir = Dir.Up; return true;
                case "右": dir = Dir.Right; return true;
                case "下": dir = Dir.Down; return true;
                case "左": dir = Dir.Left; return true;
            }

            return Enum.TryParse(text.Trim(), true, out dir) && Enum.IsDefined(typeof(Dir), dir);
        }

        /// <summary>元件种类解析：Wall / Emitter / Receiver / Splitter / NotGate（也认 not）/ Filter。</summary>
        public static bool TryParseKind(string text, out CellKind kind)
        {
            kind = CellKind.Wall;
            if (string.IsNullOrEmpty(text)) return false;

            string trimmed = text.Trim().Replace("_", string.Empty).Replace("-", string.Empty);
            if (trimmed.Equals("not", StringComparison.OrdinalIgnoreCase))
            {
                kind = CellKind.NotGate;
                return true;
            }

            return Enum.TryParse(trimmed, true, out kind) && Enum.IsDefined(typeof(CellKind), kind);
        }

        /// <summary>接收器面朝语义：any = 四面可收，in = 只认进料面。空串 = 没写，"?" = 看不懂。</summary>
        public static string ParseFace(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            switch (text.Trim().ToLowerInvariant())
            {
                case "any":
                case "all":
                case "四面":
                    return "any";

                case "in":
                case "inlet":
                case "进料面":
                    return "in";

                default:
                    return "?";
            }
        }

        /// <summary>把基准朝向按旋转档（0-3，顺时针 90°/档）换算成实际朝向。</summary>
        public static Dir Rotate(Dir baseDir, int rotationSteps)
        {
            int steps = ((rotationSteps % 4) + 4) % 4;
            Dir result = baseDir;
            for (int i = 0; i < steps; i++) result = result.RotateClockwise();
            return result;
        }

        static void Add(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }

        /// <summary>不用 <c>Math.Clamp</c>：老版本 Unity 的 .NET Standard 档案里没有它，手写一行更省心。</summary>
        static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
