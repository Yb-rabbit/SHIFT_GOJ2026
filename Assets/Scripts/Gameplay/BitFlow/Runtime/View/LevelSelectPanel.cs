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
        const float ActionHeight = 34f;

        /// <summary>列表区高度 = 面板 620 - 标题占的 96 - 底部 CLOSE 占的 90。</summary>
        const float ListHeight = 434f;

        LevelFlow flow;
        IntroCurtain curtain;
        Font font;

        GameObject canvasObject;
        RectTransform listRoot;
        Text emptyText;

        bool open;

        /// <summary>列表当前显示的是关卡列表，还是「新建关卡选尺寸」那一屏。</summary>
        bool sizePickMode;

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
            sizePickMode = false;
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

            // 「新建关卡」是同一块列表的第二屏：点 + NEW LEVEL 进来挑尺寸，点 BACK 回关卡列表。
            // 不另开一层面板 —— 尺寸只有四档，多一层 Canvas 反而要多处理一次输入锁的引用计数。
            if (sizePickMode)
            {
                RebuildSizePick();
                return;
            }

            jsonEntries.Clear();

            LevelCatalogSO catalog = flow != null ? flow.Catalog : null;
            int catalogCount = catalog != null ? catalog.Count : 0;

            // 场上没有 LevelFlow 就不去扫目录：这种面板点了也进不了关
            List<string> jsonProblems = null;
            if (flow != null)
            {
                // 先建目录再扫：首次运行目录还不存在时 Scan 会把「还没有关卡目录」记成一条 problem，
                // 面板上就会莫名其妙挂个 FILE ERROR。建完再扫，空目录 = 干净的 0 条。
                LevelJsonLibrary.EnsureFolder();
                jsonEntries.AddRange(LevelJsonLibrary.Scan(out jsonProblems));
                foreach (string problem in jsonProblems) Debug.LogWarning("[BitFlow] 关卡 JSON：" + problem);
            }

            bool hasJsonSection = jsonEntries.Count > 0 || (jsonProblems != null && jsonProblems.Count > 0);
            if (catalogCount == 0 && !hasJsonSection && flow == null)
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

            // MY LEVELS 一整段：标题 → + NEW LEVEL → 存档行 → 报错提示。
            // 新建入口排在存档列表前面：这是玩家唯一「从零做一关」的路，不该被二十条存档压到屏幕外。
            if (hasJsonSection || flow != null)
            {
                CreateCaption("MY LEVELS", ref y);

                if (flow != null)
                {
                    CreateAction("+  NEW LEVEL", BeginSizePick, ref y);
                    if (jsonEntries.Count == 0) CreateHint("NO LEVELS YET  ·  PRESS + NEW LEVEL", ref y);
                }

                foreach (LevelJsonEntry entry in jsonEntries)
                {
                    LevelJsonEntry captured = entry;
                    bool isCurrent = currentJsonPath == entry.Path;
                    CreateRow(row++, null, captured, isCurrent, () => SelectJson(captured), ref y);
                }

                if (jsonProblems != null && jsonProblems.Count > 0)
                {
                    // 像素字体没有中文字形：面板上只说「有错、看日志」，细节都在 Console
                    CreateHint("FILE ERROR  ·  SEE CONSOLE", ref y);
                }
            }

            // 自己的关卡放哪、刷新一下看到新文件 —— 少了这两个按钮，玩家得自己去翻
            // AppData/LocalLow 里的隐藏目录，等于没有这个功能
            if (flow != null)
            {
                CreateAction("SHARE FOLDER", OpenJsonFolder, ref y);
                CreateAction("REFRESH", RefreshList, ref y);
            }

            // listRoot 自上而下排：y 一路走成负数，高度取它的绝对值
            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));
        }

        /// <summary>
        /// 「新建关卡」第二屏：四档网格尺寸，点一个就开工坊。
        /// 为什么不做输入框：工程里没有 TextMeshPro / InputField，自由输入尺寸要另搭一套中文输入处理，
        /// 而四档尺寸已经覆盖了「小谜题 → 大地图」的全部需要。
        /// </summary>
        void RebuildSizePick()
        {
            emptyText.gameObject.SetActive(false);
            listRoot.gameObject.SetActive(true);

            float y = 0f;
            CreateCaption("NEW LEVEL  ·  PICK A GRID", ref y);

            Vector2Int standard = BitFlowWorkshop.SizePresets.Length > 0
                ? BitFlowWorkshop.SizePresets[0]
                : new Vector2Int(9, 8);

            foreach (Vector2Int size in BitFlowWorkshop.SizePresets)
            {
                Vector2Int captured = size;
                string label = size.x + " x " + size.y + (size == standard ? "   STANDARD" : string.Empty);
                CreateAction(label, () => BeginWorkshop(captured), ref y);
            }

            CreateAction("BACK", BackToLevels, ref y);

            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));
        }

        void BeginSizePick()
        {
            sizePickMode = true;
            Rebuild();
        }

        void BackToLevels()
        {
            sizePickMode = false;
            Rebuild();
        }

        void RefreshList()
        {
            sizePickMode = false;
            Rebuild();
        }

        /// <summary>
        /// 开工坊。顺序：<b>先关面板放锁、再开工坊</b> —— 工坊期间手牌盘必须能收到点击，
        /// 所以这一路不能留着选关面板那把输入锁。
        /// </summary>
        void BeginWorkshop(Vector2Int size)
        {
            sizePickMode = false;
            Close();

            BitFlowWorkshop.Ensure().Open(size.x, size.y);
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

        /// <summary>可点的一行动作（打开目录 / 刷新列表）。标签只用 ASCII：像素字体没有中文字形。</summary>
        void CreateAction(string label, UnityAction onClick, ref float y)
        {
            GameObject rowObject = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            rowObject.transform.SetParent(listRoot, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            SetRect(rowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, ActionHeight), new Vector2(0f, y));
            y -= ActionHeight + RowGap;

            Image background = rowObject.GetComponent<Image>();
            background.color = RowColor;

            Button button = rowObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            Text title = CreateText("Title", rowRect, label, PixelFont.Small, TextAnchor.MiddleCenter, MutedColor);
            SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// 在资源管理器里打开「玩家自己放关卡」的目录（<c>Application.persistentDataPath/LevelJson</c>）。
        ///
        /// 为什么不做系统文件对话框：那要引 System.Windows.Forms 或写一堆平台分支，
        /// 而玩家真正需要的只有「把 json 丢进去」这一件事 —— 打开目录就够了。
        /// 移动端 / 主机没有这个概念，直接跳过（路径仍会写进 Console）。
        /// </summary>
        void OpenJsonFolder()
        {
            LevelJsonLibrary.EnsureFolder();
            string folder = LevelJsonLibrary.Folder;
            Debug.Log("[BitFlow] 关卡 JSON 目录：" + folder);

            RuntimePlatform platform = Application.platform;
            bool windows = platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
            bool mac = platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor;
            bool linux = platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor;

            if (!windows && !mac && !linux) return;

            try
            {
                if (windows) System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
                else if (mac) System.Diagnostics.Process.Start("open", "\"" + folder + "\"");
                else System.Diagnostics.Process.Start("xdg-open", "\"" + folder + "\"");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[BitFlow] 打不开关卡目录：" + exception.Message + "（路径见上一条日志）");
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
                new Vector2(700f, 620f), Vector2.zero);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;

            Text title = CreateText("Title", panelRect, "SELECT LEVEL", 32, TextAnchor.MiddleCenter, TitleColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 44f), new Vector2(0f, -46f));

            // 列表区裁剪 + 竖向滚动：玩家最多能存 30 关，一行 56+10，不裁剪就会画到面板外面 ——
            // 而且溢出面板但仍在屏幕内的按钮照样能点到，看起来像「面板外面也有关卡」。
            GameObject clipObject = new GameObject("ListClip", typeof(RectTransform), typeof(RectMask2D),
                typeof(ScrollRect));
            clipObject.transform.SetParent(panelRect, false);

            RectTransform clipRect = clipObject.GetComponent<RectTransform>();
            SetRect(clipRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, ListHeight), new Vector2(0f, -96f));

            GameObject listObject = new GameObject("List", typeof(RectTransform));
            listObject.transform.SetParent(clipRect, false);
            listRoot = listObject.GetComponent<RectTransform>();
            SetRect(listRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 0f), Vector2.zero);

            ScrollRect scroll = clipObject.GetComponent<ScrollRect>();
            scroll.viewport = clipRect;
            scroll.content = listRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = false;   // 像素 UI 带惯性会滑过头，读起来像卡了一下

            // 透明的可点背景：让滚轮在行与行之间的空白处也生效，
            // 顺带把「点列表空白 = 关面板」这个误触挡掉（遮罩层还是能点）。
            Image clipImage = clipObject.AddComponent<Image>();
            clipImage.color = Color.clear;
            clipImage.raycastTarget = true;

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
