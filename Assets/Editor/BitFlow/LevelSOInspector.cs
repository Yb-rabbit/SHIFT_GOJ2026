using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// <see cref="LevelSO"/> 的检视器：在字段上方画一张网格预览，让作者一眼看出关卡长什么样、
    /// 哪些格子是空的、哪些格子撞车了。
    ///
    /// 设计原则（重要）：这是一个**纯装饰层**，不接管任何数据。
    /// - 字段区仍然走 DrawDefaultInspector，序列化格式一个字节都没变；
    /// - 唯一的写操作是「点击格子把该格整体旋转 +90°」（Shift 点击 = -90°），等价于游戏里按 R；
    /// - 把本文件删掉，关卡资产照常工作，只是没有网格图了。
    /// </summary>
    [CustomEditor(typeof(LevelSO))]
    public sealed class LevelSOInspector : Editor
    {
        const float CellSize = 32f;
        const float CellGap = 2f;
        const float RowLabelWidth = 40f;
        const float GridPadding = 4f;

        static readonly Color GridBack = new Color(0.15f, 0.16f, 0.19f);
        static readonly Color CellEmpty = new Color(0.23f, 0.24f, 0.28f);
        static readonly Color ConflictColor = new Color(0.85f, 0.28f, 0.28f);
        static readonly Color MissingColor = new Color(0.55f, 0.55f, 0.20f);

        /// <summary>墙比空格亮一点但明显暗于任何元件：一眼能分出"这是障碍，不是可玩元件"。</summary>
        static readonly Color WallColor = new Color(0.42f, 0.44f, 0.50f);

        /// <summary>坐标 → 该格上的所有摆放项（同格叠加要能看见，所以不覆盖只追加）。</summary>
        readonly Dictionary<Vector2Int, List<ElementPlacement>> byCoord =
            new Dictionary<Vector2Int, List<ElementPlacement>>();

        /// <summary>坐标 → 墙层（wallMap）里有没有墙。墙不是 placement，所以单独记一份。</summary>
        readonly HashSet<Vector2Int> wallCells = new HashSet<Vector2Int>();

        GUIStyle cellStyle;
        Vector2Int hoverCell;
        bool hasHover;

        public override void OnInspectorGUI()
        {
            LevelSO level = target as LevelSO;
            if (level == null) return;

            RebuildLookup(level);

            DrawHeader(level);
            DrawOpenEditorButton(level);
            DrawGrid(level);
            DrawDiagnostics(level);

            EditorGUILayout.Space(8f);
            DrawDefaultInspector();
        }

        void RebuildLookup(LevelSO level)
        {
            byCoord.Clear();
            if (level.placements != null)
            {
                foreach (ElementPlacement placement in level.placements)
                {
                    if (placement == null) continue;
                    if (!byCoord.TryGetValue(placement.coord, out List<ElementPlacement> list))
                    {
                        list = new List<ElementPlacement>();
                        byCoord[placement.coord] = list;
                    }
                    list.Add(placement);
                }
            }

            // 墙层按整张网格扫（width×height 就那么点大），不走 ParseWalls：
            // ParseWalls 需要 wallType 才生效，而网格该显示的墙不该依赖类型资产有没有填
            wallCells.Clear();
            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2Int coord = new Vector2Int(x, y);
                    if (level.IsWallInMap(coord)) wallCells.Add(coord);
                }
            }
        }

        void DrawHeader(LevelSO level)
        {
            int placementCount = level.placements != null ? level.placements.Count : 0;
            int supplyCount = level.supplies != null ? level.supplies.Count : 0;
            int legacyWallCount = CountLegacyWalls(level);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                level.width + " × " + level.height + " 网格",
                EditorStyles.boldLabel, GUILayout.Width(90f));

            // 老关卡的墙还躺在 placements 里，所以从摆放数里扣掉，别让人以为有 18 个元件
            string summary = (placementCount - legacyWallCount) + " 个摆放";
            if (wallCells.Count > 0) summary += " + " + wallCells.Count + " 面墙";
            summary += " / " + supplyCount + " 张手牌";

            EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);

            if (supplyCount == 0)
            {
                EditorGUILayout.LabelField("（无手牌 = 演示关）", EditorStyles.miniLabel, GUILayout.Width(120f));
            }
            EditorGUILayout.EndHorizontal();

            if (legacyWallCount > 0) DrawMigrateButton(legacyWallCount);
        }

        /// <summary>
        /// 通往 Unity 内网格窗口的入口。
        ///
        /// 做关的主通道是浏览器版 LevelStudio（菜单 SHIFT GO/BitFlow/关卡编辑器），
        /// 这个窗口退成「内置 SO」的一条支路：只有已经在选某份 LevelSO、想就地改几格时才用得上，
        /// 所以做成右下角小按钮，不抢字段区的注意力。它只改 placements / supplies，不经过 JSON。
        /// </summary>
        void DrawOpenEditorButton(LevelSO level)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUIContent content = new GUIContent(
                "在内置网格里编辑",
                "打开 Unity 内网格窗口，直接改这份 LevelSO 的 placements / supplies（不经过 JSON）");

            if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(120f)))
            {
                LevelEditorWindow.OpenFor(level);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 墙还写在 placements 里的旧关卡，给一个一键搬家按钮。
        /// 迁移只会「换表示法」：墙本来就排在元件之后，所以 Elements 的数量和顺序都不变。
        /// </summary>
        void DrawMigrateButton(int legacyWallCount)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "还有 " + legacyWallCount + " 面墙写在 placements 里（每条 8 个字段）。\n" +
                "收进墙层之后，在网格上点空格就能画墙、点墙就能擦墙。",
                EditorStyles.miniLabel);

            if (GUILayout.Button("收进墙层", GUILayout.Width(72f), GUILayout.Height(30f)))
            {
                MigrateLegacyWalls((LevelSO)target);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ 网格

        void DrawGrid(LevelSO level)
        {
            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);

            float gridWidth = RowLabelWidth + width * (CellSize + CellGap);
            float gridHeight = height * (CellSize + CellGap) + GridPadding * 2f;

            Rect area = GUILayoutUtility.GetRect(gridWidth, gridHeight, GUILayout.ExpandWidth(false));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, GridBack);
            }

            hasHover = false;

            for (int row = 0; row < height; row++)
            {
                // 屏幕上方对应网格 y 的最大值：让 y 轴朝上，和世界坐标一致
                int y = height - 1 - row;
                float rowY = area.y + GridPadding + row * (CellSize + CellGap);

                Rect rowLabel = new Rect(area.x + 2f, rowY, RowLabelWidth - 6f, CellSize);
                GUI.Label(rowLabel, "y=" + y, EditorStyles.miniLabel);

                for (int x = 0; x < width; x++)
                {
                    Rect cell = new Rect(
                        area.x + RowLabelWidth + x * (CellSize + CellGap),
                        rowY, CellSize, CellSize);

                    DrawCell(level, cell, x, y);
                }
            }

            // 列号：画在最后一行下面
            float axisY = area.y + GridPadding + height * (CellSize + CellGap);
            for (int x = 0; x < width; x++)
            {
                Rect label = new Rect(
                    area.x + RowLabelWidth + x * (CellSize + CellGap), axisY - 1f, CellSize, 12f);
                GUI.Label(label, x.ToString(), EditorStyles.centeredGreyMiniLabel);
            }

            string action = "点空格 = 画墙／点墙 = 擦墙；点元件 = 旋转 +90°（Shift = -90°）";
            string hint = hasHover
                ? "光标：(" + hoverCell.x + ", " + hoverCell.y + ")　" + action
                : action;
            EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
        }

        void DrawCell(LevelSO level, Rect cell, int x, int y)
        {
            Vector2Int coord = new Vector2Int(x, y);
            byCoord.TryGetValue(coord, out List<ElementPlacement> list);

            bool occupied = list != null && list.Count > 0;
            bool conflict = list != null && list.Count > 1;
            bool missingType = occupied && list[0].type == null;

            // 墙有两个来源：新的 wallMap、以及还躺在 placements 里的旧数据
            bool wallInMap = wallCells.Contains(coord);
            bool legacyWall = occupied && AllWalls(list);
            bool isWall = wallInMap || legacyWall;

            Color back = CellEmpty;
            if (missingType) back = MissingColor;
            else if (conflict) back = ConflictColor;
            else if (isWall) back = WallColor;
            else if (occupied) back = ColorOf(list[0].type);

            bool isHover = cell.Contains(Event.current.mousePosition);
            if (isHover)
            {
                hoverCell = coord;
                hasHover = true;
                back = Color.Lerp(back, Color.white, 0.25f);
            }

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(cell, back);

                if (wallInMap && !occupied)
                {
                    GUI.Label(cell, "#", CellLabelStyle());
                }
                else if (occupied)
                {
                    string text;
                    if (missingType) text = "?";
                    else if (conflict) text = list.Count + "×";
                    else text = LabelOf(list[0]);

                    GUI.Label(cell, text, CellLabelStyle());
                }
            }

            if (Event.current.type != EventType.MouseDown || Event.current.button != 0) return;
            if (!cell.Contains(Event.current.mousePosition)) return;

            // 点墙 = 擦掉（不管它是墙层画的还是 placements 里的旧墙）
            if (isWall)
            {
                EraseWallAt(level, coord, list);
                Event.current.Use();
                return;
            }

            // 点空格 = 画墙。这是本次改造新增的写操作，和"旋转"一样挂了 Undo
            if (!occupied)
            {
                DrawWallAt(level, coord);
                Event.current.Use();
                return;
            }

            // 点元件 = 沿用老行为：旋转 +90°（Shift = -90°）
            RotatePlacement(level, list[0], Event.current.shift ? -1 : 1);
            Event.current.Use();
        }

        static string LabelOf(ElementPlacement placement)
        {
            string label = string.IsNullOrEmpty(placement.type.label) ? "?" : placement.type.label;
            return placement.rotationSteps == 0 ? label : label + "r" + placement.rotationSteps;
        }

        static bool AllWalls(List<ElementPlacement> list)
        {
            foreach (ElementPlacement placement in list)
            {
                if (placement == null || !(placement.type is WallTypeSO)) return false;
            }
            return true;
        }

        GUIStyle CellLabelStyle()
        {
            if (cellStyle != null) return cellStyle;

            cellStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            return cellStyle;
        }

        static void RotatePlacement(LevelSO level, ElementPlacement placement, int delta)
        {
            Undo.RecordObject(level, "Rotate Placement");

            placement.rotationSteps = ((placement.rotationSteps + delta) % 4 + 4) % 4;
            EditorUtility.SetDirty(level);

            // 立刻落盘：不然改完直接关 Unity，旋转就丢了
            AssetDatabase.SaveAssetIfDirty(level);
        }

        // ------------------------------------------------------------------ 墙层的写操作
        //
        // 网格点击是这个检视器仅有的两类写操作（旋转 / 画擦墙），全部走 Undo + 立刻落盘。
        // 墙层只是「文本表示法」的替换：写的是 wallMap 字符串，不是新资产、也不改序列化结构。

        static void DrawWallAt(LevelSO level, Vector2Int coord)
        {
            if (level.wallType == null)
            {
                EditorUtility.DisplayDialog(
                    "还没指定墙类型",
                    "请先在字段区的「墙层 / 墙类型资产」里选一个 WallTypeSO，再在网格上画墙。\n" +
                    "（项目里现成的是 Assets/Data/Elements/Wall/Type_Wall.asset）",
                    "好");
                return;
            }

            Undo.RecordObject(level, "Draw Wall");
            if (!level.SetWall(coord, true)) return;

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);
        }

        static void EraseWallAt(LevelSO level, Vector2Int coord, List<ElementPlacement> list)
        {
            Undo.RecordObject(level, "Erase Wall");

            bool changed = false;

            // 旧数据：墙在 placements 里，得真正删掉那一条
            if (list != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null || !(list[i].type is WallTypeSO)) continue;
                    level.placements.Remove(list[i]);
                    changed = true;
                }
            }

            if (level.SetWall(coord, false)) changed = true;
            if (!changed) return;

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);
        }

        /// <summary>placements 里有多少条是墙（旧表示法）。</summary>
        static int CountLegacyWalls(LevelSO level)
        {
            if (level.placements == null) return 0;

            int count = 0;
            foreach (ElementPlacement placement in level.placements)
            {
                if (placement != null && placement.type is WallTypeSO) count++;
            }
            return count;
        }

        /// <summary>
        /// 把 placements 里的墙转录成 wallMap 的 '#'，再从 placements 删掉。
        /// 纯换表示法：墙本来就排在元件之后，所以 Elements 的数量与顺序都不变。
        /// </summary>
        static void MigrateLegacyWalls(LevelSO level)
        {
            if (level.wallType == null)
            {
                EditorUtility.DisplayDialog(
                    "先指定墙类型",
                    "墙层里每一格都靠「墙类型资产」生成，请先在字段区指定 wallType。",
                    "好");
                return;
            }

            List<Vector2Int> walls = new List<Vector2Int>();
            if (level.placements != null)
            {
                foreach (ElementPlacement placement in level.placements)
                {
                    if (placement != null && placement.type is WallTypeSO) walls.Add(placement.coord);
                }
            }

            if (walls.Count == 0) return;

            // Undo 快照必须在任何修改之前记，否则 Ctrl+Z 恢复的是"已经删了一半"的状态
            Undo.RecordObject(level, "Migrate Walls To Map");

            level.placements.RemoveAll(placement => placement != null && placement.type is WallTypeSO);
            foreach (Vector2Int coord in walls) level.SetWall(coord, true);

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);

            Debug.Log("[BitFlow] 已把 " + walls.Count + " 面墙收进 " + level.name + " 的 wallMap");
        }

        // ------------------------------------------------------------------ 诊断

        void DrawDiagnostics(LevelSO level)
        {
            List<string> issues = new List<string>();

            foreach (KeyValuePair<Vector2Int, List<ElementPlacement>> pair in byCoord)
            {
                Vector2Int coord = pair.Key;

                if (coord.x < 0 || coord.x >= level.width || coord.y < 0 || coord.y >= level.height)
                {
                    issues.Add("(" + coord.x + ", " + coord.y + ") 越界：不在 "
                        + level.width + "×" + level.height + " 网格内");
                }

                if (pair.Value.Count > 1)
                {
                    issues.Add("(" + coord.x + ", " + coord.y + ") 撞车："
                        + pair.Value.Count + " 个元件叠在同一格");
                }

                foreach (ElementPlacement placement in pair.Value)
                {
                    if (placement.type == null)
                    {
                        issues.Add("(" + coord.x + ", " + coord.y + ") 缺少类型资产");
                    }
                }
            }

            // 同一格既有墙层又有 placement：解析时 placements 优先，这面墙会被静默忽略。
            // 静默出错是最难查的，所以这里主动喊一声。
            foreach (Vector2Int coord in wallCells)
            {
                if (byCoord.ContainsKey(coord))
                {
                    issues.Add("(" + coord.x + ", " + coord.y + ") 墙层和 placements 撞在同一格：placements 优先，这面墙会被忽略");
                }
            }

            if (issues.Count == 0) return;

            EditorGUILayout.HelpBox(
                "发现 " + issues.Count + " 个问题：\n" + string.Join("\n", issues),
                MessageType.Warning);
        }

        // ------------------------------------------------------------------ 配色

        /// <summary>按类型资产的具体子类上色（关卡编辑器窗口也用它，保证两边配色一致）。</summary>
        internal static Color ColorOf(ElementTypeSO type)
        {
            if (type is EmitterTypeSO) return new Color(0.22f, 0.52f, 0.82f);
            if (type is ReceiverTypeSO) return new Color(0.85f, 0.48f, 0.20f);
            if (type is SplitterTypeSO) return new Color(0.56f, 0.38f, 0.80f);
            if (type is NotGateTypeSO) return new Color(0.82f, 0.28f, 0.55f);
            if (type is FilterTypeSO) return new Color(0.24f, 0.68f, 0.50f);
            if (type is WallTypeSO) return new Color(0.34f, 0.36f, 0.41f);
            return new Color(0.50f, 0.50f, 0.55f);
        }
    }
}
