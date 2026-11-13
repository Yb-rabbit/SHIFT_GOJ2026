using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// UI 字体测试：守住「字体被换回来」和「文案里用了字体没有的字符」这两个退化。
    ///
    /// 为什么值得单测：这两类问题都不会报编译错误，只在<b>打包后</b>表现为一屏方块，
    /// 而编辑器里因为系统字体 fallback 看着一切正常 —— 一直是这样才漏到打包版本的。
    ///
    /// 注意：工程里的像素字体（BoutiqueBitmap9x9）是<b>带中文子集</b>的版本，作者模式
    /// （游戏内编辑器）用它显示中文；玩家可见的文案仍保持全英文 + 箭头符号，
    /// 这样换回原版只有 ASCII 的字体时也不会有一屏方块。
    /// </summary>
    public class PixelFontTests
    {
        /// <summary>文案里实际用到的符号，逐个确认字体里有字形。</summary>
        static readonly char[] UsedSymbols = { '↑', '→', '↓', '←', '/', 'x', ':', '.', '-', '(', ')', '>', '_' };

        [Test]
        public void PixelFont_LoadsTheBitmapFont_NotTheSystemFallback()
        {
            Font font = PixelFont.Get();

            Assert.IsNotNull(font, "PixelFont.Get() 不应返回 null");
            Assert.IsNotNull(PixelFont.Get(), "第二次调用应命中缓存");
            Assert.AreSame(PixelFont.Get(), PixelFont.Get(), "字体实例应被缓存，重复加载会给每个 Text 各建一份图集");

            // 退回系统字体时 font.name 会是 Arial / LegacyRuntime，这一条就是「字体没换」的守门人
            // （资产名是全大写 BOUTIQUEBITMAP9X9_1.9，所以比较前统一转大写）
            StringAssert.Contains("BOUTIQUE", font.name.ToUpperInvariant(),
                "UI 字体应是 BoutiqueBitmap9x9，实际拿到的是 " + font.name +
                "（检查 Assets/Arts/Resources/Text/ 下的 TTF 是否还在）");
        }

        [Test]
        public void PixelFont_UsesPointFilter_SoGlyphEdgesStayCrisp()
        {
            Font font = PixelFont.Get();
            if (font == null || font.material == null || font.material.mainTexture == null)
                Assert.Ignore("字体图集尚未生成");

            Assert.AreEqual(FilterMode.Point, font.material.mainTexture.filterMode,
                "像素字体图集必须用 Point 采样；Bilinear 会把边缘插值成灰边，看起来发糊");
        }

        [Test]
        public void PixelFont_HasEveryGlyphTheUiReliesOn()
        {
            Font font = PixelFont.Get();
            if (font == null) Assert.Ignore("字体未加载");

            foreach (char c in UsedSymbols)
            {
                Assert.IsTrue(font.HasCharacter(c),
                    "字体缺少字符 '" + c + "'（U+" + ((int)c).ToString("X4") + "），用到它的 UI 在打包后会显示成方块");
            }
        }

        [Test]
        public void PixelFont_KeepsCjkGlyphs_SoAuthorModeTextIsSafe()
        {
            Font font = PixelFont.Get();
            if (font == null) Assert.Ignore("字体未加载");

            // 这条把「字体到底能不能显示中文」这个事实钉住。
            // 工程里的 BoutiqueBitmap9x9 是带中文子集的版本（2MB 左右），作者模式
            // （BitFlowBuildMode 的调色板与快捷键提示）正是靠它显示中文的 ——
            // 换成原版只有 ASCII 的字体后，编辑器里看不出问题，打包后那一片会变成方块。
            Assert.IsTrue(font.HasCharacter('接'),
                "字体没有中文字形了 —— 作者模式的中文标签会变成方块；换字体请确认它仍带中文子集");
        }

        [Test]
        public void DirectionMarks_AreArrows_NotChinese()
        {
            // 方向提示是「中文泄漏进玩家 UI」最容易复发的一条路径：
            // ElementTypeSO.Describe() 与 LevelBuilder 的兜底说明都会拼方向名。
            Assert.AreEqual("↑", Dir.Up.ToMark());
            Assert.AreEqual("→", Dir.Right.ToMark());
            Assert.AreEqual("↓", Dir.Down.ToMark());
            Assert.AreEqual("←", Dir.Left.ToMark());
        }
    }
}
