using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 角落 HUD（全部用 uGUI 的 Text，不依赖 TextMeshPro）：
    /// 左上角关卡名 + tick 计数，右上角各接收器进度，底部播放/暂停/单步/重播 + 倍速滑条，
    /// 鼠标悬停元件显示说明，结算时弹出通过/失败面板。
    /// </summary>
    public sealed class BitFlowHud : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.055f, 0.07f, 0.10f, 0.92f);
        static readonly Color TextColor = new Color(0.85f, 0.92f, 1f);
        static readonly Color AccentColor = new Color(0.35f, 0.85f, 1f);
        static readonly Color GoodColor = new Color(0.35f, 1f, 0.55f);
        static readonly Color BadColor = new Color(1f, 0.32f, 0.35f);

        // ---------------------------------------------------------------- 底部那条横带
        // 屏幕底部只有一条带子：右边是本 HUD 的控制条，左边是手牌盘（BitFlowCardTray）。
        // 两者同底边、同高度、同一种底色、左右紧贴 → 看起来就是一条横带，不再上下叠两层。
        // 112px 是算出来的上限，不是随手取的：9×8 关卡（FOV 42）最前面一排格子落在屏幕约 182px 处。
        internal const float BottomBandBottom = 26f;
        internal const float BottomBandHeight = 112f;
        internal const float BottomBarWidth = 860f;
        internal const float BottomMargin = 28f;

        BitFlowRunner runner;
        Camera mainCamera;
        Font font;

        Text titleText;
        Text statusText;
        Text tickText;
        Text speedText;
        Text pauseButtonLabel;

        RectTransform receiverPanel;
        readonly List<Text> receiverLabels = new List<Text>();

        GameObject resultPanel;
        Text resultTitle;
        Text resultDetail;
        GameObject nextLevelButton;
        LevelFlow flow;

        GameObject tooltipPanel;
        Text tooltipText;
        ElementView hoveredElement;

        bool uiPrimed;

        void Start()
        {
            runner = FindObjectOfType<BitFlowRunner>();
            mainCamera = Camera.main;
            font = LoadFont();
            flow = FindObjectOfType<LevelFlow>();

            EnsureEventSystem();
            BuildUi();

            if (runner != null)
            {
                runner.TickCompleted += OnTickCompleted;
                runner.Finished += OnFinished;
                runner.ResetPerformed += OnResetPerformed;
            }

            TryPrimeUi();
        }

        /// <summary>
        /// 把 HUD 的初始读数摆好（关卡名 / TICK / 状态 / 接收器进度）。
        /// Runner 和 HUD 挂在同一个 GameObject 上，Start 顺序不保证，所以这里可能拿到空的 Simulation，
        /// 此时返回 false，由 Update 在下一帧重试，避免玩家开局看到空面板。
        /// </summary>
        bool TryPrimeUi()
        {
            if (runner == null || runner.Simulation == null) return false;

            if (titleText != null) titleText.text = runner.LevelTitle;
            RefreshReceiverLabels(runner.Simulation);
            RefreshTickText();
            RefreshStatus();
            return true;
        }

        void OnDestroy()
        {
            if (runner == null) return;
            runner.TickCompleted -= OnTickCompleted;
            runner.Finished -= OnFinished;
            runner.ResetPerformed -= OnResetPerformed;
        }

        static Font LoadFont()
        {
            // Unity 2022 起内置字体是 LegacyRuntime.ttf（旧版是 Arial.ttf），两个都试一下
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

        void BuildUi()
        {
            // HUD 自成一体：挂在自己的子节点下，谁也不许把它当"关卡内容"清掉
            GameObject hudRoot = new GameObject("HUD");
            hudRoot.transform.SetParent(transform, false);

            GameObject canvasGo = new GameObject("HUD_Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 0 = 最顶层：结算面板和悬停提示都在这个 Canvas 里，必须压过手牌盘(-1)和作者模式(-2)，
            // 否则过关了看不见结算、鼠标移到下方面板区提示就消失。
            canvas.sortingOrder = 0;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.transform.SetParent(hudRoot.transform, false);
            Transform root = canvasGo.transform;

            BuildTopLeft(root);
            BuildReceiverPanel(root);
            BuildBottomBar(root);
            BuildResultPanel(root);
            BuildTooltip(root);
        }

        // ---------------------------------------------------------------- 左上角信息

        void BuildTopLeft(Transform root)
        {
            RectTransform panel = CreatePanel("TopLeft", root,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(430f, 96f), new Vector2(28f, -28f));

            titleText = CreateText("Title", panel, "SHIFT GO", 28, TextAnchor.UpperLeft, AccentColor);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 34f), new Vector2(16f, -10f));

            statusText = CreateText("Status", panel, "ROUTING", 18, TextAnchor.UpperLeft, TextColor);
            SetRect(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 26f), new Vector2(16f, -48f));

            tickText = CreateText("Tick", panel, "TICK 00", 18, TextAnchor.UpperLeft, TextColor);
            SetRect(tickText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 26f), new Vector2(16f, -72f));
        }

        // ---------------------------------------------------------------- 右上角接收器进度

        void BuildReceiverPanel(Transform root)
        {
            receiverPanel = CreatePanel("Receivers", root,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(280f, 150f), new Vector2(-28f, -28f));

            Text header = CreateText("Header", receiverPanel, "RECEIVERS", 18, TextAnchor.UpperLeft, AccentColor);
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 26f), new Vector2(16f, -10f));
        }

        // ---------------------------------------------------------------- 底部控制条

        void BuildBottomBar(Transform root)
        {
            // 靠右下角对齐（原来是水平居中）：居中会把手牌盘挤到上面去，那就又变成两层了
            RectTransform bar = CreatePanel("BottomBar", root,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(BottomBarWidth, BottomBandHeight),
                new Vector2(-BottomMargin, BottomBandBottom));

            Button pauseButton = CreateButton("Pause", bar, "PAUSE", new Vector2(150f, 52f), new Vector2(-330f, 0f), OnPauseClicked);
            pauseButtonLabel = pauseButton.GetComponentInChildren<Text>();

            CreateButton("Step", bar, "STEP", new Vector2(140f, 52f), new Vector2(-165f, 0f), OnStepClicked);
            CreateButton("Reset", bar, "REPLAY", new Vector2(150f, 52f), new Vector2(0f, 0f), OnResetClicked);

            speedText = CreateText("SpeedLabel", bar, "SPEED  x1.0", 18, TextAnchor.MiddleLeft, TextColor);
            SetRect(speedText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(180f, 26f), new Vector2(100f, 16f));

            CreateSlider(bar, new Vector2(220f, 24f), new Vector2(100f, -16f), 0.25f, 3f, 1f, OnSpeedChanged);
        }

        // ---------------------------------------------------------------- 结算面板

        void BuildResultPanel(Transform root)
        {
            RectTransform panel = CreatePanel("Result", root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(620f, 320f), Vector2.zero);

            resultPanel = panel.gameObject;

            // 结算面板是模态的最高层：面板自己吃掉点击（CreatePanel 默认是穿透的），
            // 否则结算摆在那儿，还能点到后面的手牌/场地，点一下又触发一次结算。
            panel.GetComponent<Image>().raycastTarget = true;

            resultTitle = CreateText("ResultTitle", panel, "PASS", 44, TextAnchor.UpperCenter, GoodColor);
            SetRect(resultTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-64f, 60f), new Vector2(0f, -24f));

            resultDetail = CreateText("ResultDetail", panel, string.Empty, 20, TextAnchor.UpperCenter, TextColor);
            SetRect(resultDetail.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-64f, 150f), new Vector2(0f, -96f));

            CreateButton("ResultReplay", panel, "REPLAY", new Vector2(200f, 56f), new Vector2(-115f, -228f), OnResetClicked);

            // 有下一关时才出现：单关演示场景里 LevelFlow 是空的，这个按钮就一直藏着
            GameObject next = CreateButton("ResultNext", panel, "NEXT LEVEL", new Vector2(220f, 56f),
                new Vector2(125f, -228f), OnNextClicked).gameObject;
            nextLevelButton = next;
            nextLevelButton.SetActive(false);

            resultPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- 悬停提示

        void BuildTooltip(Transform root)
        {
            RectTransform panel = CreatePanel("Tooltip", root,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(300f, 110f), Vector2.zero);

            tooltipText = CreateText("TooltipText", panel, string.Empty, 18, TextAnchor.UpperLeft, TextColor);
            SetRect(tooltipText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-28f, -24f), Vector2.zero);

            tooltipPanel = panel.gameObject;
            tooltipPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- 控件工厂

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
            SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, position);

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.20f, 0.28f, 0.95f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", rect, label, 20, TextAnchor.MiddleCenter, TextColor);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
        }

        Slider CreateSlider(Transform parent, Vector2 size, Vector2 position, float min, float max, float value,
            UnityAction<float> onChanged)
        {
            GameObject go = new GameObject("SpeedSlider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);

            RectTransform root = (RectTransform)go.transform;
            SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), size, position);

            // 轨道
            GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root, false);
            RectTransform backgroundRect = (RectTransform)background.transform;
            SetRect(backgroundRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 8f), Vector2.zero);
            background.GetComponent<Image>().color = new Color(0.16f, 0.24f, 0.32f, 1f);

            // 已填充部分
            GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(root, false);
            RectTransform fillAreaRect = (RectTransform)fillArea.transform;
            SetRect(fillAreaRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-18f, 8f), Vector2.zero);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillAreaRect, false);
            RectTransform fillRect = (RectTransform)fill.transform;
            SetRect(fillRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fill.GetComponent<Image>().color = AccentColor;

            // 手柄
            GameObject handleArea = new GameObject("HandleArea", typeof(RectTransform));
            handleArea.transform.SetParent(root, false);
            RectTransform handleAreaRect = (RectTransform)handleArea.transform;
            SetRect(handleAreaRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-18f, 10f), Vector2.zero);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleAreaRect, false);
            RectTransform handleRect = (RectTransform)handle.transform;
            SetRect(handleRect, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(18f, 18f), Vector2.zero);

            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = TextColor;

            Slider slider = go.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        // ---------------------------------------------------------------- 交互与刷新

        void Update()
        {
            if (!uiPrimed) uiPrimed = TryPrimeUi();

            UpdateTooltip();

            if (pauseButtonLabel != null && runner != null)
            {
                string label = runner.IsFinished ? "DONE" : (runner.IsPaused ? "PLAY" : "PAUSE");
                if (pauseButtonLabel.text != label) pauseButtonLabel.text = label;
            }
        }

        void OnPauseClicked()
        {
            if (runner != null) runner.TogglePause();
            RefreshStatus();
        }

        void OnStepClicked()
        {
            if (runner != null) runner.StepOnce();
            RefreshStatus();
        }

        void OnResetClicked()
        {
            if (resultPanel != null) resultPanel.SetActive(false);

            // 有 LevelFlow 就整关重来（预设件 + 手牌 + 撤销栈全部复原），
            // 没有就退回旧行为：只把模拟重置到 TICK 00，player 摆的东西留在场上。
            if (flow != null)
            {
                flow.Retry();
                RefreshStatus();
                return;
            }

            if (runner == null) return;
            runner.ResetLevel();
            RefreshStatus();
        }

        void OnNextClicked()
        {
            if (flow == null) return;
            if (resultPanel != null) resultPanel.SetActive(false);
            flow.Next();
        }

        void OnSpeedChanged(float value)
        {
            if (runner != null) runner.Speed = value;
            if (speedText != null) speedText.text = "SPEED  x" + value.ToString("0.00");
        }

        void UpdateTooltip()
        {
            if (tooltipPanel == null) return;
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            // 结算面板亮着的时候不弹提示：提示是先建的兄弟节点，会浮在结算上面。
            // 这一条和 ShowResult 里的 SetAsLastSibling 是同一条规则的两种写法 —— 结算最高。
            if (resultPanel != null && resultPanel.activeSelf)
            {
                if (tooltipPanel.activeSelf) tooltipPanel.SetActive(false);
                hoveredElement = null;
                return;
            }

            ElementView hovered = null;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 300f))
            {
                hovered = hit.collider.GetComponentInParent<ElementView>();
            }

            if (hovered == null || string.IsNullOrEmpty(hovered.Description))
            {
                if (tooltipPanel.activeSelf) tooltipPanel.SetActive(false);
                hoveredElement = null;
                return;
            }

            if (hovered != hoveredElement)
            {
                hoveredElement = hovered;
                tooltipText.text = hovered.Description;
            }

            tooltipPanel.SetActive(true);

            RectTransform rect = (RectTransform)tooltipPanel.transform;
            Vector2 mouse = Input.mousePosition;
            float x = Mathf.Min(mouse.x + 20f, Screen.width - rect.sizeDelta.x - 8f);
            float y = Mathf.Min(mouse.y + 20f, Screen.height - rect.sizeDelta.y - 8f);
            rect.position = new Vector3(x, y, 0f);
        }

        void OnTickCompleted(TickReport report, BitFlowSimulation simulation)
        {
            RefreshReceiverLabels(simulation);
            RefreshTickText();
            RefreshStatus();
        }

        void OnFinished(LevelResult result)
        {
            if (result == null) return;

            if (runner != null) RefreshReceiverLabels(runner.Simulation);
            RefreshTickText();
            RefreshStatus();

            // HUD 自身可能已随场景卸载，这里做防御，避免 MissingReferenceException 刷屏
            if (resultPanel == null || resultTitle == null || resultDetail == null) return;

            // 结算一出就是最高：拉到自己 Canvas 的最后一个兄弟（画在底部横带、左上信息、悬停提示之上），
            // 并顺手把悬停提示收掉 —— 提示是本 Canvas 里更晚创建的兄弟，不收起会浮在结算上面。
            resultPanel.transform.SetAsLastSibling();
            if (tooltipPanel != null) tooltipPanel.SetActive(false);

            resultPanel.SetActive(true);
            if (nextLevelButton != null)
            {
                nextLevelButton.SetActive(result.IsSuccess && flow != null && flow.HasNext);
            }
            resultTitle.text = result.IsSuccess ? "PASS" : "FAIL";
            resultTitle.color = result.IsSuccess ? GoodColor : BadColor;

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(result.IsSuccess ? "All receivers locked" : "Payload mismatch");
            builder.Append("   -   ").Append(result.TickCount).Append(" ticks\n\n");

            foreach (ReceiverResult receiver in result.Receivers)
            {
                builder.Append("RECEIVER R").Append(receiver.ReceiverId).Append("     ")
                    .Append(receiver.ReceivedCount).Append(" / ").Append(receiver.TargetCount);

                if (receiver.IsSuccess) builder.Append("    OK");
                else if (receiver.ReceivedCount > receiver.TargetCount) builder.Append("    OVERFLOW");
                else builder.Append("    BAD PAYLOAD");

                builder.Append('\n');
            }

            resultDetail.text = builder.ToString();
        }

        void OnResetPerformed()
        {
            if (resultPanel != null) resultPanel.SetActive(false);

            // 只销毁自己建出来的读数行，且先隐藏再销毁，避免同一帧出现新旧两批文字
            for (int i = 0; i < receiverLabels.Count; i++)
            {
                Text row = receiverLabels[i];
                if (row == null) continue;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            receiverLabels.Clear();

            if (runner != null)
            {
                RefreshReceiverLabels(runner.Simulation);
                RefreshTickText();
                titleText.text = runner.LevelTitle;
            }
            RefreshStatus();
        }

        void RefreshTickText()
        {
            if (runner == null || tickText == null) return;
            tickText.text = "TICK " + runner.TickIndex.ToString("00");
        }

        void RefreshStatus()
        {
            if (runner == null || statusText == null) return;

            if (runner.IsFinished) statusText.text = "SETTLED";
            else if (!runner.IsPaused) statusText.text = "ROUTING";
            else if (runner.TickIndex == 0) statusText.text = "READY  -  PRESS PLAY";
            else statusText.text = "PAUSED";
        }

        /// <summary>按接收器数量动态建行，每 tick 刷新 "3 / 6" 这类读数。</summary>
        void RefreshReceiverLabels(BitFlowSimulation simulation)
        {
            if (simulation == null || receiverPanel == null) return;

            List<ReceiverProgress> progress = simulation.GetReceiverProgress();
            while (receiverLabels.Count < progress.Count)
            {
                int index = receiverLabels.Count;
                Text row = CreateText("R" + index, receiverPanel, string.Empty, 18, TextAnchor.UpperLeft, TextColor);
                SetRect(row.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(-32f, 28f), new Vector2(16f, -44f - 30f * index));
                receiverLabels.Add(row);
            }

            receiverPanel.sizeDelta = new Vector2(receiverPanel.sizeDelta.x, 58f + 30f * receiverLabels.Count);

            for (int i = 0; i < progress.Count; i++)
            {
                ReceiverProgress entry = progress[i];
                Text row = receiverLabels[i];
                if (row == null) continue;

                ReceiverDefinition definition = runner != null ? runner.GetReceiverDefinition(entry.ReceiverId) : null;
                string name = definition != null && !string.IsNullOrEmpty(definition.Label)
                    ? definition.Label
                    : "R" + entry.ReceiverId;

                row.text = name + "    " + entry.ReceivedCount + " / " + entry.TargetCount;

                if (entry.IsOverflow || !entry.IsStillValid) row.color = BadColor;
                else if (entry.IsComplete) row.color = GoodColor;
                else row.color = TextColor;
            }
        }
    }
}
