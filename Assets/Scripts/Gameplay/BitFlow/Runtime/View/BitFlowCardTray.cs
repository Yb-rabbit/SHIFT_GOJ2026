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
    /// 玩家手牌盘：把 <see cref="LevelSO.supplies"/> 变成一排有限张数的卡，点卡 → 点格子放下。
    ///
    /// 和 BUILD MODE 的关系：两者都改 <see cref="LevelDraft"/>，但 BUILD MODE 是作者模式（无限涂刷），
    /// 这里是玩家模式（有限手牌 + 预设件锁定 + 撤销）。正式关卡由 <see cref="LevelFlow"/> 摘掉 BUILD MODE，
    /// 所以同一份草稿不会被动两遍。
    ///
    /// 交互（已确认的方案 A）：放置不消耗「一次性操作」，放下后可以再左键点它拾回、移动、旋转；
    /// 手牌张数是硬约束，压力来自牌不够，而不是怕点错 —— 点错就 Ctrl+Z。
    /// 左键 = 放卡/拾回 ｜ 右键 = 拾回 ｜ R = 转待放朝向或转鼠标下的件 ｜ Ctrl+Z = 撤销 ｜ ESC = 放下手里的卡。
    /// </summary>
    public sealed class BitFlowCardTray : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.055f, 0.07f, 0.10f, 0.92f);
        static readonly Color TextColor = new Color(0.85f, 0.92f, 1f);
        static readonly Color AccentColor = new Color(0.35f, 0.85f, 1f);
        static readonly Color MutedColor = new Color(0.55f, 0.62f, 0.72f);
        static readonly Color BadColor = new Color(1f, 0.32f, 0.35f);
        static readonly Color ButtonColor = new Color(0.12f, 0.20f, 0.28f, 0.95f);
        static readonly Color SelectedCardColor = new Color(0.20f, 0.45f, 0.60f, 0.95f);
        static readonly Color EmptyCardColor = new Color(0.09f, 0.11f, 0.14f, 0.75f);

        const float CardWidth = 148f;
        const float CardHeight = 62f;
        const float CardGap = 10f;
        const float PanelHeight = 132f;
        const float CursorHeight = 0.025f;

        /// <summary>一张卡 = 一摞牌。Count 显示的是「还没放下的张数」。</summary>
        sealed class CardEntry
        {
            public ElementTypeSO Type;
            public string Key;
            public CellKind Kind;
            public GameObject Root;
            public Image Background;
            public Text CountLabel;
        }

        BitFlowRunner runner;
        LevelFlow flow;
        Camera mainCamera;
        Font font;

        LevelSO level;
        LevelDraft draft;

        ElementTypeSO armedType;
        CellKind armedKind;
        int armedRotation;
        string statusMessage = string.Empty;

        GameObject panelRoot;
        Text titleText;
        Text hintText;
        Text statusText;
        readonly List<CardEntry> cards = new List<CardEntry>();

        Transform cursor;
        Material cursorMaterial;

        void Start()
        {
            runner = FindObjectOfType<BitFlowRunner>();
            flow = FindObjectOfType<LevelFlow>();
            mainCamera = Camera.main;
            font = LoadFont();

            if (runner == null)
            {
                Debug.LogWarning("[BitFlow] 没找到 BitFlowRunner，手牌盘关闭");
                enabled = false;
                return;
            }

            EnsureEventSystem();
            BuildUi();

            if (flow != null)
            {
                flow.LevelLoaded += OnLevelLoaded;

                // LevelFlow 的 Start 比这里早（执行顺序 -10），第一次 LevelLoaded 已经错过了，
                // 所以这里补一次；之后都靠事件。
                if (flow.Current != null) OnLevelLoaded(flow.Current);
            }
            else
            {
                OnLevelLoaded(runner.LevelAsset);
            }
        }

        void OnDestroy()
        {
            if (flow != null) flow.LevelLoaded -= OnLevelLoaded;
            if (cursorMaterial != null) Destroy(cursorMaterial);
        }

        static Font LoadFont()
        {
            Font loaded = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (loaded == null) loaded = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return loaded;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        /// <summary>换关：按新关卡重新生成草稿与卡面（预设件、手牌、撤销栈全部重来）。</summary>
        void OnLevelLoaded(LevelSO next)
        {
            level = next;
            armedType = null;
            armedRotation = 0;
            statusMessage = string.Empty;

            draft = level != null ? level.CreatePlayerDraft() : null;

            if (panelRoot != null) panelRoot.SetActive(draft != null && level.HasSupplies);
            if (draft != null) RebuildCards();

            RefreshStatus();
            RefreshCards();
            RefreshCursorColor();
        }

        void Update()
        {
            if (draft == null) return;
            if (runner == null || runner.IsBuildModeEnabled) return; // 作者模式接管时不抢输入

            HandleHotkeys();
            UpdateCursor();
            HandleMouse();
        }

        void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && armedType != null)
            {
                armedType = null;
                statusMessage = "已放下手里的卡";
                RefreshAll();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                 Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)))
            {
                if (draft.Undo()) Apply("已撤销一步");
                else
                {
                    statusMessage = "没有可撤销的操作了";
                    RefreshAll();
                }
                return;
            }

            if (!Input.GetKeyDown(KeyCode.R)) return;

            // 手里有牌时 R 转「待放朝向」，否则转鼠标下的元件
            if (armedType != null)
            {
                armedRotation = (armedRotation + 1) % 4;
                statusMessage = "待放朝向：" + RotationName(armedRotation);
                RefreshAll();
                return;
            }

            if (TryPickCell(out GridPoint cell))
            {
                if (draft.IsLocked(cell))
                {
                    statusMessage = "这是关卡给定的元件，不能旋转";
                    RefreshAll();
                }
                else if (draft.Rotate(cell)) Apply("已旋转 " + cell);
            }
        }

        static string RotationName(int steps)
        {
            switch (((steps % 4) + 4) % 4)
            {
                case 1: return "顺时针 90°";
                case 2: return "180°";
                case 3: return "顺时针 270°";
                default: return "原始朝向";
            }
        }

        // ---------------------------------------------------------------- 鼠标

        void UpdateCursor()
        {
            if (cursor == null) CreateCursor();

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi || !TryPickCell(out GridPoint cell))
            {
                if (cursor.gameObject.activeSelf) cursor.gameObject.SetActive(false);
                return;
            }

            cursor.gameObject.SetActive(true);
            cursor.position = GridMetrics.CellToWorld(cell) + Vector3.up * CursorHeight;
        }

        void HandleMouse()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!TryPickCell(out GridPoint cell)) return;

            if (Input.GetMouseButtonDown(1))
            {
                PickBack(cell);
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;

            if (armedType != null) TryPlaceArmed(cell);
            else PickBack(cell); // 手里没牌：左键点自己的元件 = 拿起来（方案 A：可自由搬动）
        }

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

        // ---------------------------------------------------------------- 放置 / 拾回

        void TryPlaceArmed(GridPoint cell)
        {
            EditableElement element = armedType.CreateEditable(cell, armedRotation);
            if (element == null)
            {
                Fail(LabelOf(armedType) + " 没有可用的配置，放不下去");
                return;
            }

            PlaceResult result = draft.TryPlace(element);

            switch (result)
            {
                case PlaceResult.Ok:
                    string placed = LabelOf(armedType) + " " + cell;
                    if (draft.Remaining(armedType.Key) <= 0) armedType = null; // 这摞用完了
                    Apply("已放下 " + placed);
                    break;

                case PlaceResult.Locked:
                    Fail("这格是关卡给定的元件，不能覆盖");
                    break;

                case PlaceResult.Occupied:
                    Fail("这格已经有元件了（先左键点它拾回）");
                    break;

                case PlaceResult.OutOfStock:
                    Fail(LabelOf(armedType) + " 已经用完了");
                    break;

                default:
                    Fail("点到网格外面了");
                    break;
            }
        }

        void PickBack(GridPoint cell)
        {
            if (draft.IsLocked(cell))
            {
                Fail("这是关卡给定的元件，不能拾回");
                return;
            }

            if (!draft.TryPick(cell, out EditableElement picked))
            {
                Fail("这格是空的");
                return;
            }

            // 拾回的牌直接拿在手里，玩家可以接着投到别处 —— 不用再去点卡面
            armedType = FindSupplyType(picked.TypeKey);
            armedKind = picked.Kind;
            armedRotation = 0;

            Apply(armedType != null
                ? "已拾回 " + LabelOf(armedType)
                : "已移除 " + picked.Kind + "（它不属于任何一摞牌）");
        }

        ElementTypeSO FindSupplyType(string typeKey)
        {
            if (level == null || level.supplies == null || string.IsNullOrEmpty(typeKey)) return null;

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                if (supply.type.Key == typeKey) return supply.type;
            }
            return null;
        }

        static string LabelOf(ElementTypeSO type)
        {
            if (type == null) return "元件";
            return string.IsNullOrEmpty(type.label) ? type.Key : type.label;
        }

        void Arm(ElementTypeSO type)
        {
            if (type == null || draft == null) return;

            if (draft.Remaining(type.Key) <= 0)
            {
                Fail(LabelOf(type) + " 已经用完了");
                return;
            }

            armedType = armedType == type ? null : type; // 再点一次同一张卡 = 取消
            armedRotation = 0;
            if (armedType != null)
            {
                // 预览色要用元件种类：直接从类型资产生成一份草稿读 Kind，跟真正放下时走同一条路径
                EditableElement preview = armedType.CreateEditable(new GridPoint(0, 0));
                armedKind = preview != null ? preview.Kind : CellKind.Wall;
                statusMessage = "已选中 " + LabelOf(armedType) + "：点格子放下";
            }
            else
            {
                statusMessage = "已放下手里的卡";
            }

            RefreshAll();
        }

        // ---------------------------------------------------------------- 落地

        /// <summary>草稿 → 运行时关卡 → 重建场景（Runner 会顺手重置到 TICK 00 并暂停）。</summary>
        void Apply(string message)
        {
            statusMessage = message;
            if (draft != null) runner.ShowDefinition(draft.ToDefinition());
            RefreshAll();
        }

        void Fail(string message)
        {
            statusMessage = message;
            RefreshAll();
        }

        void RefreshAll()
        {
            RefreshStatus();
            RefreshCards();
            RefreshCursorColor();
        }

        // ---------------------------------------------------------------- UI

        void BuildUi()
        {
            GameObject canvasGo = new GameObject("TRAY_Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9; // 在 BUILD MODE(10) 之下，避免同时存在时抢焦点

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.transform.SetParent(transform, false);
            Transform canvasRoot = canvasGo.transform;

            RectTransform panel = CreatePanel("TrayPanel", canvasRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(620f, PanelHeight), new Vector2(0f, 134f));
            panelRoot = panel.gameObject;

            titleText = CreateText("Title", panel, "手牌", 22, TextAnchor.UpperLeft, AccentColor);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(200f, 26f), new Vector2(16f, -10f));

            statusText = CreateText("Status", panel, string.Empty, 16, TextAnchor.UpperRight, TextColor);
            SetRect(statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(360f, 44f), new Vector2(-16f, -10f));

            hintText = CreateText("Hint", panel,
                "[左键] 点卡 → 点格子放下 / 点自己的元件拾回   [右键] 拾回   [R] 旋转   [Ctrl+Z] 撤销   [ESC] 放下手里的卡",
                15, TextAnchor.LowerLeft, MutedColor);
            SetRect(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(-32f, 22f), new Vector2(16f, 8f));

            panelRoot.SetActive(false);
        }

        void RebuildCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root != null) Destroy(entry.Root);
            }
            cards.Clear();

            if (level == null || level.supplies == null || panelRoot == null) return;

            float x = 16f;
            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                cards.Add(CreateCard(supply.type, new Vector2(x, 32f)));
                x += CardWidth + CardGap;
            }

            RectTransform panel = (RectTransform)panelRoot.transform;
            panel.sizeDelta = new Vector2(Mathf.Max(620f, x + 16f), PanelHeight);
        }

        CardEntry CreateCard(ElementTypeSO type, Vector2 position)
        {
            GameObject go = new GameObject("Card_" + type.Key, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panelRoot.transform, false);

            RectTransform rect = (RectTransform)go.transform;
            SetRect(rect, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(CardWidth, CardHeight), position);

            Image image = go.GetComponent<Image>();
            image.color = ButtonColor;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            ElementTypeSO captured = type;
            button.onClick.AddListener(() => Arm(captured));

            Text name = CreateText("Name", rect, LabelOf(type), 17, TextAnchor.UpperLeft, TextColor);
            SetRect(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-20f, -8f), new Vector2(0f, -6f));

            Text count = CreateText("Count", rect, string.Empty, 24, TextAnchor.LowerRight, AccentColor);
            SetRect(count.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(80f, 30f), new Vector2(-10f, 6f));

            return new CardEntry
            {
                Type = type,
                Key = type.Key,
                Background = image,
                CountLabel = count,
                Root = go,
            };
        }

        void RefreshCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root == null) continue;

                int left = draft != null ? draft.Remaining(entry.Key) : 0;
                bool selected = armedType == entry.Type;
                bool empty = left <= 0;

                entry.Background.color = selected ? SelectedCardColor : (empty ? EmptyCardColor : ButtonColor);
                entry.CountLabel.text = "×" + Mathf.Max(0, left);
                entry.CountLabel.color = empty ? MutedColor : (selected ? AccentColor : TextColor);
            }
        }

        void RefreshStatus()
        {
            if (statusText == null) return;

            if (draft == null)
            {
                statusText.text = "本关没有给玩家手牌";
            }
            else if (!draft.StockEnabled)
            {
                statusText.text = "本关没有给玩家手牌（这是演示关）";
            }
            else
            {
                string armed = armedType != null ? "\n手里：" + LabelOf(armedType) : "\n手里：空";
                statusText.text = "已放 " + draft.Count + " 件" + armed
                    + (string.IsNullOrEmpty(statusMessage) ? string.Empty : "\n" + statusMessage);
            }

            if (titleText != null && level != null)
            {
                titleText.text = "手牌 ｜ " + (string.IsNullOrEmpty(level.displayName) ? level.name : level.displayName);
            }
        }

        void RefreshCursorColor()
        {
            if (cursorMaterial == null) return;

            Color color = armedType != null ? LevelBuilder.KindColor(armedKind) : MutedColor;
            MaterialFactory.SetBaseColor(cursorMaterial, color);
            MaterialFactory.SetEmission(cursorMaterial, color, 1.4f);
        }

        void CreateCursor()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TrayCursor";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

            cursorMaterial = new Material(MaterialFactory.LitShader) { name = "M_TrayCursor" };
            Color color = armedType != null ? LevelBuilder.KindColor(armedKind) : MutedColor;
            MaterialFactory.SetBaseColor(cursorMaterial, color);
            MaterialFactory.SetEmission(cursorMaterial, color, 1.4f);
            go.GetComponent<Renderer>().sharedMaterial = cursorMaterial;

            cursor = go.transform;
            go.SetActive(false);
        }

        // ---------------------------------------------------------------- 控件工厂（与 HUD / BUILD MODE 同一套写法）

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
    }
}
