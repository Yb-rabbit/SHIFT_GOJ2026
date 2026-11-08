using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 选关面板：把所有关卡列成一列按钮，通关的打勾，当前这关高亮。
    ///
    /// 三个约束：
    /// 1. 面板自己一套 Canvas（sortingOrder 40），盖在黑幕（30）和 HUD（0）之上 ——
    ///    结算面板里的 LEVELS 按钮就是在"HUD 之上再盖一层"，不能和 HUD 共用 Canvas。
    /// 2. 打开期间用 <see cref="InputGate"/> 挡住手牌盘：玩家在挑关卡，不该同时在摆元件。
    /// 3. 切关顺序是<b>先盖黑幕、再放自己的锁、最后才 Load</b>。
    ///    锁是引用计数，18 帧里任何一刻都不会出现"两道锁都放了、黑幕还没盖住"的缝。
    /// </summary>
    public sealed class LevelSelectPanel : MonoBehaviour
    {
        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);
        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.09f, 0.97f);
        static readonly Color TitleColor = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color MutedColor = new Color(0.62f, 0.72f, 0.80f, 1f);
        static readonly Color RowColor = new Color(0.10f, 0.14f, 0.18f, 1f);
        static readonly Color RowCurrentColor = new Color(0.14f, 0.26f, 0.32f, 1f);
        static readonly Color ClearedColor = new Color(0.45f, 0.86f, 0.62f, 1f);

        const float RowHeight = 56f;
        const float RowGap = 10f;

        LevelFlow flow;
        IntroCurtain curtain;
        Font font;

        GameObject canvasObject;
        RectTransform listRoot;
        Text emptyText;

        bool open;

        void Awake()
        {
            BuildUi();
            canvasObject.SetActive(false);
        }

        void Start()
        {
            flow = FindObjectOfType<LevelFlow>();
            curtain = FindObjectOfType<IntroCurtain>();

            if (flow == null) Debug.LogWarning("[BitFlow] 没找到 LevelFlow，选关面板不会列出任何关卡");
        }

        void Update()
        {
            // 只处理 ESC 关面板。其余按键全部交给 InputGate 挡掉的那套逻辑。
            if (open && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void OnDestroy()
        {
            if (open) InputGate.Release();
        }

        /// <summary>打开面板。重复打开是空操作（避免引用计数被叠两次）。</summary>
        public void Open()
        {
            if (open) return;

            open = true;
            InputGate.Acquire();
            canvasObject.SetActive(true);
            Rebuild();
        }

        public void Close()
        {
            if (!open) return;

            open = false;
            canvasObject.SetActive(false);
            InputGate.Release();
        }

        /// <summary>通关后 HUD 会调它，让打勾立刻反映出来（面板没开就什么都不用做）。</summary>
        public void Refresh()
        {
            if (open) Rebuild();
        }

        // ---------------------------------------------------------------- 列表

        void Rebuild()
        {
            for (int i = listRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(listRoot.GetChild(i).gameObject);
            }

            LevelCatalogSO catalog = flow != null ? flow.Catalog : null;
            if (catalog == null || catalog.Count == 0)
            {
                emptyText.gameObject.SetActive(true);
                listRoot.gameObject.SetActive(false);
                return;
            }

            emptyText.gameObject.SetActive(false);
            listRoot.gameObject.SetActive(true);

            int count = catalog.Count;
            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, count * (RowHeight + RowGap));

            string currentName = flow.Current != null ? flow.Current.displayName : null;

            for (int i = 0; i < count; i++)
            {
                LevelSO level = catalog.Get(i);
                if (level == null) continue;

                bool isCurrent = !string.IsNullOrEmpty(currentName) && level.displayName == currentName;
                CreateRow(i, level, isCurrent);
            }
        }

        void CreateRow(int index, LevelSO level, bool isCurrent)
        {
            // 列表本身是自上而下排的，所以第 0 关放在最上面
            float y = -index * (RowHeight + RowGap);

            GameObject rowObject = new GameObject("Level" + index, typeof(RectTransform), typeof(Image), typeof(Button));
            rowObject.transform.SetParent(listRoot, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            SetRect(rowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, RowHeight), new Vector2(0f, y));

            Image background = rowObject.GetComponent<Image>();
            background.color = isCurrent ? RowCurrentColor : RowColor;

            Button button = rowObject.GetComponent<Button>();
            button.targetGraphic = background;

            int captured = index;                 // 闭包必须拿局部副本：直接捕获 index 会全部指向最后一个
            button.onClick.AddListener(() => Select(captured));

            string label = string.IsNullOrEmpty(level.displayName)
                ? "LEVEL " + (index + 1).ToString("00")
                : level.displayName;

            Text title = CreateText("Title", rowRect, label, 22,
                TextAnchor.MiddleLeft, isCurrent ? TitleColor : MutedColor);
            SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(430f, RowHeight - 8f), new Vector2(22f, 0f));

            string mark = SaveStore.IsCleared(level.displayName) ? "✓" : string.Empty;
            if (mark.Length > 0)
            {
                Text cleared = CreateText("Cleared", rowRect, mark, 24, TextAnchor.MiddleCenter, ClearedColor);
                SetRect(cleared.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(40f, RowHeight - 8f), new Vector2(-22f, 0f));
            }

            if (isCurrent)
            {
                Text now = CreateText("Now", rowRect, "NOW  ", 16, TextAnchor.MiddleRight, MutedColor);
                SetRect(now.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(80f, RowHeight - 8f), new Vector2(-60f, 0f));
            }
        }

        /// <summary>
        /// 选中某一关。顺序很讲究：<b>先</b>让黑幕盖住并拿到输入锁，<b>再</b>关掉面板放锁 ——
        /// 引用计数从 2 降到 1，全程都是锁着的，不会闪一帧让玩家点到正在拆的关卡。
        /// </summary>
        void Select(int index)
        {
            if (flow == null) return;

            LevelCatalogSO catalog = flow.Catalog;
            if (catalog == null || index < 0 || index >= catalog.Count) return;

            if (curtain != null) curtain.CoverImmediately();
            Close();

            flow.Load(index);
        }


        // ---------------------------------------------------------------- 搭建

        void BuildUi()
        {
            font = LoadFont();

            canvasObject = new GameObject("LevelSelectCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;      // 压住黑幕（30）与 HUD（0）

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            // 遮罩本身可点：点面板外面一下就当取消（这一层也顺手把世界点击挡住）
            GameObject dimObject = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dimObject.transform.SetParent(canvasRect, false);

            RectTransform dimRect = dimObject.GetComponent<RectTransform>();
            SetRect(dimRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Image dimImage = dimObject.GetComponent<Image>();
            dimImage.color = DimColor;

            Button dimButton = dimObject.GetComponent<Button>();
            dimButton.targetGraphic = dimImage;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Close);

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasRect, false);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(700f, 560f), Vector2.zero);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;

            Text title = CreateText("Title", panelRect, "SELECT LEVEL", 34, TextAnchor.MiddleCenter, TitleColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 44f), new Vector2(0f, -46f));

            GameObject listObject = new GameObject("List", typeof(RectTransform));
            listObject.transform.SetParent(panelRect, false);
            listRoot = listObject.GetComponent<RectTransform>();
            SetRect(listRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 0f), new Vector2(0f, -96f));

            emptyText = CreateText("Empty", panelRect, "NO LEVEL CATALOG IN THIS SCENE", 22,
                TextAnchor.MiddleCenter, MutedColor);
            SetRect(emptyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(600f, 40f), Vector2.zero);

            Button close = CreateButton("Close", panelRect, "CLOSE", new Vector2(160f, 44f), new Vector2(0f, 30f), Close);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(160f, 44f), new Vector2(0f, 32f));
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

            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), size, position);

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.20f, 0.28f, 0.95f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", go.transform, label, 20, TextAnchor.MiddleCenter, TitleColor);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
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

        static Font LoadFont()
        {
            Font loaded = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (loaded == null) loaded = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return loaded;
        }
    }
}
