using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 给「Unity 之外的关卡编辑器」（<c>Tools/LevelStudio</c>）准备输入：
    /// 把类型资产拍成一份 <c>LevelJson/exported/typeCatalog.json</c>，再把编辑器拉起来。
    ///
    /// 为什么要有类型目录：外部编辑器没有 AssetDatabase，打不开 <see cref="ElementTypeSO"/>；
    /// 但它打开 Unity 导出的关（那些关只写了 <c>type</c>）时必须知道每个 type 的默认序列/次数/朝向。
    /// 这份快照是 <b>从真实的 CreateDefinition 读出来的</b>，不是另抄一份默认值 —— 不会走偏。
    /// </summary>
    public static class LevelTypeCatalogTool
    {
        internal const string CatalogFile = LevelJsonTool.ExportedFolder + "/typeCatalog.json";

        /// <summary>Unity 工程根目录（= Assets 的上一级）。</summary>
        static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        internal static void ExportCatalogMenu()
        {
            int count = ExportCatalog(out string path);

            EditorUtility.DisplayDialog("类型目录",
                count > 0
                    ? "已导出 " + count + " 个元件类型 →\n" + path
                        + "\n\n外部关卡编辑器（Tools/LevelStudio）靠它认得 JSON 里的 type。"
                        + "\n新增 / 改名类型资产后要再导一次。"
                    : "没有可导出的类型（类型注册表是空的？先跑一次「刷新类型注册表」）。",
                "好");
        }

        /// <summary>写类型目录，返回写入的类型数量。写失败返回 0 并在 Console 说明。</summary>
        internal static int ExportCatalog(out string path)
        {
            path = Path.GetFullPath(CatalogFile);

            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            LevelTypeCatalog catalog = registry != null
                ? LevelTypeCatalogSource.Build(registry)
                : LevelTypeCatalogSource.BuildFallback();

            if (catalog.types == null || catalog.types.Count == 0) return 0;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
            }
            catch (IOException exception)
            {
                Debug.LogError("[BitFlow] 写类型目录失败：" + exception.Message);
                return 0;
            }

            Debug.Log("[BitFlow] 已导出类型目录（" + catalog.types.Count + " 个类型）→ " + path);
            return catalog.types.Count;
        }

        /// <summary>
        /// 做关主通道：直接拉起浏览器版关卡编辑器（<c>Tools/LevelStudio</c>）。
        ///
        /// 以前这里是「关卡 JSON/打开外部关卡编辑器」里的一个次要入口，现在「关卡编辑器」这个名字
        /// 只指它 —— Unity 内那张网格窗口已经退成检视器上的一个按钮。
        ///
        /// 优先起已经编译好的 exe（秒开）；exe 不存在、或比任何输入旧（刚改过 .cs / wwwroot /
        /// Unity 的 Core 源码），就退回 <c>.bat</c> 让 dotnet 重编一次 —— 绝不让作者对着旧界面改数据。
        /// </summary>
        [MenuItem("SHIFT GO/BitFlow/关卡编辑器", false, 21)]
        internal static void LaunchLevelStudio()
        {
            string studio = Path.Combine(ProjectRoot, "Tools", "LevelStudio");
            string bat = Path.Combine(studio, "启动关卡编辑器.bat");
            if (!File.Exists(bat))
            {
                EditorUtility.DisplayDialog("关卡编辑器",
                    "没找到：\n" + bat + "\n\n（Tools/LevelStudio 这个目录被删或没同步过来？）", "好");
                return;
            }

            // 先把类型目录刷一遍，免得编辑器里打开导出关时参数对不上
            ExportCatalog(out string catalog);

            string exe = FindFreshStudioExe(studio);
            string launch = exe ?? bat;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launch)
            {
                UseShellExecute = true,
                WorkingDirectory = exe != null ? Path.GetDirectoryName(exe) : studio,
            });

            Debug.Log("[BitFlow] 已启动关卡编辑器（"
                + (exe != null ? "已编译版本，免编译" : ".bat，会先编译一次")
                + "；类型目录：" + catalog + "）");
        }

        /// <summary>
        /// 找一份「可以免编译直接起」的 <c>ShiftGoLevelStudio.exe</c>；找不到或已过期返回 null。
        ///
        /// 两件事要分开看：
        /// - <b>宿主 / Core 的 .cs</b> 改了 → 编进 exe 的规则旧了，必须重编；
        /// - <b>wwwroot</b> 只是 Content，不参与编译（dotnet build 会复制到输出目录，但不碰 exe 的
        ///   时间戳），而 exe 运行时读的是输出目录里那份副本 —— 所以要比对的是**副本**新旧，
        ///   不是 exe 新旧。只看 exe 时间戳会让「刚改过界面」永远走 .bat，白等一次编译。
        /// </summary>
        static string FindFreshStudioExe(string studio)
        {
            string bin = Path.Combine(studio, "bin");
            if (!Directory.Exists(bin)) return null;

            string exe = null;
            DateTime exeStamp = DateTime.MinValue;

            // Release 优先，没有才用 Debug
            foreach (string configuration in new[] { "Release", "Debug" })
            {
                string folder = Path.Combine(bin, configuration);
                if (!Directory.Exists(folder)) continue;

                foreach (string candidate in
                    Directory.GetFiles(folder, "ShiftGoLevelStudio.exe", SearchOption.AllDirectories))
                {
                    DateTime stamp = File.GetLastWriteTimeUtc(candidate);
                    if (stamp > exeStamp)
                    {
                        exe = candidate;
                        exeStamp = stamp;
                    }
                }

                if (exe != null) break;
            }

            if (exe == null) return null;
            if (exeStamp < NewestSourceStamp(studio)) return null;

            string outputRoot = Path.GetDirectoryName(exe);
            return IsWebCopyUpToDate(studio, outputRoot) ? exe : null;
        }

        /// <summary>
        /// 输出目录里的 wwwroot 副本是不是和源目录一样新。
        ///
        /// exe 运行时读的是 <c>bin/&lt;config&gt;/netX/wwwroot</c>，源目录改了却没 build 的话，
        /// 起来的是旧界面 —— 这种情况必须退回 .bat 让它重新复制。
        /// </summary>
        static bool IsWebCopyUpToDate(string studio, string outputRoot)
        {
            string source = Path.Combine(studio, "wwwroot");
            if (!Directory.Exists(source)) return true;

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart('\\', '/');
                string copied = Path.Combine(outputRoot, "wwwroot", relative);

                if (!File.Exists(copied)) return false;
                if (File.GetLastWriteTimeUtc(copied) < File.GetLastWriteTimeUtc(file)) return false;
            }

            return true;
        }

        /// <summary>会被编进 exe 的源码里最新的修改时间（宿主 .cs + Unity 的 Core/*.cs）。</summary>
        static DateTime NewestSourceStamp(string studio)
        {
            DateTime newest = DateTime.MinValue;

            void Consider(string path)
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (stamp > newest) newest = stamp;
            }

            // 宿主自己的源码（bin / obj 里是生成物，不算输入）
            foreach (string source in Directory.GetFiles(studio, "*.cs", SearchOption.AllDirectories))
            {
                if (IsInsideBuildFolder(studio, source)) continue;
                Consider(source);
            }

            // 被编进宿主的 Core 源码：改了它 exe 里的规则就跟游戏对不上了
            string core = Path.Combine(
                ProjectRoot, "Assets", "Scripts", "Gameplay", "BitFlow", "Runtime", "Core");
            if (Directory.Exists(core))
            {
                foreach (string source in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
                {
                    Consider(source);
                }
            }

            return newest;
        }

        static bool IsInsideBuildFolder(string root, string path)
        {
            string relative = path.Substring(root.Length).Replace('\\', '/');
            return relative.StartsWith("/bin/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("/obj/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
