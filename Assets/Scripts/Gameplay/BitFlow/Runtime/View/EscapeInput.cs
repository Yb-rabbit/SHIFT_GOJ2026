using UnityEngine;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 单帧 Esc 仲裁：同一帧里可能有多个界面都想响应 Esc。
    ///
    /// 例如选关面板开着、玩家又拿着牌时按 Esc：
    /// 如果两边都直接读 <c>Input.GetKeyDown</c>，就会一帧关两个东西，
    /// 甚至把暂停面板也一起打开。这里只做一件事 —— 谁处理了谁 <see cref="Consume"/>，
    /// 后处理的逻辑用 <see cref="ConsumedThisFrame"/> 判断，不再抢。
    ///
    /// 用 <c>Time.frameCount</c> 自动换帧，不依赖调用方在开头手动 Reset：
    /// 某一帧没人消费时，下一帧第一次访问会自动回到未消费状态。
    /// </summary>
    public static class EscapeInput
    {
        static int frame = -1;
        static bool consumed;

        /// <summary>尝试占用这一帧的 Esc。占用成功返回 true；同一帧第二次调用返回 false。</summary>
        public static bool Consume()
        {
            RefreshFrame();
            if (consumed) return false;

            consumed = true;
            return true;
        }

        /// <summary>这一帧的 Esc 是否已经被别的界面处理掉了。</summary>
        public static bool ConsumedThisFrame
        {
            get
            {
                RefreshFrame();
                return consumed;
            }
        }

        static void RefreshFrame()
        {
            if (frame == Time.frameCount) return;

            frame = Time.frameCount;
            consumed = false;
        }
    }
}
