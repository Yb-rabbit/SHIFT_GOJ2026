using UnityEngine;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 全部运行时 UI 的字体入口。
    ///
    /// 为什么要有它：HUD / 开场黑幕 / 选关面板 / 手牌盘 / 作者模式各自都有一份
    /// <c>LoadFont()</c>，五份都是 <c>Resources.GetBuiltinResource</c> 拿系统字体 ——
    /// 换字体要改五处，漏一处就会出现「一个面板字体不一样」。
    ///
    /// 换的是 <b>BoutiqueBitmap9x9 1.9</b>（<c>Assets/Arts/Resources/Text/</c>），一套 9×9 像素字体：
    /// 实测字符前进量 16 号 10px、20 号 12px、32 号 19px。全项目的字号请只用 <see cref="Small"/> /
    /// <see cref="Body"/> / <see cref="Large"/> 三个档位 —— 像素字体在随手挑的字号上会糊。
    ///
    /// 两个必须记住的事实：
    /// 1. 工程里的这份字体是<b>带中文子集</b>的版本（约 2MB），作者模式的中文标签靠它显示；
    ///    玩家侧文案仍写成英文 + 箭头符号 —— 换回原版只有 ASCII 的字体时，那一屏会变成方块。
    /// 2. 除中文外只有 ASCII 和 <c>› ▸ · … — ↑ → ↓ ← █ ▌ ░ ▒ ▓</c> 这一组符号能放心用。
    ///    <c>× ✓ ✗</c> 都<b>不</b>在这个字体里 —— 乘号写 <c>x</c>，对勾写 <c>OK</c>，叉写 <c>X</c>。
    /// </summary>
    public static class PixelFont
    {
        /// <summary>Resources 下的相对路径（不带扩展名）。改字体只改这一行。</summary>
        public const string ResourcePath = "Text/BOUTIQUEBITMAP9X9_1.9";

        /// <summary>小字档：面板标题、状态行、图例。16 号 = 每字符 10px。</summary>
        public const int Small = 16;

        /// <summary>正文档：按钮、行文本、提示框。20 号 = 每字符 12px。</summary>
        public const int Body = 20;

        /// <summary>大字档：关名、结算标题。32 号 = 每字符 19px。</summary>
        public const int Large = 32;

        static Font cached;
        static bool pointFilterInstalled;

        /// <summary>
        /// 取字体。第一次调用时从 Resources 加载并缓存，之后直接返回同一个实例 ——
        /// uGUI 的 Text 会按「字体实例 + 字号」缓存字形图集，重复加载会让每个 Text
        /// 各建一份图集，白白多几十张纹理。
        /// </summary>
        public static Font Get()
        {
            if (cached != null) return cached;

            cached = Resources.Load<Font>(ResourcePath);
            if (cached != null)
            {
                InstallPointFilter();
                ApplyPointFilter(cached);
                return cached;
            }

            // 字体被挪走 / 改名时不能让整块 UI 直接崩成看不见的字：
            // 退回系统字体（编辑器里正常，打包后中文与符号会缺，但至少能看到文字）。
            Debug.LogWarning("[BitFlow] 找不到像素字体 Resources/" + ResourcePath +
                             "，已退回系统字体。UI 字号与对齐可能错位。");

            cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cached == null) cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return cached;
        }

        /// <summary>
        /// 像素字体的图集必须用 Point 采样。Unity 动态字体默认给图集 Bilinear，
        /// 字号不是字体的整数倍时，字形边缘会被插值成灰边 —— 看起来就是「有点糊」。
        /// 这里只对 Resources 里的像素字体生效，系统回退字体仍保持平滑采样。
        /// </summary>
        static void InstallPointFilter()
        {
            if (pointFilterInstalled) return;

            pointFilterInstalled = true;
            Font.textureRebuilt += OnFontTextureRebuilt;
        }

        /// <summary>动态字体会按需重建图集，重建后重新钉一次 Point，否则会被重置回 Bilinear。</summary>
        static void OnFontTextureRebuilt(Font font)
        {
            if (font == cached) ApplyPointFilter(font);
        }

        static void ApplyPointFilter(Font font)
        {
            if (font == null || font.material == null) return;

            Texture texture = font.material.mainTexture;
            if (texture != null && texture.filterMode != FilterMode.Point)
                texture.filterMode = FilterMode.Point;
        }
    }
}
