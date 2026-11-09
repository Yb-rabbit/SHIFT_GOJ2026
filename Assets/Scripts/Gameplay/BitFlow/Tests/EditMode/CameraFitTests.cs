using NUnit.Framework;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 机位「拉远倍数」回归测试。
    ///
    /// 背景：13×9 这类工坊大板按 1 倍摆位时，卡排底部会掉到屏幕下沿之外、网格前排两角也贴边，
    /// 玩家第一眼会以为最底下一行被卡片挡住了。修法是按板宽把机位整体拉远一点。
    /// 这里钉住三件事：
    /// 1) 玩家关卡（宽 ≤ 9）的倍数必须是 1 —— 一个像素都不许动；
    /// 2) 13×9 的倍数够用（实测投影的最小解是 1.105）；
    /// 3) 倍数随宽度单调不减、且有封顶。
    /// </summary>
    public class CameraFitTests
    {
        const float Tolerance = 1e-3f;

        [TestCase(3)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(GridMetrics.CameraReferenceWidth)]
        public void PlayerBoardWidths_DoNotMoveCamera(int width)
        {
            Assert.AreEqual(1f, GridMetrics.CameraPullBack(width), Tolerance,
                "宽 " + width + " 的板属于玩家关卡范围，机位必须保持原样");
        }

        [Test]
        public void NarrowBoards_ClampToOne()
        {
            // 防御性：宽度异常（0 或负数）也不能把镜头推到网格后面去
            Assert.AreEqual(1f, GridMetrics.CameraPullBack(0), Tolerance);
            Assert.AreEqual(1f, GridMetrics.CameraPullBack(-5), Tolerance);
        }

        [Test]
        public void WorkshopEleven_OnlyNudgesOut()
        {
            // 11×8 只多 5%：11 宽在工坊里也不少见，不能推得太明显
            float pullBack = GridMetrics.CameraPullBack(11);
            Assert.AreEqual(1.055f, pullBack, Tolerance);
            Assert.Less(pullBack, 1.1f, "11 宽的板不该被推远超过 10%");
        }

        [Test]
        public void WorkshopThirteen_PullsBackEnough()
        {
            // 13×9 是「底下一行被卡片挡住」的那张板：1.105 是实测刚好够用的最小值
            float pullBack = GridMetrics.CameraPullBack(13);
            Assert.AreEqual(1.11f, pullBack, Tolerance);
            Assert.GreaterOrEqual(pullBack, 1.105f - Tolerance, "13×9 推远不足时卡排下沿仍会被切掉");
            Assert.LessOrEqual(pullBack, GridMetrics.MaxCameraPullBack, "推太远板会缩成一小块");
        }

        [Test]
        public void PullBack_IsMonotonicAndCapped()
        {
            float previous = GridMetrics.CameraPullBack(1);
            for (int width = 2; width <= 60; width++)
            {
                float current = GridMetrics.CameraPullBack(width);
                Assert.GreaterOrEqual(current + Tolerance, previous, "宽度 " + width + " 的倍数比更窄的板还小");
                Assert.LessOrEqual(current, GridMetrics.MaxCameraPullBack + Tolerance, "宽度 " + width + " 超过了封顶");
                previous = current;
            }

            Assert.AreEqual(GridMetrics.MaxCameraPullBack, GridMetrics.CameraPullBack(30), Tolerance);
        }
    }
}
