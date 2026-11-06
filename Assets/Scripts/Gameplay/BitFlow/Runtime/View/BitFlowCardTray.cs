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

        // 卡面是 500×500 的方形美术图（图里已经画好图标、名字、副标题，底部留了一条浅灰槽），
        // 所以卡做成正方形、整张图铺满，张数写进那条预留槽里，不再叠第二份名字。
        //
        // 尺寸是从"不许挡住格子"倒推的：带子底边 26 + 高 112 = 顶边 138，
        // 而 9×8 关卡最前面一排格子在 FOV 42 下落在屏幕约 182px 处，还留 44px 余量。
        // 以后要加内容请往左右扩，不要再往高里叠 —— 一叠就吃格子。
        const float CardSize = 104f;
        const float CardGap = 12f;

        /// <summary>卡片贴带子底边；上面那点空位是留给选中外框探出去的 4px。</summary>
        const float CardRowY = 4f;

        const float CursorHeight = 0.025f;

        /// <summary>
        /// 卡面底部那条浅灰槽的位置（相对卡面的 0~1 比例，从 500×500 原图量出来的）：
        /// 左右各留 10%，竖直方向从 7.5% 到 32.5%。张数就写在这里。
        /// </summary>
        static readonly Vector2 SlotAnchorMin = new Vector2(0.105f, 0.075f);
        static readonly Vector2 SlotAnchorMax = new Vector2(0.895f, 0.325f);

        /// <summary>槽是浅灰底，字要用深色才看得清（这是"读得清"的关键，不是配色偏好）。</summary>
        static readonly Color SlotTextColor = new Color(0.09f, 0.12f, 0.20f);
        static readonly Color SlotTextEmptyColor = new Color(0.42f, 0.45f, 0.52f);

        /// <summary>一张卡 = 一摞牌。Count 显示的是「还没放下的张数」。</summary>
        sealed class CardEntry
        {
            public ElementTypeSO Type;
            public string Key;
            public CellKind Kind;
            public GameObject Root;

            /// <summary>卡面贴图（有美术时才有）；没有美术时退回 <see cref="Background"/> 纯色卡。</summary>
            public Image Art;

            /// <summary>卡的底色（没有卡面时它才是可见的卡面，有卡面时只用它当点击热区）。</summary>
            public Image Background;

            /// <summary>选中时露出的外框（卡面不透明，不能靠改底色表示选中）。</summary>
            public Image SelectionFrame;

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
            // 层序只有一条规则，三个 Canvas 都按它来：HUD 的是 0 且必须最上 ——
            // 结算面板和悬停提示都住在 HUD 里，被手牌盘盖住就是"过关了看不见结算"。
            // 所以辅助层一律往下排：手牌 -1、作者模式 -2，永远不往上叠。
            canvas.sortingOrder = -1;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.transform.SetParent(transform, false);
            Transform canvasRoot = canvasGo.transform;

            // 底部只有一条带子：本面板从左边距一直铺到 HUD 控制条的左边缘，
            // 同底边、同高度、同一种底色（PanelColor 与 HUD 完全一致），于是两者连成"一条"。
            // 这就是全部改动 —— 手牌盘不再单独占一层往上叠，屏幕下部只吃掉 138px，格子全露出来。
            float insetL = BitFlowHud.BottomMargin;
            float insetR = BitFlowHud.BottomMargin + BitFlowHud.BottomBarWidth;
            RectTransform panel = CreatePanel("TrayPanel", canvasRoot,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-(insetL + insetR), BitFlowHud.BottomBandHeight),
                new Vector2((insetL - insetR) * 0.5f, BitFlowHud.BottomBandBottom));
            panelRoot = panel.gameObject;

            // 只留一行反馈（已放几件 / 为什么放不下），贴带子右端、控制条左侧；关卡名交给 HUD 左上角，
            // 操作说明整行删掉：带子只有 112px 高，那一行才是真正把格子挤掉的东西。
            statusText = CreateText("Status", panel, string.Empty, 15, TextAnchor.MiddleRight, TextColor);
            SetRect(statusText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(340f, 26f), new Vector2(-16f, 0f));

            panelRoot.SetActive(false);
        }

        void RebuildCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root != null) Destroy(entry.Root);
                if (entry.SelectionFrame != null) Destroy(entry.SelectionFrame.gameObject);
            }
            cards.Clear();

            if (level == null || level.supplies == null || panelRoot == null) return;

            float x = 16f;
            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                cards.Add(CreateCard(supply.type, new Vector2(x, CardRowY)));
                x += CardSize + CardGap;
            }

            // 面板宽度由屏幕决定（左右拉伸到 HUD 控制条），卡片从左边排过来，这里不用再改尺寸
        }

        CardEntry CreateCard(ElementTypeSO type, Vector2 position)
        {
            RectTransform panelRect = (RectTransform)panelRoot.transform;

            // 选中框先建：比卡大 8px，卡面不透明，于是只有四周那 4px 的边框会露出来。
            // 不能用"改底色"表示选中 —— 那会直接盖住卡面美术。
            Image frame = CreateImage("SelectionFrame_" + type.Key, panelRect, AccentColor);
            SetRect((RectTransform)frame.transform, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(CardSize + 8f, CardSize + 8f), position + new Vector2(-4f, -4f));
            frame.gameObject.SetActive(false);

            GameObject go = new GameObject("Card_" + type.Key, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panelRect, false);

            RectTransform rect = (RectTransform)go.transform;
            SetRect(rect, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(CardSize, CardSize), position);

            Image background = go.GetComponent<Image>();
            background.color = ButtonColor;
            background.raycastTarget = true; // 整张卡（含透明区）都是点击热区

            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None; // 颜色全部由 RefreshCards 管，别被 ColorTint 冲掉

            ElementTypeSO captured = type;
            button.onClick.AddListener(() => Arm(captured));

            Sprite art = CardArtLibrary.Icon(KindOf(type));
            Image artImage = null;
            Text count;

            if (art != null)
            {
                // 有卡面：整张卡就是美术画的那张图（图标、名字、副标题都在图里），这里只补张数；
                // 底色退成透明，但仍作为点击热区存在。
                background.color = new Color(1f, 1f, 1f, 0f);

                artImage = CreateImage("Art", rect, Color.white);
                artImage.sprite = art;
                artImage.preserveAspect = true;
                artImage.raycastTarget = false;
                SetRect((RectTransform)artImage.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);

                // 张数写进卡面下方预留的那条浅灰槽；槽是浅色底，字必须是深色才看得清
                // 字号随卡尺寸走：卡 104 → 槽高 26px，22 号字正好填满又不顶格
                count = CreateText("Count", rect, string.Empty, 22, TextAnchor.MiddleCenter, SlotTextColor);
                SetRect(count.rectTransform, SlotAnchorMin, SlotAnchorMax, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
            }
            else
            {
                // 没有卡面：退回"纯色卡 + 居中名字 + 右下张数"，功能一样不缺
                Text name = CreateText("Name", rect, LabelOf(type), 17, TextAnchor.MiddleCenter, TextColor);
                SetRect(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(-16f, -26f), new Vector2(0f, 6f));

                count = CreateText("Count", rect, string.Empty, 22, TextAnchor.LowerCenter, TextColor);
                SetRect(count.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(-16f, 26f), new Vector2(0f, 8f));
            }

            return new CardEntry
            {
                Type = type,
                Key = type.Key,
                Art = artImage,
                Background = background,
                SelectionFrame = frame,
                CountLabel = count,
                Root = go,
            };
        }

        /// <summary>
        /// 这张牌对应哪种元件（决定卡面图）。交给 <see cref="ElementTypeSO"/> 自己判定，
        /// 手牌盘不重复一套"渲染器类型 → CellKind"的映射，免得以后加元件时两边对不上。
        /// </summary>
        static CellKind KindOf(ElementTypeSO type)
        {
            EditableElement preview = type.CreateEditable(new GridPoint(0, 0));
            return preview != null ? preview.Kind : CellKind.Wall;
        }

        void RefreshCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root == null) continue;

                int left = draft != null ? draft.Remaining(entry.Key) : 0;
                bool selected = armedType == entry.Type;
                bool empty = left <= 0;

                // 选中态走外框：卡面是不透明美术图，改底色会把它盖住
                if (entry.SelectionFrame != null) entry.SelectionFrame.gameObject.SetActive(selected);

                entry.CountLabel.text = "×" + Mathf.Max(0, left);
                entry.CountLabel.color = empty ? SlotTextEmptyColor : SlotTextColor;

                if (entry.Art != null)
                {
                    // 这摞用完了：卡面整体压暗（名字仍然看得清），扫一眼就知道哪张还能点
                    entry.Art.color = empty ? new Color(0.42f, 0.42f, 0.42f, 0.85f) : Color.white;
                }
                else
                {
                    entry.Background.color = selected ? SelectedCardColor : (empty ? EmptyCardColor : ButtonColor);
                }
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
                statusText.text = "演示关：没有手牌";
            }
            else
            {
                // 只留"已放几件" + 最后一条反馈；手里拿着哪张由卡片的选中外框表达，不再写一行文字
                string summary = "已放 " + draft.Count + " 件";
                statusText.text = string.IsNullOrEmpty(statusMessage) ? summary : summary + " ｜ " + statusMessage;
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

        /// <summary>一张纯色 UI 图（卡面 / 选中外框都用它，省得每处重复 new GameObject）。</summary>
        static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
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
