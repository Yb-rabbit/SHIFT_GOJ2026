using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 开场「读关卡数据」演出：画面从空场开始，按 <b>网格 → 墙 → 收发器 → 其他元件</b> 一层层长出来，
    /// 每个元件落下前先在落点闪一下光块（数据流落到格子里的视觉语言）。
    ///
    /// 为什么要有它：重播 / 换关时如果把整关一帧建完，会同时触发几十次 GameObject 创建 + 材质查询，
    /// 那一帧就是一次肉眼可见的顿（开场「卡一下」的来源之一）。拆成四段、段内再把元件摊到多帧，
    /// 帧时间就被摊平了，而且玩家看到的是「关卡正在被读出来」而不是「一瞬间全都有了」。
    ///
    /// 刻意<b>不用文字标识</b>：TextMesh 走动态字体，第一次出现一个新字符会重建字体图集，
    /// 那是一次比建元件大得多的卡顿，正好砸在演出最需要顺的时刻。几何光块没有这个风险。
    /// </summary>
    [RequireComponent(typeof(BitFlowRunner))]
    [RequireComponent(typeof(LevelBuilder))]
    public sealed class LevelReveal : MonoBehaviour
    {
        [Header("节奏（秒）")]
        [Tooltip("先等一小会儿再动：黑幕要先盖上，否则第一帧的空场会被玩家看见")]
        [SerializeField, Min(0f)] float leadIn = 0.12f;

        [Tooltip("第 1 段：地板 + 网格线（一次建完，本来就只有十几根线）")]
        [SerializeField, Min(0f)] float gridStage = 0.22f;

        [Tooltip("第 2 段：墙")]
        [SerializeField, Min(0f)] float wallStage = 0.30f;

        [Tooltip("第 3 段：发射器 + 接收器")]
        [SerializeField, Min(0f)] float ioStage = 0.30f;

        [Tooltip("第 4 段：分流器 / 非门 / 过滤器")]
        [SerializeField, Min(0f)] float logicStage = 0.30f;

        [Header("重播")]
        [Tooltip("第一次进关演全；之后的 REPLAY / 换关按这个倍速加速（1 = 不加速）")]
        [SerializeField, Range(1f, 6f)] float replaySpeedUp = 2.5f;

        [Header("落点光块")]
        [Tooltip("元件落下前先在落点闪几下，标出「这一格要来了」。关掉就是纯逐个出现，没有任何前摇")]
        [SerializeField] bool showBeacon = true;

        [Tooltip("光块堆在落点上方的总高度（一格 = 1 世界单位）。要明显高于元件本体，否则看着像元件已经落下了")]
        [SerializeField, Min(0.2f)] float beaconHeight = 0.9f;

        const int BeaconChips = 3;

        LevelBuilder builder;
        Coroutine routine;
        bool playedOnce;

        /// <summary>演出正在跑（此时场上元件是不完整的，别让任何系统按坐标去查元件）。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>整段演出结束（或被跳过）时触发，方便 HUD 之类接在「关卡真正就绪」之后。</summary>
        public event Action Finished;

        void Awake()
        {
            builder = GetComponent<LevelBuilder>();
        }

        /// <summary>
        /// 从空场开始演这一关。返回 false 表示这次没演（组件被禁用 / 没数据），调用方要自己把画面补全。
        /// 由 <see cref="BitFlowRunner"/> 在数据准备完之后调用，不挂在事件上 ——
        /// REPLAY 和换关都不发 LevelLoaded，但它们同样需要演出。
        /// </summary>
        public bool BeginReveal(LevelDefinition definition)
        {
            if (!isActiveAndEnabled || definition == null) return false;

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Reveal(definition));
            return true;
        }

        /// <summary>跳过演出、立刻把整关建出来（游戏内编辑器每改一格就重建时走这条）。</summary>
        public void Immediate()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (builder == null) builder = GetComponent<LevelBuilder>();

            builder.BuildTerrain();
            builder.BuildAllElements();

            IsPlaying = false;
            Finished?.Invoke();
        }

        IEnumerator Reveal(LevelDefinition definition)
        {
            IsPlaying = true;

            // 只有第一次进关是「完整演出」，之后重播/换关按倍速快放：
            // 玩家已经知道发生了什么，再让他等两秒就是拖节奏。
            float speed = playedOnce ? Mathf.Max(1f, replaySpeedUp) : 1f;
            playedOnce = true;

            if (leadIn > 0f) yield return new WaitForSeconds(leadIn / speed);

            // 第 1 段：网格。地板 + 十几根线，一帧建完不会顿，也就不必再拆。
            builder.BuildTerrain();
            if (gridStage > 0f) yield return new WaitForSeconds(gridStage / speed);

            yield return BuildGroup(Collect(definition, CellKind.Wall), wallStage, speed);
            yield return BuildGroup(Collect(definition, CellKind.Receiver, CellKind.Emitter), ioStage, speed);
            yield return BuildGroup(Collect(definition, CellKind.Splitter, CellKind.NotGate, CellKind.Filter, CellKind.DelayGate),
                logicStage, speed);

            routine = null;
            IsPlaying = false;
            Finished?.Invoke();
        }

        /// <summary>
        /// 把指定类型的元件摊到 duration 里逐个建出来。
        /// 每个元件都从上方落下（<see cref="ElementView.PlayDropIn"/>，0.26s 带落地挤压），
        /// 落下之前先亮一下落点光块 —— 玩家能预判「下一个在这儿」，而不是被凭空出现的方块吓一下。
        /// </summary>
        IEnumerator BuildGroup(List<ElementDefinition> pending, float duration, float speed)
        {
            if (pending.Count == 0) yield break;

            // 时长摊到每个元件上：元件多就每个快一点，整段总时长仍然可控。
            float perItem = duration / pending.Count / Mathf.Max(0.05f, speed);

            for (int i = 0; i < pending.Count; i++)
            {
                ElementDefinition element = pending[i];

                GameObject beacon = showBeacon ? SpawnBeacon(element) : null;

                // 光块先亮一小会儿再落：前摇太长会显得拖，太短又读不出来。
                float warn = Mathf.Min(perItem * 0.5f, 0.09f);
                if (warn > 0f) yield return new WaitForSeconds(warn);

                ElementView view = builder.BuildElement(element);
                if (view != null) view.PlayDropIn();
                if (beacon != null) Destroy(beacon);

                float rest = perItem - warn;
                if (rest > 0f) yield return new WaitForSeconds(rest);
            }
        }

        static List<ElementDefinition> Collect(LevelDefinition definition, params CellKind[] kinds)
        {
            List<ElementDefinition> list = new List<ElementDefinition>();
            for (int k = 0; k < kinds.Length; k++)
            {
                foreach (ElementDefinition element in definition.ElementsOfKind(kinds[k])) list.Add(element);
            }
            return list;
        }

        /// <summary>
        /// 落点光块：几颗小的自发光方块叠在格子上方，颜色就是元件类型色（和场地上的元件同一个色系）。
        /// 用几何而不是文字，理由见类注释 —— 动态字体重建比这贵得多。
        /// </summary>
        GameObject SpawnBeacon(ElementDefinition element)
        {
            GameObject root = new GameObject("RevealBeacon");
            root.transform.SetParent(builder.StageRoot, false);
            root.transform.position = GridMetrics.CellToWorld(element.Coord);

            // 光块只做"落点提示"，不能抢过元件本身：小、散、别太亮（场景开着 Bloom，亮了会糊成一团白）。
            Material material = MaterialFactory.Emissive(LevelBuilder.KindColor(element.Kind), 1f, 0.2f);

            for (int i = 0; i < BeaconChips; i++)
            {
                GameObject chip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chip.name = "chip_" + i;
                Destroy(chip.GetComponent<Collider>());
                chip.transform.SetParent(root.transform, false);
                chip.transform.localPosition = new Vector3(
                    UnityEngine.Random.Range(-0.28f, 0.28f),
                    beaconHeight * (0.55f + 0.30f * i),
                    UnityEngine.Random.Range(-0.28f, 0.28f));
                chip.transform.localScale = new Vector3(0.07f, 0.03f, 0.07f);

                Renderer renderer = chip.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return root;
        }
    }
}
