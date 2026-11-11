using System.IO;
using NUnit.Framework;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 通关记录的身份规则：内置关认显示名，JSON 关认文件路径。
    ///
    /// 这些测试只碰纯函数（<see cref="SaveStore.JsonLevelKey"/>），
    /// 不读写存档 —— 否则跑一次测试就会动到玩家本机的 <c>save_editor.json</c>。
    /// </summary>
    public class SaveStoreTests
    {
        [Test]
        public void JsonLevelKey_NullOrBlank_ReturnsNull()
        {
            Assert.IsNull(SaveStore.JsonLevelKey(null));
            Assert.IsNull(SaveStore.JsonLevelKey(string.Empty));
            Assert.IsNull(SaveStore.JsonLevelKey("   "));
        }

        [Test]
        public void JsonLevelKey_SameFileWithDifferentSeparators_ProducesSameKey()
        {
            string relative = Path.Combine("Levels", "Copy.json");
            string alternate = relative.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Assert.AreEqual(SaveStore.JsonLevelKey(relative), SaveStore.JsonLevelKey(alternate));
        }

        [Test]
        public void JsonLevelKey_TwoFilesWithSameDisplayName_HaveDifferentKeys()
        {
            string original = SaveStore.JsonLevelKey(Path.Combine("Levels", "Copy.json"));
            string renamed = SaveStore.JsonLevelKey(Path.Combine("Levels", "Copy_01.json"));

            Assert.AreNotEqual(original, renamed,
                "两个 JSON 文件即使显示名相同，也必须按各自的路径分开记通关");
        }

        [Test]
        public void JsonLevelKey_IsAbsolute_SoWorkingDirectoryCannotChangeIdentity()
        {
            string key = SaveStore.JsonLevelKey(Path.Combine("Levels", "Copy.json"));

            Assert.IsTrue(Path.IsPathRooted(key), "存档键必须是绝对路径，否则换个工作目录就认不出同一关");
        }
    }
}
