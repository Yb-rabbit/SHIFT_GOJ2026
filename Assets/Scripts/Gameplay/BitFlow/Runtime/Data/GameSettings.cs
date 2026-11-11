using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 玩家设置（目前只有音频，帮助提示开关先一并存着）。
    ///
    /// 存储放在 <see cref="SaveStore"/> 的存档文件里，而不是 PlayerPrefs：
    /// 项目里所有跨关数据本来就只有一个存档，另开一套会让"清进度"和"清设置"两套生命周期打架。
    /// <see cref="SaveStore.ClearClearedLevels"/>（RESET PROGRESS）不碰设置，
    /// 只有 <see cref="SaveStore.ClearAll"/> 才把设置一起恢复默认。
    ///
    /// 音量用 <see cref="AudioListener.volume"/> 做总闸：
    /// 现在场景里没有 AudioMixer，以后加 BGM / 更多音效也不用逐条改音量，
    /// 而且 0% 与"关掉"都表现为完全静音，不会出现某一路漏音。
    /// </summary>
    public static class GameSettings
    {
        public const int DefaultVolumePercent = 80;

        /// <summary>音频总开关。关闭时保留 <see cref="AudioVolumePercent"/>，再打开还是原来的音量。</summary>
        public static bool AudioOn => SaveStore.Data.audioOn != 0;

        /// <summary>音频总音量（0 ~ 100）。</summary>
        public static int AudioVolumePercent => Mathf.Clamp(SaveStore.Data.audioVolumePercent, 0, 100);

        /// <summary>是否显示玩法提示。帮助面板接进来之前先只做持久化。</summary>
        public static bool HelpHintOn => SaveStore.Data.helpHintOn != 0;

        /// <summary>打开/关闭音频，并立即写盘、立即生效。</summary>
        public static void SetAudioOn(bool on)
        {
            SaveData data = SaveStore.Data;
            int value = on ? 1 : 0;
            if (data.audioOn == value) return;

            data.audioOn = value;
            SaveStore.Save();
            ApplyAudio();
        }

        public static void ToggleAudio()
        {
            SetAudioOn(!AudioOn);
        }

        /// <summary>设置音量（夹在 0 ~ 100）。关着音频时也会记住新音量，但不会自己把音频打开。</summary>
        public static void SetAudioVolumePercent(int percent)
        {
            percent = Mathf.Clamp(percent, 0, 100);

            SaveData data = SaveStore.Data;
            if (data.audioVolumePercent == percent) return;

            data.audioVolumePercent = percent;
            SaveStore.Save();
            ApplyAudio();
        }

        /// <summary>按步长加减音量（暂停面板的 [-] / [+] 用 10% 一档）。</summary>
        public static void AdjustAudioVolume(int delta)
        {
            SetAudioVolumePercent(AudioVolumePercent + delta);
        }

        public static void SetHelpHintOn(bool on)
        {
            SaveData data = SaveStore.Data;
            int value = on ? 1 : 0;
            if (data.helpHintOn == value) return;

            data.helpHintOn = value;
            SaveStore.Save();
        }

        /// <summary>把当前设置落到 <see cref="AudioListener.volume"/>。进出关卡、开关面板后都可以再调，幂等。</summary>
        public static void ApplyAudio()
        {
            AudioListener.volume = AudioOn ? AudioVolumePercent / 100f : 0f;
        }

        /// <summary>
        /// 进游戏第一帧之前先把音量对上：否则上一局的 0% 会一直留到第一次打开暂停面板。
        /// 这里会触发一次存档读取，读不到就按默认值（开、80%）继续。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyOnStartup()
        {
            ApplyAudio();
        }
    }
}
