using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 把 JSON 里的一格解析成「类型资产 + 旋转档」。
    ///
    /// 为什么是接口：找资产要走 AssetDatabase（Editor 专属），而这一层是 Runtime ——
    /// 序列化核心放 Runtime 才能被 EditMode 测试直接驱动（测试里塞一个内存字典实现即可）。
    /// 编辑器侧的实现见 Assets/Editor/BitFlow/LevelJsonTool.cs。
    /// </summary>
    public interface ILevelJsonTypeResolver
    {
        /// <summary>
        /// <c>type</c>（精确，= 类型资产 Key）或 <c>kind</c>（宽松简写）二选一。
        /// 返回 null 表示这一格没法落地（调用方负责记错误）。
        /// </summary>
        ElementTypeSO Resolve(LevelJsonSerializer.LevelJsonCell cell, out int rotationSteps);

        /// <summary>wallRows 里的 # 要用的墙类型资产；返回 null 表示这一关带不了墙层。</summary>
        WallTypeSO ResolveWall();
    }

    /// <summary>读 JSON 的结果：解析出来的关卡 + 给人看的诊断。有 Errors 时 Level 里对应格子已被跳过。</summary>
    public sealed class LevelJsonReadResult
    {
        public LevelSO Level;

        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool HasLevel => Level != null;

        /// <summary>没有错误、且确实读出了一份关卡。</summary>
        public bool Ok => Level != null && Errors.Count == 0;
    }

    /// <summary>
    /// 关卡 JSON 的「可手写格式」：双向转换 <see cref="LevelSO"/> ↔ 文本。
    ///
    /// 和 <see cref="LevelDraftSerializer"/>（游戏内存档）的区别：那份是给游戏读的、字段全是下标数字；
    /// 这份是给人写的 —— 元件按 (y, x) 排序、方向写 Up/Right/Down/Left、墙用字符画一行一个 y。
    ///
    /// 两种写元件的姿势：
    /// - <c>type</c>：精确引用类型资产（= <see cref="ElementTypeSO.Key"/>，即资产名），导出永远带它，往返无损；
    /// - <c>kind</c>：只写 Emitter / Receiver / Splitter / NotGate / Filter + 绝对朝向，
    ///   由导入器去现有资产里找等价的一份，找不到才造变体 —— 外部作者不用认识 Unity 资产名。
    ///
    /// 语义要点（也写进了 docs/LEVEL_JSON.md）：
    /// - <c>sequence</c> / <c>count</c> / <c>face</c> 出现即视为「这一格覆盖类型资产」；
    /// - <c>count</c> 是发射总位数 / 接收总个数（和 LevelDraft 一致，不是资产上的「轮数」）；
    /// - <c>playerEditable</c> 缺省 false = 关卡预设件（玩家搬不动）；
    /// - <c>wallRows</c> 优先，没有才看 <c>wallMap</c>（兼容旧的手写格式）。
    /// </summary>
    public static class LevelJsonSerializer
    {
        public const int SchemaVersion = 1;

        /// <summary>JSON 顶层。所有字段都给了初值 —— JsonUtility 对缺失字段保留初值，所以初值就是"没写"。</summary>
        [Serializable]
        public sealed class LevelJsonFile
        {
            /// <summary>格式版本。比工具支持的更新时只报警告（未知字段会被忽略）。</summary>
            public int schema = SchemaVersion;

            public string displayName = string.Empty;

            public int width = 9;
            public int height = 8;
            public float tickInterval = 0.5f;

            /// <summary>墙类型资产 Key（留空 = 用目录里第一份墙类型）。</summary>
            public string wallType = string.Empty;

            /// <summary>墙层字符画：一行一个 y（从下往上，第一行是 y=0），'#' 是墙，其余留空。优先于 wallMap。</summary>
            public List<string> wallRows;

            /// <summary>兼容用：老式的整块墙图（\n 分行）。只在没有 wallRows 时生效。</summary>
            public string wallMap = string.Empty;

            public List<LevelJsonCell> elements;

            public List<LevelJsonSupply> supplies;
        }

        /// <summary>一格元件。数值字段用 -1 表示"没写"，字符串用空串表示"没写"。</summary>
        [Serializable]
        public sealed class LevelJsonCell
        {
            /// <summary>精确模式：类型资产 Key。</summary>
            public string type = string.Empty;

            /// <summary>宽松模式：Wall / Emitter / Receiver / Splitter / NotGate / Filter。</summary>
            public string kind = string.Empty;

            public int x;
            public int y;

            /// <summary>整体旋转 0~3（顺时针 90°/档）。<c>type</c> 模式靠它定位朝向。</summary>
            public int rot;

            /// <summary>绝对朝向 Up / Right / Down / Left（也认 ↑ → ↓ ← 和 上/右/下/左）。<c>kind</c> 模式靠它。</summary>
            public string dir = string.Empty;

            /// <summary>分流器的 0-臂（Only for Splitter）。</summary>
            public string dir2 = string.Empty;

            /// <summary>序列（发射器吐位节奏 / 接收器期望序列）。出现 = 覆盖类型资产。</summary>
            public string sequence = string.Empty;

            /// <summary>发射总位数 / 接收总个数。-1 = 没写 = 跟随类型资产。</summary>
            public int count = -1;

            /// <summary>接收器专用：any = 四面可收，in = 只认进料面。空串 = 跟随类型资产。</summary>
            public string face = string.Empty;

            /// <summary>过滤器放行的值（0/1）。-1 = 没写。</summary>
            public int pass = -1;

            /// <summary>允许玩家搬动 / 拾回这一格。缺省 false = 关卡预设件。</summary>
            public bool playerEditable;
        }

        /// <summary>一摞手牌。同一类型写多条 = 多给几张（与 LevelSO.CreatePlayerDraft 的累加语义一致）。</summary>
        [Serializable]
        public sealed class LevelJsonSupply
        {
            public string type = string.Empty;
            public string kind = string.Empty;

            /// <summary>本关给几张（含已放下的）。-1 = 没写 = 1 张。</summary>
            public int count = -1;
        }

        // ------------------------------------------------------------------ 导出

        /// <summary>
        /// 把关卡资产写成 JSON 文本。输出是幂等的：同一份资产导出多少次字节都相同
        /// （元件按 (y, x) 排序、浮点固定小数为 3 位），所以 git diff 只会出现真正的改动。
        /// </summary>
        public static string ToJson(LevelSO level)
        {
            if (level == null) return string.Empty;

            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);

            StringBuilder builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"schema\": ").Append(SchemaVersion).Append(",\n");
            builder.Append("  \"displayName\": ").Append(Quote(level.displayName)).Append(",\n");
            builder.Append("  \"width\": ").Append(width).Append(",\n");
            builder.Append("  \"height\": ").Append(height).Append(",\n");
            builder.Append("  \"tickInterval\": ").Append(Number(level.tickInterval));

            string[] wallRows = BuildWallRows(level, width, height);
            if (wallRows.Length > 0)
            {
                string wallKey = level.wallType != null ? level.wallType.Key : string.Empty;
                builder.Append(",\n  \"wallType\": ").Append(Quote(wallKey));
                builder.Append(",\n  \"wallRows\": [\n");
                for (int i = 0; i < wallRows.Length; i++)
                {
                    builder.Append("    ").Append(Quote(wallRows[i]));
                    if (i < wallRows.Length - 1) builder.Append(',');
                    builder.Append('\n');
                }
                builder.Append("  ]");
            }

            List<ElementPlacement> ordered = new List<ElementPlacement>();
            if (level.placements != null)
            {
                foreach (ElementPlacement placement in level.placements)
                {
                    if (placement != null && placement.type != null) ordered.Add(placement);
                }
            }
            ordered.Sort(ComparePlacements);

            if (ordered.Count > 0)
            {
                builder.Append(",\n  \"elements\": [\n");
                for (int i = 0; i < ordered.Count; i++)
                {
                    builder.Append("    ").Append(PlacementJson(ordered[i]));
                    if (i < ordered.Count - 1) builder.Append(',');
                    builder.Append('\n');
                }
                builder.Append("  ]");
            }

            if (level.supplies != null && level.supplies.Count > 0)
            {
                List<string> stacks = new List<string>();
                foreach (ElementSupply supply in level.supplies)
                {
                    if (supply == null || supply.type == null) continue;
                    stacks.Add("{ \"type\": " + Quote(supply.type.Key)
                        + ", \"kind\": " + Quote(KindOf(supply.type).ToString())
                        + ", \"count\": " + Mathf.Max(0, supply.count) + " }");
                }

                if (stacks.Count > 0)
                {
                    builder.Append(",\n  \"supplies\": [\n");
                    for (int i = 0; i < stacks.Count; i++)
                    {
                        builder.Append("    ").Append(stacks[i]);
                        if (i < stacks.Count - 1) builder.Append(',');
                        builder.Append('\n');
                    }
                    builder.Append("  ]");
                }
            }

            builder.Append("\n}\n");
            return builder.ToString();
        }

        /// <summary>一格摆放 → 一行 JSON。只写「确实需要写」的字段，人读起来才不会全是噪声。</summary>
        static string PlacementJson(ElementPlacement placement)
        {
            ElementTypeSO type = placement.type;
            CellKind kind = KindOf(type);

            StringBuilder builder = new StringBuilder();
            builder.Append("{ \"type\": ").Append(Quote(type.Key));
            builder.Append(", \"kind\": ").Append(Quote(kind.ToString()));
            builder.Append(", \"x\": ").Append(placement.coord.x);
            builder.Append(", \"y\": ").Append(placement.coord.y);

            int steps = ((placement.rotationSteps % 4) + 4) % 4;
            builder.Append(", \"rot\": ").Append(steps);

            Dir primary;
            Dir secondary;
            ReadDirs(placement, out primary, out secondary);
            builder.Append(", \"dir\": ").Append(Quote(primary.ToString()));
            if (kind == CellKind.Splitter) builder.Append(", \"dir2\": ").Append(Quote(secondary.ToString()));

            // sequence / count / face 只在「这一格确实覆盖了资产」时才写出来 ——
            // 导出 → 导入 → 再导出才能字节一致
            if (placement.overrideSequence)
            {
                string sequence = string.IsNullOrEmpty(placement.sequence) ? "1" : placement.sequence;
                builder.Append(", \"sequence\": ").Append(Quote(sequence));
            }

            if (placement.overrideCount) builder.Append(", \"count\": ").Append(Mathf.Max(0, placement.count));
            if (placement.overrideFace) builder.Append(", \"face\": ").Append(Quote(placement.acceptAnyFace ? "any" : "in"));
            if (placement.overridePassValue) builder.Append(", \"pass\": ").Append(placement.passValue == 0 ? 0 : 1);
            if (placement.playerEditable) builder.Append(", \"playerEditable\": true");

            builder.Append(" }");
            return builder.ToString();
        }

        /// <summary>
        /// wallMap → 一行一个 y 的字符画（第一行是 y=0，从下往上，和 wallMap 的行序一致）。
        /// 整块铺满 width × height 且只保留 '#'，所以导出多少次都是同一份文本；
        /// 一面墙都没有时返回空数组（等于这一关不带墙层）。
        /// </summary>
        static string[] BuildWallRows(LevelSO level, int width, int height)
        {
            if (level.wallType == null || string.IsNullOrEmpty(level.wallMap)) return Array.Empty<string>();

            string[] lines = level.wallMap.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            char[,] grid = new char[height, width];
            bool anyWall = false;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) grid[y, x] = '.';
            }

            for (int y = 0; y < lines.Length && y < height; y++)
            {
                string line = lines[y];
                for (int x = 0; x < line.Length && x < width; x++)
                {
                    if (line[x] != '#') continue;
                    grid[y, x] = '#';
                    anyWall = true;
                }
            }

            if (!anyWall) return Array.Empty<string>();

            string[] rows = new string[height];
            char[] buffer = new char[width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) buffer[x] = grid[y, x];
                rows[y] = new string(buffer);
            }
            return rows;
        }

        // ------------------------------------------------------------------ 小工具

        /// <summary>类型资产 → 元件种类（建一份 0 号定义再读 Kind，够用，不用在资产上加字段）。</summary>
        internal static CellKind KindOf(ElementTypeSO type)
        {
            if (type == null) return CellKind.Wall;
            ElementDefinition definition = type.CreateDefinition(new GridPoint(0, 0), 0, 0);
            return definition != null ? definition.Kind : CellKind.Wall;
        }

        /// <summary>摆放落成之后的实际朝向（类型资产基准朝向 × 旋转档）。</summary>
        internal static void ReadDirs(ElementPlacement placement, out Dir primary, out Dir secondary)
        {
            primary = Dir.Right;
            secondary = Dir.Down;
            if (placement == null || placement.type == null) return;

            ElementDefinition definition = placement.type.CreateDefinition(
                new GridPoint(placement.coord.x, placement.coord.y), placement.rotationSteps, 0);
            if (definition == null) return;

            switch (definition)
            {
                case EmitterDefinition emitter:
                    primary = emitter.Facing;
                    break;

                case ReceiverDefinition receiver:
                    primary = receiver.FeedFace;
                    break;

                case SplitterDefinition splitter:
                    primary = splitter.OneArm;
                    secondary = splitter.ZeroArm;
                    break;

                case NotGateDefinition notGate:
                    primary = notGate.OutputDir;
                    break;

                case FilterDefinition filter:
                    primary = filter.OutputDir;
                    break;
            }
        }

        static int ComparePlacements(ElementPlacement left, ElementPlacement right)
        {
            if (left.coord.y != right.coord.y) return left.coord.y.CompareTo(right.coord.y);
            return left.coord.x.CompareTo(right.coord.x);
        }

        static string Number(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";

            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(c); break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        // ------------------------------------------------------------------ 导入

        /// <summary>JSON 文本 → DTO。语法错误只记进 errors 并返回 null，绝不抛出去。</summary>
        public static LevelJsonFile FromJson(string json, List<string> errors)
        {
            if (errors == null) errors = new List<string>();

            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
            {
                errors.Add("JSON 是空的");
                return null;
            }

            try
            {
                LevelJsonFile file = JsonUtility.FromJson<LevelJsonFile>(json);
                if (file == null)
                {
                    errors.Add("JSON 顶层必须是一个 { } 对象");
                    return null;
                }
                return file;
            }
            catch (Exception exception)
            {
                errors.Add("JSON 语法错误：" + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// 朝向解析：Up/Right/Down/Left（不分大小写），也认 ↑ → ↓ ← 和 上/右/下/左 ——
        /// 外部作者用哪种写法都不用回来看文档。
        /// </summary>
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

        /// <summary>接收器面朝语义：any = 四面可收，in = 只认进料面。空串 = 没写。</summary>
        internal static string ParseFace(string text)
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

        /// <summary>
        /// JSON 文本 → 关卡资产（内存里的 ScriptableObject，不落盘）。
        /// 单格出错只影响这一格：记进 Errors 并跳过，其余照常读出来，外部作者一次能看到所有问题。
        /// </summary>
        public static LevelJsonReadResult Read(string json, ILevelJsonTypeResolver resolver)
        {
            LevelJsonReadResult result = new LevelJsonReadResult();

            LevelJsonFile file = FromJson(json, result.Errors);
            if (file == null) return result;

            if (resolver == null)
            {
                result.Errors.Add("没有可用的类型解析器（找不到元件类型资产）");
                return result;
            }

            if (file.schema > SchemaVersion)
            {
                result.Warnings.Add("JSON 的 schema 是 " + file.schema + "，比本工具支持的 " + SchemaVersion
                    + " 新：不认识的字段会被忽略");
            }

            LevelSO level = ScriptableObject.CreateInstance<LevelSO>();
            level.displayName = string.IsNullOrEmpty(file.displayName) ? "CUSTOM LEVEL" : file.displayName;
            level.width = Mathf.Max(1, file.width);
            level.height = Mathf.Max(1, file.height);
            level.tickInterval = file.tickInterval > 0.01f ? Mathf.Clamp(file.tickInterval, 0.05f, 5f) : 0.35f;
            level.placements = new List<ElementPlacement>();
            level.supplies = new List<ElementSupply>();

            string wallMap = BuildWallMap(file, level.height, result.Warnings);
            if (!string.IsNullOrEmpty(wallMap))
            {
                WallTypeSO wallType = resolver.ResolveWall();
                if (wallType == null)
                {
                    result.Warnings.Add("这一关有墙，但找不到墙类型资产 → 墙会被忽略");
                }
                else
                {
                    level.wallType = wallType;
                    level.wallMap = wallMap;
                }
            }

            ReadElements(file, level, resolver, result);
            ReadSupplies(file, level, resolver, result);
            WarnWallOverlap(level, result);

            // 统一按 (y, x) 排序：同一份 JSON 无论行序怎么排，落出来的资产都一样，导出也是幂等的
            level.placements.Sort((left, right) =>
            {
                if (left.coord.y != right.coord.y) return left.coord.y.CompareTo(right.coord.y);
                return left.coord.x.CompareTo(right.coord.x);
            });

            result.Level = level;
            return result;
        }

        static void ReadElements(LevelJsonFile file, LevelSO level, ILevelJsonTypeResolver resolver, LevelJsonReadResult result)
        {
            if (file.elements == null) return;

            HashSet<Vector2Int> used = new HashSet<Vector2Int>();
            for (int i = 0; i < file.elements.Count; i++)
            {
                LevelJsonCell cell = file.elements[i];
                if (cell == null) continue;

                string where = "elements[" + i + "]";
                Vector2Int coord = new Vector2Int(cell.x, cell.y);

                if (coord.x < 0 || coord.y < 0 || coord.x >= level.width || coord.y >= level.height)
                {
                    result.Warnings.Add(where + " 坐标 " + Describe(coord) + " 越界（本关 "
                        + level.width + "×" + level.height + "），已跳过");
                    continue;
                }

                if (!used.Add(coord))
                {
                    result.Warnings.Add(where + " 坐标 " + Describe(coord) + " 与前面的格子重复，已跳过");
                    continue;
                }

                int steps;
                ElementTypeSO type = resolver.Resolve(cell, out steps);
                if (type == null)
                {
                    used.Remove(coord);
                    result.Errors.Add(where + " 解析不出元件类型：" + DescribeCell(cell));
                    continue;
                }

                ElementPlacement placement = new ElementPlacement
                {
                    type = type,
                    coord = coord,
                    rotationSteps = ((steps % 4) + 4) % 4,
                    playerEditable = cell.playerEditable,
                };

                if (!string.IsNullOrEmpty(cell.sequence))
                {
                    placement.overrideSequence = true;
                    placement.sequence = cell.sequence.Trim();
                }

                if (cell.count >= 0)
                {
                    placement.overrideCount = true;
                    placement.count = Mathf.Max(0, cell.count);
                }

                string face = ParseFace(cell.face);
                if (face == "?")
                {
                    result.Warnings.Add(where + " face \"" + cell.face + "\" 看不懂（只认 any / in），按跟随资产处理");
                }
                else if (face.Length > 0)
                {
                    placement.overrideFace = true;
                    placement.acceptAnyFace = face != "in";
                }

                if (cell.pass >= 0)
                {
                    if (string.IsNullOrEmpty(cell.type))
                    {
                        // 宽松模式（kind）才能改放行值：存在摆放上，不依赖「专门为它造一份类型资产」
                        placement.overridePassValue = true;
                        placement.passValue = (byte)(cell.pass == 0 ? 0 : 1);
                    }
                    else
                    {
                        result.Warnings.Add(where + " 写了 pass，但用 type 精确引用时改不了过滤器放行值；"
                            + "改用 kind=Filter 才能覆盖");
                    }
                }

                level.placements.Add(placement);
            }
        }

        static void ReadSupplies(LevelJsonFile file, LevelSO level, ILevelJsonTypeResolver resolver, LevelJsonReadResult result)
        {
            if (file.supplies == null) return;

            for (int i = 0; i < file.supplies.Count; i++)
            {
                LevelJsonSupply supply = file.supplies[i];
                if (supply == null) continue;

                string where = "supplies[" + i + "]";
                LevelJsonCell probe = new LevelJsonCell { type = supply.type, kind = supply.kind };

                int steps;
                ElementTypeSO type = resolver.Resolve(probe, out steps);
                if (type == null)
                {
                    result.Errors.Add(where + " 解析不出元件类型：" + DescribeCell(probe));
                    continue;
                }

                int count = supply.count >= 0 ? supply.count : 1;
                if (count == 0)
                {
                    result.Warnings.Add(where + " count=0 等于不给这张牌，已跳过");
                    continue;
                }

                level.supplies.Add(new ElementSupply { type = type, count = count });
            }
        }

        /// <summary>墙层和元件叠在同一格时，墙会被盖掉 —— 这种图画错很难自己发现，直接说出来。</summary>
        static void WarnWallOverlap(LevelSO level, LevelJsonReadResult result)
        {
            if (string.IsNullOrEmpty(level.wallMap)) return;

            HashSet<Vector2Int> placed = new HashSet<Vector2Int>();
            foreach (ElementPlacement placement in level.placements) placed.Add(placement.coord);

            int overlapped = 0;
            foreach (Vector2Int coord in level.ParseWalls())
            {
                if (placed.Contains(coord)) overlapped++;
            }

            if (overlapped > 0)
            {
                result.Warnings.Add("墙层和 elements 有 " + overlapped + " 个格子重叠，那些格子的墙会被元件盖掉");
            }
        }

        static string BuildWallMap(LevelJsonFile file, int height, List<string> warnings)
        {
            if (file.wallRows != null && file.wallRows.Count > 0)
            {
                if (file.wallRows.Count > height)
                {
                    warnings.Add("wallRows 有 " + file.wallRows.Count + " 行，超过 height=" + height + "，多出来的行会被忽略");
                }

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < file.wallRows.Count; i++)
                {
                    if (i > 0) builder.Append('\n');
                    builder.Append(file.wallRows[i] ?? string.Empty);
                }
                return builder.ToString();
            }

            return string.IsNullOrEmpty(file.wallMap) ? string.Empty : file.wallMap;
        }

        static string Describe(Vector2Int coord)
        {
            return "(" + coord.x + "," + coord.y + ")";
        }

        static string DescribeCell(LevelJsonCell cell)
        {
            if (cell == null) return "(空)";

            string type = string.IsNullOrEmpty(cell.type) ? "-" : cell.type;
            string kind = string.IsNullOrEmpty(cell.kind) ? "-" : cell.kind;
            return "kind=" + kind + " type=" + type + " 坐标=" + Describe(new Vector2Int(cell.x, cell.y));
        }
    }
}
