using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 出包前的关卡对账：把「源（LevelJson/）」和「包（Resources/BitFlow/Levels）」摆在一起，
    /// 一眼看出谁没进去、谁改了没重新打包。
    ///
    /// 为什么需要它：关卡进包是<b>两步</b> —— 先 <see cref="LevelSeedBundler.Bundle"/> 抄进 Resources，
    /// 再出包。抄这一步忘了跑、或者 JSON 丢错了目录（比如丢进运行时目录而不是工程里），
    /// 出包照样成功，只是包里少一关，事后只能靠翻日志猜。这个菜单把那个猜的过程换成一张表。
    ///
    /// 顺带做一件日志做不到的事：如果 <c>Builds/</c> 下留着出包产物，就把每关的名字拿去
    /// <c>resources.assets</c> 里做一次二进制字符串匹配 —— 这是「这关到底在不在包里」的硬证据，
    /// 不看时间戳、不靠日志。
    /// </summary>
    public static class LevelBundlePreflight
    {
        const string ReportName = "_preflight.txt";

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/打包前自检（关卡）", false, 45)]
        static void RunMenu()
        {
            string report = Build();
            Debug.Log("[BitFlow] 打包前自检\n" + report);

            // DisplayDialog 对超长文本不友好，长报告只给前几屏，全文在 Console / 报告文件里
            string[] lines = report.Split('\n');
            string dialog = lines.Length > 22
                ? string.Join("\n", lines, 0, 22)
                    + "\n…（全文见 Console 或 " + LevelJsonTool.ReportFolder + "/" + ReportName + "）"
                : report;

            EditorUtility.DisplayDialog("打包前自检（关卡）", dialog, "好");
        }

        static string Build()
        {
            Dictionary<string, string> sources = LevelSeedBundler.CollectSourceFiles();
            string targetAbsolute = Path.GetFullPath(LevelSeedBundler.TargetFolder);

            Dictionary<string, string> packed =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(targetAbsolute))
            {
                foreach (string file in Directory.GetFiles(targetAbsolute, "*.json"))
                {
                    packed[Path.GetFileName(file)] = file;
                }
            }

            List<string> missing = new List<string>();
            List<string> differing = new List<string>();
            List<string> stale = new List<string>();

            foreach (KeyValuePair<string, string> pair in sources)
            {
                if (!packed.TryGetValue(pair.Key, out string destination))
                {
                    missing.Add(pair.Key);
                    continue;
                }

                if (!SameText(pair.Value, destination)) differing.Add(pair.Key);
            }

            foreach (KeyValuePair<string, string> pair in packed)
            {
                if (!sources.ContainsKey(pair.Key)) stale.Add(pair.Key);
            }

            missing.Sort(StringComparer.OrdinalIgnoreCase);
            differing.Sort(StringComparer.OrdinalIgnoreCase);
            stale.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("内置关卡自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            builder.AppendLine();
            builder.AppendLine("源（LevelJson/ + incoming/）：" + sources.Count + " 个");
            builder.AppendLine("包（" + LevelSeedBundler.TargetFolder + "）：" + packed.Count + " 个");
            builder.AppendLine();

            AppendList(builder, "❌ 源里有、包里没有（照现在这样出包不会带上它）：", missing, "无");
            AppendList(builder, "⚠ 内容不同（改了源但没跑「生成内置关卡包」）：", differing, "无");
            AppendList(builder, "⚠ 包里有、源里没有（下次打包会被自动删掉）：", stale, "无");

            builder.AppendLine();
            builder.AppendLine("已出包产物（Builds/ 下的 *_Data/resources.assets）：");
            List<string> assets = FindBuiltAssets();
            if (assets.Count == 0)
            {
                builder.AppendLine("  （没找到。包出到工程外时这一栏会空着，看上面的差集一样能判断。）");
            }
            else
            {
                DateTime newest = NewestStamp(packed);
                foreach (string asset in assets) AppendBuiltAsset(builder, asset, sources, newest);
            }

            builder.AppendLine();
            builder.AppendLine(Conclude(missing, differing));
            builder.AppendLine();
            builder.AppendLine("提示：有手牌的关「预设布局未通关」是预期结果（玩家要用牌解），不是错误。");

            string text = builder.ToString();
            TrySave(text);
            return text;
        }

        static void AppendList(StringBuilder builder, string title, List<string> items, string empty)
        {
            builder.AppendLine(title);
            if (items.Count == 0)
            {
                builder.AppendLine("  " + empty);
                return;
            }

            foreach (string item in items) builder.AppendLine("  " + item);
        }

        /// <summary>把包里的每一关拿去 <c>resources.assets</c> 里找名字 —— 在就是真的在。</summary>
        /// <param name="newestPacked">
        /// 包内关卡文件里最新的修改时间。产物比它旧就说明这份 assets 是<b>旧包</b>，
        /// 里面的 ✗ 是历史状态，不是「现在出包也会缺」—— 不说这句，第一眼就会误判。
        /// </param>
        static void AppendBuiltAsset(StringBuilder builder, string assetPath, Dictionary<string, string> sources,
            DateTime newestPacked)
        {
            FileInfo info = new FileInfo(assetPath);
            builder.AppendLine("  " + Relative(assetPath) + "（" + info.LastWriteTime.ToString("MM-dd HH:mm") + "）");

            if (newestPacked != DateTime.MinValue && info.LastWriteTime < newestPacked)
            {
                builder.AppendLine("    ⚠ 这份产物比包内关卡旧（关卡最新 " + newestPacked.ToString("MM-dd HH:mm")
                    + "）—— 下面的 ✗ 只说明它是旧包，不代表现在出包会缺。");
            }

            string blob;
            try
            {
                // resources.assets 里的 TextAsset 名是明文字符串，按字节单映射读出来就能 Contains
                blob = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(assetPath));
            }
            catch (Exception exception)
            {
                builder.AppendLine("    读不了这份 assets：" + exception.Message);
                return;
            }

            List<string> names = new List<string>(sources.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                string level = Path.GetFileNameWithoutExtension(name);
                bool found = blob.IndexOf(level, StringComparison.Ordinal) >= 0;
                builder.AppendLine("    " + level + (found ? " 在包里 ✓" : " 不在包里 ✗"));
            }
        }

        /// <summary>包内关卡文件里最新的修改时间（判断已出包产物是新是旧）。</summary>
        static DateTime NewestStamp(Dictionary<string, string> packed)
        {
            DateTime newest = DateTime.MinValue;
            foreach (string file in packed.Values)
            {
                DateTime stamp = File.GetLastWriteTime(file);
                if (stamp > newest) newest = stamp;
            }

            return newest;
        }

        static List<string> FindBuiltAssets()
        {
            List<string> found = new List<string>();

            string builds = Path.GetFullPath("Builds");
            if (!Directory.Exists(builds)) return found;

            foreach (string dataDir in Directory.GetDirectories(builds, "*_Data", SearchOption.AllDirectories))
            {
                string file = Path.Combine(dataDir, "resources.assets");
                if (File.Exists(file)) found.Add(file);
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        static bool SameText(string left, string right)
        {
            try
            {
                return string.Equals(File.ReadAllText(left), File.ReadAllText(right), StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 自检读不了 " + left + " / " + right + "：" + exception.Message);
                return false;
            }
        }

        static string Conclude(List<string> missing, List<string> differing)
        {
            if (missing.Count == 0 && differing.Count == 0)
            {
                return "结论：源和包一致，可以直接出包。";
            }

            return "结论：先跑 SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包，再出包。"
                + "（出包前钩子也会自动刷一遍，但那一步只看 LevelJson/，收不到别处的 JSON）";
        }

        static string Relative(string absolute)
        {
            string root = Path.GetFullPath(".");
            if (absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return absolute.Substring(root.Length).TrimStart('\\', '/');
            }

            return absolute;
        }

        static void TrySave(string text)
        {
            try
            {
                string folder = Path.GetFullPath(LevelJsonTool.ReportFolder);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, ReportName), text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 自检报告写不了：" + exception.Message);
            }
        }
    }
}
