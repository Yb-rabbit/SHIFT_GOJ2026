using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 护栏：<c>Runtime/Core</c> 必须保持「纯 C#」。
    ///
    /// 外部关卡编辑器（<c>Tools/LevelStudio</c>）是把这批源文件<b>直接链接编译</b>进去的，
    /// 所以 Core 里一旦出现 UnityEngine / UnityEditor，工具立刻编不过 —— 而且是在别人机器上才炸。
    /// 这条测试让它在 Unity 里就红掉，并指名道姓是哪个文件。
    /// </summary>
    public class CoreLayerPurityTests
    {
        static string CoreFolder =>
            Path.Combine(Application.dataPath, "Scripts", "Gameplay", "BitFlow", "Runtime", "Core");

        static string CsprojPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "LevelStudio", "ShiftGoLevelStudio.csproj"));

        static string[] CoreSources()
        {
            Assert.IsTrue(Directory.Exists(CoreFolder), "Core 目录不存在：" + CoreFolder);

            string[] files = Directory.GetFiles(CoreFolder, "*.cs", SearchOption.TopDirectoryOnly);
            Assert.IsNotEmpty(files, "Core 目录里一个 .cs 都没有？" + CoreFolder);
            return files;
        }

        [Test]
        public void CoreDoesNotTryToUseUnity()
        {
            foreach (string file in CoreSources())
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);

                StringAssert.DoesNotContain("using UnityEngine", text,
                    name + " 引入了 UnityEngine —— Core 要能被非 Unity 进程（关卡编辑器）编译");
                StringAssert.DoesNotContain("using UnityEditor", text,
                    name + " 引入了 UnityEditor —— Core 要能被非 Unity 进程（关卡编辑器）编译");
            }
        }

        [Test]
        public void CoreDoesNotInheritUnityBaseTypes()
        {
            foreach (string file in CoreSources())
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);

                StringAssert.DoesNotContain(": MonoBehaviour", text,
                    name + " 继承了 MonoBehaviour —— Core 里只放纯 C# 逻辑（界面/组件放 Runtime/View）");
                StringAssert.DoesNotContain(": ScriptableObject", text,
                    name + " 继承了 ScriptableObject —— Core 里只放纯 C# 逻辑（资产放 Runtime/Data）");
            }
        }

        /// <summary>
        /// 反向确认工具工程还在用通配把整个 Core 目录编进去。
        /// 如果那里被改成逐个列举，以后新加的 Core 文件就会被工具悄悄漏掉（编辑器和游戏算出两套结果）。
        /// </summary>
        [Test]
        public void LevelStudioCompilesWholeCoreFolder()
        {
            // 只 checkout 了 Assets 的环境（没有 Tools 目录）不算失败，只是这条护栏没法验。
            if (!File.Exists(CsprojPath)) Assert.Inconclusive("没找到关卡编辑器工程：" + CsprojPath);

            string csproj = File.ReadAllText(CsprojPath);
            StringAssert.Contains(@"Runtime\Core\*.cs", csproj,
                "工具工程里没有 Core 的通配 Include —— 新加的 Core 文件会被漏编");
        }
    }
}
