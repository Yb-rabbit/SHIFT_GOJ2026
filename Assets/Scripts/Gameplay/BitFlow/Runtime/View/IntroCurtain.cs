using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 换关黑幕：把"上一关结算的残影 + 新关卡忽然出现"藏起来，顺便在镜头推近之前交代本关的三件事 ——
    /// 关名、这次<b>第一次出现</b>的元件（带一行说明）、本关这一趟要做什么
    /// （发牌的关手牌清单 / 沙盒关 / 演示关，见 <see cref="DescribeLine"/>）。
    ///
    /// 时间轴（默认 2.5s，三段都在 Inspector 里调）：
    /// <code>
    /// 0.0 ─ 0.6   全黑淡到 72% 不透明，标题同时淡入（此时关卡已经在后面建好了）
    /// 0.6 ─ 1.4   停留：读关名 / 新元件 / 手牌
    /// 1.4 ─ 2.5   相机开始推近，黑幕与文字一起退场；演完才放开输入
    /// </code>
    ///
    /// 两个刻意的设计：
    /// 1. <b>黑幕里不出现任何编号</b>。编号是运行时 Id，同一类型的几台元件长得一模一样，
    ///    写出来玩家也对不上号；新元件靠"类型名 + 一行说明"介绍，场上靠外形和颜色对应。
    /// 2. <b>输入用 <see cref="InputGate"/> 挡，不用 <c>enabled = false</c></b>。
    ///    手牌盘在 Start 里订阅了 LevelFlow.LevelLoaded，关组件会连带停掉换关后的刷新。
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class IntroCurtain : MonoBehaviour
    {
        [Header("时间轴（秒）")]
        [Tooltip("0 → 这个时刻：全黑慢慢淡到半透明，标题同时淡入")]
        [SerializeField, Min(0f)] float fadeInDuration = 0.6f;

        [Tooltip("半透明黑幕 + 内容停留多久（玩家读关名和新元件的时间）")]
        [SerializeField, Min(0f)] float holdDuration = 0.8f;

        [Tooltip("镜头推近的同时，黑幕与文字一起淡出的时长")]
        [SerializeField, Min(0f)] float fadeOutDuration = 1.1f;

        [Header("外观")]
        [Tooltip("停留阶段黑幕的不透明度。留一点透明度，能隐约看见后面已经建好的关卡")]
        [SerializeField, Range(0f, 1f)] float holdAlpha = 0.72f;

        [Tooltip("进场自动演一次。关掉只用于调试（比如想直接看清网格）")]
        [SerializeField] bool playOnStart = true;

        static readonly Color DimColor = Color.black;
        static readonly Color TitleColor = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color MutedColor = new Color(0.62f, 0.72f, 0.80f, 1f);
        static readonly Color HandColor = new Color(0.78f, 0.86f, 0.92f, 1f);

        LevelFlow flow;
        BitFlowRunner runner;
        CameraRig cameraRig;

        Image dim;
        CanvasGroup content;
        Text titleText;
        Text handText;
        RectTransform chipRow;

        Font font;
        Coroutine routine;
        bool holdsGate;

        /// <summary>
        /// 由外部演出（开场终端 <see cref="IntroTerminal"/>）驱动：先别盖幕、先别开演，
        /// 等它 <see cref="Cue"/> 一下再走正常时间轴。
        ///
        /// 由 IntroTerminal 在自己的 Awake 里置 true —— 它执行顺序 -30，比这里的 -20 早，
        /// 所以这里 Awake 读到的一定是已经设好的值。
        /// </summary>
        public bool WaitForCue { get; set; }

        /// <summary>
        /// 是不是还占着画面（等 cue / 正在演 / 还捏着输入锁）。HUD 靠它决定四角要不要亮着：
        /// 演出期间屏幕角落挂着 TICK 00 和 RECEIVERS 会毁掉那块黑场。
        /// </summary>
        public bool IsBusy => WaitForCue || holdsGate || routine != null;

        /// <summary>
        /// 整段黑幕演完、输入已经放开。<b>每关都会触发一次</b> ——
        /// HUD 用它把四角错峰飞回来，不需要自己去猜演出什么时候结束。
        /// </summary>
        public event Action Finished;

        /// <summary>WaitForCue 期间攒下的关卡，Cue 的时候再演。</summary>
        LevelSO pendingLevel;

        void Awake()
        {
            // Awake 里就盖住：LevelFlow.Start（执行顺序 -10）会在第一帧把关卡建出来，
            // 黑幕必须比它更早出现在屏幕上，否则玩家会看到一闪而过的空网格。
            BuildUi();

            // 前面有人在演（开场终端）：这时候不能盖幕 —— 黑幕 sortingOrder 30 比终端 25 高，
            // 一盖上去终端就演在后面了。
            if (WaitForCue)
            {
                SetDim(0f);
                SetContent(0f);
                return;
            }

            SetDim(1f);
            SetContent(0f);
        }

        void Start()
        {
            flow = FindFirstObjectByType<LevelFlow>();
            runner = FindFirstObjectByType<BitFlowRunner>();
            cameraRig = FindFirstObjectByType<CameraRig>();

            // 有外部演出（开场终端）盖在前面时，先把镜头摆到开场远景：
            // 终端是半透明的，背景里的机器和网格必须已经是"开场机位"，
            // 否则玩家会先看到一个编辑器视角，再在退场时被拽一次。
            if (WaitForCue && cameraRig != null) cameraRig.Prepare();

            if (flow != null)
            {
                flow.LevelLoaded += OnLevelLoaded;

                // LevelFlow 的 Start 比这里早，第一次 LevelLoaded 已经错过了 —— 这里补一次
                if (flow.Current != null)
                {
                    RequestPlay(flow.Current);
                    return;
                }

                // 开场演出还没演完（关卡被它按住了，所以 Current 还是空的）：
                // 这里什么都不做，等它演完调 Cue()
                if (WaitForCue) return;
            }
            else if (runner != null && runner.LevelAsset != null)
            {
                RequestPlay(runner.LevelAsset);
                return;
            }

            // 前面还有一段演出（开场终端）在等：这会儿撤幕就等于把画面提前交给玩家了
            if (WaitForCue) return;

            // 场景里没有可演的关卡（单关演示场景正在跑作者模式之类）：直接把幕撤掉
            Finish();
        }

        void OnDestroy()
        {
            if (flow != null) flow.LevelLoaded -= OnLevelLoaded;

            // 演出中途被卸载（换场景）时别把输入锁留在一个已经死掉的组件上
            DropGate();
        }

        void OnLevelLoaded(LevelSO level)
        {
            RequestPlay(level);
        }

        /// <summary>
        /// 外部演出演完了，由 <see cref="IntroTerminal"/> 调一下：现在才轮到开场黑幕。
        /// 没攒下关卡就直接撤幕 —— 宁可少演一段，也不能让玩家对着黑屏干等。
        /// </summary>
        public void Cue()
        {
            WaitForCue = false;

            if (pendingLevel != null)
            {
                Begin(pendingLevel);
                return;
            }

            Finish();
        }

        /// <summary>该演一关了。WaitForCue 期间只记不演，等 <see cref="Cue"/>。</summary>
        void RequestPlay(LevelSO level)
        {
            if (WaitForCue)
            {
                pendingLevel = level;
                return;
            }

            Begin(level);
        }

        /// <summary>
        /// 立刻盖上全黑 + 挡住输入，但<b>不</b>开始演出。
        /// 选关面板切关时用：先盖幕再让 LevelFlow 换关，玩家就不会看见新关卡"啪"地跳出来，
        /// 随后 <see cref="LevelFlow.LevelLoaded"/> 会接着把整段演出演完。
        /// </summary>
        public void CoverImmediately()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            HoldGate();
            SetDim(1f);
            SetContent(0f);
        }

        void Begin(LevelSO level)
        {
            if (!playOnStart)
            {
                Finish();
                return;
            }

            Fill(level);

            // 幕盖上的这一刻就把镜头摆到开场机位并冻在那里：读数据、生成元件的整段时间镜头一动不动，
            // 只有 PlayIntro 的最后一段才推近。否则镜头会在幕后先自己推完一遍，
            // 退场时再被拽回远景重推 —— 那就是玩家看到的「开场卡一下」。
            if (cameraRig != null) cameraRig.Prepare();

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(PlayIntro());
        }

        IEnumerator PlayIntro()
        {
            HoldGate();

            SetDim(1f);
            SetContent(0f);

            // 阶段 1：黑幕淡到半透明，文字同时淡入
            yield return Fade(fadeInDuration, 1f, holdAlpha, 0f, 1f);

            // 阶段 2：停留
            if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

            // 阶段 3：镜头推近 + 黑幕退场
            if (cameraRig != null) cameraRig.Play();
            yield return Fade(fadeOutDuration, holdAlpha, 0f, 1f, 0f);

            routine = null;
            DropGate();

            Finished?.Invoke();
        }

        /// <summary>
        /// 一边推进 dim / content，一边等时间。<b>用 unscaledDeltaTime</b>：
        /// 暂停或改 timeScale 都不该把黑幕卡在半路上。
        /// </summary>
        IEnumerator Fade(float duration, float dimFrom, float dimTo, float contentFrom, float contentTo)
        {
            if (duration <= 0f)
            {
                SetDim(dimTo);
                SetContent(contentTo);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                SetDim(Mathf.Lerp(dimFrom, dimTo, t));
                SetContent(Mathf.Lerp(contentFrom, contentTo, t));
                yield return null;
            }

            SetDim(dimTo);
            SetContent(contentTo);
        }

        /// <summary>没有可演的关卡 / 演出被关掉时：撤幕、放开输入，一帧都不占着屏幕。</summary>
        void Finish()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            SetDim(0f);
            SetContent(0f);
            DropGate();

            Finished?.Invoke();
        }

        void HoldGate()
        {
            if (holdsGate) return;

            holdsGate = true;
            InputGate.Acquire();
        }

        void DropGate()
        {
            if (!holdsGate) return;

            holdsGate = false;
            InputGate.Release();
        }

        // ---------------------------------------------------------------- 内容

        void Fill(LevelSO level)
        {
            titleText.text = level != null && !string.IsNullOrEmpty(level.displayName)
                ? level.displayName
                : "BIT FLOW";

            BuildNewElementChips(level);
            handText.text = DescribeLine(level);
        }

        /// <summary>
        /// 黑幕底部那一行：本关"进来该干什么"。
        ///
        /// 早先这里恒拼「HAND aa x1」，没发牌的关就退化成「HAND  -」——
        /// 那既不是一份手牌清单，也不像一句人话；而目录第一关（主页那一关，纯演示关）
        /// 正好没有手牌，玩家一进门看到的就是它，看起来像是文字被截断了。
        ///
        /// 现在按关卡数据分三档说：发了牌 → 手牌清单；没发牌但允许自己摆 → 沙盒关；
        /// 其余 → 演示关，直接把唯一的入口（PLAY 那颗按钮）写在脸上。
        /// </summary>
        string DescribeLine(LevelSO level)
        {
            if (level != null && level.HasSupplies)
            {
                string hand = DescribeHand(level);
                if (!string.IsNullOrEmpty(hand)) return hand;
            }

            // 关名下面的这一行一律只写英文 ASCII：像素字体没有中文字形（见 PixelFont）。
            if (level != null && level.allowBuild) return "SANDBOX  -  BUILD, THEN PRESS PLAY";

            // 目录第一关就是主页那一关（纯演示、没有手牌，唯一的入口是 PLAY）：
            // 顺手把"再往下怎么走"也点出来，第一关按完 PASS 的去处就是 LEVELS 按钮。
            // 运行时 JSON 关卡没有"第几关"这回事，一律按下面的演示关处理。
            if (flow != null && !flow.IsJsonLevel && flow.CurrentIndex == 0 && flow.Current == level)
            {
                return "WELCOME  -  PRESS PLAY, THEN PICK A LEVEL";
            }

            // 玩家自己存 / 导入的关卡：不是内置演示关，别再叫它 DEMO。
            // 这里只写「自定义关卡」，具体的组队/解题提示交给关卡本身的布局。
            if (flow != null && flow.IsJsonLevel)
            {
                return "CUSTOM LEVEL  -  PRESS PLAY TO WATCH";
            }

            return "DEMO  -  PRESS PLAY TO WATCH";
        }

        /// <summary>
        /// 本关手牌的第一眼交代。同一类型写了两条 supply 是"再多给几张"（<see cref="LevelDraft"/> 就是这么记账的），
        /// 所以这里按 Key 去重合并后再累加，和状态行、卡排上的数字永远一致。
        /// 一条能用的都没有时返回空串，由 <see cref="DescribeLine"/> 换成一句关卡说明。
        /// </summary>
        static string DescribeHand(LevelSO level)
        {
            if (level == null || level.supplies == null || level.supplies.Count == 0) return string.Empty;

            List<string> order = new List<string>();
            Dictionary<string, int> counts = new Dictionary<string, int>();
            Dictionary<string, string> names = new Dictionary<string, string>();

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;

                string key = supply.type.Key;
                if (!counts.ContainsKey(key))
                {
                    order.Add(key);
                    counts[key] = 0;
                    names[key] = string.IsNullOrEmpty(supply.type.label) ? supply.type.name : supply.type.label;
                }

                counts[key] += Mathf.Max(0, supply.count);
            }

            // 全是被扣掉 / 为空的条目：没有清单可说，交给调用方换一句关卡说明
            if (order.Count == 0) return string.Empty;

            // 乘号写 x 不写 ×：像素字体里没有 U+00D7（打包后会变方块）。
            System.Text.StringBuilder builder = new System.Text.StringBuilder("HAND  ");
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) builder.Append("   ");
                builder.Append(names[order[i]]).Append(" x").Append(counts[order[i]]);
            }
            return builder.ToString();
        }

        /// <summary>
        /// 本次<b>第一次出现</b>的元件：一块类型色的方块 + "类型名 label" + 一行说明。
        /// 见过的类型不再提示（<see cref="SaveStore"/> 记着），所以第二关之后黑幕基本只剩关名和手牌。
        /// </summary>
        void BuildNewElementChips(LevelSO level)
        {
            for (int i = chipRow.childCount - 1; i >= 0; i--)
            {
                Destroy(chipRow.GetChild(i).gameObject);
            }

            HashSet<string> keys = new HashSet<string>();
            List<ElementTypeSO> fresh = new List<ElementTypeSO>();

            if (level != null && level.supplies != null)
            {
                foreach (ElementSupply supply in level.supplies)
                {
                    if (supply == null || supply.type == null) continue;
                    if (!keys.Add(supply.type.Key)) continue;              // 同一类型写两条只算一次
                    if (SaveStore.IsSeen(supply.type.Key)) continue;

                    fresh.Add(supply.type);
                }
            }

            // 先记后画：这一帧已经抓好列表，记完立刻落盘，下次进来就不重复介绍了
            SaveStore.MarkSeen(keys);

            if (fresh.Count == 0) return;

            // 最多摆三张：黑幕上塞满反而没人看
            int shown = Mathf.Min(fresh.Count, 3);
            for (int i = 0; i < shown; i++) CreateChip(fresh[i], i, shown);
        }

        void CreateChip(ElementTypeSO type, int index, int count)
        {
            // 一行最多三张：以行为中心左右撑开。356 是实算的 ——
            // 320 的 chip 之间要留 36 的空，三张总共 1052，正好塞进 1100 宽的内容区。
            float x = (index - (count - 1) * 0.5f) * 356f;

            GameObject chip = new GameObject("Chip", typeof(RectTransform));
            chip.transform.SetParent(chipRow, false);

            RectTransform rect = chip.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(340f, 92f);
            rect.anchoredPosition = new Vector2(x, 0f);

            // 色块贴着 chip 左边放：CreateImage 默认是按中心锚点摆的，
            // 这里必须再用 PlaceLeft 掰到左侧，否则会和右边的文字叠在一起
            Image swatch = CreateImage("Swatch", rect, KindColorOf(type), new Vector2(56f, 56f), Vector2.zero);
            PlaceLeft(swatch.rectTransform, 12f, 0f, 56f, 56f);

            string description = type.Describe();
            string[] lines = description.Split('\n');
            string head = lines.Length > 0 ? lines[0] : description;
            string detail = lines.Length > 1 ? lines[1] : string.Empty;

            // head 用正文档、detail 用小字档，两档都是像素字体的整数倍字号（不会糊）。
            // detail 宽 248 是按最长的一条算的：「NEED 101101 x1  ANY FACE」23 字符 × 10px = 230。
            Text headText = CreateText("Head", rect, head, PixelFont.Body, TextAnchor.MiddleLeft, TitleColor);
            PlaceLeft(headText.rectTransform, 84f, 20f, 248f, 26f);

            Text detailText = CreateText("Detail", rect, detail, PixelFont.Small, TextAnchor.MiddleLeft, MutedColor);
            PlaceLeft(detailText.rectTransform, 84f, -12f, 248f, 22f);

            swatch.raycastTarget = false;
        }

        /// <summary>
        /// 类型资产 → 本体颜色。走 <see cref="LevelBuilder.KindColor"/>，和场上那件元件的颜色完全一致，
        /// 所以"黑幕上看到的方块颜色"和"网格里要找的东西"是同一个色。
        /// </summary>
        static Color KindColorOf(ElementTypeSO type)
        {
            try
            {
                ElementDefinition definition = type.CreateDefinition(new GridPoint(0, 0), 0, 0);
                if (definition != null) return LevelBuilder.KindColor(definition.Kind);
            }
            catch (System.Exception)
            {
                // 类型资产配置不全时 CreateDefinition 可能抛异常：黑幕不能因此整个不演
            }

            return MutedColor;
        }

        // ---------------------------------------------------------------- 搭建（和 HUD 同一套写法，不依赖场景里有没有 Canvas）

        void BuildUi()
        {
            font = PixelFont.Get();

            GameObject canvasObject = new GameObject("IntroCurtainCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;      // 盖住 HUD（0），但要被选关面板（40）盖住

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            // 全屏黑幕。挡手牌输入靠的是 InputGate，这一层只管挡住鼠标
            //（raycastTarget 由 SetDim 按可见度开关，全透明时一定放开）。
            dim = CreateImage("Dim", canvasRect, DimColor, Vector2.zero, Vector2.zero);
            SetRect(dim.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup));
            contentObject.transform.SetParent(canvasRect, false);

            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            SetRect(contentRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(1100f, 420f), Vector2.zero);

            content = contentObject.GetComponent<CanvasGroup>();
            content.blocksRaycasts = false;
            content.interactable = false;

            titleText = CreateText("Title", contentRect, "BIT FLOW", 48, TextAnchor.MiddleCenter, TitleColor);
            SetRect(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(1100f, 74f), new Vector2(0f, -34f));

            Image rule = CreateImage("Rule", contentRect, new Color(0.35f, 0.55f, 0.65f, 0.9f), new Vector2(240f, 2f), Vector2.zero);
            SetRect(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(240f, 2f), new Vector2(0f, -116f));

            GameObject chipHolder = new GameObject("Chips", typeof(RectTransform));
            chipHolder.transform.SetParent(contentRect, false);
            chipRow = chipHolder.GetComponent<RectTransform>();
            SetRect(chipRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(1060f, 100f), new Vector2(0f, -196f));

            handText = CreateText("Hand", contentRect, string.Empty, PixelFont.Body, TextAnchor.MiddleCenter, HandColor);
            SetRect(handText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(1060f, 30f), new Vector2(0f, 40f));
        }

        Text CreateText(string name, Transform parent, string text, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text label = go.GetComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = anchor;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        static Image CreateImage(string name, Transform parent, Color color, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void PlaceLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        void SetDim(float alpha)
        {
            if (dim == null) return;

            Color color = DimColor;
            color.a = Mathf.Clamp01(alpha);
            dim.color = color;

            bool visible = color.a > 0.001f;
            dim.enabled = visible;

            // 演出期间顺手把鼠标也挡住（否则能隔着黑幕点到 HUD 的 REPLAY / PAUSE）。
            // 一旦淡到全透明就立刻放开：万一演出没跑完，玩家也不会对着一块看不见的板子点不动。
            dim.raycastTarget = visible;
        }

        void SetContent(float alpha)
        {
            if (content == null) return;

            content.alpha = Mathf.Clamp01(alpha);

            // 全透明时整块关掉：不在 HUD 上留一堆透明文字参与合批
            bool visible = content.alpha > 0.001f;
            if (content.gameObject.activeSelf != visible) content.gameObject.SetActive(visible);
        }
    }
}
