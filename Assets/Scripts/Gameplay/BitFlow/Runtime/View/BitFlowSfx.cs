using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 运行时音效播放入口。全部走一个隐藏的 <see cref="AudioSource"/> + <c>PlayOneShot</c>：
    /// 音效很短、允许重叠，不需要为每条音效维护一个 AudioSource。
    ///
    /// 音量不在这里逐条乘：总音量和开关由 <see cref="GameSettings.ApplyAudio"/> 写到
    /// <see cref="AudioListener.volume"/>，这里只负责"该不该播"。
    /// 关掉音频时直接 return，连 PlayOneShot 都不调，省掉无声播放的开销。
    /// </summary>
    public static class BitFlowSfx
    {
        static AudioSource source;

        static AudioSource Source
        {
            get
            {
                // 切场景 / 停止 Play 后宿主会被销毁，这里按引用判空即可自动重建。
                if (source != null) return source;

                GameObject host = new GameObject("[BitFlow] Sfx");
                if (Application.isPlaying) Object.DontDestroyOnLoad(host);

                source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;       // 2D：发射器在屏幕里怎么转都不该改变音量
                source.volume = 1f;
                source.ignoreListenerPause = true;
                return source;
            }
        }

        public static void EmitterShot()
        {
            SfxLibrarySO library = SfxLibrarySO.Load();
            if (library != null) Play(library.EmitterShot);
        }

        public static void ReceiverMatch()
        {
            SfxLibrarySO library = SfxLibrarySO.Load();
            if (library != null) Play(library.ReceiverMatch);
        }

        public static void TerminalOpen()
        {
            SfxLibrarySO library = SfxLibrarySO.Load();
            if (library != null) Play(library.TerminalOpen);
        }

        /// <summary>整关通过（结算面板弹出 PASS 的那一刻）。</summary>
        public static void LevelSuccess()
        {
            SfxLibrarySO library = SfxLibrarySO.Load();
            if (library != null) Play(library.LevelSuccess);
        }

        /// <summary>整关失败（结算面板弹出 FAIL 的那一刻）。</summary>
        public static void LevelFail()
        {
            SfxLibrarySO library = SfxLibrarySO.Load();
            if (library != null) Play(library.LevelFail);
        }

        static void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (!GameSettings.AudioOn) return;

            Source.PlayOneShot(clip);
        }
    }
}
