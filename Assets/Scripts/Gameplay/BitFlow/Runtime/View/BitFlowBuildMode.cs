using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 游戏内关卡编辑模式（BUILD MODE）。
    /// 设计原则：编辑只发生在 <see cref="LevelDraft"/> 这份内存草稿上，每次改动都交给
    /// <see cref="BitFlowRunner.ShowDefinition"/> 重建场景与模拟，因此编辑出来的关卡与
    /// Assets/Data 里的 LevelSO 走的是同一条渲染/模拟管线，不存在"编辑态与运行态两套逻辑"。
    /// 想存下来时写 persistentDataPath 下的 JSON（<see cref="LevelDraftSerializer"/>），绝不改资产。
    ///
    /// 操作（鼠标优先，键盘只是加速）：滚轮换件｜左键放置（可按住拖动连铺）｜右键删除（可按住拖动连擦）｜
    /// 中键或 R 旋转鼠标下的元件｜F5 保存｜F9 读取｜B 或 ESC 进出。数字键 1-7 仍可直接点选。
    /// </summary>
    public sealed class BitFlowBuildMode : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.055f, 0.07f, 0.10f, 0.92f);
        static readonly Color TextColor = new Color(0.85f, 0.92f, 1f);
        static readonly Color AccentColor = new Color(0.35f, 0.85f, 1f);
        static readonly Color MutedColor = new Color(0.55f, 0.62f, 0.72f);
        static readonly Color BadColor = new Color(1f, 0.32f, 0.35f);
        static readonly Color ButtonColor = new Color(0.12f, 0.20f, 0.28f, 0.95f);

        /// <summary>调色板顺序（和数字键 1-7 一一对应）。</summary>
        static readonly CellKind[] Palette =
        {
            CellKind.Wall,
            CellKind.Emitter,
            CellKind.Receiver,
            CellKind.Splitter,
            CellKind.NotGate,
            CellKind.Filter,
        };

        static readonly string[] PaletteNames = { "墙", "发射器", "接收器", "分流器", "非门", "过滤器" };

        const int EraseIndex = 6; // 第 7 个按钮＝擦除
        const float CursorHeight = 0.02f;

        BitFlowRunner runner;
        Camera mainCamera;
        Font font;

        bool ready;
        bool editing;
        int selectedIndex;
        LevelDraft draft;
        string statusMessage = string.Empty;

        GameObject panelRoot;
        Text statusText;
        Text selectionText;
        readonly List<Image> paletteBackgrounds = new List<Image>();
        readonly List<Text> paletteLabels = new List<Text>();

        Transform cursor;
        Material cursorMaterial;
        ElementView highlightedView;

        // 拖动放置/擦除的状态机：按下时开始，经过新格子才落地，松开结束
        bool painting;
        bool erasePainting;
        bool hasPainted;
        GridPoint lastPainted;

        void Start()
        {
            runner = FindObjectOfType<BitFlowRunner>();
            mainCamera = Camera.main;
            // 作者模式<b>故意</b>不换像素字体：这里的文案是中文（调色板、状态行、快捷键说明），
            // 而像素字体没有中文字形，打包后会变成方块。它是开发工具、不面向玩家，
            // 所以保留系统字体 —— 玩家侧的 HUD / 黑幕 / 手牌盘 / 选关面板才走 PixelFont。
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (runner == null)
            {
                Debug.LogWarning("[BitFlow] 没找到 BitFlowRunner，BUILD MODE 关闭");
                enabled = false;
                return;
            }

            EnsureEventSystem();
            BuildUi();
            ready = true;
            RefreshStatus();
        }

        void OnDestroy()
        {
            ClearHighlight();
            if (cursorMaterial != null) Destroy(cursorMaterial);
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        void Update()
        {
            if (!ready) return;

            // 暂停 / 选关 / 黑幕期间不接管热键：否则暂停时按 B 还能在背后切建造模式。
            if (InputGate.Locked) return;

            if (Input.GetKeyDown(KeyCode.B))
            {
                ToggleEditing();
            }
            else if (editing && Input.GetKeyDown(KeyCode.Escape) && EscapeInput.Consume())
            {
                SetEditing(false);
            }

            if (!editing) return;

            HandleHotkeys();
            HandleWheel();
            UpdateCursor();
            HandleMouse();
        }

        void HandleHotkeys()
        {
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Select(i);
            }
            if (Input.GetKeyDown(KeyCode.Alpha7)) Select(EraseIndex);

            if (Input.GetKeyDown(KeyCode.R) && TryPickCell(out GridPoint cell) && draft.Rotate(cell))
            {
                ApplyDraft("已旋转 " + cell);
            }

            if (Input.GetKeyDown(KeyCode.F5)) SaveDraft();
            if (Input.GetKeyDown(KeyCode.F9)) LoadDraft();
        }

        // ---------------------------------------------------------------- 进出编辑模式

        void ToggleEditing()
        {
            SetEditing(!editing);
        }

        void SetEditing(bool value)
        {
            if (value)
            {
                // Runner 的 Start 还没跑完时没有 definition 可载入，这一帧先不进去
                if (runner.Definition == null) return;

                draft = LevelDraft.FromDefinition(runner.Definition);
                runner.PauseForEditing();
                statusMessage = "改完按 F5 保存";
            }
            else
            {
                ClearHighlight();
                painting = false;
                hasPainted = false;
                if (cursor != null) cursor.gameObject.SetActive(false);
            }

            editing = value;
            if (panelRoot != null) panelRoot.SetActive(value);
            RefreshStatus();
        }

        // ---------------------------------------------------------------- 鼠标 / 拾取

        void UpdateCursor()
        {
            if (cursor == null) CreateCursor();

            if (!TryPickCell(out GridPoint cell))
            {
                if (cursor.gameObject.activeSelf) cursor.gameObject.SetActive(false);
                ClearHighlight();
                return;
            }

            cursor.gameObject.SetActive(true);
            cursor.position = GridMetrics.CellToWorld(cell) + Vector3.up * CursorHeight;

            ElementView view = runner.Builder != null ? runner.Builder.GetElementAt(cell) : null;
            if (view != highlightedView)
            {
                ClearHighlight();
                highlightedView = view;
                if (highlightedView != null) highlightedView.SetHighlighted(true);
            }
        }

        /// <summary>滚轮换件：不用记数字键，滚一下就是下一个元件（含擦除）。</summary>
        void HandleWheel()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.1f) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            int next = selectedIndex + (scroll > 0f ? 1 : -1);
            if (next > EraseIndex) next = 0;
            else if (next < 0) next = EraseIndex;

            Select(next);
        }

        void HandleMouse()
        {
            // 松开任何键都结束这一轮拖动；要重按才算开始，避免"手一抖连着删一片"
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2))
            {
                painting = false;
                hasPainted = false;
            }

            // 点在 HUD 按钮/滑条上时不要顺手往地里放元件
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                painting = false;
                hasPainted = false;
                return;
            }

            if (!TryPickCell(out GridPoint cell))
            {
                painting = false;
                hasPainted = false;
                return;
            }

            // 中键旋转：手不用离开格子去按 R
            if (Input.GetMouseButtonDown(2))
            {
                if (draft.Rotate(cell)) ApplyDraft("已旋转 " + cell);
                return;
            }

            if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Delete))
            {
                if (draft.Remove(cell)) ApplyDraft("已删除 " + cell);
                return;
            }

            // 左键=放置、右键=删除；按住不放拖过新格子就连续生效
            if (Input.GetMouseButtonDown(1))
            {
                painting = true;
                erasePainting = true;
                hasPainted = false;
            }
            else if (Input.GetMouseButtonDown(0))
            {
                painting = true;
                erasePainting = selectedIndex == EraseIndex;
                hasPainted = false;
            }

            if (!painting) return;

            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            {
                painting = false;
                hasPainted = false;
                return;
            }

            if (hasPainted && lastPainted.Equals(cell)) return;

            lastPainted = cell;
            hasPainted = true;

            if (erasePainting)
            {
                if (draft.Remove(cell)) ApplyDraft("已删除 " + cell);
                return;
            }

            string kindName = PaletteNames[selectedIndex];
            draft.PlaceDefault(Palette[selectedIndex], cell);
            ApplyDraft("已放置 " + kindName + " " + cell);
        }

        /// <summary>鼠标 → 地面平面交点 → 网格坐标（越界的点直接判否，避免在台面外乱放）。</summary>
        bool TryPickCell(out GridPoint cell)
        {
            cell = default;

            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null || draft == null) return false;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;

            GridPoint candidate = GridMetrics.WorldToCell(ray.GetPoint(distance));
            if (!draft.InBounds(candidate)) return false;

            cell = candidate;
            return true;
        }

        // ---------------------------------------------------------------- 改动落地

        /// <summary>草稿 → 运行时关卡 → 重建场景（Runner 会顺手把模拟重置到 TICK 00 并暂停）。</summary>
        void ApplyDraft(string message)
        {
            statusMessage = message;
            if (draft != null) runner.ShowDefinition(draft.ToDefinition());

            ClearHighlight(); // 重建后旧的 ElementView 已经销毁，别留悬挂引用
            RefreshStatus();
        }

        void SaveDraft()
        {
            if (draft == null) return;

            bool ok = LevelDraftSerializer.Save(draft);
            statusMessage = ok ? "已保存到 " + LevelDraftSerializer.DefaultPath : "保存失败（详见 Console）";
            RefreshStatus();
        }

        void LoadDraft()
        {
            LevelDraft loaded = LevelDraftSerializer.Load();
            if (loaded == null)
            {
                statusMessage = "没读到自定义关卡：" + LevelDraftSerializer.DefaultPath;
                RefreshStatus();
                return;
            }

            draft = loaded;
            ApplyDraft("已读取自定义关卡（" + loaded.Count + " 个元件）");
        }

        // ---------------------------------------------------------------- UI

        void BuildUi()
        {
            // 自带 Canvas：和 HUD 完全解耦，HUD 那边不用为编辑模式加任何分支
            GameObject canvasGo = new GameObject("BUILD_Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 层序只认一条规则：HUD(0) 永远最上，辅助面板往下排（手牌 -1、这里 -2）。
            // BUILD MODE 只在作者模式出现，正式关卡由 LevelFlow 摘掉，和手牌不会同时抢焦点。
            canvas.sortingOrder = -2;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.transform.SetParent(transform, false);
            Transform canvasRoot = canvasGo.transform;

            // 放在底部控制条（高 92 + 下边距 26）上方，避免压住 PLAY/SPEED
            RectTransform panel = CreatePanel("BuildPanel", canvasRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(900f, 128f), new Vector2(0f, 134f));
            panelRoot = panel.gameObject;

            Text title = CreateText("Title", panel, "BUILD MODE", 22, TextAnchor.UpperLeft, AccentColor);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(220f, 26f), new Vector2(16f, -10f));

            selectionText = CreateText("Selection", panel, string.Empty, 16, TextAnchor.UpperLeft, MutedColor);
            SetRect(selectionText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(220f, 24f), new Vector2(240f, -12f));

            statusText = CreateText("Status", panel, string.Empty, 16, TextAnchor.UpperRight, TextColor);
            SetRect(statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(620f, 24f), new Vector2(-16f, -12f));

            for (int i = 0; i <= EraseIndex; i++)
            {
                int index = i; // 闭包捕获：必须复制到局部变量
                string label = i == EraseIndex ? "擦除" : PaletteNames[i];
                Button button = CreateButton("Palette_" + i, panel, label, new Vector2(118f, 38f),
                    new Vector2(16f + i * 122f, -2f), () => Select(index));

                paletteBackgrounds.Add(button.GetComponent<Image>());
                paletteLabels.Add(button.GetComponentInChildren<Text>());

                // 按钮左侧一颗色块 = 该元件在场景里的本体颜色（ghost 也是这个色），建立"颜色=类型"的直觉
                CreateSwatch(button.transform, i == EraseIndex ? BadColor : LevelBuilder.KindColor(Palette[i]));
            }

            Text hint = CreateText("Hint", panel,
                "[滚轮] 换件   [左键] 放置/拖动连铺   [右键] 删除/拖动连擦   [中键 或 R] 旋转   [F5] 保存   [F9] 读取   [B/ESC] 退出   ｜   青=1  橙=0",
                15, TextAnchor.LowerLeft, MutedColor);
            SetRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(-32f, 22f), new Vector2(16f, 8f));

            panelRoot.SetActive(false);
            RefreshPalette();
        }

        void RefreshPalette()
        {
            for (int i = 0; i < paletteBackgrounds.Count; i++)
            {
                bool selected = i == selectedIndex;
                paletteBackgrounds[i].color = selected ? new Color(0.20f, 0.45f, 0.60f, 0.95f) : ButtonColor;
                paletteLabels[i].color = selected ? AccentColor : TextColor;
            }

            if (selectionText != null)
            {
                selectionText.text = "当前：" + (selectedIndex == EraseIndex ? "擦除" : PaletteNames[selectedIndex]);
            }

            // 光标颜色跟着选件走：擦除模式用红色，一眼能看出"现在点下去是删"
            if (cursorMaterial != null)
            {
                Color color = SelectedCursorColor();
                MaterialFactory.SetBaseColor(cursorMaterial, color);
                MaterialFactory.SetEmission(cursorMaterial, color, 1.4f);
            }
        }

        /// <summary>ghost 光标颜色 = 即将放下的元件颜色；擦除模式是红色，一眼看出"现在点下去是删"。</summary>
        Color SelectedCursorColor()
        {
            return selectedIndex == EraseIndex ? BadColor : LevelBuilder.KindColor(Palette[selectedIndex]);
        }

        void CreateCursor()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BuildCursor";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

            cursorMaterial = new Material(MaterialFactory.LitShader) { name = "M_BuildCursor" };
            Color color = SelectedCursorColor();
            MaterialFactory.SetBaseColor(cursorMaterial, color);
            MaterialFactory.SetEmission(cursorMaterial, color, 1.4f);
            go.GetComponent<Renderer>().sharedMaterial = cursorMaterial;

            cursor = go.transform;
            go.SetActive(false);
        }

        /// <summary>调色板按钮左侧的小色块：颜色与场景里元件本体一致，帮助玩家把颜色和元件对应起来。</summary>
        static void CreateSwatch(Transform parent, Color color)
        {
            GameObject go = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            SetRect(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 22f), new Vector2(7f, 0f));

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        void ClearHighlight()
        {
            if (highlightedView != null) highlightedView.SetHighlighted(false);
            highlightedView = null;
        }

        void Select(int index)
        {
            selectedIndex = Mathf.Clamp(index, 0, EraseIndex);
            RefreshPalette();
            RefreshStatus();
        }

        void RefreshStatus()
        {
            if (statusText == null) return;

            if (!editing || draft == null)
            {
                statusText.text = "按 B 进入编辑模式";
                return;
            }

            statusText.text = "元件 " + draft.Count
                + " ｜发 " + draft.CountOf(CellKind.Emitter)
                + " ｜收 " + draft.CountOf(CellKind.Receiver)
                + " ｜分 " + draft.CountOf(CellKind.Splitter)
                + " ｜非 " + draft.CountOf(CellKind.NotGate)
                + " ｜滤 " + draft.CountOf(CellKind.Filter)
                + " ｜墙 " + draft.CountOf(CellKind.Wall)
                + (string.IsNullOrEmpty(statusMessage) ? string.Empty : "\n" + statusMessage);
        }

        // ---------------------------------------------------------------- 控件工厂（与 HUD 同一套写法）

        RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            SetRect(rect, anchorMin, anchorMax, pivot, size, position);

            Image image = go.GetComponent<Image>();
            image.color = PanelColor;
            image.raycastTarget = false;
            return rect;
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        Button CreateButton(string name, Transform parent, string label, Vector2 size, Vector2 position,
            UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            SetRect(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), size, position);

            Image image = go.GetComponent<Image>();
            image.color = ButtonColor;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", rect, label, 18, TextAnchor.MiddleCenter, TextColor);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
        }
    }
}
