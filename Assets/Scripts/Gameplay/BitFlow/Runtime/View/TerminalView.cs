using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 终端演出的渲染层：一块玻璃面板 + 逐行滚动的日志。播放节奏全部由
    /// <see cref="TerminalPlayer"/> 决定，这里只负责"把它的状态画出来"。
    ///
    /// 两个刻意的做法：
    /// 1. <b>行槽复用</b>：预建 <see cref="TerminalPalette.MaxVisibleLines"/> 个 Text，每帧重新填内容，
    ///    而不是每来一行新建一个。台词一多就是几百个 GameObject，换场景时全是 GC 抖动。
    /// 2. <b>自己建 Canvas</b>：和 HUD / 黑幕一样不依赖场景里有没有 Canvas，挂上就能播。
    /// </summary>
    public sealed class TerminalView : MonoBehaviour
    {
        /// <summary>比 HUD(0) 高、比开场黑幕(30) 低：黑幕永远能盖住终端。</summary>
        const int SortingOrder = 25;

        const float FadeDuration = 0.45f;

        TerminalScript script;
        TerminalPlayer player;

        CanvasGroup group;
        Text titleText;
        Text statusText;
        Text hintText;

        readonly Text[] slots = new Text[TerminalPalette.MaxVisibleLines];

        /// <summary>面板本体。开机/断电动画缩放的是它，连描边和文字一起压扁才像显像管。</summary>
        RectTransform panelRect;

        /// <summary>面板内侧那层底色。塌缩到最后靠它闪出一条白线 —— 改它，不要改 Panel，Panel 被 Edge 完全盖住了。</summary>
        Image innerImage;

        int scrollTop;
        bool raisedFinished;

        /// <summary>
        /// 收场干扰期：<see cref="Update"/> 和 <see cref="Refresh"/> 让路，
        /// 否则每帧刷新会把乱码又写回正常台词，干扰动画就白做了。
        /// </summary>
        bool glitching;

        bool acceptsInput = true;
        float alpha = 1f;

        /// <summary>整段播完（含最后一行停顿）。只触发一次。</summary>
        public event Action Finished;

        public TerminalPlayer Player => player;
        public bool IsFinished => player != null && player.IsFinished;

        /// <summary>建好 UI 并立刻开始播。返回的组件可以被调用方淡出、销毁。</summary>
        public static TerminalView Create(Transform parent, TerminalScript script)
        {
            GameObject go = new GameObject("TerminalView", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TerminalView view = go.AddComponent<TerminalView>();
            view.BuildUi();
            view.Play(script);
            return view;
        }

        /// <summary>开播（或换剧本重播）。</summary>
        public void Play(TerminalScript target)
        {
            // 没配剧本就用内置的开场段：场景里挂上就能演，不必先建资产
            script = target != null ? target : TerminalScript.CreateDefaultIntro();

            player = new TerminalPlayer(script);
            player.LineStarted += OnLineStarted;

            scrollTop = 0;
            raisedFinished = false;
            glitching = false;
            alpha = 1f;

            if (group != null) group.alpha = 1f;
            if (panelRect != null) panelRect.localScale = Vector3.one;
            if (innerImage != null) innerImage.color = TerminalPalette.PanelBg;
            if (titleText != null) titleText.text = script.ChannelTitle ?? string.Empty;

            Refresh();

            player.Begin();

            // Begin 里可能当场 Finished（空剧本）
            CheckFinished();
            Refresh();
        }

        /// <summary>演出期间玩家按键。"跳过"由 <see cref="TerminalPlayer.RequestAdvance"/> 定义。</summary>
        public void SetAcceptsInput(bool value)
        {
            acceptsInput = value;
        }

        /// <summary>整体透明度（0~1）。淡出用。</summary>
        public void SetAlpha(float value)
        {
            alpha = Mathf.Clamp01(value);
            if (group != null) group.alpha = alpha;
        }

        /// <summary>渐变到某个透明度，调用方 yield 它即可。</summary>
        public IEnumerator FadeTo(float target, float duration)
        {
            float from = alpha;

            if (duration <= 0f)
            {
                SetAlpha(target);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            SetAlpha(target);
        }

        // ---------------------------------------------------------------- 开关机动画

        /// <summary>压扁成一条线时的高度倍数。别用 0：RectTransform 缩放为 0 会让 uGUI 的布局算出 NaN。</summary>
        const float PanelLineScale = 0.012f;

        /// <summary>开机回弹强度。<c>1.7</c> 是"有弹性但不弹到失真"的常用值。</summary>
        const float Overshoot = 1.7f;

        /// <summary>塌缩到最后那一刹的过曝色：整块面板先烧成一条白线，再灭。</summary>
        static readonly Color CollapseFlash = new Color(0.80f, 0.93f, 1.00f, 1f);

        /// <summary>
        /// 开机：先点亮一条横线，再把它撑满整个面板，最后轻微过冲回原位 —— 显像管通电的观感。
        ///
        /// 透明度也在这段里从 0 升到 1，所以调用方只要先 <see cref="SetAlpha"/>(0)
        /// 再 <c>yield return view.PlayOpen(秒)</c>，不必自己再补一次淡入
        /// （两次淡入叠在一起会让前 0.1 秒发灰）。
        /// </summary>
        public IEnumerator PlayOpen(float duration)
        {
            if (panelRect == null || duration <= 0f)
            {
                SetPanelScale(1f, 1f);
                SetAlpha(1f);
                yield break;
            }

            // 前 22% 只点亮那条线：面板还是扁的，但已经看得见，像扫描线在找信号
            float lineTime = duration * 0.22f;
            float expandTime = duration - lineTime;

            SetPanelScale(1f, PanelLineScale);
            SetAlpha(0f);

            float elapsed = 0f;
            while (elapsed < lineTime)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Clamp01(elapsed / lineTime));
                yield return null;
            }

            SetAlpha(1f);

            elapsed = 0f;
            while (elapsed < expandTime)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / expandTime);

                SetPanelScale(1f, Mathf.Lerp(PanelLineScale, 1f, EaseOutBack(t, Overshoot)));
                yield return null;
            }

            SetPanelScale(1f, 1f);
        }

        /// <summary>
        /// 收场：信号被判成乱码、面板抖到断电、最后塌成一条线熄灭。
        ///
        /// 和 <see cref="PlayOpen"/> 反向对称（线 → 面 / 面 → 线），玩家读到的是
        /// "这台机器被拔了线"，而不是"这块 UI 淡出了" —— 淡出会让人等下一个界面，
        /// 断电会让人准备开始玩。
        ///
        /// 三段时间按 5 : 3.4 : 余数 分：干扰最长，因为它承载信息（乱码 + CONNECTION LOST），
        /// 塌缩和熄灭只是收尾，拖长了就变成等待。
        /// </summary>
        public IEnumerator PlayDisconnect(float duration)
        {
            glitching = true;
            acceptsInput = false;

            if (panelRect == null || duration <= 0f)
            {
                SetAlpha(0f);
                yield break;
            }

            ShowDisconnected();

            float stressTime = duration * 0.50f;
            float collapseTime = duration * 0.34f;
            float blackoutTime = duration - stressTime - collapseTime;

            // 第一段：干扰。抖幅递增 —— 越接近断电越失控
            float elapsed = 0f;
            float nextScramble = 0f;
            while (elapsed < stressTime)
            {
                elapsed += Time.unscaledDeltaTime;

                float amp = Mathf.Lerp(12f, 30f, elapsed / stressTime);
                ShakePanel(UnityEngine.Random.Range(-amp, amp), UnityEngine.Random.Range(-amp * 0.30f, amp * 0.30f));

                if (elapsed >= nextScramble)
                {
                    nextScramble = elapsed + 0.05f;
                    ScrambleVisible();
                }

                yield return null;
            }

            // 第二段：断电塌缩。竖向走 t² —— 先慢后极快，像电容放完电那一下
            // 先重写一次"断开"那行：干扰期它被打成了乱码，不重写会带着一身噪点塌下去
            ShowDisconnected();

            elapsed = 0f;
            while (elapsed < collapseTime)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / collapseTime);

                SetPanelScale(Mathf.Lerp(1f, 0.78f, t), Mathf.Lerp(1f, PanelLineScale, t * t));

                // 越扁越亮：最后阶段面板几乎只剩一条过曝的白线，熄灭才有"断电"的实感
                if (innerImage != null) innerImage.color = Color.Lerp(TerminalPalette.PanelBg, CollapseFlash, t * t);

                ShakePanel(UnityEngine.Random.Range(-5f, 5f), 0f);
                yield return null;
            }

            ShakePanel(0f, 0f);

            // 第三段：余辉熄灭
            elapsed = 0f;
            while (elapsed < blackoutTime)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(1f - Mathf.Clamp01(elapsed / blackoutTime));
                yield return null;
            }

            SetAlpha(0f);
        }

        void Update()
        {
            // 干扰期不再刷新：台词已经被打成乱码，这里的 Refresh 每帧都会把它写回去
            if (player == null || glitching) return;

            // unscaled：演出期间游戏时间可能被暂停，台词照样该往下走
            player.Tick(Time.unscaledDeltaTime);

            if (acceptsInput && AnyAdvancePressed()) player.RequestAdvance();

            CheckFinished();
            Refresh();
        }

        static bool AnyAdvancePressed()
        {
            return Input.GetMouseButtonDown(0)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        void CheckFinished()
        {
            if (raisedFinished || player == null || !player.IsFinished) return;

            raisedFinished = true;
            acceptsInput = false;
            Finished?.Invoke();
        }

        void OnLineStarted(int index)
        {
            // 新行超出可见区就整体往上滚一格。剧本通常只有十来行，滚一次就够，
            // 所以这里不做平滑插值 —— 直接换内容反而更像老终端刷屏。
            if (index - scrollTop >= TerminalPalette.MaxVisibleLines) scrollTop = index - TerminalPalette.MaxVisibleLines + 1;
        }

        // ---------------------------------------------------------------- 搭建

        void BuildUi()
        {
            Font font = PixelFont.Get();

            GameObject canvasObject = new GameObject("TerminalCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            group = canvasObject.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;     // 点击穿透到面板下面：这里只读键盘/鼠标左键
            group.interactable = false;

            RectTransform root = canvasObject.GetComponent<RectTransform>();

            // 面板后面压一层黑：不然 3D 场景的亮面会吃掉正文的可读性
            Image scrim = CreateImage("Scrim", root, TerminalPalette.ScrimColor);
            Stretch(scrim.rectTransform, 0f);

            Image panel = CreateImage("Panel", root, TerminalPalette.PanelBg);
            SetRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(TerminalPalette.PanelWidth, TerminalPalette.PanelHeight),
                new Vector2(0f, TerminalPalette.PanelOffsetY));

            // 动画缩放的靶子（见 PlayOpen / PlayDisconnect）
            panelRect = panel.rectTransform;

            // 1px 描边：外层铺满再外扩 1px，内层缩回 1px 盖住内部
            Image edge = CreateImage("Edge", panel.rectTransform, TerminalPalette.PanelEdge);
            Stretch(edge.rectTransform, 1f);

            Image inner = CreateImage("Inner", edge.rectTransform, TerminalPalette.PanelBg);
            Stretch(inner.rectTransform, -1f);
            innerImage = inner;

            float contentWidth = TerminalPalette.PanelWidth - TerminalPalette.TextLeft * 2f;

            titleText = CreateText("Title", inner.rectTransform, font, TerminalPalette.SmallSize,
                TerminalPalette.MutedColor, TextAnchor.MiddleLeft);
            SetRectTopLeft(titleText.rectTransform, contentWidth * 0.6f, 36f, new Vector2(TerminalPalette.TextLeft, -8f));

            statusText = CreateText("Status", inner.rectTransform, font, TerminalPalette.SmallSize,
                TerminalPalette.MutedColor, TextAnchor.MiddleRight);
            SetRectTopRight(statusText.rectTransform, contentWidth * 0.4f, 36f, new Vector2(-TerminalPalette.TextLeft, -8f));

            Image divider = CreateImage("Divider", inner.rectTransform, TerminalPalette.PanelEdge);
            SetRectTopStretch(divider.rectTransform, 0f, -48f, 1f);

            for (int i = 0; i < slots.Length; i++)
            {
                Text slot = CreateText("Line_" + i.ToString("00"), inner.rectTransform, font,
                    TerminalPalette.BodySize, TerminalPalette.BodyColor, TextAnchor.UpperLeft);
                SetRectTopLeft(slot.rectTransform, contentWidth, TerminalPalette.LineHeight,
                    new Vector2(TerminalPalette.TextLeft, -TerminalPalette.TextTop - i * TerminalPalette.LineHeight));
                slots[i] = slot;
            }

            // 按键提示留在面板内部底边：贴在屏幕最底下会压在面板外面，
            // 位置随分辨率比例跑，看着像漏出来的 UI 碎片。
            hintText = CreateText("Hint", inner.rectTransform, font, TerminalPalette.SmallSize,
                TerminalPalette.MutedColor, TextAnchor.MiddleLeft);
            hintText.text = "SPACE / CLICK   NEXT";
            SetRectBottomLeft(hintText.rectTransform, contentWidth, 26f, new Vector2(TerminalPalette.TextLeft, 14f));

            AddScanlines(root);
        }

        /// <summary>CRT 扫描线：2×4 的贴图平铺，每 4px 一条 18% 黑。</summary>
        void AddScanlines(RectTransform root)
        {
            GameObject go = new GameObject("Scanlines", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(root, false);

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = ScanTexture();
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, 1920f / 2f, 1080f / 4f);   // 1 像素 = 1 UI 单位

            Stretch(raw.rectTransform, 0f);
        }

        // 扫描线贴图整场演出共用一张：每个实例各建一张会让每次开演多出一份 VRAM，
        // 而它其实是常量（2×4 的点图），没有任何"每个面板不一样"的必要。
        static Texture2D scanTexture;

        static Texture2D ScanTexture()
        {
            // Unity 的 fake-null：贴图被卸载（退出 Play / 域重载）后这里会判成 null，自动重建
            if (scanTexture != null) return scanTexture;

            Texture2D texture = new Texture2D(2, 4, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };

            for (int y = 0; y < 4; y++)
            {
                Color color = y == 3 ? new Color(0f, 0f, 0f, 0.18f) : new Color(0f, 0f, 0f, 0f);
                texture.SetPixel(0, y, color);
                texture.SetPixel(1, y, color);
            }

            texture.Apply();
            scanTexture = texture;
            return scanTexture;
        }

        // ---------------------------------------------------------------- 每帧刷新

        void Refresh()
        {
            if (player == null) return;

            if (statusText != null)
            {
                int shown = Mathf.Clamp(player.LineIndex + 1, 0, player.LineCount);
                statusText.text = "LINE " + shown.ToString("00") + "/" + player.LineCount.ToString("00")
                    + "   T " + player.Elapsed.ToString("0.00") + "s";
            }

            if (hintText != null) hintText.enabled = !player.IsFinished;

            bool cursor = !player.IsFinished && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;

            for (int i = 0; i < slots.Length; i++)
            {
                Text slot = slots[i];
                if (slot == null) continue;

                int index = scrollTop + i;
                bool visible = player.LineIndex >= 0 && index <= player.LineIndex && index < player.LineCount;

                if (!visible)
                {
                    if (!string.IsNullOrEmpty(slot.text)) slot.text = string.Empty;
                    continue;
                }

                string body = script.TextOf(index);

                // 已经过去的行整行显示；当前行按打字进度切
                bool typing = index == player.LineIndex && player.CurrentPhase == TerminalPlayer.Phase.Typing;
                int reveal = typing ? player.RevealedChars : body.Length;
                if (reveal > body.Length) reveal = body.Length;
                if (reveal < 0) reveal = 0;

                slot.text = Compose(script.SpeakerOf(index), body.Substring(0, reveal),
                    cursor && index == player.LineIndex);
            }
        }

        /// <summary>
        /// 一行日志：说话人栏（定宽 → 靠字符对齐，不靠 RectTransform）+ &gt; + 正文。
        /// 等宽位图字体让"补空格对齐"这件事真的成立，这也是选它的主要理由。
        ///
        /// 装饰符一律用 ASCII（<c>&gt;</c>、<c>_</c>）：位图字体只覆盖 ASCII + 少量符号，
        /// 用 <c>›</c> / <c>▌</c> 这类在编辑器里会被系统字体悄悄顶替，打包后变成方块。
        ///
        /// 分隔符要写字面的 <c>&gt;</c>，<b>不能</b>写成 HTML 实体（<c>&amp;gt;</c>）：uGUI 的 Text
        /// 只认 <c>&lt;b&gt;</c> / <c>&lt;color&gt;</c> 这类富文本标签，<b>不</b>解析实体，
        /// 那五个字符会原样画在屏幕上，末尾的分号还会被读成标点错误。
        /// </summary>
        static string Compose(TerminalSpeaker speaker, string shown, bool cursor)
        {
            string speakerHex = ColorUtility.ToHtmlStringRGB(TerminalPalette.SpeakerColor(speaker));
            string bodyHex = ColorUtility.ToHtmlStringRGB(TerminalPalette.BodyColor);

            string label = TerminalPalette.SpeakerLabel(speaker).PadRight(TerminalPalette.SpeakerCols);
            string body = cursor ? shown + "<color=#" + speakerHex + ">_</color>" : shown;

            return "<color=#" + speakerHex + ">" + label + "></color>"
                 + "<color=#" + bodyHex + "> " + body + "</color>";
        }

        // ---------------------------------------------------------------- 干扰效果

        /// <summary>
        /// 乱码字符集。<b>只用 ASCII</b>，并且刻意排除 <c>&lt;</c> <c>&gt;</c> <c>&amp;</c> <c>#</c>：
        /// uGUI 的富文本解析器会把它们当标签或实体，一行乱码可能整行变空白或者染色串行。
        /// </summary>
        const string NoiseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@%*?!|/\\_=+~^:;.-";

        /// <summary>
        /// 把最后一行改写成"断开连接"。这句不在剧本里 —— 它是演出的一部分，
        /// 换剧本时不该跟着变，所以硬编码在这儿而不是塞进 <see cref="TerminalScript"/>。
        /// </summary>
        void ShowDisconnected()
        {
            if (script == null || slots.Length == 0) return;

            int slotIndex = script.Lines.Count - 1 - scrollTop;
            if (slotIndex < 0 || slotIndex >= slots.Length) return;

            Text slot = slots[slotIndex];
            if (slot == null) return;

            string hex = ColorUtility.ToHtmlStringRGB(TerminalPalette.AlarmColor);
            string label = TerminalPalette.SpeakerLabel(TerminalSpeaker.Sys).PadRight(TerminalPalette.SpeakerCols);

            slot.text = "<color=#" + hex + ">" + label + "></color>"
                      + "<color=#" + hex + "> CONNECTION LOST.</color>";
        }

        /// <summary>
        /// 把屏上的行打成乱码。
        ///
        /// 只换一部分槽位（62%）、只在每 0.05 秒的一帧换：每帧全换会糊成一片均匀白噪，
        /// 反而看不出"字符在崩"，只有保持大部分正常字、零星几行烂掉才像信号不稳。
        /// </summary>
        void ScrambleVisible()
        {
            if (script == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                Text slot = slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.text)) continue;
                if (UnityEngine.Random.value > 0.62f) continue;

                int index = scrollTop + i;
                int length = script.TextOf(index).Length;
                if (length <= 0) continue;

                slot.text = Compose(script.SpeakerOf(index), Noise(length), false);
            }
        }

        static string Noise(int length)
        {
            StringBuilder text = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                text.Append(NoiseChars[UnityEngine.Random.Range(0, NoiseChars.Length)]);
            }

            return text.ToString();
        }

        /// <summary>先冲过头再回落到 1。<paramref name="c1"/> 越大回弹越夸张。</summary>
        static float EaseOutBack(float t, float c1)
        {
            float u = t - 1f;
            return 1f + (c1 + 1f) * u * u * u + c1 * u * u;
        }

        void SetPanelScale(float x, float y)
        {
            if (panelRect != null) panelRect.localScale = new Vector3(x, y, 1f);
        }

        /// <summary>抖动叠在面板原本的 anchoredPosition 上：抖幅传 0 就正好回原位，不会越抖越偏。</summary>
        void ShakePanel(float x, float y)
        {
            if (panelRect == null) return;

            panelRect.anchoredPosition = new Vector2(x, TerminalPalette.PanelOffsetY + y);
        }

        // ---------------------------------------------------------------- uGUI 小工具

        static Text CreateText(string name, Transform parent, Font font, int size, Color color, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1f;
            return text;
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-inset, -inset);
            rect.offsetMax = new Vector2(inset, inset);
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

        static void SetRectTopLeft(RectTransform rect, float width, float height, Vector2 position)
        {
            SetRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(width, height), position);
        }

        static void SetRectTopRight(RectTransform rect, float width, float height, Vector2 position)
        {
            SetRect(rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(width, height), position);
        }

        static void SetRectBottomLeft(RectTransform rect, float width, float height, Vector2 position)
        {
            SetRect(rect, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(width, height), position);
        }

        /// <summary>横向铺满、纵向固定高度，贴在父节点顶部（分隔线用）。</summary>
        static void SetRectTopStretch(RectTransform rect, float horizontalInset, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-horizontalInset * 2f, height);
            rect.anchoredPosition = new Vector2(0f, y);
        }
    }
}
