using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 音效清单的接线测试。
    ///
    /// <see cref="BitFlowSfx"/> 对空 clip 是静默的，所以「音效没声」这类问题在代码里看不出来，
    /// 只会表现为玩家觉得少了点反馈。这里直接把 Resources 下的清单读出来，逐个槽位检查有没有挂上。
    /// </summary>
    public class SfxLibraryTests
    {
        static SfxLibrarySO LoadLibrary()
        {
            SfxLibrarySO library = Resources.Load<SfxLibrarySO>(SfxLibrarySO.ResourcePath);
            Assert.IsNotNull(library, "Assets/Resources/" + SfxLibrarySO.ResourcePath + " 找不到，所有音效都会静音");
            return library;
        }

        [Test]
        public void AllSlots_AreWired()
        {
            SfxLibrarySO library = LoadLibrary();

            Assert.IsNotNull(library.EmitterShot, "发射器音效没挂");
            Assert.IsNotNull(library.ReceiverMatch, "接收器命中音效没挂");
            Assert.IsNotNull(library.TerminalOpen, "终端开机音效没挂");
            Assert.IsNotNull(library.LevelSuccess, "过关结算音效没挂");
            Assert.IsNotNull(library.LevelFail, "失败结算音效没挂");
        }

        [Test]
        public void ResultClips_AreDistinct()
        {
            SfxLibrarySO library = LoadLibrary();

            // 两条结算音挂成同一段素材的话，玩家闭着眼分不出这关过没过。
            Assert.AreNotSame(library.LevelSuccess, library.LevelFail, "过 / 败结算音不能是同一条素材");
        }

        [Test]
        public void Load_ReturnsCachedInstance()
        {
            SfxLibrarySO first = SfxLibrarySO.Load();
            SfxLibrarySO second = SfxLibrarySO.Load();

            Assert.IsNotNull(first);
            Assert.AreSame(first, second, "每帧播放都要取清单，必须命中缓存而不是反复 Resources.Load");
        }
    }
}
