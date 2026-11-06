using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// tick 驱动器：持有 BitFlowSimulation，按固定间隔推进逻辑，再把 TickReport 翻译成视觉表现
    /// （位从 A 滑到 B、销毁红闪、接收器进度/锁定）。逻辑推进与动画播放完全解耦，
    /// 所以播放/暂停/单步/倍速/重播都只是操作这一条时间轴。
    /// </summary>
    [RequireComponent(typeof(LevelBuilder))]
    public sealed class BitFlowRunner : MonoBehaviour
    {
        [Header("关卡")]
        [Tooltip("留空则使用代码内置的演示关卡（DemoLevels.CreateLevel01）")]
        [SerializeField] LevelSO levelAsset;

        [Header("节奏")]
        [Tooltip("1 倍速时每个 tick 的时长（秒）")]
        [SerializeField, Min(0.05f)] float baseTickInterval = 0.35f;

        [Tooltip("进入 Play 后是否立即推进。默认关闭：先停在 TICK 00 的静态布局上，等你按 PLAY 开始观察")]
        [SerializeField] bool autoStart;

        readonly Dictionary<int, BitActorView> bitViews = new Dictionary<int, BitActorView>();
        readonly Dictionary<int, ReceiverDefinition> receiverById = new Dictionary<int, ReceiverDefinition>();

        LevelBuilder builder;
        LevelDefinition definition;
        BitFlowSimulation simulation;

        float timer;
        float speed = 1f;
        bool paused;

        /// <summary>每推进一个 tick 后触发（HUD 用它刷新读数）。</summary>
        public event System.Action<TickReport, BitFlowSimulation> TickCompleted;

        /// <summary>整关结算时触发。</summary>
        public event System.Action<LevelResult> Finished;

        /// <summary>重置（重播）后触发，HUD 需要重建读数。</summary>
        public event System.Action ResetPerformed;

        public BitFlowSimulation Simulation => simulation;
        public LevelBuilder Builder => builder;
        public LevelDefinition Definition => definition;
        public string LevelTitle => definition != null ? definition.DisplayName : string.Empty;
        public bool IsPaused => paused;
        public bool IsFinished => simulation != null && simulation.IsFinished;
        public int TickIndex => simulation != null ? simulation.TickIndex : 0;

        /// <summary>
        /// 运行时关卡覆盖（游戏内编辑器用）。非空时优先于 levelAsset，
        /// 这样编辑结果只活在内存里，REPLAY 也不会把改动丢掉，同时不会写脏 Assets/Data 下的资产。
        /// </summary>
        public LevelDefinition RuntimeOverride { get; private set; }

        /// <summary>倍速：0.25 ~ 3，tick 时长 = baseTickInterval / speed。</summary>
        public float Speed
        {
            get => speed;
            set => speed = Mathf.Clamp(value, 0.25f, 3f);
        }

        void Awake()
        {
            builder = GetComponent<LevelBuilder>();

            // 场景里没挂 BIT 模式时自动补一个：保证任何场景直接 Play 都能进编辑模式。
            // 不想开放编辑就删掉这段，并在场景里移除 BitFlowBuildMode 即可。
            if (GetComponent<BitFlowBuildMode>() == null) gameObject.AddComponent<BitFlowBuildMode>();
        }

        void Start()
        {
            BuildLevel();
            paused = !autoStart;
        }

        void BuildLevel()
        {
            definition = RuntimeOverride != null
                ? RuntimeOverride
                : levelAsset != null ? levelAsset.ToDefinition() : DemoLevels.CreateLevel01();
            if (baseTickInterval <= 0f) baseTickInterval = definition.TickInterval;

            receiverById.Clear();
            foreach (ElementDefinition element in definition.Elements)
            {
                if (element is ReceiverDefinition receiver) receiverById[receiver.Id] = receiver;
            }

            bitViews.Clear();
            simulation = new BitFlowSimulation(definition);
            builder.Build(levelAsset, definition);
            timer = 0f;
        }

        void Update()
        {
            if (simulation == null || paused || simulation.IsFinished) return;

            float interval = CurrentInterval;
            timer += Time.deltaTime;
            if (timer >= interval)
            {
                timer -= interval;
                AdvanceTick(false, interval);
            }
        }

        float CurrentInterval => Mathf.Max(0.05f, baseTickInterval / Mathf.Clamp(speed, 0.25f, 3f));

        /// <summary>播放 / 暂停切换。</summary>
        public void TogglePause()
        {
            if (simulation == null || simulation.IsFinished) return;
            paused = !paused;
            timer = 0f;
        }

        public void Play()
        {
            if (simulation != null && !simulation.IsFinished) paused = false;
        }

        /// <summary>单步：推进一个 tick 并直接吸附到终点（不做插值，便于逐帧观察）。</summary>
        public void StepOnce()
        {
            if (simulation == null || simulation.IsFinished) return;
            paused = true;
            timer = 0f;
            AdvanceTick(true, CurrentInterval);
        }

        /// <summary>重播：重建关卡与模拟，从第 0 个 tick 重新开始。编辑出来的关卡同样保留。</summary>
        public void ResetLevel()
        {
            StopAllCoroutines();
            DestroyBitViews();

            BuildLevel();

            // 重播后同样先停住：让你先看清 TICK 00 的布局，再按 PLAY 开始
            paused = !autoStart;
            ResetPerformed?.Invoke();
        }

        /// <summary>
        /// 用一份运行时关卡重建场景并停在 TICK 00（游戏内编辑器每改一次格子就会调用）。
        /// 只覆盖内存里的 definition，不碰 levelAsset。
        /// </summary>
        public void ShowDefinition(LevelDefinition next)
        {
            if (next == null) return;

            RuntimeOverride = next;
            StopAllCoroutines();
            DestroyBitViews();
            BuildLevel();

            paused = true;
            ResetPerformed?.Invoke();
        }

        /// <summary>切进编辑模式时用：停住时间轴，避免编辑和推进互相打架。</summary>
        public void PauseForEditing()
        {
            if (simulation == null) return;
            paused = true;
            timer = 0f;
        }

        void DestroyBitViews()
        {
            foreach (BitActorView view in bitViews.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            bitViews.Clear();
        }

        void AdvanceTick(bool snap, float interval)
        {
            TickReport report = simulation.Step();
            ApplyReport(report, snap, snap ? 0f : Mathf.Max(0.05f, interval * 0.7f));
            TickCompleted?.Invoke(report, simulation);

            if (report.IsFinished)
            {
                paused = true;
                Finished?.Invoke(simulation.BuildResult());
            }
        }

        void ApplyReport(TickReport report, bool snap, float moveDuration)
        {
            // 1) 销毁：格子上爆红闪，同时让所在元件红闪（撞墙 / 过滤器拒绝 / 逆行撞入）
            foreach (BitDestroyed destroyed in report.Destroyed)
            {
                SpawnFlash(GridMetrics.CellToWorld(destroyed.Coord), new Color(1f, 0.18f, 0.2f));

                ElementView element = builder.GetElementAt(destroyed.Coord);
                if (element != null) element.PlayError();

                if (bitViews.TryGetValue(destroyed.BitId, out BitActorView destroyedView) && destroyedView != null)
                {
                    Destroy(destroyedView.gameObject);
                }
                bitViews.Remove(destroyed.BitId);
            }

            // 2) 新位出现
            foreach (BitSpawned spawned in report.Spawns)
            {
                CreateBitView(spawned.BitId, spawned.Value, GridMetrics.CellToWorld(spawned.Coord));
            }

            // 3) 位移（从当前视觉位置滑向目标格，避免与上一段动画打架）
            foreach (BitMoved move in report.Moves)
            {
                if (!move.Moved) continue;
                if (!bitViews.TryGetValue(move.BitId, out BitActorView view) || view == null) continue;

                Vector3 target = GridMetrics.CellToWorld(move.To);
                if (snap || moveDuration <= 0f) view.transform.position = target;
                else StartCoroutine(SlideRoutine(view, target, moveDuration));
            }

            // 4) 接收器反馈：匹配亮绿、不匹配红脉冲
            foreach (DeliveryEvent delivery in report.Deliveries)
            {
                ElementView view = builder.GetElement(delivery.ReceiverId);
                if (view == null) continue;

                if (delivery.Matched) view.PlayAccepted();
                else view.PlayError();
            }

            // 5) 值可能被非门改掉：按模拟器终态同步一次颜色
            foreach (BitSnapshot snapshot in simulation.GetBits())
            {
                if (bitViews.TryGetValue(snapshot.BitId, out BitActorView view) && view != null)
                {
                    view.SetValue(snapshot.Value);
                }
            }

            RefreshProgressViews();
        }

        IEnumerator SlideRoutine(BitActorView view, Vector3 target, float duration)
        {
            Vector3 start = view.transform.position;
            float elapsed = 0f;
            while (elapsed < duration && view != null)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float eased = k * k * (3f - 2f * k); // smoothstep：起步与收尾都柔和
                view.transform.position = Vector3.Lerp(start, target, eased);
                yield return null;
            }
            if (view != null) view.transform.position = target;
        }

        void CreateBitView(int bitId, byte value, Vector3 position)
        {
            GameObject go = new GameObject("Bit_" + bitId);
            go.transform.SetParent(builder.BitsRoot, false);

            BitActorView view = go.AddComponent<BitActorView>();
            view.Init(bitId, value, position);
            bitViews[bitId] = view;
        }

        void SpawnFlash(Vector3 position, Color color)
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flash.name = "Flash";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.SetParent(builder.BitsRoot, false);
            flash.transform.position = position + Vector3.up * 0.32f;

            Material material = new Material(MaterialFactory.LitShader) { name = "M_Flash" };
            MaterialFactory.SetBaseColor(material, color);
            MaterialFactory.SetEmission(material, color, 5f);
            flash.GetComponent<Renderer>().sharedMaterial = material;

            flash.AddComponent<FlashBurst>().Init(color, 0.42f);
        }

        /// <summary>刷新接收器进度条/锁定状态，以及发射器序列点阵的消耗进度。</summary>
        void RefreshProgressViews()
        {
            foreach (ReceiverProgress progress in simulation.GetReceiverProgress())
            {
                ElementView view = builder.GetElement(progress.ReceiverId);
                if (view == null) continue;

                view.SetReceiverProgress(progress.ReceivedCount, progress.TargetCount);

                if (progress.IsComplete && progress.IsStillValid && !progress.IsOverflow)
                {
                    view.SetLocked();
                }
            }

            foreach (EmitterProgress progress in simulation.GetEmitterProgress())
            {
                ElementView view = builder.GetElement(progress.EmitterId);
                if (view == null) continue;

                view.SetEmitterProgress(progress.EmittedCount);
            }
        }

        public ReceiverDefinition GetReceiverDefinition(int receiverId)
        {
            return receiverById.TryGetValue(receiverId, out ReceiverDefinition receiver) ? receiver : null;
        }
    }
}
