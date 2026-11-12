using System.Collections;
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

        // ---------------------------------------------------------------- 底部控制条
        // 屏幕底部现在没有"一条横带"了：待排区搬到场景里（躺在网格前方地面上的卡排），
        // 底部中间整条让出来，控制条只收在右下角一小块，宽度从 860 压到 600（关掉倍速滑杆后进一步收到 480）。
        // 112px 是算出来的上限，不是随手取的：9×8 关卡（FOV 42）最前面一排格子从屏幕约 188px 开始。
        internal const float BottomBandBottom = 26f;
        internal const float BottomMargin = 28f;

        /// <summary>
        /// 底部控制条上的「倍速滑杆」开关。<b>暂时关掉</b>（先不给玩家调速），但整套实现一个字没删：
        /// <see cref="CreateSlider"/>、<see cref="OnSpeedChanged"/>、<c>speedText</c> 都还在，
        /// 改回 <c>true</c> 就原样恢复 —— 尺寸和按钮排布会跟着下面几个常量一起自动还原。
        /// 用 static readonly 而不是 const：const 会被编译器折叠成 if(false)，触发 CS0162 警告。
        /// </summary>
        internal static readonly bool ShowSpeedControl = false;

        /// <summary>
        /// 控制条尺寸跟着滑杆走：有滑杆时是「上排按钮 + 下排滑杆」的两行条（600×112）；
        /// 关掉后只剩一排按钮，宽度按四个按钮（PAUSE / STEP / REPLAY / 倍速）实算：
        /// 120+100+120+100 宽 + 3 个 20 的缝 + 两侧 20 边距 = 540。
        /// 待排区是在运行期读 <see cref="BottomBarScreenRect"/> 来避让的，收到哪它就跟到哪，
        /// 不需要改 BitFlowCardTray 一个字节。
        /// </summary>
        internal static readonly float BottomBandHeight = ShowSpeedControl ? 112f : 64f;
        internal static readonly float BottomBarWidth = ShowSpeedControl ? 600f : 540f;

        /// <summary>
        /// 按钮行的纵向位置（相对条中心）：两行条里贴在上一行（26），
        /// 只剩一排按钮时垂直居中（0）—— 否则按钮会顶到上边缘之外。
        /// </summary>
        static readonly float ButtonRowY = ShowSpeedControl ? 26f : 0f;

        BitFlowRunner runner;
        Camera mainCamera;
        Font font;

        Text titleText;
        Text statusText;
        Text tickText;
        Text speedText;
        Text pauseButtonLabel;
        Text speedButtonLabel;

        // 四个底部按钮的引用：工坊要把它们整套换成 DONE / TEST / CLEAR / HOME，
        // 换完还能原样换回来（退出工坊 = 玩家继续玩关）。
        Button pauseButtonControl;
        Button stepButtonControl;
        Button resetButtonControl;
        Button speedButtonControl;
        Text stepButtonLabel;
        Text resetButtonLabel;

        /// <summary>底部四个按钮现在是不是工坊动作条。工坊期间不许 <see cref="Update"/> 把标签改回 PLAY/PAUSE。</summary>
        bool workshopBar;

        RectTransform receiverPanel;
        readonly List<Text> receiverLabels = new List<Text>();

        RectTransform bottomBarRect;

        /// <summary>
        /// 控制条在屏幕上的矩形（像素，左下为原点）。待排区靠它算"卡排最多能铺多宽"，
        /// 这样窄画面里最右那张卡也不会被控制条压住 —— 两个模块互不引用具体数值，只认这条矩形。
        /// </summary>
        internal static Rect BottomBarScreenRect { get; private set; }

        static readonly Vector3[] cornerBuffer = new Vector3[4];

        GameObject resultPanel;
        Text resultTitle;
        Text resultDetail;
        GameObject nextLevelButton;
        LevelFlow flow;
        LevelSelectPanel levelSelect;

        GameObject tooltipPanel;
        Text tooltipText;
        ElementView hoveredElement;

        bool uiPrimed;

        // ---------------------------------------------------------------- 四角进出场

        /// <summary>一个角的收起/归位状态。收起时朝自己那个角退出去，而不是原地淡成透明。</summary>
        sealed class Chrome
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Vector2 home;
            public Vector2 slide;
            public float delay;
            public bool overshoot;
        }

        const float ChromeFadeDuration = 0.30f;
        const float ChromeSlideDistance = 44f;

        static readonly Color ButtonColor = new Color(0.12f, 0.20f, 0.28f, 0.95f);
        static readonly Color ButtonPulseColor = new Color(0.22f, 0.50f, 0.64f, 0.98f);

        readonly List<Chrome> chromes = new List<Chrome>();

        IntroCurtain curtain;
        Coroutine chromeRoutine;
        bool chromeHidden;

        Text playHintText;
        Vector2 playHintHome;
        Image pauseButtonImage;

        /// <summary>提示的显隐进度（0~1），由 <see cref="UpdatePlayHint"/> 每帧推进。</summary>
        float playHintShown;

        /// <summary>玩家按过一次 PLAY 就锁住，之后换关不再叫号。</summary>
        bool playHintSeen;

        void Start()
        {
            runner = FindObjectOfType<BitFlowRunner>();
            mainCamera = Camera.main;
            font = PixelFont.Get();
            flow = FindObjectOfType<LevelFlow>();
            levelSelect = FindObjectOfType<LevelSelectPanel>();

            EnsureEventSystem();
            BuildUi();
            PausePanel.Ensure();

            if (runner != null)
            {
                runner.TickCompleted += OnTickCompleted;
                runner.Finished += OnFinished;
                runner.ResetPerformed += OnResetPerformed;
            }

            TryPrimeUi();

            // 演出期间四角必须收起来（终端和黑幕都是半透明的，角落挂着读数会毁掉黑场）。
            // 这个判断放在 Start 而不是 Awake：BuildUi 刚建完，离第一帧还有整段 Start 阶段，
            // 在这里收起来玩家一帧都看不到它。
            curtain = FindObjectOfType<IntroCurtain>();
            if (curtain != null)
            {
                curtain.Finished += OnCurtainFinished;

                if (curtain.IsBusy) HideChromeNow();
            }
            else if (InputGate.Locked)
            {
                // 没有黑幕但有别的东西挡着（选关面板直接开着进关之类）
                HideChromeNow();
            }
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
            if (curtain != null) curtain.Finished -= OnCurtainFinished;

            if (runner == null) return;
            runner.TickCompleted -= OnTickCompleted;
            runner.Finished -= OnFinished;
            runner.ResetPerformed -= OnResetPerformed;
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
            // 430×76：标题一行 + 「TICK 00 ... ROUTING」一行。
            // 原来这三行是 96 高，但 tick 和状态都属于「扫一眼」的信息，
            // 并排比上下叠省一半高度 —— 这面板正好压在网格远角上，越矮越好。
            RectTransform panel = CreatePanel("TopLeft", root,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(430f, 76f), new Vector2(28f, -28f));

            titleText = CreateText("Title", panel, "SHIFT GO", PixelFont.Large, TextAnchor.UpperLeft, AccentColor);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 36f), new Vector2(16f, -8f));

            tickText = CreateText("Tick", panel, "TICK 00", PixelFont.Small, TextAnchor.UpperLeft, TextColor);
            SetRect(tickText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 24f), new Vector2(16f, -46f));

            // 状态靠右：READY / ROUTING / PAUSED / DONE，都只有一个词
            //（以前是 "READY  -  PRESS PLAY"，可下面那颗按钮就写着 PLAY，等于说了两遍）
            statusText = CreateText("Status", panel, "ROUTING", PixelFont.Small, TextAnchor.UpperRight, TextColor);
            SetRect(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-32f, 24f), new Vector2(-16f, -46f));

            // 往左上角退：方向就是它自己所在的那个角，收起来时像被屏幕边缘吸走
            RegisterChrome(panel, new Vector2(-ChromeSlideDistance, ChromeSlideDistance), 0f, false);
        }

        // ---------------------------------------------------------------- 右上角接收器进度

        void BuildReceiverPanel(Transform root)
        {
            receiverPanel = CreatePanel("Receivers", root,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(280f, 150f), new Vector2(-28f, -28f));

            Text header = CreateText("Header", receiverPanel, "RECEIVERS", PixelFont.Small, TextAnchor.UpperLeft, AccentColor);
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(-32f, 26f), new Vector2(16f, -10f));

            // 比左上角晚 0.10s：三个角一起飞会像整块 UI 一起弹出来，错峰才有"逐个就位"的秩序感
            RegisterChrome(receiverPanel, new Vector2(ChromeSlideDistance, ChromeSlideDistance), 0.10f, false);
        }

        // ---------------------------------------------------------------- 底部控制条

        void BuildBottomBar(Transform root)
        {
            // 靠右下角对齐（原来是水平居中）：居中会把手牌盘挤到上面去，那就又变成两层了
            RectTransform bar = CreatePanel("BottomBar", root,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(BottomBarWidth, BottomBandHeight),
                new Vector2(-BottomMargin, BottomBandBottom));
            bottomBarRect = bar;

            // 排布：四个按钮一行。y 走 ButtonRowY —— 有滑杆时贴上一行（26），
            // 滑杆关掉后整行垂直居中（0），否则 64 高的条里按钮会顶出上边缘。
            // x 是实算的（条宽 540 = 四个按钮 120+100+120+100 + 三个 20 的缝 + 两侧 20 边距）：
            //   PAUSE -250..-130 / STEP -110..-10 / REPLAY 10..130 / SPEED 150..250
            pauseButtonControl = CreateButton("Pause", bar, "PAUSE", new Vector2(120f, 44f), new Vector2(-190f, ButtonRowY), OnPauseClicked);
            pauseButtonLabel = pauseButtonControl.GetComponentInChildren<Text>();
            pauseButtonImage = pauseButtonControl.GetComponent<Image>();

            stepButtonControl = CreateButton("Step", bar, "STEP", new Vector2(100f, 44f), new Vector2(-60f, ButtonRowY), OnStepClicked);
            stepButtonLabel = stepButtonControl.GetComponentInChildren<Text>();

            resetButtonControl = CreateButton("Reset", bar, "REPLAY", new Vector2(120f, 44f), new Vector2(70f, ButtonRowY), OnResetClicked);
            resetButtonLabel = resetButtonControl.GetComponentInChildren<Text>();

            // 倍速：一个小按钮循环 1× → 2× → 3× → 4× → 1×。
            // 比滑杆省一块地方（不用两行条），也不会出现"拖到 1.37 倍"这种没人用得上的档位。
            speedButtonControl = CreateButton("Speed", bar, "1x", new Vector2(100f, 44f), new Vector2(200f, ButtonRowY), OnSpeedClicked);
            speedButtonLabel = speedButtonControl.GetComponentInChildren<Text>();

            // 倍速滑杆：暂时关闭（见 ShowSpeedControl），标签和滑杆一起挂在这个开关下。
            // OnSpeedChanged / CreateSlider 保留未删，重新打开时不需要再补任何东西。
            if (ShowSpeedControl)
            {
                speedText = CreateText("SpeedLabel", bar, "SPEED  x1.0", 16, TextAnchor.MiddleLeft, TextColor);
                SetRect(speedText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(160f, 22f), new Vector2(-240f, -28f));

                CreateSlider(bar, new Vector2(260f, 24f), new Vector2(-70f, -28f), 0.25f, 4f, 1f, OnSpeedChanged);
            }

            // 「从哪开始」的叫号：挂在控制条正上方，箭头朝下指着 PLAY。
            // 压在条里会挤掉按钮，飘在条外又会和网格打架，贴着上沿 +8 是唯一不挡东西的位置。
            playHintText = CreateText("PlayHint", bar, "PRESS PLAY :)", PixelFont.Small, TextAnchor.LowerRight, GoodColor);
            SetRect(playHintText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(420f, 26f), new Vector2(0f, 8f));
            playHintHome = playHintText.rectTransform.anchoredPosition;
            playHintText.enabled = false;

            // 最后一个就位，并且带过冲：全场只有这一块要玩家动手，收尾得比上面两个角重一点
            RegisterChrome(bar, new Vector2(ChromeSlideDistance, -ChromeSlideDistance), 0.20f, true);
        }

        // ---------------------------------------------------------------- 工坊动作条
        //
        // 工坊不另建一条底栏，而是<b>把现有这四颗按钮换成别的事</b>：
        // 按钮的尺寸和位置都是算死的（540×64，120+100+120+100），新起一条就要重新对一遍，
        // 还会动到 BottomBarScreenRect —— 那条矩形是待排区用来避让的，一改卡排就得重新排版。
        // 换标签 + 换监听 = 零布局风险，退出时再原样换回来。

        /// <summary>
        /// 四个按钮换成功坊动作条。顺序不变（左 → 右）：DONE / TEST / CLEAR / HOME。
        /// 传进来的都是 <see cref="BitFlowWorkshop"/> 的公开入口 —— HUD 不认识工坊内部，
        /// 只负责把点击转过去。
        /// </summary>
        public void EnterWorkshopBar(UnityAction done, UnityAction test, UnityAction clear, UnityAction home)
        {
            workshopBar = true;

            Bind(pauseButtonControl, pauseButtonLabel, "DONE", done);
            Bind(stepButtonControl, stepButtonLabel, "TEST", test);
            Bind(resetButtonControl, resetButtonLabel, "CLEAR", clear);
            Bind(speedButtonControl, speedButtonLabel, "HOME", home);

            // 工坊里没有「正放着」这件事，暂停键的呼吸高亮要收掉
            if (pauseButtonImage != null) pauseButtonImage.color = ButtonColor;
        }

        /// <summary>退出工坊：把四个按钮恢复成 PAUSE / STEP / REPLAY / 倍速。</summary>
        public void ExitWorkshopBar()
        {
            if (!workshopBar) return;
            workshopBar = false;

            Bind(pauseButtonControl, pauseButtonLabel, "PAUSE", OnPauseClicked);
            Bind(stepButtonControl, stepButtonLabel, "STEP", OnStepClicked);
            Bind(resetButtonControl, resetButtonLabel, "REPLAY", OnResetClicked);
            Bind(speedButtonControl, speedButtonLabel, SpeedLabel(), OnSpeedClicked);
        }

        /// <summary>TEST 按钮在「正在试跑」时改成 STOP，玩家一眼知道再点一下就回到编辑。</summary>
        public void SetWorkshopTesting(bool testing)
        {
            if (!workshopBar || stepButtonLabel == null) return;

            string label = testing ? "STOP" : "TEST";
            if (stepButtonLabel.text != label) stepButtonLabel.text = label;
        }

        string SpeedLabel()
        {
            float speed = runner != null ? runner.Speed : 1f;
            return Mathf.RoundToInt(speed).ToString() + "x";
        }

        static void Bind(Button button, Text label, string text, UnityAction onClick)
        {
            if (button == null) return;

            // RemoveAllListeners 而不是额外加一个：工坊反复进出时监听会一层层叠上去，
            // 最后点一次 DONE 等于把这辈子的 DONE 都执行一遍。
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            if (label != null && label.text != text) label.text = text;
        }

        void LateUpdate()
        {
            // ScreenSpaceOverlay 的 Canvas 下，GetWorldCorners() 给的就是屏幕像素（左下为原点），
            // 正是待排区需要的坐标系 —— 所以这里不用去猜 CanvasScaler 把 600 缩放成了多少。
            //
            // 但飞行途中不许发布：这条矩形是待排区的"能铺多宽"边界，
            // 跟着 ±44px 的滑入和 5% 的弹动一起动，卡排就会每 0.25s 抖一下。
            // 演出中被遮挡时它也不需要更新 —— 那段时间手牌盘本来就不排版。
            if (bottomBarRect == null || chromeHidden || chromeRoutine != null) return;

            PublishBottomBarRect();
        }

        void PublishBottomBarRect()
        {
            if (bottomBarRect == null) return;

            bottomBarRect.GetWorldCorners(cornerBuffer);
            BottomBarScreenRect = new Rect(
                cornerBuffer[0].x,
                cornerBuffer[0].y,
                cornerBuffer[2].x - cornerBuffer[0].x,
                cornerBuffer[2].y - cornerBuffer[0].y);
        }

        // ---------------------------------------------------------------- 四角进出场
        //
        // 演出（开场终端 / 换关黑幕）期间四角必须收起来：这两层都是半透明的，
        // 角落挂着 "TICK 00"、"RECEIVERS" 和四个按钮，观众会以为游戏已经开始了。
        // 收的时候朝各自的角退出去（不是原地淡掉）——"被屏幕边缘吸走"读起来是演出开始，
        // 原地淡掉读起来只是 UI 消失。
        //
        // 归位错峰 0 / 0.10 / 0.20s，右下角控制条压轴且带过冲：
        // 三个角同时飞回来像整块 UI 弹了一下，错峰才有"逐个就位"的秩序感。

        void RegisterChrome(RectTransform rect, Vector2 slide, float delay, bool overshoot)
        {
            if (rect == null) return;

            CanvasGroup group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();

            chromes.Add(new Chrome
            {
                rect = rect,
                group = group,
                home = rect.anchoredPosition,
                slide = slide,
                delay = delay,
                overshoot = overshoot
            });
        }

        /// <summary>把某个角摆到进度 <paramref name="t"/>：0 = 收在角落外，1 = 原位。</summary>
        static void PlaceChrome(Chrome chrome, float t)
        {
            t = Mathf.Clamp01(t);

            // 位置可以过冲，透明度不行：alpha 过冲会被 Clamp01 吃掉，看起来就是"卡在 1 不动"
            float eased = chrome.overshoot ? EaseOutBack(t) : EaseOutCubic(t);

            chrome.group.alpha = t;
            chrome.group.interactable = t > 0.6f;
            chrome.group.blocksRaycasts = t > 0.6f;
            chrome.rect.anchoredPosition = chrome.home + chrome.slide * (1f - eased);
        }

        /// <summary>立刻收起（不演动画）。只在开场那一帧用 —— 那一帧还没画出来，演也没人看得见。</summary>
        void HideChromeNow()
        {
            if (chromeRoutine != null)
            {
                StopCoroutine(chromeRoutine);
                chromeRoutine = null;
            }

            chromeHidden = true;
            playHintShown = 0f;

            // 先把"归位状态"的矩形交出去（此刻控制条还在原位），再把它滑走。
            // 待排区整场演出都读这条，收起来之后 LateUpdate 就不再发布了。
            PublishBottomBarRect();

            foreach (Chrome chrome in chromes) PlaceChrome(chrome, 0f);

            if (playHintText != null) playHintText.enabled = false;
            if (pauseButtonImage != null) pauseButtonImage.color = ButtonColor;

            // 控制条可能正被 PlayStartNudge 缩放，收起来时抹平，别让它带着残值起飞
            if (bottomBarRect != null) bottomBarRect.localScale = Vector3.one;
        }

        IEnumerator RevealChromeRoutine()
        {
            chromeHidden = false;

            foreach (Chrome chrome in chromes) PlaceChrome(chrome, 0f);

            float end = 0f;
            foreach (Chrome chrome in chromes) end = Mathf.Max(end, chrome.delay + ChromeFadeDuration);

            // unscaled：暂停 / 改 timeScale 都不该把 UI 卡在半路上
            float elapsed = 0f;
            while (elapsed < end)
            {
                elapsed += Time.unscaledDeltaTime;

                foreach (Chrome chrome in chromes) PlaceChrome(chrome, (elapsed - chrome.delay) / ChromeFadeDuration);

                yield return null;
            }

            foreach (Chrome chrome in chromes) PlaceChrome(chrome, 1f);

            // 压轴：右下角控制条单独再弹一下。只在还没开始玩的时候演
            if (!playHintSeen) yield return PlayStartNudge();

            chromeRoutine = null;
        }

        /// <summary>控制条归位之后再弹一次（1 → 1.05 → 1，一次衰减正弦），把眼睛拽到 PLAY 上。</summary>
        IEnumerator PlayStartNudge()
        {
            if (bottomBarRect == null) yield break;

            float duration = 0.32f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float scale = 1f + 0.05f * Mathf.Sin(k * Mathf.PI * 2f) * (1f - k);

                bottomBarRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            bottomBarRect.localScale = Vector3.one;
        }

        void OnCurtainFinished()
        {
            if (!chromeHidden || chromeRoutine != null) return;

            chromeRoutine = StartCoroutine(RevealChromeRoutine());
        }

        /// <summary>
        /// 右下角那颗 PLAY 的专属提示：文字淡入淡出 + 箭头上下浮 + 按钮呼吸。
        /// 条件盯着 runner 而不是自己记状态 —— 一旦真的跑起来（TickIndex &gt; 0）就自动收，
        /// 玩家用键盘/重播/任何别的方式起步都不会漏掉这一步。
        /// </summary>
        void UpdatePlayHint()
        {
            if (runner != null && runner.TickIndex > 0) playHintSeen = true;

            bool want = !chromeHidden
                        && !playHintSeen
                        && !workshopBar      // 工坊里那颗按钮写着 DONE，招呼它「PRESS PLAY」是错的
                        && runner != null
                        && !runner.IsFinished
                        && runner.TickIndex == 0
                        && runner.IsPaused;

            playHintShown = Mathf.MoveTowards(playHintShown, want ? 1f : 0f, Time.unscaledDeltaTime / 0.25f);

            if (playHintText != null)
            {
                bool visible = playHintShown > 0.001f;
                if (playHintText.enabled != visible) playHintText.enabled = visible;

                if (visible)
                {
                    // 只浮文字，不动控制条：按钮跟着抖会读成"UI 坏了"
                    playHintText.rectTransform.anchoredPosition = playHintHome
                        + new Vector2(0f, Mathf.Sin(Time.unscaledTime * 4.2f) * 3f * playHintShown);

                    Color color = GoodColor;
                    color.a = playHintShown * (0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 4.2f));
                    playHintText.color = color;
                }
            }

            if (pauseButtonImage != null)
            {
                float pulse = playHintShown * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f));
                pauseButtonImage.color = Color.Lerp(ButtonColor, ButtonPulseColor, pulse * 0.85f);
            }
        }

        static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>冲过头再回落：多冲约 8%，够看出"弹"，又不会撞到屏幕边缘。</summary>
        static float EaseOutBack(float t)
        {
            float u = t - 1f;
            return 1f + 2.7f * u * u * u + 1.7f * u * u;
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

            resultTitle = CreateText("ResultTitle", panel, "PASS", 40, TextAnchor.UpperCenter, GoodColor);
            SetRect(resultTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-64f, 60f), new Vector2(0f, -24f));

            resultDetail = CreateText("ResultDetail", panel, string.Empty, PixelFont.Body, TextAnchor.UpperCenter, TextColor);
            SetRect(resultDetail.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-64f, 150f), new Vector2(0f, -96f));

            // 三个按钮并排：REPLAY（重开本关）/ LEVELS（选关）/ NEXT LEVEL（有下一关才出现）
            CreateButton("ResultReplay", panel, "REPLAY", new Vector2(150f, 56f), new Vector2(-190f, -228f), OnResetClicked);
            CreateButton("ResultLevels", panel, "LEVELS", new Vector2(150f, 56f), new Vector2(-30f, -228f), OnLevelsClicked);

            // 有下一关时才出现：单关演示场景里 LevelFlow 是空的，这个按钮就一直藏着
            GameObject next = CreateButton("ResultNext", panel, "NEXT LEVEL", new Vector2(170f, 56f),
                new Vector2(145f, -228f), OnNextClicked).gameObject;
            nextLevelButton = next;
            nextLevelButton.SetActive(false);

            resultPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- 悬停提示

        void BuildTooltip(Transform root)
        {
            // 380 宽是按最长的一条元件说明算的：「NEED 101101 x1  ANY FACE」26 字符，
            // 20 号像素字体每字符 12.5px → 325px，留 28px 内边距正好装下。
            RectTransform panel = CreatePanel("Tooltip", root,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(380f, 104f), Vector2.zero);

            tooltipText = CreateText("TooltipText", panel, string.Empty, PixelFont.Body, TextAnchor.UpperLeft, TextColor);
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
            image.color = ButtonColor;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", rect, label, PixelFont.Body, TextAnchor.MiddleCenter, TextColor);
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

            // 黑幕一盖上就把四角收走：开场终端和换关都走这条。
            // （Curtain 开演的第一件事就是 SetDim(1f) 把屏幕刷黑，所以这里"瞬间收"看不见。）
            if (curtain != null && curtain.IsBusy && !chromeHidden) HideChromeNow();

            // 兜底：黑幕被外部掐掉（关组件 / 切场景 / 演到一半 Destroy）时，
            // 不能把四角永远锁在隐藏状态 —— 那就成了"开场之后没有 HUD"。
            if (chromeHidden && chromeRoutine == null && !InputGate.Locked && (curtain == null || !curtain.IsBusy))
            {
                chromeRoutine = StartCoroutine(RevealChromeRoutine());
            }

            UpdatePlayHint();

            if (!workshopBar && pauseButtonLabel != null && runner != null)
            {
                string label = runner.IsFinished ? "DONE" : (runner.IsPaused ? "PLAY" : "PAUSE");
                if (pauseButtonLabel.text != label) pauseButtonLabel.text = label;
            }

            if (!workshopBar) RefreshSpeedLabel();
        }

        void OnPauseClicked()
        {
            // 玩家已经找到这颗按钮了，招呼可以收了（换关也不再叫号）
            playHintSeen = true;

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

        /// <summary>
        /// 打开选关面板。结算面板留在原地不动 —— 选关面板是更上层的 Canvas，
        /// 玩家从选关里退回来还能接着看结算、点 NEXT。
        /// </summary>
        void OnLevelsClicked()
        {
            if (levelSelect == null) levelSelect = FindObjectOfType<LevelSelectPanel>();
            if (levelSelect != null) levelSelect.Open();
        }

        void OnSpeedChanged(float value)
        {
            if (runner != null) runner.Speed = value;
            if (speedText != null) speedText.text = "SPEED  x" + value.ToString("0.00");
            RefreshSpeedLabel();
        }

        /// <summary>倍速按钮：1× → 2× → 3× → 4× → 1×。换关时 <see cref="BitFlowRunner.LoadLevel"/> 会把速度归 1，标签跟着走。</summary>
        void OnSpeedClicked()
        {
            if (runner == null) return;

            int next = Mathf.RoundToInt(runner.Speed) + 1;
            if (next > 4) next = 1;

            runner.Speed = next;
            RefreshSpeedLabel();
        }

        /// <summary>标签每帧跟 runner 的真实速度对齐，而不是自己维护一份计数 —— 换关、重播都不会读错。</summary>
        void RefreshSpeedLabel()
        {
            if (speedButtonLabel == null || runner == null) return;

            string label = Mathf.RoundToInt(runner.Speed) + "x";
            if (speedButtonLabel.text != label) speedButtonLabel.text = label;
        }

        void UpdateTooltip()
        {
            if (tooltipPanel == null) return;
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            // 结算面板亮着，或者任何一层输入锁（暂停 / 选关 / 工坊 / 开场黑幕）生效时都不弹提示。
            // HUD 的 Canvas 在底层，提示又是先建的兄弟节点，只判断结算面板会让它从半透明幕布里透出来。
            if ((resultPanel != null && resultPanel.activeSelf) || InputGate.Locked)
            {
                HideTooltip();
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
                HideTooltip();
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

        /// <summary>关掉提示并忘掉悬停对象：下一帧重新射线，避免旧提示在锁解除后闪一下。</summary>
        void HideTooltip()
        {
            if (tooltipPanel != null && tooltipPanel.activeSelf) tooltipPanel.SetActive(false);
            hoveredElement = null;
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

            // 工坊试跑不弹结算面板：玩家只是想知道机器跑不跑得起来，
            // 弹一个 PASS/FAIL 盖住网格，还得再点一次关掉。结论由工坊那行状态文字说。
            // 结算音也一起省掉：试跑是调试动作，响一声反而像正式过关。
            if (workshopBar) return;

            // 结算音放在面板判空之前：HUD 面板万一没建起来，结论也还能靠耳朵听到。
            if (result.IsSuccess) BitFlowSfx.LevelSuccess();
            else BitFlowSfx.LevelFail();

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

            // 通关就落盘：选关面板靠这份存档打勾。失败什么都不写，
            // 所以"重试一次就成功"和"一次成功"在存档里长得一样。
            if (result.IsSuccess)
            {
                // JSON 关必须按文件路径记：它可能和内置关重名（从内置关导出的副本），
                // 按 runner.LevelTitle 记会让副本继承内置关的勾。
                if (flow != null && flow.IsJsonLevel)
                {
                    SaveStore.MarkJsonCleared(flow.CurrentJsonPath);
                }
                else
                {
                    SaveStore.MarkCleared(runner != null ? runner.LevelTitle : result.DisplayName);
                }

                if (levelSelect != null) levelSelect.Refresh();
            }

            // 只留两行有用信息：结论 + 用了几个 tick，下面是每台接收器的读数。
            // 以前的 "ALL RECEIVERS LOCKED   -   12 TICKS" 只是把 PASS 又用长句说了一遍。
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            if (result.IsSuccess)
            {
                builder.Append("CLEARED");
            }
            else
            {
                builder.Append(CountPassed(result)).Append("/").Append(result.Receivers.Count).Append(" OK");
            }

            builder.Append("\n").Append(result.TickCount).Append(" TICKS\n\n");

            // 明细按 receiver 列表顺序列，但不写编号（理由同 HUD 右上角）：
            // 玩家能对照的只有"收到几个 / 该收几个"，以及场上那台正在红闪的接收器。
            foreach (ReceiverResult receiver in result.Receivers)
            {
                builder.Append(receiver.ReceivedCount).Append("/").Append(receiver.TargetCount);

                // 状态用 OK / OVER / MISS 三个短词：像素字体里没有 ✓ ✗ 这两个字形
                if (receiver.IsSuccess) builder.Append("   OK");
                else if (receiver.ReceivedCount > receiver.TargetCount) builder.Append("   OVER");
                else builder.Append("   MISS");

                builder.Append('\n');
            }

            resultDetail.text = builder.ToString();
        }

        /// <summary>结算里"几台接收器达标"的分子。判定规则和 <see cref="ReceiverResult.IsSuccess"/> 完全一致。</summary>
        static int CountPassed(LevelResult result)
        {
            int passed = 0;
            foreach (ReceiverResult receiver in result.Receivers)
            {
                if (receiver.IsSuccess) passed++;
            }
            return passed;
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

            // 一个词说完。以前的 "READY  -  PRESS PLAY" 是把操作提示写进了状态行，
            // 而底部那颗按钮上就写着 PLAY —— 同一句话说两遍，还占掉一行宽度。
            if (runner.IsFinished) statusText.text = "DONE";
            else if (!runner.IsPaused) statusText.text = "ROUTING";
            else if (runner.TickIndex == 0) statusText.text = "READY";
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
                Text row = CreateText("R" + index, receiverPanel, string.Empty, PixelFont.Body, TextAnchor.UpperLeft, TextColor);
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

                // 这里不写编号：编号只是运行时 Id，同一类型的接收器长得一模一样，
                // 写出来玩家也对不上号（以前 HUD 写类型标签、结算写全局 Id、提示写 #Id，三处还各不相同）。
                // 哪台接收器不匹配，由场上那台红闪来说明就够了。
                // 对勾/叉号不用 ✓ ✗：像素字体里没有这两个字形（打包后会变方块），
                // 用 OK / X 两个字母代替，颜色仍然承担主要区分（绿=达标，红=出错）。
                string state = entry.IsOverflow || !entry.IsStillValid ? "  X" : (entry.IsComplete ? "  OK" : string.Empty);
                row.text = entry.ReceivedCount + "/" + entry.TargetCount + state;

                if (entry.IsOverflow || !entry.IsStillValid) row.color = BadColor;
                else if (entry.IsComplete) row.color = GoodColor;
                else row.color = TextColor;
            }
        }
    }
}
