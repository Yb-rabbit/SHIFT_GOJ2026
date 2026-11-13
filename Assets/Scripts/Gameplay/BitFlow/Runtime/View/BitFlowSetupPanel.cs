using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 工坊的「参数小窗」：屏幕左边一块常驻面板，三个页签。
    ///
    /// <b>CELL</b>：改选中那一件的参数 —— 朝向、发射序列每一位、发射总位数、接收条件（进料面 / 四面可收），
    /// 并且实时算给你看「这台机器到底会吐什么」（<c>EMITS 1 0 0 1</c>）以及接收器和发射器<b>对不对得上</b>
    /// （<see cref="ReceiverDefinition.ExpectedValueAt"/> 是从左往右读，而 <see cref="EmitterDefinition.BuildCycle"/>
    /// 是从末位往前弹栈，两者天然反着 —— 所以这里必须有一键 MATCH，不能靠玩家自己数）。
    ///
    /// <b>HAND</b>：给玩家发多少张牌。这一页填的数字会写进存档的 <c>supplies</c>，
    /// 玩家的手牌盘读的就是它；不填 = 不限量（作者草图模式）。
    ///
    /// 为什么是常驻面板而不是每次弹窗：作者改完一格马上要看下一格，弹窗关来关去比翻页累。
    /// 也不用 <c>InputGate</c> 挡世界点击 —— 面板只占左边一条，右边的格子照样能点，
    /// 点上去会通过 <see cref="BitFlowCardTray.CellSelected"/> 自动切到那一格。
    ///
    /// 所有改动都走 <see cref="LevelDraft.Edit"/> / <see cref="LevelDraft.EditStock"/>，
    /// 所以每一步都能 Ctrl+Z 撤回来，和放置/拾回共用一个撤销栈。
    /// </summary>
    public sealed class BitFlowSetupPanel : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.08f, 0.96f);
        static readonly Color TextColor = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color MutedColor = new Color(0.55f, 0.66f, 0.76f, 1f);
        static readonly Color IdleColor = new Color(0.11f, 0.17f, 0.23f, 0.95f);
        static readonly Color ActiveColor = new Color(0.16f, 0.44f, 0.56f, 0.98f);
        static readonly Color GoodColor = new Color(0.45f, 0.86f, 0.62f, 1f);
        static readonly Color BadColor = new Color(1f, 0.50f, 0.40f, 1f);

        // ---------------------------------------------------------------- 版式（按 1920×1080 基准）

        const float PanelWidth = 392f;
        const float Padding = 16f;

        /// <summary>面板内部可用宽度（所有 x / 宽度都按它算）。</summary>
        const float InnerWidth = PanelWidth - Padding * 2f;

        /// <summary>序列最多几位。Sequence 本身不限，但一行只放得下 8 个位按钮。</summary>
        const int MaxSequence = 8;

        /// <summary>发射位数 / 期望个数的上限。够做所有现有关卡，再大就只是为难玩家。</summary>
        const int MaxCount = 32;

        /// <summary>排序层：压在 HUD（0）之上、黑幕（30）之下 —— 切关时黑幕要能把它盖住。</summary>
        const int SortingOrder = 25;

        enum Tab
        {
            Cell = 0,
            Hand = 1,
            Name = 2,
        }

        BitFlowCardTray tray;
        LevelDraft draft;
        IReadOnlyList<ElementTypeSO> palette;

        GameObject canvasObject;
        RectTransform panelRect;

        EditableElement selected;
        Tab tab = Tab.Cell;
        bool open;

        /// <summary>NAME 页的输入框 / 错误行。整块面板每次刷新都会重建，引用只在当帧有效。</summary>
        InputField nameField;
        Text nameErrorText;
        string nameError;

        /// <summary>防止 onValueChanged 里改 text 又触发一次回调。</summary>
        bool cleaningName;

        /// <summary>NAME 页聚焦时占用全局输入锁：输入空格 / R / Ctrl+Z 不能同时去抓手牌。</summary>
        bool nameGateHeld;

        /// <summary>当前行顶部（相对面板顶边，向下为负）。<see cref="BeginRow"/> 边走边减。</summary>
        float cursor;

        public bool IsOpen => open;

        // ---------------------------------------------------------------- 开 / 关

        /// <summary>
        /// 取小窗（没有就现建一个）。和 <c>BitFlowWorkshop.Ensure</c> 一样：
        /// 老场景里没有这个组件，改生成脚本不会回溯到已存在的场景，所以必须能自己把自己装起来。
        /// </summary>
        public static BitFlowSetupPanel Ensure(BitFlowCardTray cardTray, IReadOnlyList<ElementTypeSO> types, LevelDraft target)
        {
            BitFlowSetupPanel existing = FindFirstObjectByType<BitFlowSetupPanel>();
            if (existing == null)
            {
                GameObject root = new GameObject("BitFlowSetupPanel");
                existing = root.AddComponent<BitFlowSetupPanel>();
            }

            existing.Bind(cardTray, types);
            existing.SetDraft(target);
            return existing;
        }

        public void Bind(BitFlowCardTray cardTray, IReadOnlyList<ElementTypeSO> types)
        {
            palette = types;

            if (tray == cardTray) return;

            if (tray != null) tray.CellSelected -= OnCellSelected;
            tray = cardTray;
            if (tray != null) tray.CellSelected += OnCellSelected;
        }

        /// <summary>换一份草稿（工坊 CLEAR 会造一个新的）。</summary>
        public void SetDraft(LevelDraft target)
        {
            draft = target;
            selected = null;
            nameError = null;
            if (open) Refresh();
        }

        public void Open()
        {
            if (canvasObject == null) BuildUi();

            ReleaseNameGate();   // 防御：上一次若停在 NAME 页，重新开面板时先还锁
            open = true;
            tab = Tab.Cell;
            nameError = null;
            canvasObject.SetActive(true);

            // 进来先把托盘里已经选中的那件显示出来（刚放下的件会自动成为选中项）
            selected = tray != null ? tray.SelectedElement : null;
            Refresh();
        }

        public void Close()
        {
            open = false;
            selected = null;
            nameError = null;
            ReleaseNameGate();

            if (canvasObject != null) canvasObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (tray != null) tray.CellSelected -= OnCellSelected;
            ReleaseNameGate();
        }

        void OnCellSelected(EditableElement element)
        {
            selected = element;

            // 用 X 关掉小窗之后又点了一格：作者显然还想改参数，直接把它叫回来。
            // （面板收起来就再也打不开的话，「参数改不了」这个问题只是换了个形状又回来了。）
            if (!open && element != null && tray != null) Open();
            else if (open) Refresh();
        }

        // ---------------------------------------------------------------- 搭 UI 骨架

        void BuildUi()
        {
            canvasObject = new GameObject("SetupCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            // 面板本体：贴屏幕左边、垂直居中。没有全屏遮罩 —— 右边的格子必须还能点。
            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.GetComponent<RectTransform>();
            SetRect(panelRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(PanelWidth, 480f), new Vector2(24f, 0f));

            Image background = panelObject.GetComponent<Image>();
            background.color = PanelColor;

            // 面板范围内不许点到世界：否则点按钮时手一抖，底下就多摆了一件
            background.raycastTarget = true;

            canvasObject.SetActive(false);
        }

        // ---------------------------------------------------------------- 重建

        /// <summary>
        /// 整块重建。面板上的一切都是「一次点击 = 一次重建」，省掉一堆字段同步；
        /// 条目最多十来行，重建的开销看不出来。
        /// </summary>
        public void Refresh()
        {
            if (panelRect == null || !open) return;

            for (int i = panelRect.childCount - 1; i >= 0; i--)
            {
                GameObject child = panelRect.GetChild(i).gameObject;
                child.SetActive(false);   // 先隐形，免得旧内容和新内容重叠一帧
                Destroy(child);
            }

            cursor = -Padding;

            RectTransform header = BeginRow(28f, 6f);
            AddLabel(header, tab == Tab.Cell ? "PIECE PARAMS" : tab == Tab.Hand ? "PLAYER HAND" : "LEVEL NAME", 0f, InnerWidth - 40f,
                TextAnchor.MiddleLeft, TextColor, PixelFont.Body);
            AddButton(header, "X", InnerWidth - 34f, 34f, Close, IdleColor, PixelFont.Body);

            RectTransform tabs = BeginRow(34f, 10f);
            AddButton(tabs, "CELL", 0f, 112f, () => SwitchTab(Tab.Cell),
                tab == Tab.Cell ? ActiveColor : IdleColor, PixelFont.Body);
            AddButton(tabs, "HAND", 120f, 112f, () => SwitchTab(Tab.Hand),
                tab == Tab.Hand ? ActiveColor : IdleColor, PixelFont.Body);
            AddButton(tabs, "NAME", 240f, 112f, () => SwitchTab(Tab.Name),
                tab == Tab.Name ? ActiveColor : IdleColor, PixelFont.Body);

            if (tab == Tab.Cell) BuildCellTab();
            else if (tab == Tab.Hand) BuildHandTab();
            else BuildNameTab();

            panelRect.sizeDelta = new Vector2(PanelWidth, -cursor + Padding);
        }

        void SwitchTab(Tab next)
        {
            if (tab == Tab.Name && next != Tab.Name) ReleaseNameGate();

            tab = next;
            nameError = null;

            if (open && tab == Tab.Name && !nameGateHeld)
            {
                InputGate.Acquire();
                nameGateHeld = true;
            }

            Refresh();
        }

        void ReleaseNameGate()
        {
            if (!nameGateHeld) return;

            nameGateHeld = false;
            InputGate.Release();
        }

        // ---------------------------------------------------------------- 版式小工具

        /// <summary>起一行：宽度 = 面板内宽，高度固定，返回该行的容器（子元素按 x 从左往右摆）。</summary>
        RectTransform BeginRow(float height, float gap)
        {
            GameObject go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(panelRect, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            SetRect(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-Padding * 2f, height), new Vector2(0f, cursor));

            cursor -= height + gap;
            return rect;
        }

        Text AddLabel(RectTransform row, string content, float x, float width, TextAnchor anchor, Color color, int fontSize)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(row, false);

            SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(width, 0f), new Vector2(x, 0f));

            Text text = go.GetComponent<Text>();
            text.font = PixelFont.Get();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        Button AddButton(RectTransform row, string label, float x, float width, Action onClick, Color color, int fontSize)
        {
            GameObject go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);

            SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(width, 0f), new Vector2(x, 0f));

            Image image = go.GetComponent<Image>();
            image.color = color;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            Text text = AddLabel(go.GetComponent<RectTransform>(), label, 0f, width,
                TextAnchor.MiddleCenter, TextColor, fontSize);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            return button;
        }

        /// <summary>
        /// 单行文本输入。工程里只有旧版 <see cref="InputField"/>（没有 TMP），
        /// 所以输入框也走同一套像素字体；文字颜色 / 字号和面板其它读数一致。
        /// </summary>
        InputField AddInputField(RectTransform row, string initial, float x, float width, Action<string> onChanged)
        {
            GameObject go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(row, false);

            SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(width, 0f), new Vector2(x, 0f));

            Image background = go.GetComponent<Image>();
            background.color = IdleColor;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(go.transform, false);
            SetRect(textObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0f, 0.5f), new Vector2(-16f, -6f), new Vector2(8f, 0f));

            Text text = textObject.GetComponent<Text>();
            text.font = PixelFont.Get();
            text.fontSize = PixelFont.Body;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = TextColor;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            InputField input = go.GetComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = text;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            input.characterLimit = 20;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.caretColor = TextColor;
            input.selectionColor = new Color(ActiveColor.r, ActiveColor.g, ActiveColor.b, 0.55f);
            input.text = initial ?? string.Empty;
            if (onChanged != null) input.onValueChanged.AddListener(value => onChanged(value));

            return input;
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

        // ---------------------------------------------------------------- CELL 页

        void BuildCellTab()
        {
            if (selected == null)
            {
                RectTransform none = BeginRow(40f, 10f);
                AddLabel(none, "CLICK A PIECE TO EDIT IT", 0f, InnerWidth, TextAnchor.MiddleLeft, TextColor, PixelFont.Small);

                RectTransform how = BeginRow(72f, 10f);
                AddLabel(how, "LEFT CLICK = SELECT + EDIT" + "\n" + "RIGHT CLICK = PICK IT BACK",
                    0f, InnerWidth, TextAnchor.UpperLeft, MutedColor, PixelFont.Small);

                RectTransform keys = BeginRow(60f, 10f);
                AddLabel(keys, "SPACE GRAB / DROP" + "\n" + "R ROTATE   ·   CTRL+Z UNDO",
                    0f, InnerWidth, TextAnchor.UpperLeft, MutedColor, PixelFont.Small);
                return;
            }

            RectTransform info = BeginRow(44f, 8f);
            AddLabel(info, Describe(selected), 0f, InnerWidth, TextAnchor.MiddleLeft, TextColor, PixelFont.Small);

            // 玩家能不能搬这一件：工坊里唯一决定"存档里 playerEditable 是 true 还是 false"的开关
            BuildLockRow();

            // 延迟器四面等价，DIR 行对它没有意义（转了也不会换向），
            // 只留 DELAY 行，玩家不会以为自己没转对。
            if (selected.SupportsRotation) BuildDirRow(selected.Kind == CellKind.Splitter ? "ONE" : "DIR", false);

            if (selected.Kind == CellKind.Splitter) BuildDirRow("ZERO", true);
            if (selected.Kind == CellKind.Filter) BuildPassRow();
            if (selected.Kind == CellKind.DelayGate) BuildDelayRow();

            if (selected.Kind == CellKind.Emitter || selected.Kind == CellKind.Receiver)
            {
                BuildSequenceRow();
                BuildCountRow();
            }

            if (selected.Kind == CellKind.Emitter) BuildEmitRow();
            if (selected.Kind == CellKind.Receiver) BuildReceiverRows();
        }

        /// <summary>
        /// 「玩家能不能动这一件」：FIXED = 关卡预设件（搬不动、拾不回、也转不了），
        /// FREE = 玩家可以随便搬（有手牌的解谜关里，玩家要改的正是这一类）。
        ///
        /// 存档里就是 <c>playerEditable</c>，所以这是作者唯一能决定「玩家能不能动这一格」的地方 ——
        /// 少了它，工坊存出来的每一件都被锁死（这正是之前"预设件拖不动"的根因）。
        /// </summary>
        void BuildLockRow()
        {
            bool locked = draft != null && draft.IsLocked(selected.Coord);

            RectTransform row = BeginRow(38f, 8f);
            AddLabel(row, "PLAYER", 0f, 80f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);
            AddButton(row, "FIXED", 84f, 134f, () => SetLocked(true),
                locked ? ActiveColor : IdleColor, PixelFont.Small);
            AddButton(row, "FREE", 226f, 134f, () => SetLocked(false),
                locked ? IdleColor : ActiveColor, PixelFont.Small);
        }

        /// <summary>切换「玩家能不能搬这一格」。走 <see cref="LevelDraft.EditLock"/>，所以能 Ctrl+Z 撤回来。</summary>
        void SetLocked(bool locked)
        {
            if (draft == null || selected == null) return;
            if (draft.IsLocked(selected.Coord) == locked) return;

            draft.EditLock(selected.Coord, locked);

            // 换锁定也算草稿变了：手牌盘的 Apply 会重建舞台并广播 DraftChanged
            if (tray != null) tray.CommitDraft(locked ? "FIXED" : "FREE");
            else Refresh();
        }

        /// <summary>朝向行：四个方向按钮，当前朝向高亮。<paramref name="secondary"/> = 分流器的 0 臂。</summary>
        void BuildDirRow(string title, bool secondary)
        {
            RectTransform row = BeginRow(38f, 8f);
            AddLabel(row, title, 0f, 50f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            Dir current = secondary ? selected.SecondaryDir : selected.PrimaryDir;
            Dir[] dirs = { Dir.Up, Dir.Right, Dir.Down, Dir.Left };
            string[] labels = { "UP", "RIGHT", "DOWN", "LEFT" };

            for (int i = 0; i < dirs.Length; i++)
            {
                Dir dir = dirs[i];
                bool active = current == dir;
                Action onClick = secondary ? (Action)(() => SetSecondaryDir(dir)) : () => SetPrimaryDir(dir);

                AddButton(row, labels[i], 54f + i * 77f, 73f, onClick, active ? ActiveColor : IdleColor, PixelFont.Small);
            }
        }

        /// <summary>过滤器：放行 0 还是 1。</summary>
        void BuildPassRow()
        {
            RectTransform row = BeginRow(38f, 8f);
            AddLabel(row, "PASS", 0f, 50f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            AddButton(row, "0", 54f, 72f, () => SetPass(0), selected.PassValue == 0 ? ActiveColor : IdleColor, PixelFont.Body);
            AddButton(row, "1", 132f, 72f, () => SetPass(1), selected.PassValue != 0 ? ActiveColor : IdleColor, PixelFont.Body);
        }

        /// <summary>延迟器：延迟 1~4 个 tick，四个按钮直接选。</summary>
        void BuildDelayRow()
        {
            RectTransform row = BeginRow(38f, 8f);
            AddLabel(row, "DELAY", 0f, 60f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            int current = Mathf.Clamp(selected.DelayTicks, 1, 4);
            for (int i = 0; i < 4; i++)
            {
                int ticks = i + 1;
                AddButton(row, ticks.ToString(), 64f + i * 58f, 52f,
                    () => SetDelay(ticks), current == ticks ? ActiveColor : IdleColor, PixelFont.Body);
            }
        }

        /// <summary>序列行：每一位都是一个按钮，点一下 0 ↔ 1。</summary>
        void BuildSequenceRow()
        {
            string sequence = Normalize(selected.Sequence);
            RectTransform row = BeginRow(38f, 8f);
            AddLabel(row, "SEQ", 0f, 50f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            for (int i = 0; i < sequence.Length && i < MaxSequence; i++)
            {
                int index = i;
                bool one = sequence[i] == '1';
                AddButton(row, one ? "1" : "0", 54f + i * 37f, 34f,
                    () => ToggleBit(index), one ? ActiveColor : IdleColor, PixelFont.Body);
            }
        }

        /// <summary>数量行：NUM 是「发几位 / 收几位」，LEN 是序列本身几位一循环。</summary>
        void BuildCountRow()
        {
            RectTransform row = BeginRow(38f, 8f);
            AddLabel(row, "NUM", 0f, 50f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            AddButton(row, "-", 54f, 40f, () => StepCount(-1), IdleColor, PixelFont.Body);
            AddLabel(row, Mathf.Max(0, selected.Count).ToString(), 96f, 48f,
                TextAnchor.MiddleCenter, TextColor, PixelFont.Body);
            AddButton(row, "+", 148f, 40f, () => StepCount(1), IdleColor, PixelFont.Body);

            AddButton(row, "LEN-", 198f, 76f, () => StepLength(-1), IdleColor, PixelFont.Small);
            AddButton(row, "LEN+", 280f, 76f, () => StepLength(1), IdleColor, PixelFont.Small);
        }

        /// <summary>发射器：这台机器到底按什么顺序吐位（按模拟器真实规则算，不是把序列原样贴出来）。</summary>
        void BuildEmitRow()
        {
            RectTransform row = BeginRow(46f, 8f);
            AddLabel(row, "EMITS  " + DescribeEmitOrder(selected), 0f, InnerWidth,
                TextAnchor.MiddleLeft, GoodColor, PixelFont.Small);
        }

        /// <summary>接收器：进料面模式、和最近发射器的匹配结论，以及一键对齐。</summary>
        void BuildReceiverRows()
        {
            RectTransform faceRow = BeginRow(38f, 8f);
            AddLabel(faceRow, "FACE", 0f, 50f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            AddButton(faceRow, "ANY SIDE", 54f, 148f, () => SetFace(true),
                selected.AcceptAnyFace ? ActiveColor : IdleColor, PixelFont.Small);
            AddButton(faceRow, "IN ONLY", 210f, 148f, () => SetFace(false),
                selected.AcceptAnyFace ? IdleColor : ActiveColor, PixelFont.Small);

            bool matches = ReceiverMatches(selected, out string detail);

            RectTransform checkRow = BeginRow(40f, 8f);
            AddLabel(checkRow, (matches ? "MATCH   " : "MISMATCH   ") + detail, 0f, InnerWidth,
                TextAnchor.MiddleLeft, matches ? GoodColor : BadColor, PixelFont.Small);

            if (matches) return;

            RectTransform fixRow = BeginRow(34f, 8f);
            AddButton(fixRow, "MATCH EMITTER", 0f, InnerWidth, MatchEmitter, IdleColor, PixelFont.Small);
        }

        // ---------------------------------------------------------------- NAME 页

        /// <summary>
        /// 关卡名。DONE 写进 JSON 的 <c>displayName</c>，选关列表、通关记录、
        /// 工坊保存后的回话都用它。输入期间占住 <see cref="InputGate"/>，
        /// 否则空格 / R / Ctrl+Z 会同时被手牌盘吃掉。
        /// </summary>
        void BuildNameTab()
        {
            nameErrorText = null;
            string current = draft != null ? draft.DisplayName : string.Empty;

            RectTransform info = BeginRow(30f, 6f);
            BitFlowWorkshop workshop = FindFirstObjectByType<BitFlowWorkshop>();
            bool editing = workshop != null && workshop.IsEditingExisting;
            AddLabel(info, editing ? "EDITING AN EXISTING LEVEL  ·  DONE SAVES IN PLACE" : "NEW LEVEL  ·  DONE SAVES A NEW FILE",
                0f, InnerWidth, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            RectTransform nameRow = BeginRow(38f, 8f);
            AddLabel(nameRow, "NAME", 0f, 54f, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);
            nameField = AddInputField(nameRow, current, 54f, InnerWidth - 54f, OnNameChanged);

            RectTransform hint = BeginRow(38f, 8f);
            AddLabel(hint, "A-Z  0-9  -  _  SPACE  ·  MAX 20 CHARS", 0f, InnerWidth,
                TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            if (!string.IsNullOrEmpty(nameError))
            {
                RectTransform errorRow = BeginRow(28f, 8f);
                nameErrorText = AddLabel(errorRow, nameError, 0f, InnerWidth,
                    TextAnchor.MiddleLeft, BadColor, PixelFont.Small);
            }

            RectTransform actions = BeginRow(36f, 10f);
            AddButton(actions, "APPLY", 0f, 172f, ApplyName, ActiveColor, PixelFont.Small);
            AddButton(actions, "BACK TO CELL", 184f, 176f, () => SwitchTab(Tab.Cell), IdleColor, PixelFont.Small);

            if (nameField != null)
            {
                nameField.Select();
                nameField.ActivateInputField();
            }
        }

        void OnNameChanged(string raw)
        {
            if (cleaningName || nameField == null) return;

            string cleaned = BitFlowWorkshop.SanitizeTypedDisplayName(raw);
            if (string.Equals(cleaned, raw, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(nameError))
                {
                    nameError = null;
                    if (nameErrorText != null) nameErrorText.text = string.Empty;
                }
                return;
            }

            cleaningName = true;
            nameField.text = cleaned;
            nameField.caretPosition = cleaned.Length;
            cleaningName = false;
        }

        void ApplyName()
        {
            if (draft == null || nameField == null) return;

            string raw = nameField.text;
            BitFlowWorkshop workshop = FindFirstObjectByType<BitFlowWorkshop>();
            if (workshop == null)
            {
                nameError = "WORKSHOP NOT FOUND";
                Refresh();
                return;
            }

            if (!workshop.TryRenameDraft(raw, out string error))
            {
                nameError = error;
                Refresh();
                return;
            }

            nameError = null;
            SwitchTab(Tab.Cell);
        }

        // ---------------------------------------------------------------- HAND 页

        void BuildHandTab()
        {
            bool limited = draft != null && draft.StockEnabled;

            RectTransform modeRow = BeginRow(34f, 8f);
            AddLabel(modeRow, limited ? "MODE   LIMITED" : "MODE   UNLIMITED", 0f, InnerWidth,
                TextAnchor.MiddleLeft, limited ? TextColor : MutedColor, PixelFont.Body);

            RectTransform hint = BeginRow(40f, 12f);
            AddLabel(hint, "PRESS + TO GIVE CARDS", 0f, InnerWidth, TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);

            if (palette != null)
            {
                for (int i = 0; i < palette.Count; i++)
                {
                    ElementTypeSO type = palette[i];
                    if (type == null || string.IsNullOrEmpty(type.Key)) continue;

                    // 发射器 / 接收器不发成手牌：它们定的是这一关的题（发什么位、怎么算过），
                    // 玩家能自己摆就等于自己改题。墙 / 非门 / 过滤器 / 分流器都只是"位怎么走"，
                    // 怎么摆都是解题。判定只有 <see cref="SupplyKinds"/> 一份，工坊和自检共用。
                    if (!SupplyKinds.IsSupplyKind(KindOf(type))) continue;

                    BuildSupplyRow(type);
                }
            }

            RectTransform totalRow = BeginRow(36f, 10f);
            AddLabel(totalRow, "TOTAL  " + (draft != null ? draft.StockTotal : 0), 0f, 150f,
                TextAnchor.MiddleLeft, TextColor, PixelFont.Body);
            AddButton(totalRow, "NO HAND", 200f, 160f, ClearHand, IdleColor, PixelFont.Small);

            RectTransform foot = BeginRow(48f, 8f);
            AddLabel(foot, "SAVED IN supplies  ·  ALLOW BUILD ON", 0f, InnerWidth,
                TextAnchor.MiddleLeft, MutedColor, PixelFont.Small);
        }

        /// <summary>一种元件一行：名字 + 减 + 张数 + 加。张数为 0 = 这一摞不给。</summary>
        void BuildSupplyRow(ElementTypeSO type)
        {
            int count = draft != null ? draft.Granted(type.Key) : 0;

            RectTransform row = BeginRow(34f, 6f);
            AddLabel(row, LabelOf(type), 0f, 190f, TextAnchor.MiddleLeft,
                count > 0 ? TextColor : MutedColor, PixelFont.Small);

            AddButton(row, "-", 194f, 40f, () => StepStock(type, -1), IdleColor, PixelFont.Body);
            AddLabel(row, count.ToString(), 238f, 60f, TextAnchor.MiddleCenter,
                count > 0 ? TextColor : MutedColor, PixelFont.Body);
            AddButton(row, "+", 302f, 40f, () => StepStock(type, 1), IdleColor, PixelFont.Body);
        }

        // ---------------------------------------------------------------- 改数据

        /// <summary>
        /// 改选中那一件。走 <see cref="LevelDraft.Edit"/>，所以每一步都能 Ctrl+Z。
        /// <c>CommitDraft</c> 会重建舞台并回调 <c>CellSelected</c>（撤销之后元件是新克隆），
        /// 因此这里不用自己去追引用。
        /// </summary>
        void Edit(Action<EditableElement> mutate, string message)
        {
            if (tray == null || draft == null || selected == null) return;
            if (!draft.Edit(selected.Coord, mutate)) return;

            tray.CommitDraft(message);
        }

        void SetPrimaryDir(Dir dir)
        {
            Edit(e => e.PrimaryDir = dir, "DIR " + dir.ToString().ToUpperInvariant());
        }

        void SetSecondaryDir(Dir dir)
        {
            Edit(e => e.SecondaryDir = dir, "ARM 0 " + dir.ToString().ToUpperInvariant());
        }

        void SetPass(byte value)
        {
            Edit(e => e.PassValue = value, "PASS " + value);
        }

        void SetDelay(int ticks)
        {
            Edit(e => e.DelayTicks = Mathf.Clamp(ticks, 1, 4), "DELAY " + Mathf.Clamp(ticks, 1, 4) + " TICK");
        }

        void ToggleBit(int index)
        {
            Edit(e => e.Sequence = ToggleBitAt(e.Sequence, index), "BIT " + index);
        }

        void StepCount(int delta)
        {
            Edit(e => e.Count = Mathf.Clamp(e.Count + delta, 1, MaxCount), "NUM " + delta);
        }

        void StepLength(int delta)
        {
            Edit(e =>
            {
                string current = Normalize(e.Sequence);
                e.Sequence = Resize(current, current.Length + delta);
            }, "LEN " + delta);
        }

        void SetFace(bool any)
        {
            Edit(e => e.AcceptAnyFace = any, any ? "FACE ANY" : "FACE IN");
        }

        /// <summary>
        /// 把接收器对齐到最近的发射器。
        ///
        /// 关键在 <see cref="Reverse"/>：模拟器按「末位先弹」组成循环单元，
        /// 所以「序列 110 的发射器」实际吐的是 0,1,1；接收器却是从左往右比。
        /// 直接抄一份序列会得到必挂的接收器 —— 上一版就是这个坑。
        /// </summary>
        void MatchEmitter()
        {
            EditableElement emitter = NearestEmitter(selected != null ? selected.Coord : default);
            if (emitter == null) return;

            string sequence = Reverse(Normalize(emitter.Sequence));
            int count = Mathf.Clamp(emitter.Count, 1, MaxCount);

            Edit(e =>
            {
                e.Sequence = sequence;
                e.Count = count;
            }, "MATCHED EMITTER");
        }

        void StepStock(ElementTypeSO type, int delta)
        {
            if (draft == null || type == null) return;

            int next = Mathf.Clamp(draft.Granted(type.Key) + delta, 0, MaxCount);
            draft.EditStock(type.Key, KindOf(type), next);

            if (tray != null) tray.CommitDraft("HAND " + (draft.StockEnabled ? draft.StockTotal : 0));
            else Refresh();
        }

        void ClearHand()
        {
            if (draft == null || !draft.StockEnabled) return;

            draft.EditClearHand();
            if (tray != null) tray.CommitDraft("NO HAND");
            else Refresh();
        }

        // ---------------------------------------------------------------- 序列小工具

        /// <summary>
        /// 面板上的一行摘要。<b>必须是纯 ASCII</b>：像素字体没有中文字形，
        /// 把 <see cref="EditableElement.Summary"/>（中文）直接贴上来只会显示一排方块。
        /// </summary>
        static string Describe(EditableElement element)
        {
            string where = "(" + element.Coord.X + "," + element.Coord.Y + ")";

            switch (element.Kind)
            {
                case CellKind.Emitter:
                    return "EMITTER " + where + "   FACE " + DirName(element.PrimaryDir);
                case CellKind.Receiver:
                    return "RECEIVER " + where + "   FACE " + DirName(element.PrimaryDir);
                case CellKind.Splitter:
                    return "SPLITTER " + where;
                case CellKind.NotGate:
                    return "NOT GATE " + where + "   OUT " + DirName(element.PrimaryDir);
                case CellKind.Filter:
                    return "FILTER " + where + "   PASS " + element.PassValue;
                case CellKind.DelayGate:
                    return "DELAY " + where + "   " + Mathf.Clamp(element.DelayTicks, 1, 4) + " TICK";
                default:
                    return "WALL " + where;
            }
        }

        static string DirName(Dir dir)
        {
            return dir.ToString().ToUpperInvariant();
        }

        /// <summary>只留 0/1（空格、逗号之类会被忽略）；空串兜底成 "1"。</summary>
        static string Normalize(string sequence)
        {
            if (string.IsNullOrEmpty(sequence)) return "1";

            System.Text.StringBuilder builder = new System.Text.StringBuilder(sequence.Length);
            foreach (char c in sequence)
            {
                if (c == '0' || c == '1') builder.Append(c);
            }

            return builder.Length == 0 ? "1" : builder.ToString();
        }

        static string ToggleBitAt(string sequence, int index)
        {
            char[] chars = Normalize(sequence).ToCharArray();
            if (index < 0 || index >= chars.Length) return new string(chars);

            chars[index] = chars[index] == '1' ? '0' : '1';
            return new string(chars);
        }

        /// <summary>把序列变长 / 变短（变长补 1，变短砍尾巴）。</summary>
        static string Resize(string sequence, int length)
        {
            int target = Mathf.Clamp(length, 1, MaxSequence);
            string current = Normalize(sequence);

            if (current.Length == target) return current;
            if (current.Length > target) return current.Substring(0, target);

            return current + new string('1', target - current.Length);
        }

        static string Reverse(string sequence)
        {
            char[] chars = Normalize(sequence).ToCharArray();
            System.Array.Reverse(chars);
            return new string(chars);
        }

        /// <summary>按模拟器真实规则（<see cref="EmitterDefinition.BuildEmitOrder"/>）算出实际吐位顺序。</summary>
        static string DescribeEmitOrder(EditableElement element)
        {
            EmitterDefinition definition = element.ToDefinition(0) as EmitterDefinition;
            if (definition == null) return "-";

            List<byte> order = definition.BuildEmitOrder();
            if (order.Count == 0) return "NOTHING";

            System.Text.StringBuilder builder = new System.Text.StringBuilder(order.Count * 2);
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(order[i] == 1 ? '1' : '0');
            }

            return builder.ToString();
        }

        /// <summary>
        /// 接收器和最近的发射器对不对得上：个数一致 + 每一位都相同。
        /// 对不上时用 <paramref name="detail"/> 说明差在哪 —— 作者不该靠肉眼数位。
        /// </summary>
        bool ReceiverMatches(EditableElement receiver, out string detail)
        {
            detail = string.Empty;

            EditableElement emitter = NearestEmitter(receiver.Coord);
            if (emitter == null)
            {
                detail = "NO EMITTER ON BOARD";
                return false;
            }

            EmitterDefinition emitterDefinition = emitter.ToDefinition(0) as EmitterDefinition;
            ReceiverDefinition receiverDefinition = receiver.ToDefinition(0) as ReceiverDefinition;
            if (emitterDefinition == null || receiverDefinition == null)
            {
                detail = "CANNOT READ";
                return false;
            }

            List<byte> emits = emitterDefinition.BuildEmitOrder();
            if (emits.Count == 0)
            {
                detail = "EMITTER SILENT";
                return false;
            }

            int need = Mathf.Max(0, receiverDefinition.TargetCount);
            if (need != emits.Count)
            {
                detail = "NUM " + need + " VS " + emits.Count;
                return false;
            }

            for (int i = 0; i < need; i++)
            {
                byte expected = receiverDefinition.ExpectedValueAt(i);
                if (expected != emits[i])
                {
                    detail = "BIT " + (i + 1) + " IS " + emits[i] + " NOT " + expected;
                    return false;
                }
            }

            detail = emits.Count + " BITS";
            return true;
        }

        /// <summary>离这一格最近的发射器（曼哈顿距离）。没有就返回 null。</summary>
        EditableElement NearestEmitter(GridPoint coord)
        {
            if (draft == null) return null;

            EditableElement best = null;
            int bestDistance = int.MaxValue;

            foreach (EditableElement candidate in draft.Cells)
            {
                if (candidate == null || candidate.Kind != CellKind.Emitter) continue;

                int distance = Mathf.Abs(candidate.Coord.X - coord.X) + Mathf.Abs(candidate.Coord.Y - coord.Y);
                if (distance >= bestDistance) continue;

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>这张牌对应哪种元件（存档里的 kind 兜底）。交给类型资产自己判定，不另抄一份映射表。</summary>
        static CellKind KindOf(ElementTypeSO type)
        {
            if (type == null) return CellKind.Wall;

            EditableElement preview = type.CreateEditable(new GridPoint(0, 0));
            return preview != null ? preview.Kind : CellKind.Wall;
        }

        static string LabelOf(ElementTypeSO type)
        {
            if (type == null) return "ELEMENT";
            return string.IsNullOrEmpty(type.label) ? type.Key : type.label;
        }
    }
}
