using System.Collections;
using UnityEngine;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 开场终端演出的<b>调度者</b>：挂在场景里，Awake 就把画面盖住并挡住输入，
    /// 然后放 <see cref="TerminalView"/> 演完整段台词，收场时把舞台交给
    /// <see cref="IntroCurtain"/>（关卡名 / 新元件）。
    ///
    /// 为什么需要两层（TerminalView + 这个）而不是一个组件干完：
    /// <see cref="TerminalView"/> 只管"把一段剧本画出来"，玩法里以后可能还要用它
    /// （比如关卡中途插一段机关日志）。"什么时候播、播完接什么"是流程问题，
    /// 换一套开场流程时只该动这一个文件。
    ///
    /// 执行顺序 -30 比 IntroCurtain 的 -20 更早，原因是 Awake 里必须抢先把
    /// <see cref="IntroCurtain.WaitForCue"/> 设成 true —— 否则黑幕会先盖上来，
    /// 终端就演在黑幕后面了（黑幕 sortingOrder 30 &gt; 终端 25）。
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class IntroTerminal : MonoBehaviour
    {
        [Header("剧本")]
        [Tooltip("留空则用代码内置的开场台词（TerminalScript.CreateDefaultIntro）")]
        [SerializeField] TerminalScript script;

        [Header("时间轴（秒）")]
        [Tooltip("开机动画时长：一条线撑满面板 + 轻微过冲。0 = 直接出现")]
        [SerializeField, Min(0f)] float fadeInDuration = 0.55f;

        [Tooltip("最后一行打完之后再留多久，让玩家读完")]
        [SerializeField, Min(0f)] float tailHold = 1.2f;

        [Tooltip("断开动画时长：信号干扰 → 断电塌成一条线 → 余辉熄灭。结束后才把舞台交给开场黑幕")]
        [SerializeField, Min(0f)] float disconnectDuration = 0.9f;

        [Header("调试")]
        [Tooltip("关掉就只盖黑、不演，直接进关卡（调试网格时用）")]
        [SerializeField] bool playOnStart = true;

        IntroCurtain curtain;
        LevelFlow flow;
        BitFlowRunner runner;
        TerminalView view;
        IntroStage stage;
        bool holdsGate;

        void Awake()
        {
            // 先把黑幕按住：它 Awake 时会把自己盖满，那样终端就演在它后面了
            curtain = FindObjectOfType<IntroCurtain>();
            if (curtain != null) curtain.WaitForCue = true;

            // 再把关卡按住：演出要在空场上播。两个入口都得堵 ——
            // LevelFlow 的 Start 会加载第一关，BitFlowRunner 的 Start 自己也会建关。
            // 它们的 Start 都还在后面（这一帧的 Awake 全部先于所有 Start），所以来得及。
            flow = FindObjectOfType<LevelFlow>();
            if (flow != null) flow.HoldAutoStart();

            runner = FindObjectOfType<BitFlowRunner>();
            if (runner != null) runner.HoldStart();

            HoldGate();
        }

        IEnumerator Start()
        {
            if (!playOnStart)
            {
                ReleaseFlow();
                DropGate();
                if (curtain != null) curtain.Cue();
                enabled = false;
                yield break;
            }

            view = TerminalView.Create(transform, script != null ? script : TerminalScript.CreateDefaultIntro());
            view.SetAlpha(0f);   // PlayOpen 里也会压到 0，这里先设一次，免得 Create 到子协程首帧之间闪一帧满亮度

            // 终端面板是半透明的：先把背后的开场舞台搭起来（空网格 + 左右两台机器），
            // 否则那段台词在画面上就是对着两块空白在说话。
            // 必须在 PlayOpen 之前 —— 开机动画的第一帧就该看到机器在后面。
            stage = IntroStage.Build();

            // 开机动画的第一帧 = "弹窗启动的那一刻"：音效放在 PlayOpen 之前，
            // 不能等协程第一帧结束再补，否则画面已经亮了才响。
            BitFlowSfx.TerminalOpen();

            // 开机动画自带透明度上升（线点亮 → 撑满 → 回弹），这里不要再 FadeTo 一次，
            // 两次淡入叠在前 0.1 秒会让画面先发一层灰
            yield return view.PlayOpen(fadeInDuration);

            // 等演完。判断放在循环里而不是订阅事件：演出一结束就该继续走，
            // 而且 view 可能被外部（切场景）提前销毁，这时 IsFinished 也不再可靠，
            // 所以两个条件都要看。
            while (view != null && !view.IsFinished) yield return null;

            if (tailHold > 0f) yield return new WaitForSecondsRealtime(tailHold);

            // 收场不是淡出，是"断线"：乱码 → 抖动 → 塌成一条线 → 熄。
            // 这样玩家不会停在"等下一个界面"的状态，而是被推着进画面
            if (view != null) yield return view.PlayDisconnect(disconnectDuration);

            if (view != null) Destroy(view.gameObject);
            view = null;

            // 舞台拆在放关卡之前：灯光 / 雾 / 机位在这里全部还回原样，
            // 紧接着 ReleaseFlow 建出来的关卡拿到的就是它本来那套环境。
            // 这一刻屏幕上已经全黑（断开动画收在熄灭上），拆场景不会被看见。
            if (stage != null)
            {
                stage.Dispose();
                stage = null;
            }

            // 顺序很重要：先把关卡放出来（它同步触发 LevelLoaded，黑幕只是把这一关
            // 记成 pendingLevel，画面还是黑屏不动的），再 cue 黑幕。
            // 反过来的话黑幕会因为「还没有关卡可演」直接撤幕，玩家会先亮一帧空场景。
            ReleaseFlow();

            // 先放开自己的锁再 cue：黑幕的 Begin 里会再拿一把，
            // 两边各持各的锁，谁先谁后都不会把对方的锁解掉（见 InputGate 引用计数）
            DropGate();
            if (curtain != null) curtain.Cue();

            enabled = false;
        }

        /// <summary>
        /// 把关卡放出来：先让 LevelFlow 补做被压住的首次加载
        /// （它会同步走完 LoadLevel → BuildLevel → LevelLoaded），
        /// 再让 Runner 兜底（单关场景没有 LevelFlow，得靠它自己建）。
        /// 两步都是幂等的：谁先建好，另一个就什么都不做。
        /// </summary>
        void ReleaseFlow()
        {
            if (flow != null) flow.ReleaseAutoStart();
            if (runner != null) runner.ReleaseStart();
        }

        void OnDestroy()
        {
            // 演出中途被卸载（切场景）时别把输入锁留在一个已经死掉的组件上
            DropGate();

            // 舞台改了相机 / 灯光 / 雾这些全局状态，被提前卸载时必须还回去，
            // 否则下一个场景会继承开场的黑场灯光
            if (stage != null)
            {
                stage.Dispose();
                stage = null;
            }

            // 闸门也要还回去：万一演出被提前掐掉，关卡还得能自己进来，
            // 否则玩家会停在一个永远没有关卡的场景里。
            ReleaseFlow();
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
    }
}
