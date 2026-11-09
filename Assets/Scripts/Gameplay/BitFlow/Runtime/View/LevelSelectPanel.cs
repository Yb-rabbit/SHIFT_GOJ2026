using System.Collections.Generic;
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
        const float CaptionHeight = 26f;
        const float HintHeight = 22f;

        LevelFlow flow;
        IntroCurtain curtain;
        Font font;

        GameObject canvasObject;
        RectTransform listRoot;
        Text emptyText;

        bool open;

        // 每次 Open 重扫一遍目录：玩家/AI 直接改 JSON 文件，重开面板就能看到最新内容
        readonly List<LevelJsonEntry> jsonEntries = new List<LevelJsonEntry>();

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

            jsonEntries.Clear();

            LevelCatalogSO catalog = flow != null ? flow.Catalog : null;
            int catalogCount = catalog != null ? catalog.Count : 0;

            // 场上没有 LevelFlow 就不去扫目录：这种面板点了也进不了关
            List<string> jsonProblems = null;
            if (flow != null)
            {
                jsonEntries.AddRange(LevelJsonLibrary.Scan(out jsonProblems));
                foreach (string problem in jsonProblems) Debug.LogWarning("[BitFlow] 关卡 JSON：" + problem);
            }

            bool hasJsonSection = jsonEntries.Count > 0 || (jsonProblems != null && jsonProblems.Count > 0);
            if (catalogCount == 0 && !hasJsonSection)
            {
                emptyText.gameObject.SetActive(true);
                listRoot.gameObject.SetActive(false);
                return;
            }

            emptyText.gameObject.SetActive(false);
            listRoot.gameObject.SetActive(true);

            string currentName = flow != null && flow.Current != null ? flow.Current.displayName : null;
            string currentJsonPath = flow != null ? flow.CurrentJsonPath : null;

            float y = 0f;
            int row = 0;

            for (int i = 0; i < catalogCount; i++)
            {
                LevelSO level = catalog.Get(i);
                if (level == null) continue;

                bool isCurrent = string.IsNullOrEmpty(currentJsonPath) && !string.IsNullOrEmpty(currentName)
                    && level.displayName == currentName;

                int captured = i;
                CreateRow(row++, level, null, isCurrent, () => Select(captured), ref y);
            }

            if (hasJsonSection)
            {
                CreateCaption("CUSTOM  ·  LEVEL JSON", ref y);

                foreach (LevelJsonEntry entry in jsonEntries)
                {
                    LevelJsonEntry captured = entry;
                    bool isCurrent = currentJsonPath == entry.Path;
                    CreateRow(row++, null, captured, isCurrent, () => SelectJson(captured), ref y);
                }

                if (jsonProblems != null && jsonProblems.Count > 0)
                {
                    // 像素字体没有中文字形：面板上只说「有错、看日志」，细节都在 Console
                    CreateHint("JSON ERROR  ·  SEE CONSOLE", ref y);
                }
            }

            // listRoot 自上而下排：y 一路走成负数，高度取它的绝对值
            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));
        }

        /// <summary>
        /// 画一行可点的关卡。level 与 entry 二选一：
        /// 前者是关卡表里的资产，后者是运行时从 JSON 读进来的关卡。
        /// </summary>
        void CreateRow(int row, LevelSO level, LevelJsonEntry entry, bool isCurrent, UnityAction onClick, ref float y)
        {
            GameObject rowObject = new GameObject("Row" + row, typeof(RectTransform), typeof(Image), typeof(Button));
            rowObject.transform.SetParent(listRoot, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            SetRect(rowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, RowHeight), new Vector2(0f, y));
            y -= RowHeight + RowGap;

            Image background = rowObject.GetComponent<Image>();
            background.color = isCurrent ? RowCurrentColor : RowColor;

            Button button = rowObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            string displayName = level != null ? level.displayName : entry.DisplayName;
            string subtitle = entry != null ? "JSON  ·  " + entry.Subtitle : string.Empty;

            string label = string.IsNullOrEmpty(displayName)
                ? "LEVEL " + (row + 1).ToString("00")
                : displayName;

            Text title = CreateText("Title", rowRect, label, PixelFont.Body,
                TextAnchor.MiddleLeft, isCurrent ? TitleColor : MutedColor);

            if (string.IsNullOrEmpty(subtitle))
            {
                SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(430f, RowHeight - 8f), new Vector2(22f, 0f));
            }
            else
            {
                // 两行：上面是关卡名，下面是「从哪个文件读来的」，作者一眼知道该改哪份
                SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(430f, 24f), new Vector2(22f, -8f));

                Text detail = CreateText("Detail", rowRect, subtitle, PixelFont.Small,
                    TextAnchor.MiddleLeft, MutedColor);
                SetRect(detail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(430f, 18f), new Vector2(22f, -32f));
            }

            // 打勾用 OK 两个字母：像素字体没有 ✓ 字形（打包后会变方块）
            if (SaveStore.IsCleared(displayName))
            {
                Text cleared = CreateText("Cleared", rowRect, "OK", PixelFont.Small, TextAnchor.MiddleCenter, ClearedColor);
                SetRect(cleared.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(40f, RowHeight - 8f), new Vector2(-22f, 0f));
            }

            if (isCurrent)
            {
                Text now = CreateText("Now", rowRect, "NOW  ", PixelFont.Small, TextAnchor.MiddleRight, MutedColor);
                SetRect(now.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(80f, RowHeight - 8f), new Vector2(-60f, 0f));
            }
        }

        /// <summary>分区小标题（不可点）。</summary>
        void CreateCaption(string content, ref float y)
        {
            Text caption = CreateText("Caption", listRoot, content, PixelFont.Small, TextAnchor.MiddleLeft, MutedColor);
            SetRect(caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, CaptionHeight), new Vector2(0f, y));
            y -= CaptionHeight;
        }

        /// <summary>灰色提示行（不可点），用来报「有错、看 Console」这类信息。</summary>
        void CreateHint(string content, ref float y)
        {
            Text hint = CreateText("Hint", listRoot, content, PixelFont.Small, TextAnchor.MiddleLeft, MutedColor);
            SetRect(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, HintHeight), new Vector2(0f, y));
            y -= HintHeight;
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

        /// <summary>同 <see cref="Select"/>，只是关卡来自运行时 JSON 文件。</summary>
        void SelectJson(LevelJsonEntry entry)
        {
            if (flow == null || entry == null) return;

            // 先确认这份 JSON 真能读通再盖黑幕：读不通就留在面板上，
            // 否则黑幕盖住却等不到关卡，玩家会盯着一片黑。
            if (!LevelJsonLibrary.TryLoad(entry.Path, out LevelSO probe, out List<string> problems))
            {
                Debug.LogWarning("[BitFlow] 关卡 JSON 读不进来：" + entry.Path + " → " + string.Join("；", problems));
                return;
            }
            LevelJsonLibrary.DestroyLevel(probe);

            if (curtain != null) curtain.CoverImmediately();
            Close();

            flow.LoadJson(entry);
        }

        // ---------------------------------------------------------------- 搭建

        void BuildUi()
        {
            font = PixelFont.Get();

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

            Text title = CreateText("Title", panelRect, "SELECT LEVEL", 32, TextAnchor.MiddleCenter, TitleColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 44f), new Vector2(0f, -46f));

            GameObject listObject = new GameObject("List", typeof(RectTransform));
            listObject.transform.SetParent(panelRect, false);
            listRoot = listObject.GetComponent<RectTransform>();
            SetRect(listRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 0f), new Vector2(0f, -96f));

            emptyText = CreateText("Empty", panelRect, "NO LEVELS", PixelFont.Body,
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

            Text text = CreateText("Label", go.transform, label, PixelFont.Body, TextAnchor.MiddleCenter, TitleColor);
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
    }
}
