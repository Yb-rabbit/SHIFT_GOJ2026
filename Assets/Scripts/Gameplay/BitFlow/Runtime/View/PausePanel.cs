using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// Esc 暂停面板：真正停住游戏时间（<c>Time.timeScale = 0</c>），而不是只暂停模拟。
    ///
    /// 和选关面板分开做一套 Canvas（sortingOrder 35）：
    /// - 选关面板在 40，暂停时按 L 不会把选关面板叠在暂停面板上面；
    /// - 黑幕在 30，暂停面板压得住它，但黑幕演出的那几帧 <see cref="InputGate"/> 已经锁住，进不来。
    ///
    /// 打开期间用 <see cref="InputGate.Acquire"/> 挡住手牌盘 / 建造模式 / 选关热键；
    /// 关闭时恢复打开前的 <see cref="Time.timeScale"/>，不假设它一定是 1。
    /// </summary>
    public sealed class PausePanel : MonoBehaviour
    {
        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.66f);
        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.09f, 0.98f);
        static readonly Color TitleColor = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color MutedColor = new Color(0.62f, 0.72f, 0.80f, 1f);

        const float PanelWidth = 460f;
        const float PanelHeight = 400f;

        LevelFlow flow;
        Font font;

        GameObject canvasObject;
        Button resumeButton;

        bool open;
        float previousTimeScale = 1f;

        /// <summary>由 HUD 在 Start 里调用；场景里已经有就复用，不重复建 Canvas。</summary>
        public static PausePanel Ensure()
        {
            PausePanel existing = FindObjectOfType<PausePanel>();
            if (existing != null) return existing;

            GameObject go = new GameObject("PausePanel");
            return go.AddComponent<PausePanel>();
        }

        public bool IsOpen => open;

        void Awake()
        {
            BuildUi();
            canvasObject.SetActive(false);
        }

        void Start()
        {
            flow = FindObjectOfType<LevelFlow>();
        }

        void LateUpdate()
        {
            // 放在 LateUpdate：选关、建造、卡牌这几个 Update 先处理 Esc，
            // 只有没人消费时，暂停才把这一下当成"打开暂停"。
            if (open)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    EscapeInput.Consume();
                    Close();
                }
                return;
            }

            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (EscapeInput.ConsumedThisFrame) return;
            if (!CanOpen()) return;

            // 先占住这一帧的 Esc，再打开；避免同帧又有一个 LateUpdate 响应。
            if (!EscapeInput.Consume()) return;
            Open();
        }

        bool CanOpen()
        {
            if (open) return false;
            if (InputGate.Locked) return false;

            BitFlowWorkshop workshop = FindObjectOfType<BitFlowWorkshop>();
            return workshop == null || !workshop.IsOpen;
        }

        void Open()
        {
            if (open) return;

            if (flow == null) flow = FindObjectOfType<LevelFlow>();

            open = true;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            InputGate.Acquire();
            if (canvasObject != null) canvasObject.SetActive(true);

            EnsureEventSystem();
            if (EventSystem.current != null && resumeButton != null)
            {
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
            }
        }

        void Close()
        {
            if (!open) return;

            open = false;
            if (canvasObject != null) canvasObject.SetActive(false);
            InputGate.Release();
            Time.timeScale = previousTimeScale;

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void OnDisable()
        {
            Close();
        }

        void OnDestroy()
        {
            Close();
        }

        void Resume()
        {
            Close();
        }

        void Restart()
        {
            Close();

            if (flow == null) flow = FindObjectOfType<LevelFlow>();
            if (flow == null || !flow.Retry())
            {
                Debug.LogWarning("[BitFlow] 暂停面板：当前没有可重试的关卡");
            }
        }

        void OpenLevelSelect()
        {
            Close();

            LevelSelectPanel panel = FindObjectOfType<LevelSelectPanel>();
            if (panel != null) panel.Open();
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        void BuildUi()
        {
            font = PixelFont.Get();

            canvasObject = new GameObject("PauseCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;      // 低于选关 40，高于黑幕 30 / HUD 0

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            GameObject dimObject = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dimObject.transform.SetParent(canvasRect, false);

            RectTransform dimRect = dimObject.GetComponent<RectTransform>();
            SetRect(dimRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Image dimImage = dimObject.GetComponent<Image>();
            dimImage.color = DimColor;

            Button dimButton = dimObject.GetComponent<Button>();
            dimButton.targetGraphic = dimImage;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Resume);

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasRect, false);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(PanelWidth, PanelHeight), Vector2.zero);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;

            Text title = CreateText("Title", panelRect, "PAUSED", 32, TextAnchor.MiddleCenter, TitleColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(PanelWidth - 48f, 48f), new Vector2(0f, -34f));

            Text hint = CreateText("Hint", panelRect, "ESC TO RESUME", PixelFont.Small,
                TextAnchor.MiddleCenter, MutedColor);
            SetRect(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(PanelWidth - 48f, 22f), new Vector2(0f, 22f));

            float y = 76f;
            resumeButton = CreateButton("Resume", panelRect, "RESUME", y, Resume);
            y -= 68f;
            CreateButton("Restart", panelRect, "RESTART", y, Restart);
            y -= 68f;
            CreateButton("LevelSelect", panelRect, "LEVEL SELECT", y, OpenLevelSelect);
            y -= 68f;
            CreateButton("Quit", panelRect, "QUIT", y, Quit);
        }

        Button CreateButton(string name, Transform parent, string label, float y,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(280f, 52f), new Vector2(0f, y));

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.20f, 0.28f, 0.95f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", go.transform, label, PixelFont.Body,
                TextAnchor.MiddleCenter, TitleColor);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            return button;
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
