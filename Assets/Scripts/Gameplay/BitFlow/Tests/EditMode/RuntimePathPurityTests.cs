using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ShiftGo.BitFlow.Data;
using UnityEngine;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 运行时关卡目录的「不许回指工程」护栏。
    ///
    /// 为什么要这层测试：开发期最顺手的一句是 <c>Path.GetFullPath("LevelJson")</c> 或
    /// <c>Application.dataPath + "/../LevelJson"</c> —— 它在本机 EditMode 里指得通，于是
    /// 「读取作者源目录」这种调试捷径很容易被顺手写进运行时。但工程相对路径在打包后指向
    /// <b>玩家磁盘上的任意位置</b>（WebGL 上干脆不存在），表现是「作者的关卡在编辑器里能看见、
    /// 发出去一台都看不见」，而且不报错、不抛异常，只能靠有人想起来去看。
    ///
    /// 真正的关卡库只有一个：<see cref="LevelJsonLibrary.Folder"/>（persistentDataPath 下）。
    /// 作者源目录 → 包内 Resources 的那一步由编辑器侧的 <c>LevelSeedBundler</c> 完成，
    /// 运行时不该、也不需要知道工程目录长什么样。
    ///
    /// <b>两处刻意的例外</b>（别照字面「修正」它们）：
    /// - <c>LevelJsonLibrary.ProgramFolder</c> 用 <c>Application.dataPath</c> —— 这个属性在 Player 里
    ///   指向 <c>&lt;exe&gt;\&lt;产品名&gt;_Data</c>，取父目录就是 exe 所在目录，是<b>合法的 Player API</b>。
    ///   所以下面的黑名单里没有它。
    /// - <c>ShareFolder</c> 故意落在程序旁边的 <c>Levels/share</c>（编辑器里就是工程根的 <c>Levels/</c>），
    ///   因为它的用途是「让玩家一眼找到、能手丢文件」，藏在 AppData 里就失去意义。
    ///   它不是关卡库，所以不受 <see cref="LevelJsonLibrary.Folder"/> 的包含关系约束。
    ///
    /// 这类测试只能防「回退」，防不了「从一开始就写错的复杂逻辑」—— 但回退正是最容易发生的那种。
    /// </summary>
    public class RuntimePathPurityTests
    {
        /// <summary>运行时程序集源码根（和 <c>ShiftGo.BitFlow.asmdef</c> 的目录一致）。</summary>
        const string RuntimeFolder = "Assets/Scripts/Gameplay/BitFlow/Runtime";

        /// <summary>
        /// 运行时源码里不该出现的字面量：都是「工程内相对路径」，本机指得通、打包后一定指错。
        ///
        /// 故意<b>不</b>禁的几种（见类注释）：
        /// - <c>Application.dataPath</c>：Player 里有合法语义（定位程序目录）；
        /// - <c>"LevelJson"</c>（不带斜杠）：那是 <see cref="LevelJsonLibrary.FolderName"/>，
        ///   拼到 persistentDataPath 后面正是对的做法。
        /// </summary>
        static readonly string[] ForbiddenLiterals =
        {
            "\"Assets/",
            "\"Assets\\",
            "\"LevelJson/",
            "\"LevelJson\\",
            "\"Library/",
            "\"Library\\",
        };

        [Test]
        public void Folder_IsUnderPersistentDataPath()
        {
            string folder = Normalize(LevelJsonLibrary.Folder);
            string root = Normalize(Application.persistentDataPath);

            Assert.IsTrue(
                folder.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase),
                "关卡库必须在 persistentDataPath 之下，否则打包后写不进去 / 也读不回来。\n"
                + "  目录：" + folder + "\n  期望前缀：" + root);
        }

        [Test]
        public void Folder_IsNotTheAuthorSourceFolder()
        {
            // 工程根的 LevelJson/（EditMode 的 CWD 就是工程根）是设计侧的交接站，
            // 和运行时库同名但一个字符都不该重合 —— 混起来就会「编辑器里能看见、发出去看不见」。
            string folder = Normalize(LevelJsonLibrary.Folder);
            string authorSource = Normalize("LevelJson");

            Assert.AreNotEqual(authorSource, folder,
                "运行时的关卡库指到了工程根的作者源目录。打包后这个路径在玩家机上不存在。");
        }

        [Test]
        public void Folder_IsNotInsideTheProjectFolder()
        {
            // 编辑器里 dataPath = <工程>/Assets；取父目录就是工程根。真跑在播放器里时这个断言同样成立
            // （播放器的 dataPath 指向构建产物的 _Data，和关卡库没有包含关系）。
            string projectRoot = Normalize(Path.GetDirectoryName(Application.dataPath));
            string folder = Normalize(LevelJsonLibrary.Folder);

            Assert.IsFalse(
                folder.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase),
                "关卡库落进了工程目录 —— 说明取路径时用了工程相对路径，打包后这台机器之外都不存在。\n"
                + "  目录：" + folder + "\n  工程根：" + projectRoot);
        }

        [Test]
        public void IncomingFolder_StaysUnderTheLevelFolder()
        {
            // 收件箱是纯内部目录，没有「玩家要看得见」的理由，必须跟着关卡库走。
            // （ShareFolder 不在其列，理由见类注释。）
            string folder = Normalize(LevelJsonLibrary.Folder);
            string incoming = Normalize(LevelJsonLibrary.IncomingFolder);

            Assert.IsTrue(
                incoming.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase),
                "收件箱必须由 LevelJsonLibrary.Folder 拼出来（这样换平台只改一处）：" + incoming);
        }

        [Test]
        public void RuntimeSource_HasNoProjectPathLiterals()
        {
            List<string> files = new List<string>(RuntimeSources());
            Assert.IsNotEmpty(files, "没扫到运行时源码，路径变了？" + RuntimeFolder);

            List<string> offenders = new List<string>();

            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();

                    // 注释里举例子是正常的（文档性说明常常要写出「工程里那个目录」），不算违规
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || trimmed.StartsWith("/*", StringComparison.Ordinal)) continue;

                    foreach (string pattern in ForbiddenLiterals)
                    {
                        if (line.IndexOf(pattern, StringComparison.Ordinal) < 0) continue;

                        offenders.Add(Path.GetFileName(file) + ":" + (i + 1)
                            + "  " + pattern + "  →  " + trimmed);
                        break; // 一行报一次就够，免得同一个字面量刷两遍
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "运行时源码里出现了「工程内相对路径」字面量。打包后这些路径指向玩家磁盘上的任意位置"
                + "（WebGL 上不存在），表现为「编辑器里能看见关卡、发出去一台都看不见」，而且不报错。\n"
                + "正确做法：路径一律由 LevelJsonLibrary.Folder 派生；作者源 → 包内那一步交给 LevelSeedBundler。\n"
                + string.Join("\n", offenders));
        }

        // ------------------------------------------------------------------ 工具

        static IEnumerable<string> RuntimeSources()
        {
            string root = Path.GetFullPath(RuntimeFolder);
            if (!Directory.Exists(root)) yield break;

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        /// <summary>统一分隔符与大小写比较口径：Windows 给反斜杠、macOS 给正斜杠。</summary>
        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }
    }
}
