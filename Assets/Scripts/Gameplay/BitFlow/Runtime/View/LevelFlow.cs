using System;
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

        /// <summary>换关后触发（参数是新的 LevelSO），HUD / 手牌盘靠它刷新。</summary>
        public event Action<LevelSO> LevelLoaded;

        public BitFlowRunner Runner => runner;
        public int CurrentIndex => index;
        public int LevelCount => catalog != null ? catalog.Count : 0;
        public LevelSO Current => catalog != null ? catalog.Get(index) : null;
        public bool HasNext => catalog != null && index + 1 < catalog.Count;

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
            if (LevelCount > 0) Load(0);
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

            index = target;
            runner.LoadLevel(level);
            LevelLoaded?.Invoke(level);
            return true;
        }

        /// <summary>下一关；已经是最后一关则返回 false（HUD 据此把按钮收起来）。</summary>
        public bool Next()
        {
            if (index < 0) return Load(0);
            if (!HasNext) return false;
            return Load(index + 1);
        }

        /// <summary>重试当前关：连预设布局和手牌一起复原，撤销栈也一起清掉。</summary>
        public bool Retry()
        {
            return Load(index < 0 ? 0 : index);
        }
    }
}
