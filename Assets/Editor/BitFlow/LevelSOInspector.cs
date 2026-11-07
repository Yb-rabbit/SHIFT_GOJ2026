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

        /// <summary>坐标 → 该格上的所有摆放项（同格叠加要能看见，所以不覆盖只追加）。</summary>
        readonly Dictionary<Vector2Int, List<ElementPlacement>> byCoord =
            new Dictionary<Vector2Int, List<ElementPlacement>>();

        GUIStyle cellStyle;
        Vector2Int hoverCell;
        bool hasHover;

        public override void OnInspectorGUI()
        {
            LevelSO level = target as LevelSO;
            if (level == null) return;

            RebuildLookup(level);

            DrawHeader(level);
            DrawGrid(level);
            DrawDiagnostics(level);

            EditorGUILayout.Space(8f);
            DrawDefaultInspector();
        }

        void RebuildLookup(LevelSO level)
        {
            byCoord.Clear();
            if (level.placements == null) return;

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

        void DrawHeader(LevelSO level)
        {
            int placementCount = level.placements != null ? level.placements.Count : 0;
            int supplyCount = level.supplies != null ? level.supplies.Count : 0;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                level.width + " × " + level.height + " 网格",
                EditorStyles.boldLabel, GUILayout.Width(90f));
            EditorGUILayout.LabelField(
                placementCount + " 个摆放 / " + supplyCount + " 张手牌",
                EditorStyles.miniLabel);

            if (supplyCount == 0)
            {
                EditorGUILayout.LabelField("（无手牌 = 演示关）", EditorStyles.miniLabel, GUILayout.Width(120f));
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

            string hint = hasHover
                ? "光标：(" + hoverCell.x + ", " + hoverCell.y + ")　点击有元件的格子 = 旋转 +90°，Shift+点击 = -90°"
                : "点击有元件的格子 = 旋转 +90°，Shift+点击 = -90°";
            EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
        }

        void DrawCell(LevelSO level, Rect cell, int x, int y)
        {
            byCoord.TryGetValue(new Vector2Int(x, y), out List<ElementPlacement> list);

            bool occupied = list != null && list.Count > 0;
            bool conflict = list != null && list.Count > 1;
            bool missingType = occupied && list[0].type == null;

            Color back = CellEmpty;
            if (missingType) back = MissingColor;
            else if (conflict) back = ConflictColor;
            else if (occupied) back = ColorOf(list[0].type);

            bool isHover = cell.Contains(Event.current.mousePosition);
            if (isHover)
            {
                hoverCell = new Vector2Int(x, y);
                hasHover = true;
                back = Color.Lerp(back, Color.white, 0.25f);
            }

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(cell, back);

                if (occupied)
                {
                    string text = conflict
                        ? list.Count + "×"
                        : ((string.IsNullOrEmpty(list[0].type.label) ? "?" : list[0].type.label)
                           + (list[0].rotationSteps == 0 ? string.Empty : "r" + list[0].rotationSteps));

                    GUI.Label(cell, text, CellLabelStyle());
                }
            }

            // 点击 = 旋转（Editor 里唯一会改数据的操作，已挂 Undo）
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && cell.Contains(Event.current.mousePosition)
                && occupied)
            {
                RotatePlacement(level, list[0], Event.current.shift ? -1 : 1);
                Event.current.Use();
            }
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
