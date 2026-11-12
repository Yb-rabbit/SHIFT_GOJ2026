using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 把游戏内 BUILD MODE 存下来的 <c>custom_level.json</c> 转成正式关卡资产 <see cref="LevelSO"/>。
    ///
    /// 为什么需要这个工具：游戏里编辑走的是 <see cref="LevelDraft"/>（内存草稿 + JSON 存档），
    /// 而正式关卡是 Assets 里的 <see cref="LevelSO"/>，两者之间本来没有桥 —— 在游戏里搭好的关，
    /// 只能照着截图回来手抄坐标。这个工具就是那座桥。
    ///
    /// 转换的核心难点：JSON 里的每一格存的是**展开后的参数**（绝对朝向 / 序列 / 数量 / 放行值），
    /// 而 LevelSO 只存「类型资产引用 + 旋转档」，参数是资产自己带的。所以：
    /// 1. 先在现有类型资产里找「旋转 0~3 档能复现这一格参数」的那一份；
    /// 2. 找不到（例如 BUILD MODE 调色板摆出来的发射器一律是「序列 1 × 1」，和目录里的 E1/E2 都不同），
    ///    就按这一格的参数生成一份等价变体类型资产，保证导入无损；
    /// 3. 参数完全相同的格子共用同一份变体，不会一格一个资产。
    ///
    /// 设计原则与 <see cref="LevelSOInspector"/> 一致：只新增资产，绝不改写已有的关卡 / 类型资产。
    /// </summary>
    public static class LevelImportTool
    {
        const string GeneratedFolder = "Assets/Data/Elements/Generated";
        const string LevelsFolder = "Assets/Data/Levels";
        const string CatalogPath = "Assets/Data/Levels/LevelCatalog.asset";

        [MenuItem("SHIFT GO/BitFlow/导入 custom_level.json 为关卡资产", false, 20)]
        public static void ImportFromJson()
        {
            string startFolder = Path.GetDirectoryName(LevelDraftSerializer.DefaultPath);
            string path = EditorUtility.OpenFilePanel("选择游戏内保存的关卡 JSON", startFolder, "json");
            if (string.IsNullOrEmpty(path)) return;

            LevelDraft draft = LevelDraftSerializer.Load(path);
            if (draft == null)
            {
                EditorUtility.DisplayDialog(
                    "导入失败",
                    "读不出这个文件，可能不是 BUILD MODE 存的关卡：\n" + path,
                    "好");
                return;
            }

            ImportDraft(draft, Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>把一份草稿直接落成关卡资产（菜单与将来的批处理都走这里）。</summary>
        public static void ImportDraft(LevelDraft draft, string sourceName)
        {
            if (draft == null) return;

            List<ElementTypeSO> pool = LoadTypePool();
            Dictionary<string, ElementTypeSO> variants = new Dictionary<string, ElementTypeSO>();

            LevelSO level = ScriptableObject.CreateInstance<LevelSO>();
            level.displayName = string.IsNullOrEmpty(draft.DisplayName) ? "CUSTOM LEVEL" : draft.DisplayName;
            level.width = Mathf.Max(1, draft.Width);
            level.height = Mathf.Max(1, draft.Height);
            level.tickInterval = Mathf.Max(0.05f, draft.TickInterval);
            level.placements = new List<ElementPlacement>();
            level.supplies = new List<ElementSupply>();

            // 输出顺序固定成 (y, x)：同一份 JSON 导入多少次，资产里 placements 的顺序都一模一样，
            // diff 才好读（LevelSO.ToDefinition 的 Id 分配也照这个顺序，UI 读数不会漂）。
            List<EditableElement> cells = new List<EditableElement>(draft.Cells);
            cells.Sort(CompareByCoord);

            int reused = 0;
            int created = 0;
            List<string> skipped = new List<string>();

            foreach (EditableElement cell in cells)
            {
                Resolution resolution = ResolveType(cell, pool, variants);
                if (resolution.Type == null)
                {
                    skipped.Add(cell.Coord + " " + cell.Kind);
                    continue;
                }

                if (resolution.Created) created++;
                else reused++;

                level.placements.Add(new ElementPlacement
                {
                    type = resolution.Type,
                    coord = new Vector2Int(cell.Coord.X, cell.Coord.Y),
                    rotationSteps = resolution.Steps,
                    // JSON 的 locked = 关卡预设件（玩家搬不动）→ LevelSO 里是 playerEditable 取反
                    playerEditable = !draft.IsLocked(cell.Coord),
                });
            }

            BuildSupplies(draft, level);

            string assetPath = NextLevelPath();
            AssetDatabase.CreateAsset(level, assetPath);
            EditorUtility.SetDirty(level);

            int catalogIndex = AppendToCatalog(level);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = level;
            EditorGUIUtility.PingObject(level);

            string summary = "已生成 " + assetPath + "\n"
                + level.placements.Count + " 个元件（复用类型 " + reused + " / 新建变体 " + created + "）\n"
                + level.supplies.Count + " 张手牌"
                + (catalogIndex >= 0
                    ? "\n已追加到 LevelCatalog 第 " + catalogIndex + " 项（索引 0 是开局第一关）"
                    : "\n没找到 " + CatalogPath + "，需要手动把关卡拖进关卡表");

            if (skipped.Count > 0)
                summary += "\n\n有 " + skipped.Count + " 格跳过：\n" + string.Join("\n", skipped);

            Debug.Log("[BitFlow] 关卡导入完成：" + sourceName + "\n" + summary);
            EditorUtility.DisplayDialog("导入完成", summary, "好");
        }

        // ------------------------------------------------------------------ 类型解析

        internal readonly struct Resolution
        {
            public readonly ElementTypeSO Type;
            public readonly int Steps;
            public readonly bool Created;

            public Resolution(ElementTypeSO type, int steps, bool created)
            {
                Type = type;
                Steps = steps;
                Created = created;
            }
        }

        /// <summary>
        /// 给一格找「类型资产 + 旋转档」。优先复用现有资产（含之前导入生成的变体），
        /// 实在没有等价的一份才新建变体 —— 所以第二次导入同一关时 created 会掉到 0。
        /// </summary>
        /// <param name="saveAsset">
        /// false = 允许造变体但不落盘（校验 JSON 时用：能跑通模拟器，又不该在工程里留下垃圾资产）。
        /// </param>
        internal static Resolution ResolveType(
            EditableElement cell,
            List<ElementTypeSO> pool,
            Dictionary<string, ElementTypeSO> variants,
            bool saveAsset = true)
        {
            // 1) 先试 JSON 自带的 typeKey：从正式关卡进 BUILD MODE 改过的关都带着它，基本一击命中
            if (!string.IsNullOrEmpty(cell.TypeKey))
            {
                ElementTypeSO keyed = FindByKey(pool, cell.TypeKey);
                if (keyed != null)
                {
                    int steps = FindRotationSteps(keyed, cell);
                    if (steps >= 0) return new Resolution(keyed, steps, false);
                }
            }

            // 2) 再按 Kind 全池搜：同 Kind 的其它资产可能基准朝向不同，但转一下正好对上
            foreach (ElementTypeSO candidate in pool)
            {
                if (KindOf(candidate) != cell.Kind) continue;

                int steps = FindRotationSteps(candidate, cell);
                if (steps >= 0) return new Resolution(candidate, steps, false);
            }

            // 3) 都没有等价资产 → 造一份参数完全一致的变体（同参数共用一个）
            string signature = Signature(cell);
            if (variants.TryGetValue(signature, out ElementTypeSO cached) && cached != null)
            {
                return new Resolution(cached, 0, false);
            }

            ElementTypeSO variant = CreateVariant(cell, variants.Count + 1, saveAsset);
            if (variant == null) return default;

            variants[signature] = variant;
            pool.Add(variant);
            return new Resolution(variant, 0, true);
        }

        /// <summary>枚举 0~3 档，返回能让这个类型资产复现该格参数的那一档；没有则 -1。</summary>
        static int FindRotationSteps(ElementTypeSO type, EditableElement cell)
        {
            for (int steps = 0; steps < 4; steps++)
            {
                ElementDefinition definition = type.CreateDefinition(cell.Coord, steps, 0);
                if (definition == null || definition.Kind != cell.Kind) continue;

                if (SameConfig(EditableElement.FromDefinition(definition), cell)) return steps;
            }
            return -1;
        }

        /// <summary>
        /// 比较「类型资产展开后的配置」和「JSON 里那一格」是不是同一件事。
        /// 只比语义字段：墙没有可比参数，分流器比两臂，发射器/接收器比序列与数量，过滤器比放行值。
        /// </summary>
        static bool SameConfig(EditableElement expected, EditableElement actual)
        {
            if (expected.Kind != actual.Kind) return false;
            if (expected.Kind == CellKind.Wall) return true;
            if (expected.PrimaryDir != actual.PrimaryDir) return false;

            switch (expected.Kind)
            {
                case CellKind.Splitter:
                    return expected.SecondaryDir == actual.SecondaryDir;

                case CellKind.Emitter:
                case CellKind.Receiver:
                    return NormalizeSequence(expected.Sequence) == NormalizeSequence(actual.Sequence)
                        && expected.Count == actual.Count;

                case CellKind.Filter:
                    return (expected.PassValue == 0 ? 0 : 1) == (actual.PassValue == 0 ? 0 : 1);

                case CellKind.DelayGate:
                    // 延迟器没有方向（PrimaryDir 对它是无意义的显示值），只比延迟数
                    return Mathf.Clamp(expected.DelayTicks, 1, 4) == Mathf.Clamp(actual.DelayTicks, 1, 4);

                default:
                    return true;
            }
        }

        internal static ElementTypeSO FindByKey(List<ElementTypeSO> pool, string typeKey)
        {
            foreach (ElementTypeSO candidate in pool)
            {
                if (candidate != null && candidate.Key == typeKey) return candidate;
            }
            return null;
        }

        /// <summary>
        /// 按一格的具体参数生成等价类型资产（写在 Assets/Data/Elements/Generated）。
        /// <paramref name="saveAsset"/> = false 时只在内存里造一份（校验 JSON 用，不留垃圾资产）。
        /// </summary>
        internal static ElementTypeSO CreateVariant(EditableElement cell, int serial, bool saveAsset = true)
        {
            if (saveAsset) EnsureFolder(GeneratedFolder);

            int suffix = Mathf.Max(1, serial);
            string path = GeneratedFolder + "/Type_" + cell.Kind + "_Auto" + suffix + ".asset";
            if (saveAsset)
            {
                while (File.Exists(path))
                {
                    suffix++;
                    path = GeneratedFolder + "/Type_" + cell.Kind + "_Auto" + suffix + ".asset";
                }
            }

            ElementTypeSO variant;
            switch (cell.Kind)
            {
                case CellKind.Emitter:
                    EmitterTypeSO emitter = ScriptableObject.CreateInstance<EmitterTypeSO>();
                    emitter.facing = cell.PrimaryDir;
                    // 变体资产只能表达「序列 × 轮数」，而 JSON 给的是「序列 + 总位数」。
                    // 先把总位数展开成一条显式位流再反写，repeatCount 固定 1，
                    // 模拟器"末字符先弹"正好还原 JSON 要求的发射顺序（含 6 位这种非整除长度）。
                    emitter.sequence = ExpandToSequence(NormalizeSequence(cell.Sequence), Mathf.Max(0, cell.Count));
                    emitter.repeatCount = 1;
                    variant = emitter;
                    break;

                case CellKind.Receiver:
                    ReceiverTypeSO receiver = ScriptableObject.CreateInstance<ReceiverTypeSO>();
                    receiver.feedFace = cell.PrimaryDir;
                    receiver.targetSequence = NormalizeSequence(cell.Sequence);
                    receiver.targetCount = Mathf.Max(1, cell.Count);
                    variant = receiver;
                    break;

                case CellKind.Splitter:
                    SplitterTypeSO splitter = ScriptableObject.CreateInstance<SplitterTypeSO>();
                    splitter.oneArm = cell.PrimaryDir;
                    splitter.zeroArm = cell.SecondaryDir;
                    variant = splitter;
                    break;

                case CellKind.NotGate:
                    NotGateTypeSO notGate = ScriptableObject.CreateInstance<NotGateTypeSO>();
                    notGate.outputDir = cell.PrimaryDir;
                    variant = notGate;
                    break;

                case CellKind.Filter:
                    FilterTypeSO filter = ScriptableObject.CreateInstance<FilterTypeSO>();
                    filter.outputDir = cell.PrimaryDir;
                    filter.passValue = cell.PassValue == 0 ? (byte)0 : (byte)1;
                    variant = filter;
                    break;

                case CellKind.DelayGate:
                    DelayGateTypeSO delayGate = ScriptableObject.CreateInstance<DelayGateTypeSO>();
                    delayGate.delayTicks = Mathf.Clamp(cell.DelayTicks, 1, 4);
                    variant = delayGate;
                    break;

                default:
                    variant = ScriptableObject.CreateInstance<WallTypeSO>();
                    break;
            }

            variant.label = LabelPrefix(cell.Kind) + suffix;
            variant.name = "Type_" + cell.Kind + "_Auto" + suffix; // 不落盘时也要有 Key（Key 默认取资产名）

            if (saveAsset)
            {
                AssetDatabase.CreateAsset(variant, path);
                EditorUtility.SetDirty(variant);
            }
            return variant;
        }

        static string LabelPrefix(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Emitter: return "E";
                case CellKind.Receiver: return "R";
                case CellKind.Splitter: return "L";
                case CellKind.NotGate: return "N";
                case CellKind.Filter: return "F";
                case CellKind.DelayGate: return "D";
                default: return "W";
            }
        }

        /// <summary>参数指纹：同参数的格子共用一份变体资产。</summary>
        static string Signature(EditableElement cell)
        {
            return cell.Kind + "|" + (int)cell.PrimaryDir + "|" + (int)cell.SecondaryDir + "|"
                + NormalizeSequence(cell.Sequence) + "|" + cell.Count + "|" + (cell.PassValue == 0 ? 0 : 1)
                + "|" + Mathf.Clamp(cell.DelayTicks, 1, 4);
        }

        static string NormalizeSequence(string sequence)
        {
            if (string.IsNullOrEmpty(sequence)) return "1";

            for (int i = 0; i < sequence.Length; i++)
            {
                if (sequence[i] == '0' || sequence[i] == '1') return sequence;
            }
            return "1";
        }

        /// <summary>
        /// 把「循环单元 + 总位数」展开成一条显式序列，用于写进变体资产（repeatCount 固定 1 时
        /// 模拟器"末字符先弹"正好还原出原本要求的发射顺序）。
        /// 例：sequence="1011"、count=6 → 顺序 1,1,0,1,1,1 → 返回 "111011"。
        /// </summary>
        static string ExpandToSequence(string sequence, int count)
        {
            if (count <= 0 || string.IsNullOrEmpty(sequence)) return "1";

            List<byte> cycle = new List<byte>();
            for (int i = sequence.Length - 1; i >= 0; i--)
            {
                if (sequence[i] == '1') cycle.Add(1);
                else if (sequence[i] == '0') cycle.Add(0);
            }
            if (cycle.Count == 0) return "1";

            char[] chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                chars[count - 1 - i] = cycle[i % cycle.Count] == 1 ? '1' : '0';
            }
            return new string(chars);
        }

        internal static CellKind KindOf(ElementTypeSO type)
        {
            if (type == null) return CellKind.Wall;

            ElementDefinition definition = type.CreateDefinition(new GridPoint(0, 0), 0, 0);
            return definition != null ? definition.Kind : CellKind.Wall;
        }

        internal static List<ElementTypeSO> LoadTypePool()
        {
            List<ElementTypeSO> pool = new List<ElementTypeSO>();

            string[] guids = AssetDatabase.FindAssets("t:ElementTypeSO");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ElementTypeSO type = AssetDatabase.LoadAssetAtPath<ElementTypeSO>(path);
                if (type != null) pool.Add(type);
            }
            return pool;
        }

        // ------------------------------------------------------------------ 手牌 / 落盘

        /// <summary>
        /// JSON 里的 stock 是「剩余张数」，LevelSO 的 supply.count 是「本局总共给你几张」。
        /// 所以要把已经摆在场上的同类型元件补回去，否则读回来的有限元件关会少几张牌。
        /// </summary>
        static void BuildSupplies(LevelDraft draft, LevelSO level)
        {
            if (!draft.StockEnabled) return; // 作者模式搭出来的关没有手牌 → 导入成演示关

            List<ElementTypeSO> pool = LoadTypePool();

            foreach (KeyValuePair<string, int> pair in draft.Stock)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;

                ElementTypeSO type = null;
                int placed = 0;

                foreach (ElementPlacement placement in level.placements)
                {
                    if (placement.type == null || placement.type.Key != pair.Key) continue;
                    type = placement.type;
                    placed++;
                }

                if (type == null) type = FindByKey(pool, pair.Key);
                if (type == null) continue; // 手牌对应的类型资产被删了，跳过

                level.supplies.Add(new ElementSupply
                {
                    type = type,
                    count = Mathf.Max(0, pair.Value) + placed,
                });
            }
        }

        /// <summary>找 Levels 目录里第一个没被占用的 Level_NN.asset 路径。</summary>
        internal static string NextLevelPath()
        {
            EnsureFolder(LevelsFolder);

            for (int index = 1; index < 1000; index++)
            {
                string candidate = LevelsFolder + "/Level_" + index.ToString("00") + ".asset";
                if (!File.Exists(candidate)) return candidate;
            }
            return LevelsFolder + "/Level_999.asset";
        }

        /// <summary>把关卡追加到关卡表末尾，返回它的索引（索引 0 就是开局第一关）；没有关卡表返回 -1。</summary>
        internal static int AppendToCatalog(LevelSO level)
        {
            LevelCatalogSO catalog = AssetDatabase.LoadAssetAtPath<LevelCatalogSO>(CatalogPath);
            if (catalog == null) return -1;

            if (catalog.levels == null) catalog.levels = new List<LevelSO>();
            if (catalog.levels.Contains(level)) return catalog.levels.IndexOf(level);

            catalog.levels.Add(level);
            EditorUtility.SetDirty(catalog);
            return catalog.levels.Count - 1;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent)) return;

            parent = parent.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static int CompareByCoord(EditableElement left, EditableElement right)
        {
            if (left.Coord.Y != right.Coord.Y) return left.Coord.Y.CompareTo(right.Coord.Y);
            return left.Coord.X.CompareTo(right.Coord.X);
        }
    }
}
