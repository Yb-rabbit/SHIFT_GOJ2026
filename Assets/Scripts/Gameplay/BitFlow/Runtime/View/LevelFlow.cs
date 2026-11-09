using System;
using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 关卡循环：第一关是谁、通关后去哪、重试怎么重来。只管顺序与加载，不碰渲染与模拟。
    ///
    /// 没有关卡表时它什么都不做（场景里直接放一个 LevelSO 的演示用法依然有效），
    /// 所以这个组件是可选的：想跑单关就用 BitFlowRunner 的 levelAsset，想做关卡串就挂上它。
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class LevelFlow : MonoBehaviour
    {
        [Header("关卡表")]
        [Tooltip("按顺序排好的关卡。留空则沿用 BitFlowRunner 上挂的那一关")]
        [SerializeField] LevelCatalogSO catalog;

        [Header("规则")]
        [Tooltip("正式关卡请保持勾选：进来先把 BUILD MODE 摘掉，否则玩家可以无限补元件")]
        [SerializeField] bool disableBuildMode = true;

        BitFlowRunner runner;
        int index = -1;
        bool autoStartHeld;
        bool pendingAutoStart;

        // 运行时从 JSON 读进来的关卡：不是资产，得由这里负责建、负责回收。
        // 只在 LoadJson 期间有效，切回目录关卡就销毁。
        LevelSO currentJsonLevel;
        string currentJsonPath;

        /// <summary>换关后触发（参数是新的 LevelSO），HUD / 手牌盘靠它刷新。</summary>
        public event Action<LevelSO> LevelLoaded;

        public BitFlowRunner Runner => runner;
        public int CurrentIndex => index;
        public int LevelCount => catalog != null ? catalog.Count : 0;
        public LevelSO Current => currentJsonLevel != null
            ? currentJsonLevel
            : (catalog != null ? catalog.Get(index) : null);
        public bool HasNext => currentJsonLevel == null && catalog != null && index + 1 < catalog.Count;

        /// <summary>当前跑的是不是运行时 JSON 关卡（不是资产，重试要重新读盘）。</summary>
        public bool IsJsonLevel => currentJsonLevel != null;

        /// <summary>当前 JSON 关卡的磁盘路径；不是 JSON 关卡时为 null。</summary>
        public string CurrentJsonPath => currentJsonPath;

        /// <summary>关卡表本体（选关面板要按它列全部关卡）。可能为 null：单关演示场景没挂表。</summary>
        public LevelCatalogSO Catalog => catalog;

        void Awake()
        {
            runner = FindObjectOfType<BitFlowRunner>();
            if (runner == null)
            {
                Debug.LogWarning("[BitFlow] LevelFlow 没找到 BitFlowRunner，已自动禁用");
                enabled = false;
                return;
            }

            if (disableBuildMode) runner.DisableBuildMode();
        }

        void Start()
        {
            if (LevelCount == 0) return;

            // 开场演出还没演完：这会儿读关卡、建网格都是在给演出当背景噪音，
            // 而且黑幕退场时网格已经长完了、"从无到有"那一层就白演了。
            // 记下"欠了一次加载"，等演出的调度者调 ReleaseAutoStart 再补。
            if (autoStartHeld)
            {
                pendingAutoStart = true;
                return;
            }

            Load(0);
        }

        /// <summary>
        /// 压住「进来就自动加载第一关」。开场演出（<see cref="IntroTerminal"/>）要先在
        /// <b>空场</b>上播，所以必须在自己的 Awake 里、本组件的 Start 之前调用。
        /// 没有关卡表的场景调它也无害。
        /// </summary>
        public void HoldAutoStart()
        {
            autoStartHeld = true;
        }

        /// <summary>
        /// 放开被压住的首次加载，并立刻补做。返回是否真的加载了 ——
        /// 调用方（开场演出的调度者）据此判断接着该不该继续 cue 黑幕。
        /// 没被压住时调用等于空操作，不会重复加载。
        /// </summary>
        public bool ReleaseAutoStart()
        {
            autoStartHeld = false;

            if (!pendingAutoStart) return false;

            pendingAutoStart = false;
            return Load(0);
        }

        /// <summary>加载关卡表里的第 index 关；越界或空项返回 false。</summary>
        public bool Load(int target)
        {
            if (catalog == null) return false;

            LevelSO level = catalog.Get(target);
            if (level == null)
            {
                Debug.LogWarning("[BitFlow] 关卡表第 " + target + " 项是空的，跳过");
                return false;
            }

            ClearJsonLevel();
            index = target;
            runner.LoadLevel(level);
            LevelLoaded?.Invoke(level);
            return true;
        }

        /// <summary>
        /// 加载一份运行时 JSON 关卡（选关面板里的 CUSTOM 行）。读盘/解析失败只警告不改当前关。
        ///
        /// 走的是同一条路：JSON → 临时 <see cref="LevelSO"/> → <see cref="BitFlowRunner.LoadLevel"/>，
        /// 所以打包后的 Player 和编辑器 Play 行为完全一致，没有第二套加载逻辑。
        /// </summary>
        public bool LoadJson(LevelJsonEntry entry)
        {
            return entry != null && LoadJson(entry.Path);
        }

        /// <summary>按磁盘路径加载运行时 JSON 关卡。</summary>
        public bool LoadJson(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (!LevelJsonLibrary.TryLoad(path, out LevelSO level, out List<string> problems))
            {
                Debug.LogWarning("[BitFlow] 关卡 JSON 读不进来：" + path + " → " + string.Join("；", problems));
                return false;
            }

            ClearJsonLevel();

            currentJsonLevel = level;
            currentJsonPath = path;
            index = -1;

            runner.LoadLevel(level);
            LevelLoaded?.Invoke(level);
            return true;
        }

        /// <summary>下一关；已经是最后一关则返回 false（HUD 据此把按钮收起来）。JSON 关卡没有「下一关」。</summary>
        public bool Next()
        {
            if (IsJsonLevel) return false;
            if (index < 0) return Load(0);
            if (!HasNext) return false;
            return Load(index + 1);
        }

        /// <summary>
        /// 重试当前关：连预设布局和手牌一起复原，撤销栈也一起清掉。
        /// JSON 关卡重新读盘 —— 玩家手改了文件，重试一次就生效。
        /// </summary>
        public bool Retry()
        {
            if (IsJsonLevel) return LoadJson(currentJsonPath);
            return Load(index < 0 ? 0 : index);
        }

        void ClearJsonLevel()
        {
            LevelSO previous = currentJsonLevel;
            currentJsonLevel = null;
            currentJsonPath = null;
            LevelJsonLibrary.DestroyLevel(previous);
        }

        void OnDestroy()
        {
            ClearJsonLevel();
        }
    }
}
