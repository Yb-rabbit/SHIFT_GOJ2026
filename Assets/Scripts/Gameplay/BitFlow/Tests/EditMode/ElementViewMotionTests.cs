using NUnit.Framework;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 摆放动效的曲线测试。
    ///
    /// 为什么单独测这两条曲线：它们纯数值、不碰场景，但一旦算错，
    /// 表现就是"整片元件悬空不落地"或者"陷进地板"—— 这种问题在静帧截图里看不出来，
    /// 手动试玩也容易误判成相机角度问题。所以在 EditMode 里把端点钉死。
    /// </summary>
    public class ElementViewMotionTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void DropOffset_StartsInTheAirAndEndsOnTheGround()
        {
            Assert.AreEqual(1f, ElementView.DropOffset01(0f), Tolerance, "刚放下时必须还在最高点");
            Assert.AreEqual(0f, ElementView.DropOffset01(1f), Tolerance, "动画结束时必须完全落地");
        }

        [Test]
        public void DropOffset_IsMonotonicallyFalling()
        {
            float previous = ElementView.DropOffset01(0f);

            for (int i = 1; i <= 20; i++)
            {
                float current = ElementView.DropOffset01(i / 20f);
                Assert.LessOrEqual(current, previous + Tolerance, "下落过程中不能回弹到更高的位置");
                previous = current;
            }
        }

        [Test]
        public void DropOffset_LandsBeforeTheAnimationEnds()
        {
            // 后段 28% 的时间留给落地挤压回弹，所以"踩实"必须早于 t=1
            Assert.Greater(ElementView.DropOffset01(0.6f), 0f, "0.6 处应该还在空中");
            Assert.AreEqual(0f, ElementView.DropOffset01(0.8f), Tolerance, "0.8 处应该已经踩实");
        }

        [Test]
        public void DropScaleY_SquashesOnlyAfterLanding()
        {
            Assert.AreEqual(1f, ElementView.DropScaleY(0f), Tolerance, "空中不该有形变");
            Assert.AreEqual(1f, ElementView.DropScaleY(0.6f), Tolerance, "落地前不该有形变");
            Assert.Less(ElementView.DropScaleY(0.75f), 0.95f, "落地之后应该能看到压扁");
            Assert.AreEqual(1f, ElementView.DropScaleY(1f), Tolerance,
                "结束时必须恢复原状，否则元件会永远扁着");
        }

        [Test]
        public void DropScaleY_NeverCollapsesOrStretches()
        {
            for (int i = 0; i <= 20; i++)
            {
                float scale = ElementView.DropScaleY(i / 20f);
                Assert.GreaterOrEqual(scale, 0.88f - Tolerance, "压得太狠会看起来像陷进地板");
                Assert.LessOrEqual(scale, 1f + Tolerance, "不该被拉长");
            }
        }
    }
}
