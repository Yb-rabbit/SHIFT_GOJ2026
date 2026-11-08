using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 关卡编辑窗口：在网格上直接点格子摆元件，写完即存进 <see cref="LevelSO"/>。
    ///
    /// 和 <see cref="LevelSOInspector"/> 的关系：Inspector 那张网格图是「看 + 转」，
    /// 想在空格子上放东西还得回列表里 Add 一项再手打坐标。这个窗口把「放」也补上，
    /// 于是做一关不用离开编辑器、也不用照抄坐标。
    ///
    /// 操作（鼠标优先，和游戏内 BUILD MODE 保持同一套直觉）：
    /// 左键放置选中的类型｜右键删除｜Shift+左键顺时针转 90°｜Ctrl+左键逆时针转 90°｜
    /// 按住左键拖过格子 = 连铺。
    ///
    /// 数据上它只改 <see cref="LevelSO.placements"/> / <see cref="LevelSO.supplies"/> 这两个
    /// 已经存在的公开列表，序列化格式一个字节都没变；所有改动都挂了 Undo，
    /// Ctrl+Z 能一路退回去，所以可以放心在上面试排版。
    /// </summary>
    public sealed class LevelEditorWindow : EditorWindow
    {
        const float CellGap = 2f;
        const float RowLabelWidth = 24f;
        const float MinCellSize = 14f;
        const float MaxCellSize = 48f;

        static readonly Color GridBack = new Color(0.15f, 0.16f, 0.19f);
        static readonly Color CellEmpty = new Color(0.23f, 0.24f, 0.28f);
        static readonly Color ConflictColor = new Color(0.85f, 0.28f, 0.28f);
        static readonly Color MissingColor = new Color(0.55f, 0.55f, 0.20f);

        /// <summary>墙层格子：比空格亮、比任何元件暗，和检视器里那张网格保持同一套颜色。</summary>
        static readonly Color WallCellColor = new Color(0.42f, 0.44f, 0.50f);

        static readonly Color HoverOverlay = new Color(1f, 1f, 1f, 0.20f);
        static readonly Color SelectedTint = new Color(0.24f, 0.48f, 0.72f);
        static readonly Color IdleTint = new Color(0.21f, 0.21f, 0.22f);

        [SerializeField] LevelSO level;
        [SerializeField] float cellSize = 34f;
        [SerializeField] Vector2 paletteScroll;
        [SerializeField] Vector2 canvasScroll;

        ElementTypeSO brush;
        bool erasing;
        bool painting;
        string status = string.Empty;

        /// <summary>选择模式下点中的格子；参数面板跟着它走（-1,-1 = 还没选）。</summary>
        [SerializeField] Vector2Int selectedCoord = new Vector2Int(-1, -1);
        [SerializeField] bool hasSelection;
        Vector2 paramScroll;

        readonly List<ElementTypeSO> palette = new List<ElementTypeSO>();
        GUIStyle cellStyle;
        GUIStyle tinyStyle;

        [MenuItem("SHIFT GO/BitFlow/关卡编辑器", false, 21)]
        static void Open()
        {
            LevelEditorWindow window = GetWindow<LevelEditorWindow>("BITFLOW 关卡编辑器");
            window.minSize = new Vector2(820f, 560f);
            window.Show();
        }

        void OnEnable()
        {
            if (level == null && Selection.activeObject is LevelSO selected) level = selected;
            RefreshPalette();
            Undo.undoRedoPerformed += Repaint;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        void OnFocus()
        {
            if (palette.Count == 0) RefreshPalette();
        }

        /// <summary>在 Project 里点关卡资产，窗口自动跟着走（省一次拖拽）。</summary>
        void OnSelectionChange()
        {
            if (Selection.activeObject is LevelSO selected && selected != level)
            {
                level = selected;
                Repaint();
            }
        }

        void RefreshPalette()
        {
            palette.Clear();
            palette.AddRange(LevelImportTool.LoadTypePool());
            palette.Sort(CompareTypes);

            if (brush != null && !palette.Contains(brush)) brush = null;
        }

        static int CompareTypes(ElementTypeSO left, ElementTypeSO right)
        {
            int leftKind = (int)LevelImportTool.KindOf(left);
            int rightKind = (int)LevelImportTool.KindOf(right);
            if (leftKind != rightKind) return leftKind.CompareTo(rightKind);
            return string.CompareOrdinal(
                left != null ? left.name : string.Empty,
                right != null ? right.name : string.Empty);
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawLevelFields();

            if (level == null)
            {
                EditorGUILayout.HelpBox(
                    "上面选一个关卡资产（或在 Project 里点中 Level_XX.asset，这个窗口会自动跟过去）。",
                    MessageType.Info);
                return;
            }

            if (Event.current.type == EventType.MouseUp) painting = false;

            EditorGUILayout.BeginHorizontal();
            DrawPalettePanel();
            DrawCanvasPanel();
            EditorGUILayout.EndHorizontal();

            DrawParamPanel();
            DrawSupplies();
            DrawStatusBar();
        }

        // ------------------------------------------------------------------ 顶部字段

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            level = (LevelSO)EditorGUILayout.ObjectField(
                level, typeof(LevelSO), false, GUILayout.Width(220f));

            if (GUILayout.Button("新建关卡", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                CreateNewLevel();

            if (level != null && GUILayout.Button("加入关卡表", EditorStyles.toolbarButton, GUILayout.Width(78f)))
            {
                int catalogIndex = LevelImportTool.AppendToCatalog(level);
                status = catalogIndex >= 0
                    ? "已在 LevelCatalog 第 " + catalogIndex + " 项（索引 0 是开局第一关）"
                    : "没找到 Assets/Data/Levels/LevelCatalog.asset，需要手动拖进关卡表";
                AssetDatabase.SaveAssets();
            }

            if (GUILayout.Button("刷新调色板", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                RefreshPalette();
                status = "调色板已刷新：" + palette.Count + " 个类型资产";
            }

            if (level != null && GUILayout.Button(
                    new GUIContent("试跑这一关", "用当前 placements 直接跑一遍模拟器，告诉你哪一关能通、哪个接收器第几位对不上"),
                    EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                RunLevelCheck();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("全部保存", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                AssetDatabase.SaveAssets();
                status = "已保存到磁盘";
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawLevelFields()
        {
            if (level == null) return;

            EditorGUI.BeginChangeCheck();

            string displayName = EditorGUILayout.TextField("Display Name", level.displayName);
            int width = EditorGUILayout.IntSlider("Width", level.width, 3, 24);
            int height = EditorGUILayout.IntSlider("Height", level.height, 3, 16);
            float tickInterval = EditorGUILayout.Slider("Tick Interval", level.tickInterval, 0.1f, 1.5f);

            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(level, "Edit Level Fields");
            level.displayName = displayName;
            level.width = width;
            level.height = height;
            level.tickInterval = tickInterval;
            EditorUtility.SetDirty(level);
        }

        void CreateNewLevel()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "新建关卡", "Level_05", "asset", "选个位置保存关卡资产", "Assets/Data/Levels");
            if (string.IsNullOrEmpty(path)) return;

            LevelSO created = ScriptableObject.CreateInstance<LevelSO>();
            created.displayName = System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ')
                + " - BIT FLOW";
            created.width = 9;
            created.height = 8;
            created.tickInterval = 0.35f;

            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();

            level = created;
            Selection.activeObject = created;
            status = "已新建 " + path;
        }

        // ------------------------------------------------------------------ 调色板

        void DrawPalettePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(200f));
            EditorGUILayout.LabelField("类型调色板", EditorStyles.boldLabel);

            DrawModeButton("选择 / 检查（不放置）", !erasing && brush == null, SelectInspectMode);
            DrawModeButton("擦除", erasing, SelectEraseMode);

            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll, GUILayout.ExpandHeight(true));

            CellKind lastKind = (CellKind)(-1);
            foreach (ElementTypeSO type in palette)
            {
                CellKind kind = LevelImportTool.KindOf(type);
                if (kind != lastKind)
                {
                    EditorGUILayout.Space(5f);
                    EditorGUILayout.LabelField(KindName(kind), EditorStyles.miniBoldLabel);
                    lastKind = kind;
                }
                DrawTypeButton(type);
            }

            if (palette.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没扫到类型资产。Assets/Data/Elements 下（含 Emitter/Receiver/Gate 等分类子目录）应该有 Type_* 的 ElementTypeSO。",
                    MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void SelectInspectMode()
        {
            brush = null;
            erasing = false;
            status = "选择模式：点击格子不改数据，Shift / Ctrl + 点击可以转格子";
        }

        void SelectEraseMode()
        {
            brush = null;
            erasing = true;
            status = "擦除模式：左键 / 右键点格子清掉上面的元件";
        }

        void DrawModeButton(string text, bool active, System.Action onClick)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, active ? SelectedTint : IdleTint);
            }

            GUI.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 10f, 18f), text, EditorStyles.miniLabel);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) onClick();
        }

        void DrawTypeButton(ElementTypeSO type)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            bool active = !erasing && brush == type;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, active ? SelectedTint : IdleTint);
                EditorGUI.DrawRect(new Rect(rect.x + 3f, rect.y + 4f, 14f, 14f), LevelSOInspector.ColorOf(type));
            }

            GUI.Label(new Rect(rect.x + 22f, rect.y + 3f, rect.width - 26f, 18f), LabelFor(type), EditorStyles.miniLabel);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                brush = type;
                erasing = false;
                status = "笔刷：" + LabelFor(type) + "  (" + type.name + ")";
            }
        }

        // ------------------------------------------------------------------ 网格

        void DrawCanvasPanel()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            cellSize = EditorGUILayout.Slider("格子大小", cellSize, MinCellSize, MaxCellSize, GUILayout.Width(210f));

            if (GUILayout.Button("围一圈墙", EditorStyles.miniButton, GUILayout.Width(72f)))
                SurroundWithWall();

            if (GUILayout.Button("清空摆件", EditorStyles.miniButton, GUILayout.Width(72f)))
                ClearPlacements();

            GUILayout.FlexibleSpace();
            int wallCount = WallCountInMap();
            string countText = level.placements.Count + " 个摆放";
            if (wallCount > 0) countText += " + " + wallCount + " 面墙";
            EditorGUILayout.LabelField(countText, EditorStyles.miniLabel, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            canvasScroll = EditorGUILayout.BeginScrollView(canvasScroll, GUILayout.ExpandHeight(true));
            DrawGrid();
            DrawDiagnostics();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        void DrawGrid()
        {
            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);

            float step = cellSize + CellGap;
            float gridWidth = RowLabelWidth + width * step;
            float gridHeight = height * step;

            Rect area = GUILayoutUtility.GetRect(
                gridWidth + 4f, gridHeight + 4f, GUILayout.ExpandWidth(false));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(area.x, area.y, gridWidth, gridHeight), GridBack);
            }

            for (int y = 0; y < height; y++)
            {
                Rect rowLabel = new Rect(area.x, area.y + (height - 1 - y) * step, RowLabelWidth, cellSize);
                GUI.Label(rowLabel, y.ToString(), TinyStyle());

                for (int x = 0; x < width; x++)
                {
                    DrawCell(area, new Vector2Int(x, y), height);
                }
            }

            // 列号贴在最底下：y 轴向上长，列号靠底边更符合阅读习惯
            float columnTop = area.y + height * step;
            for (int x = 0; x < width; x++)
            {
                GUI.Label(
                    new Rect(area.x + RowLabelWidth + x * step, columnTop, cellSize, 14f),
                    x.ToString(),
                    TinyStyle());
            }
        }

        Rect CellRect(Rect area, Vector2Int coord, int height)
        {
            float step = cellSize + CellGap;
            float x = area.x + RowLabelWidth + coord.x * step;
            float y = area.y + (height - 1 - coord.y) * step;
            return new Rect(x, y, cellSize, cellSize);
        }

        void DrawCell(Rect area, Vector2Int coord, int height)
        {
            Rect cell = CellRect(area, coord, height);

            ElementPlacement placement = FindPlacement(coord);
            int stacked = CountAt(coord);
            bool wallInMap = level.IsWallInMap(coord);

            if (Event.current.type == EventType.Repaint)
            {
                Color background = CellEmpty;
                if (stacked > 1 || (wallInMap && stacked > 0)) background = ConflictColor;
                else if (wallInMap) background = WallCellColor;
                else if (placement != null)
                {
                    background = placement.type != null
                        ? LevelSOInspector.ColorOf(placement.type)
                        : MissingColor;
                }

                EditorGUI.DrawRect(cell, background);

                if (wallInMap && stacked == 0)
                {
                    GUI.Label(cell, "#", CellStyle());
                }
                else if (placement != null)
                {
                    GUI.Label(cell, LabelFor(placement.type), CellStyle());

                    if (placement.rotationSteps != 0)
                    {
                        GUI.Label(
                            new Rect(cell.x + cell.width - 18f, cell.y - 1f, 18f, 12f),
                            (placement.rotationSteps * 90).ToString(),
                            TinyStyle());
                    }

                    if (placement.playerEditable)
                    {
                        GUI.Label(
                            new Rect(cell.x, cell.y + cell.height - 12f, cell.width, 12f),
                            "可搬",
                            TinyStyle());
                    }
                }

                if (cell.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(cell, HoverOverlay);
                }
            }

            if (!cell.Contains(Event.current.mousePosition)) return;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                RemoveAt(coord);
                Event.current.Use();
                Repaint();
                return;
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (Event.current.shift) RotateAt(coord, 1);
                else if (Event.current.control) RotateAt(coord, -1);
                else
                {
                    painting = true;
                    PaintAt(coord);
                }

                Event.current.Use();
                Repaint();
                return;
            }

            if (Event.current.type == EventType.MouseDrag && painting)
            {
                PaintAt(coord);
                Event.current.Use();
                Repaint();
            }
        }

        // ------------------------------------------------------------------ 数据操作

        /// <summary>把笔刷盖到这一格；已经有别的类型就原地替换（旋转归零），空格就追加一项。</summary>
        void PaintAt(Vector2Int coord)
        {
            if (erasing)
            {
                RemoveAt(coord);
                return;
            }
            if (brush == null)
            {
                // 选择模式：点格子 = 选中它，下面的参数面板就换成这一格的参数
                SelectCell(coord);
                return;
            }

            ElementPlacement existing = FindPlacement(coord);

            // 墙笔刷走墙层（wallMap），不进 placements：
            // 一面墙只需要"在哪一格"这一条信息，没必要占一条 8 字段的摆放项
            if (brush is WallTypeSO)
            {
                PaintWallAt(coord);
                return;
            }

            if (existing != null && existing.type == brush && CountAt(coord) == 1) return; // 重复点同一格不留噪声 undo

            Undo.RecordObject(level, "Place Element");

            // 这格原来是墙层的墙：换成元件时顺手擦掉。
            // 不擦的话墙会被 placements 静默压过去，作者会以为"墙还在，只是没显示"
            bool erasedWall = level.SetWall(coord, false);

            if (existing != null)
            {
                existing.type = brush;
                existing.rotationSteps = 0;
                existing.playerEditable = false;
            }
            else
            {
                level.placements.Add(new ElementPlacement
                {
                    type = brush,
                    coord = coord,
                    rotationSteps = 0,
                    playerEditable = false,
                });
            }

            EditorUtility.SetDirty(level);
            status = "已放置 " + LabelFor(brush) + " @ (" + coord.x + ", " + coord.y + ")"
                     + (erasedWall ? "（顺手擦掉了这一格的墙）" : string.Empty);
        }

        /// <summary>墙笔刷：直接写墙层。这一格已经有墙就什么都不做（连点不会堆一堆 undo）。</summary>
        void PaintWallAt(Vector2Int coord)
        {
            Undo.RecordObject(level, "Draw Wall");

            // 用墙笔刷画第一面墙时顺手把 wallType 定下来，省得作者再去字段里找
            if (level.wallType == null && brush is WallTypeSO brushWall) level.wallType = brushWall;

            bool changed = level.SetWall(coord, true);
            EditorUtility.SetDirty(level);

            if (changed) status = "已画墙 @ (" + coord.x + ", " + coord.y + ")";
        }

        void RemoveAt(Vector2Int coord)
        {
            bool hadWall = level.IsWallInMap(coord);
            if (CountAt(coord) == 0 && !hadWall) return;

            Undo.RecordObject(level, "Remove Placement");
            for (int i = level.placements.Count - 1; i >= 0; i--)
            {
                ElementPlacement placement = level.placements[i];
                if (placement == null || placement.coord != coord) continue;
                level.placements.RemoveAt(i);
            }

            // 墙层里的墙也要一起擦：右键 / 橡皮在这两种表示法下的意思都是"清掉这一格"
            level.SetWall(coord, false);

            EditorUtility.SetDirty(level);
            status = "已清掉 (" + coord.x + ", " + coord.y + ") 上的"
                     + (hadWall && CountAt(coord) == 0 ? "墙" : "元件");
        }

        void RotateAt(Vector2Int coord, int delta)
        {
            ElementPlacement placement = FindPlacement(coord);
            if (placement == null)
            {
                status = "(" + coord.x + ", " + coord.y + ") 是空的，没东西可转";
                return;
            }

            Undo.RecordObject(level, "Rotate Placement");
            placement.rotationSteps = ((placement.rotationSteps + delta) % 4 + 4) % 4;
            EditorUtility.SetDirty(level);
            status = "(" + coord.x + ", " + coord.y + ") 转到 " + (placement.rotationSteps * 90) + "°";
        }

        /// <summary>
        /// 把整圈边框画成墙 —— 现有关卡几乎都是这么起的底，省掉几十次点击。
        /// 现在写的是墙层（wallMap）：一圈墙只占 height 行字符，而不是几十条 8 字段的摆放项。
        /// </summary>
        void SurroundWithWall()
        {
            ElementTypeSO wall = FindWallType();

            Undo.RecordObject(level, "Surround With Wall");

            // 用这个按钮画墙时顺手把 wallType 定下来，省得作者再去字段里找一遍
            if (level.wallType == null && wall is WallTypeSO wallType) level.wallType = wallType;

            if (level.wallType == null)
            {
                status = "调色板里没有墙类型资产（WallTypeSO），先刷新调色板或新建一个 Type_Wall";
                return;
            }

            int added = 0;
            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool onEdge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    if (!onEdge) continue;

                    Vector2Int coord = new Vector2Int(x, y);
                    if (CountAt(coord) > 0) continue; // 已有元件（或旧数据里的墙）的格子不动，避免覆盖手工摆放

                    if (level.SetWall(coord, true)) added++;
                }
            }

            EditorUtility.SetDirty(level);
            status = added > 0 ? "围了一圈墙：新增 " + added + " 格（写在墙层里）" : "边框上已经有东西了，没有新增";
        }

        /// <summary>墙层里现在有几面墙。</summary>
        int WallCountInMap()
        {
            int count = 0;
            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (level.IsWallInMap(new Vector2Int(x, y))) count++;
                }
            }
            return count;
        }

        void ClearPlacements()
        {
            int wallCount = WallCountInMap();
            if (level.placements.Count == 0 && wallCount == 0) return;

            int choice;
            if (wallCount == 0)
            {
                // 没有墙就别硬塞三个按钮（否则会出现两个"算了"）
                choice = EditorUtility.DisplayDialog(
                    "清空摆件",
                    "把这一关的 " + level.placements.Count + " 个摆放全部删掉？\n（Ctrl+Z 可以撤回，关卡资产的其他字段不受影响）",
                    "清空", "算了") ? 0 : 1;
            }
            else
            {
                choice = EditorUtility.DisplayDialogComplex(
                    "清空摆件",
                    "这一关有 " + level.placements.Count + " 个摆放、" + wallCount + " 面墙。\n" +
                    "要清掉哪些？（Ctrl+Z 可以撤回，关卡资产的其他字段不受影响）",
                    "只清摆放", "算了", "连墙一起清");
            }

            if (choice == 1) return;

            Undo.RecordObject(level, "Clear Placements");
            level.placements.Clear();

            // 默认（第一个按钮）保留墙：墙是关卡的骨架，多半还想留着重新摆元件
            if (choice == 2) level.wallMap = string.Empty;

            EditorUtility.SetDirty(level);
            status = choice == 2 ? "已清空摆放和墙层，Ctrl+Z 可以撤回" : "已清空摆放（墙层保留），Ctrl+Z 可以撤回";
        }

        ElementPlacement FindPlacement(Vector2Int coord)
        {
            for (int i = 0; i < level.placements.Count; i++)
            {
                ElementPlacement placement = level.placements[i];
                if (placement != null && placement.coord == coord) return placement;
            }
            return null;
        }

        int CountAt(Vector2Int coord)
        {
            int count = 0;
            for (int i = 0; i < level.placements.Count; i++)
            {
                ElementPlacement placement = level.placements[i];
                if (placement != null && placement.coord == coord) count++;
            }
            return count;
        }

        ElementTypeSO FindWallType()
        {
            foreach (ElementTypeSO type in palette)
            {
                if (type is WallTypeSO) return type;
            }
            return null;
        }

        // ------------------------------------------------------------------ 诊断 / 手牌 / 状态栏

        void DrawDiagnostics()
        {
            List<string> issues = new List<string>();
            int width = Mathf.Max(1, level.width);
            int height = Mathf.Max(1, level.height);

            for (int i = 0; i < level.placements.Count; i++)
            {
                ElementPlacement placement = level.placements[i];
                if (placement == null) continue;

                Vector2Int coord = placement.coord;

                if (coord.x < 0 || coord.x >= width || coord.y < 0 || coord.y >= height)
                {
                    issues.Add("(" + coord.x + ", " + coord.y + ") 越界：不在 "
                        + width + "×" + height + " 网格内");
                }

                if (placement.type == null)
                {
                    issues.Add("(" + coord.x + ", " + coord.y + ") 缺少类型资产");
                    continue;
                }

                // 撞车只报一次（同一个坐标下的第二个元件就不再重复喊）
                if (CountAt(coord) > 1 && FindPlacement(coord) == placement)
                {
                    issues.Add("(" + coord.x + ", " + coord.y + ") 撞车："
                        + CountAt(coord) + " 个元件叠在同一格");
                }
            }

            if (issues.Count == 0) return;

            EditorGUILayout.HelpBox(
                "发现 " + issues.Count + " 个问题：\n" + string.Join("\n", issues.ToArray()),
                MessageType.Warning);
        }

        void DrawSupplies()
        {
            if (level == null) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("玩家手牌（留空 = 演示关，无限涂刷）", EditorStyles.boldLabel);

            int removeIndex = -1;

            for (int i = 0; i < level.supplies.Count; i++)
            {
                ElementSupply supply = level.supplies[i];
                if (supply == null) continue;

                EditorGUILayout.BeginHorizontal();

                ElementTypeSO type = (ElementTypeSO)EditorGUILayout.ObjectField(
                    supply.type, typeof(ElementTypeSO), false, GUILayout.Width(240f));
                int count = EditorGUILayout.IntField(supply.count, GUILayout.Width(48f));

                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f))) removeIndex = i;
                if (supply.type != null)
                {
                    EditorGUILayout.LabelField("Key: " + supply.type.Key, EditorStyles.miniLabel);
                }

                EditorGUILayout.EndHorizontal();

                if (type == supply.type && count == supply.count) continue;

                Undo.RecordObject(level, "Edit Supply");
                supply.type = type;
                supply.count = Mathf.Max(0, count);
                EditorUtility.SetDirty(level);
            }

            if (removeIndex >= 0)
            {
                Undo.RecordObject(level, "Remove Supply");
                level.supplies.RemoveAt(removeIndex);
                EditorUtility.SetDirty(level);
            }

            // 手牌是按「类型资产」记账的：同一个类型写两条不会变成两摞，只会合成一摞更多张。
            // 这里把合并结果显式说出来，免得看着两条以为玩家该拿到两份。
            Dictionary<string, int> stacksByKey = new Dictionary<string, int>();
            Dictionary<string, int> totalByKey = new Dictionary<string, int>();
            Dictionary<string, ElementTypeSO> typeByKey = new Dictionary<string, ElementTypeSO>();

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;

                string key = supply.type.Key;
                stacksByKey.TryGetValue(key, out int stacks);
                totalByKey.TryGetValue(key, out int total);

                stacksByKey[key] = stacks + 1;
                totalByKey[key] = total + Mathf.Max(0, supply.count);
                typeByKey[key] = supply.type;
            }

            List<string> merged = new List<string>();
            foreach (KeyValuePair<string, int> pair in stacksByKey)
            {
                if (pair.Value <= 1) continue;
                merged.Add("  " + LabelFor(typeByKey[pair.Key]) + "：" + pair.Value + " 条 → 玩家拿到一摞 "
                    + totalByKey[pair.Key] + " 张");
            }

            if (merged.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "同一类型写了多条（手牌按类型资产记账，会合成一摞）：\n" + string.Join("\n", merged.ToArray()),
                    MessageType.Info);
            }

            if (GUILayout.Button("+ 加手牌（笔刷类型；已有一摞就 +1）", EditorStyles.miniButton, GUILayout.Width(240f)))
            {
                Undo.RecordObject(level, "Add Supply");

                // 手牌按类型资产记账，同类型再点一次就是给那一摞加一张 ——
                // 不然会攒出一堆长得一样、数字还共用的条目
                ElementSupply existing = null;
                if (brush != null)
                {
                    foreach (ElementSupply supply in level.supplies)
                    {
                        if (supply == null || supply.type == null) continue;
                        if (supply.type.Key != brush.Key) continue;

                        existing = supply;
                        break;
                    }
                }

                if (existing != null)
                {
                    existing.count += 1;
                    status = LabelFor(brush) + " 加到 " + existing.count + " 张（同类型只有一摞牌）";
                }
                else
                {
                    level.supplies.Add(new ElementSupply { type = brush, count = 1 });
                    status = brush != null
                        ? "已加一摞 " + LabelFor(brush)
                        : "已加一摞空手牌，记得给它选类型";
                }

                EditorUtility.SetDirty(level);
            }
        }

        void DrawStatusBar()
        {
            EditorGUILayout.Space(2f);

            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(status)
                    ? "左键放置｜右键删除｜Shift / Ctrl + 左键 转 90°｜按住左键拖过格子 = 连铺"
                    : status,
                EditorStyles.helpBox);
        }

        // ------------------------------------------------------------------ 选中格参数

        void SelectCell(Vector2Int coord)
        {
            selectedCoord = coord;
            hasSelection = true;

            ElementPlacement placement = FindPlacement(coord);
            status = placement == null || placement.type == null
                ? "(" + coord.x + ", " + coord.y + ") 是空格"
                : "选中 (" + coord.x + ", " + coord.y + ") " + LabelFor(placement.type)
                  + "    " + placement.ParamsSummary();
        }

        /// <summary>
        /// 选中格的参数面板：发射序列 / 发射数 / 发射方向，或接收序列 / 应收数 / 进料面。
        /// 参数存在 placement 上（属于关卡设计），和类型资产解耦 —— 同一关里两个同型号发射器
        /// 可以用完全不同的节奏。改任意一个值就自动接管，不用先勾「启用」。
        /// </summary>
        void DrawParamPanel()
        {
            if (!hasSelection) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            ElementPlacement placement = FindPlacement(selectedCoord);
            if (placement == null || placement.type == null)
            {
                EditorGUILayout.LabelField(
                    "(" + selectedCoord.x + ", " + selectedCoord.y + ") 是空格 —— 选择模式下点一个有元件的格子才能改参数",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            // 用类型资产展开一次，拿到"没改过参数时"的真实值，用来预填输入框
            ElementDefinition effective = placement.type.CreateDefinition(
                new GridPoint(placement.coord.x, placement.coord.y), placement.rotationSteps, 0);
            if (effective == null)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            string header = LabelFor(placement.type) + "   (" + placement.coord.x + ", " + placement.coord.y + ")"
                + (placement.HasParamOverride ? "    本格独立参数" : "    跟随类型资产");

            switch (effective)
            {
                case EmitterDefinition emitter:
                    EditorGUILayout.LabelField(header + "    发射器", EditorStyles.boldLabel);
                    DrawEmitterParams(placement, emitter);
                    break;

                case ReceiverDefinition receiver:
                    EditorGUILayout.LabelField(header + "    接收器", EditorStyles.boldLabel);
                    DrawReceiverParams(placement, receiver);
                    break;

                default:
                    EditorGUILayout.LabelField(header + " —— 这种元件没有可调参数", EditorStyles.miniLabel);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        void DrawEmitterParams(ElementPlacement placement, EmitterDefinition effective)
        {
            EditorGUI.BeginChangeCheck();

            string sequence = EditorGUILayout.TextField(
                new GUIContent("发射序列", "位从末字符开始弹。例：1011 → 1,1,0,1，再循环填充到「发射数」长度"),
                placement.overrideSequence ? placement.sequence : effective.Sequence);

            int count = EditorGUILayout.IntField(
                new GUIContent("发射数（总位数）", "这一格一共发几位；序列循环填充，不必被序列长度整除（想发 6 位就填 6）"),
                placement.overrideCount ? placement.count : effective.EmitCount);

            Dir dir = (Dir)EditorGUILayout.EnumPopup(
                new GUIContent("发射方向", "等同于给这一格换一个旋转档；不会把上面的参数变成「独立」"),
                effective.Facing);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(level, "Edit Emitter Params");

                if (sequence != (placement.overrideSequence ? placement.sequence : effective.Sequence))
                {
                    placement.overrideSequence = true;
                    placement.sequence = CleanBits(sequence);
                }

                if (count != (placement.overrideCount ? placement.count : effective.EmitCount))
                {
                    placement.overrideCount = true;
                    placement.count = Mathf.Max(0, count);
                }

                if (dir != effective.Facing) SetDirection(placement, dir);

                EditorUtility.SetDirty(level);
                status = "已改 " + LabelFor(placement.type) + " @ (" + placement.coord.x + ", "
                    + placement.coord.y + ")：" + placement.ParamsSummary();
            }

            DrawEmitterPreview(placement, effective);
            DrawParamActions(placement);
        }

        /// <summary>把「序列 + 发射数」按模拟器的规则展开一遍，让作者一眼看到到底会发什么。</summary>
        void DrawEmitterPreview(ElementPlacement placement, EmitterDefinition effective)
        {
            string sequence = placement.overrideSequence ? placement.sequence : effective.Sequence;
            int count = placement.overrideCount ? placement.count : effective.EmitCount;

            EmitterDefinition preview = new EmitterDefinition
            {
                Sequence = CleanBits(sequence),
                EmitCount = Mathf.Max(0, count),
            };

            List<byte> cycle = preview.BuildCycle();
            List<byte> order = preview.BuildEmitOrder();

            EditorGUILayout.LabelField("实际发射顺序", BitsText(order), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "循环单元 " + BitsText(cycle) + "（" + cycle.Count + " 位，含 1 共 " + CountOnes(cycle)
                + " 个）→ 循环填充到 " + Mathf.Max(0, count) + " 位",
                EditorStyles.miniLabel);
        }

        void DrawReceiverParams(ElementPlacement placement, ReceiverDefinition effective)
        {
            EditorGUI.BeginChangeCheck();

            string sequence = EditorGUILayout.TextField(
                new GUIContent("接收序列", "期望收到的位，按循环比对。例：1 表示每一位都得是 1"),
                placement.overrideSequence ? placement.sequence : effective.TargetSequence);

            int count = EditorGUILayout.IntField(
                new GUIContent("接收数（总位数）", "这一格期望收几位；收满且每位都对才算通，多收也算失败"),
                placement.overrideCount ? placement.count : effective.TargetCount);

            bool anyFace = EditorGUILayout.Toggle(
                new GUIContent("四面都能收", "勾上（默认）= 位从任何方向进来都算数；关掉 = 只认下面那个进料面，其余面撞上销毁"),
                placement.overrideFace ? placement.acceptAnyFace : effective.AcceptAnyFace);

            Dir dir = effective.FeedFace;
            using (new EditorGUI.DisabledScope(anyFace))
            {
                dir = (Dir)EditorGUILayout.EnumPopup(
                    new GUIContent("进料面", anyFace
                        ? "四面可收时这个朝向只用于显示，不影响判定"
                        : "位要朝这个面的反方向走进来"),
                    effective.FeedFace);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(level, "Edit Receiver Params");

                if (sequence != (placement.overrideSequence ? placement.sequence : effective.TargetSequence))
                {
                    placement.overrideSequence = true;
                    placement.sequence = CleanBits(sequence);
                }

                if (count != (placement.overrideCount ? placement.count : effective.TargetCount))
                {
                    placement.overrideCount = true;
                    placement.count = Mathf.Max(0, count);
                }

                if (anyFace != effective.AcceptAnyFace)
                {
                    placement.overrideFace = true;
                    placement.acceptAnyFace = anyFace;
                }

                if (dir != effective.FeedFace) SetDirection(placement, dir);

                EditorUtility.SetDirty(level);
                status = "已改 " + LabelFor(placement.type) + " @ (" + placement.coord.x + ", "
                    + placement.coord.y + ")：" + placement.ParamsSummary();
            }

            DrawReceiverPreview(placement, effective);
            DrawParamActions(placement);
        }

        void DrawReceiverPreview(ElementPlacement placement, ReceiverDefinition effective)
        {
            string sequence = CleanBits(placement.overrideSequence ? placement.sequence : effective.TargetSequence);
            int count = Mathf.Max(0, placement.overrideCount ? placement.count : effective.TargetCount);

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i >= 24 && count > 24)
                {
                    builder.Append("…");
                    break;
                }
                builder.Append(sequence[i % sequence.Length]);
            }

            EditorGUILayout.LabelField("期望收到的位", builder.Length > 0 ? builder.ToString() : "（不期望收任何位）",
                EditorStyles.miniLabel);
        }

        void DrawParamActions(ElementPlacement placement)
        {
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!placement.HasParamOverride))
            {
                if (GUILayout.Button("恢复跟随类型资产", EditorStyles.miniButton, GUILayout.Width(140f)))
                {
                    Undo.RecordObject(level, "Reset Placement Params");
                    placement.overrideSequence = false;
                    placement.overrideCount = false;
                    placement.overrideFace = false;
                    EditorUtility.SetDirty(level);
                    status = LabelFor(placement.type) + " 的参数已恢复成跟随类型资产";
                }
            }

            if (GUILayout.Button("参数同步到本关同类型格子", EditorStyles.miniButton, GUILayout.Width(190f)))
            {
                SyncParamsToSameType(placement);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>本关里所有同类型资产的格子改成同样的参数（同型号统一节奏时用）。</summary>
        void SyncParamsToSameType(ElementPlacement source)
        {
            Undo.RecordObject(level, "Sync Placement Params");

            int changed = 0;
            foreach (ElementPlacement other in level.placements)
            {
                if (other == null || other == source || other.type != source.type) continue;

                other.overrideSequence = source.overrideSequence;
                other.sequence = source.sequence;
                other.overrideCount = source.overrideCount;
                other.count = source.count;
                other.overrideFace = source.overrideFace;
                other.acceptAnyFace = source.acceptAnyFace;
                changed++;
            }

            EditorUtility.SetDirty(level);
            status = changed > 0
                ? "同类型 " + LabelFor(source.type) + " 另有 " + changed + " 格跟着改了"
                : "本关没有别的 " + LabelFor(source.type);
        }

        /// <summary>把「实际朝向」反推成旋转档：同一份类型资产转 0~3 次里哪个等于这个方向。</summary>
        void SetDirection(ElementPlacement placement, Dir dir)
        {
            for (int steps = 0; steps < 4; steps++)
            {
                ElementDefinition candidate = placement.type.CreateDefinition(
                    new GridPoint(placement.coord.x, placement.coord.y), steps, 0);
                if (candidate == null) continue;

                bool matches;
                if (candidate is EmitterDefinition emitter) matches = emitter.Facing == dir;
                else if (candidate is ReceiverDefinition receiver) matches = receiver.FeedFace == dir;
                else if (candidate is SplitterDefinition splitter) matches = splitter.OneArm == dir;
                else matches = false;

                if (!matches) continue;

                placement.rotationSteps = steps;
                return;
            }
        }

        /// <summary>只留 0 与 1（空格、逗号之类的分隔符忽略），空则回 "1"。</summary>
        static string CleanBits(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "1";

            StringBuilder builder = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '0' || raw[i] == '1') builder.Append(raw[i]);
            }
            return builder.Length > 0 ? builder.ToString() : "1";
        }

        static string BitsText(List<byte> bits, int maxShow = 32)
        {
            if (bits == null || bits.Count == 0) return "（空）";

            StringBuilder builder = new StringBuilder(bits.Count + 8);
            int show = Mathf.Min(bits.Count, maxShow);
            for (int i = 0; i < show; i++) builder.Append(bits[i] == 0 ? '0' : '1');
            if (bits.Count > show) builder.Append("…（共 ").Append(bits.Count).Append(" 位）");
            return builder.ToString();
        }

        static int CountOnes(List<byte> bits)
        {
            int count = 0;
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i] != 0) count++;
            }
            return count;
        }

        // ------------------------------------------------------------------ 试跑

        /// <summary>
        /// 拿当前 placements 直接跑一遍模拟器（和玩家通关走的是同一套逻辑），
        /// 输出「多少 tick 通关」或「哪个接收器第几位对不上」。
        /// 改关卡参数前跑一次、改完再跑一次，就知道自己动的是对还是错。
        /// </summary>
        void RunLevelCheck()
        {
            if (level == null) return;

            LevelDefinition definition = level.ToDefinition();

            // 和批量自检菜单、"玩家真通关"共用同一份实现，报告口径不会走偏
            string report = LevelCheck.Run(definition, out bool _);

            status = report;
            Debug.Log("[BitFlow] 试跑 " + definition.DisplayName + "\n" + report);
        }

        // ------------------------------------------------------------------ 小工具

        static string LabelFor(ElementTypeSO type)
        {
            if (type == null) return "?";
            return string.IsNullOrEmpty(type.label) ? type.name : type.label;
        }

        static string KindName(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Emitter: return "发射器";
                case CellKind.Receiver: return "接收器";
                case CellKind.Splitter: return "分流器";
                case CellKind.NotGate: return "非门";
                case CellKind.Filter: return "过滤器";
                default: return "墙";
            }
        }

        GUIStyle CellStyle()
        {
            if (cellStyle != null) return cellStyle;

            cellStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            return cellStyle;
        }

        GUIStyle TinyStyle()
        {
            if (tinyStyle != null) return tinyStyle;

            tinyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 8,
                clipping = TextClipping.Clip,
            };
            return tinyStyle;
        }
    }
}
