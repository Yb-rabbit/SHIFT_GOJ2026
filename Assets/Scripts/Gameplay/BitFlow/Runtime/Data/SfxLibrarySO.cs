using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 音效清单：把 <c>Assets/Audio</c> 下的音频资产集中挂到一份 Resources 资源上，
    /// 运行时用 <see cref="Load"/> 取，不依赖任何场景 / 预制体上的序列化引用。
    ///
    /// 和 <see cref="LevelTypeRegistrySO"/> 同一个思路：
    /// 场景是代码动态搭的，音效不能只靠 Inspector 拖引用；
    /// 放进 Resources 后 EditMode 测试、直接 Play 任意场景、打包后都能走同一条加载路径。
    ///
    /// 资产路径固定为 <c>Resources/BitFlow/SfxLibrary</c>，改路径会让所有音效静音（不崩）。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/BitFlow/Sfx Library", fileName = "SfxLibrary")]
    public sealed class SfxLibrarySO : ScriptableObject
    {
        /// <summary>Resources 下的固定路径（不含扩展名）。</summary>
        public const string ResourcePath = "BitFlow/SfxLibrary";

        [Header("发射器")]
        [Tooltip("发射器吐出每一个位时播放（同一 tick 多台发射器只播一次，避免叠成噪音）")]
        [SerializeField] AudioClip emitterShot;

        [Header("接收器")]
        [Tooltip("位进入接收器且与目标序列匹配时播放；不匹配不播，避免和红闪重复提示")]
        [SerializeField] AudioClip receiverMatch;

        [Header("终端")]
        [Tooltip("开场终端面板启动（开机动画开始）的那一刻播放")]
        [SerializeField] AudioClip terminalOpen;

        static SfxLibrarySO cached;
        static bool warned;

        public AudioClip EmitterShot => emitterShot;
        public AudioClip ReceiverMatch => receiverMatch;
        public AudioClip TerminalOpen => terminalOpen;

        /// <summary>取音效清单（带缓存）。找不到时只警告一次，播放端按 null 静音处理。</summary>
        public static SfxLibrarySO Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<SfxLibrarySO>(ResourcePath);
            if (cached == null && !warned)
            {
                warned = true;
                Debug.LogWarning("[BitFlow] 找不到音效清单 Resources/" + ResourcePath +
                                 ".asset：所有音效将保持静音。");
            }
            return cached;
        }

        /// <summary>编辑器里改完清单后清缓存，免得还在用旧的音频引用。</summary>
        public static void ClearCache()
        {
            cached = null;
            warned = false;
        }
    }
}
