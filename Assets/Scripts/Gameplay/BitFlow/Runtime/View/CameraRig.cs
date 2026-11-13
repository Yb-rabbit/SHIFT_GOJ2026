using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 固定机位：开场约 2 秒从高位俯瞰推近到最终机位，然后彻底锁死（不接受任何输入）。
    /// 关卡尺寸变化时自动重新计算机位，保证 9×8 与其它尺寸都能完整入画。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("推近")]
        [Tooltip("开场推近时长（秒）")]
        [SerializeField, Min(0f)] float introDuration = 2f;

        [Tooltip("进 Play 就自己推一次近。正式关卡请关掉：推近的发起权交给黑幕（Prepare 摆位、Play 才推），" +
                 "否则开场会在黑幕后面推完一遍，退场时再被拽回远景重推 —— 表现就是「镜头猛地一顿」")]
        [SerializeField] bool autoPlay = false;

        [Header("机位")]
        [Tooltip("最终机位相对网格中心的偏移")]
        // 俯角固定 37.2°（y:z ≈ 0.76），只靠整体比例推近/拉远；overviewOffset 同步按同一比例缩放。
        [SerializeField] Vector3 finalOffset = new Vector3(0f, 7.6f, -10f);

        [Tooltip("开场机位相对网格中心的偏移（更远更高）")]
        [SerializeField] Vector3 overviewOffset = new Vector3(0f, 14.4f, -17.6f);

        [Tooltip("视线目标相对网格中心的抬高量")]
        [SerializeField] float lookHeight = 1.1f;

        /// <summary>单帧最多推进这么多秒。低帧率时宁可推得慢一点，也不要一帧跳掉一大段进度。</summary>
        const float MaxStep = 1f / 30f;

        BitFlowRunner runner;
        bool configured;
        float elapsed;

        /// <summary>上一次算机位时的网格尺寸（<see cref="Frame"/> 靠它判重：尺寸没变就不用重新摆）。</summary>
        int framedWidth = -1;
        int framedHeight = -1;

        Vector3 fromPosition;
        Quaternion fromRotation;
        Vector3 toPosition;
        Quaternion toRotation;

        void Awake()
        {
            runner = FindFirstObjectByType<BitFlowRunner>();

            // 默认不自动演：先把镜头冻住，等黑幕调 Prepare（摆到开场机位）和 Play（真正开始推）。
            // 用 enabled 而不是一个「等会儿再推」的标志位 —— LateUpdate 一行都不跑，最省也最不会抢戏。
            if (!autoPlay) enabled = false;
        }

        /// <summary>
        /// 只把镜头摆到开场机位并<b>停在那里</b>，不推进（黑幕盖上那一刻调它）。
        /// 和 <see cref="Play"/> 分开的原因：开场时关卡要先在幕后建好/演完，
        /// 如果镜头这时候已经开始推，玩家会看到「推完了又跳回远景重推一次」——
        /// 上一版开场那一下顿挫就是这么来的。
        /// </summary>
        public void Prepare()
        {
            if (runner == null) runner = FindFirstObjectByType<BitFlowRunner>();

            elapsed = 0f;
            enabled = false;

            // 关卡可能还没建（选关面板刚点完）：那就等 Play 时再算，别拿上一关的尺寸摆位。
            if (runner != null && runner.Definition != null) Configure(runner.Definition);
            else configured = false;
        }

        /// <summary>
        /// 重新推一次近（换关 / 重试 / 黑幕演出结束时调用）。
        ///
        /// 必须走这个入口而不是直接改 elapsed：推完之后这里会把 <c>enabled</c> 关掉锁死镜头，
        /// 一旦锁死，换关时 <see cref="LateUpdate"/> 根本不会再跑，<c>configured</c> 也不会复位 ——
        /// 表现就是"第二关开始镜头再也不推近了"。所以这里三样一起复位。
        /// </summary>
        public void Play()
        {
            if (runner == null) runner = FindFirstObjectByType<BitFlowRunner>();

            configured = false;
            elapsed = 0f;
            enabled = true;

            // 每次都从开场机位重算：换关后网格尺寸可能变了，沿用上一关的机位会偏。
            // 已经 Prepare 过时这里算出来的位置和当前完全一致，所以不会产生位移跳变。
            if (runner != null && runner.Definition != null) Configure(runner.Definition);
        }

        void LateUpdate()
        {
            if (!configured)
            {
                if (runner == null || runner.Definition == null) return;
                Configure(runner.Definition);
            }

            if (elapsed >= introDuration)
            {
                // 锁定：之后不再动摄像机
                enabled = false;
                return;
            }

            // 限幅：开场/换关那一帧的 delta 可能是 0.2~0.5s（建关、字体图集重建、shader 变体编译），
            // 直接累加会让推近一步吃掉 1/4 进度，肉眼就是「镜头卡一下然后猛冲一段」。
            elapsed += Mathf.Min(Time.deltaTime, MaxStep);
            float k = Mathf.Clamp01(introDuration <= 0f ? 1f : elapsed / introDuration);
            float eased = k * k * (3f - 2f * k);

            transform.position = Vector3.Lerp(fromPosition, toPosition, eased);
            transform.rotation = Quaternion.Slerp(fromRotation, toRotation, eased);
        }

        /// <summary>
        /// 立刻把镜头摆到<b>最终机位</b>并锁死，不演推近。
        ///
        /// 为什么需要这个入口：<see cref="Prepare"/> / <see cref="Play"/> 只有 <see cref="IntroCurtain"/>
        /// 会调，而机位是按网格中心 + 网格尺寸算的 —— 工坊换一张 11×8 的板、或选关面板换一关，
        /// 舞台重建了但没人重新算机位，画面就停在上一关的中心（小网格看起来贴在屏幕一角）。
        ///
        /// 判重只看「网格尺寸变没变」：尺寸一样就什么都不做（编辑器每改一格都会走这里，等于零开销），
        /// 尺寸一变就立刻落到新机位 —— 哪怕开场推近正演到一半。
        /// 那种情况（开场 2 秒内有人换了板）本来就极少，而且"推完锁死在上一关的机位上"是更糟的结果：
        /// 之后再也没人重算，画面会一直歪着。
        /// </summary>
        public void Frame(LevelDefinition level = null)
        {
            if (runner == null) runner = FindFirstObjectByType<BitFlowRunner>();

            LevelDefinition target = level;
            if (target == null && runner != null) target = runner.Definition;
            if (target == null) return;

            // 尺寸没变、而且已经算过机位：什么都不用做
            if (configured && framedWidth == target.Width && framedHeight == target.Height) return;

            ComputePose(target);
            transform.position = toPosition;
            transform.rotation = toRotation;

            elapsed = introDuration;
            configured = true;
            enabled = false;
        }

        void Configure(LevelDefinition level)
        {
            ComputePose(level);
            transform.position = fromPosition;
            transform.rotation = fromRotation;
            configured = true;
        }

        /// <summary>只算不说话：把两个机位算好，并把网格尺寸记下来（<see cref="Frame"/> 靠它判重）。</summary>
        void ComputePose(LevelDefinition level)
        {
            Vector3 center = GridMetrics.GridCenter(level.Width, level.Height, lookHeight);

            // 板比参考板宽就整体拉远一点：13×9 的卡排按 1 倍摆位会掉到屏幕下沿之外，
            // 玩家会以为最底下一行被卡片挡住了。两个机位乘同一个倍数 —— 俯角、推近轨迹都不变。
            // 宽度 ≤ 9 的板（所有玩家关卡）倍数是 1，画面逐像素与改动前一致。
            float pullBack = GridMetrics.CameraPullBack(level.Width);

            toPosition = center + finalOffset * pullBack;
            toRotation = Quaternion.LookRotation(center - toPosition, Vector3.up);

            fromPosition = center + overviewOffset * pullBack;
            fromRotation = Quaternion.LookRotation(center - fromPosition, Vector3.up);

            framedWidth = level.Width;
            framedHeight = level.Height;
        }
    }
}
