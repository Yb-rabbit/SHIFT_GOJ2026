using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 「哪些工程文件算一关、从哪几个目录收」——同步到运行时目录（<see cref="LevelJsonRuntimeSetup"/>）
    /// 和内置关卡包（<see cref="LevelSeedBundler"/>）共用这一份规则，免得两边慢慢长出不一样的清单。
    /// </summary>
    internal static class LevelSeedSources
    {
        /// <summary>
        /// 来源：根目录 = 当前在做的关；incoming = 设计侧交上来的批次。
        /// <c>exported/</c> 不参与 —— 那是 Unity 侧导出的参照样例，重复进选关列表只会更乱。
        /// </summary>
        internal static readonly string[] Folders = { LevelJsonTool.RootFolder, LevelJsonTool.IncomingFolder };

        /// <summary>不是关卡的文件：模板、关卡顺序表、类型快照。</summary>
        internal static readonly string[] NotLevels = { "_template.json", "catalog.json", "typeCatalog.json" };

        /// <summary>文件名是不是「不该当关卡处理」的：下划线开头（模板 / 草稿）或保留名。</summary>
        internal static bool IsNotLevel(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            if (fileName.StartsWith("_", StringComparison.Ordinal)) return true;

            foreach (string skip in NotLevels)
            {
                if (string.Equals(fileName, skip, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 把作者关卡「预放进包」：<c>LevelJson/</c>（+ <c>incoming/</c>）→ <c>Assets/Resources/BitFlow/Levels/</c>。
    ///
    /// 为什么是这条线：运行时只有 <see cref="LevelJsonLibrary"/> 一个入口，它只认
    /// <c>persistentDataPath/LevelJson</c> 里的普通文件。预放入的关卡不另开通道，只是
    /// <b>先躺在包里，首次启动由 <see cref="ShiftGo.BitFlow.Data.BundledLevels"/> 用同一份写盘代码放进同一个目录</b> ——
    /// 于是作者关和玩家自己造的关在列表、编辑、删除、导出上完全一样，没有任何特殊分支。
    ///
    /// 目标目录是<b>生成物</b>：源里删掉的关卡会连文件带 <c>.meta</c> 一起清掉，不会偷偷留在包里。
    /// 生成结果要提交进版本库（别人 clone 出来直接就有作者关）。
    /// </summary>
    public static class LevelSeedBundler
    {
        /// <summary>包内落点（Resources 下，运行时 <c>Resources.LoadAll&lt;TextAsset&gt;</c> 直接取）。</summary>
        internal const string TargetFolder = "Assets/Resources/BitFlow/Levels";

        /// <summary>一次打包的结果，菜单和出包日志都说这些数。</summary>
        internal sealed class Report
        {
            /// <summary>这次真正进包的关卡数（读得通、验得过）。</summary>
            public int Levels;

            /// <summary>新写或内容有变化、被重新导入的个数。</summary>
            public int Updated;

            /// <summary>因为源里已经没有而从包里删掉的个数。</summary>
            public int Removed;

            /// <summary>想进包但没进去的个数（读不了 / 验不过）。原因都在 Console。</summary>
            public int Skipped;

            public override string ToString()
            {
                return Levels + " 关 → " + TargetFolder
                    + "（新写/更新 " + Updated + "，删除过期 " + Removed + "，跳过 " + Skipped + "）";
            }
        }

        // ------------------------------------------------------------------ 菜单

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包", false, 44)]
        static void BundleMenu()
        {
            Report report = Bundle();

            EditorUtility.DisplayDialog("内置关卡包",
                report.Levels > 0
                    ? "已打包 " + report.Levels + " 个关卡：\n" + TargetFolder
                        + "\n\n新写/更新：" + report.Updated
                        + "\n删除过期：" + report.Removed
                        + (report.Skipped > 0 ? "\n跳过（读不通，见 Console）：" + report.Skipped : "")
                        + "\n\n出包时会自动再跑一遍；平时改了 LevelJson/ 想立刻看到效果就点这个。"
                    : "包里没有可用的关卡，内置包已清空。\n\n来源："
                        + Path.GetFullPath(LevelJsonTool.RootFolder) + "\n"
                        + Path.GetFullPath(LevelJsonTool.IncomingFolder)
                        + "\n\n（exported/ 是导出参照，不参与打包；下划线开头的是模板 / 草稿）",
                "好");

            EditorUtility.RevealInFinder(TargetFolder);
        }

        // ------------------------------------------------------------------ 打包

        /// <summary>
        /// 把工程里的作者关卡复制进 Resources，返回这次的结果统计。
        /// 复制是<b>字节原样</b>的（不重新序列化），复制前用运行时那套 <c>Parse</c> 验一遍：
        /// 打进包里的东西必须是「点得进去」的关，坏 JSON 只在这里报一次，不带到玩家那边。
        /// </summary>
        internal static Report Bundle()
        {
            Report report = new Report();

            Dictionary<string, string> sources = CollectSources();
            EnsureAssetFolder(TargetFolder);

            // 没有注册表就验不了货：照搬并在 Console 说清楚，总比打出一个空包强
            bool canValidate = LevelTypeRegistrySO.Load() != null;
            if (!canValidate && sources.Count > 0)
            {
                Debug.LogWarning("[BitFlow] 内置关卡包：还没有类型注册表，关卡照搬但没法验证。"
                    + "先跑菜单 SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表。");
            }

            string targetAbsolute = Path.GetFullPath(TargetFolder);

            // 这一版包里「确实要存在」的文件名：最后拿它清理过期文件
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> pair in sources)
            {
                string text;
                try
                {
                    text = File.ReadAllText(pair.Value);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[BitFlow] 内置关卡包：读不了 " + pair.Value + "，" + exception.Message);
                    report.Skipped++;
                    continue;
                }

                if (canValidate && !IsReadable(text, out string reason))
                {
                    Debug.LogWarning("[BitFlow] 内置关卡包：跳过读不通的关卡 " + pair.Key + " → " + reason);
                    report.Skipped++;
                    continue;
                }

                string destination = Path.Combine(targetAbsolute, pair.Key);
                if (!IsSameContent(destination, text))
                {
                    try
                    {
                        File.WriteAllText(destination, text);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[BitFlow] 内置关卡包：写不进 " + destination + "，" + exception.Message);
                        report.Skipped++;
                        continue;
                    }

                    // 同步导入：紧跟着出包时必须已经是最新内容，不能等下一次资源刷新
                    AssetDatabase.ImportAsset(TargetFolder + "/" + pair.Key,
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    report.Updated++;
                }

                keep.Add(pair.Key);
                report.Levels++;
            }

            report.Removed = RemoveStale(targetAbsolute, keep);
            Debug.Log("[BitFlow] 内置关卡包：" + report);
            return report;
        }

        /// <summary>
        /// 收集「文件名 → 绝对路径」。同名以<b>先出现的目录</b>为准（根目录优先于 incoming），
        /// 和 <see cref="LevelJsonRuntimeSetup.SyncRuntimeLevels"/> 的语义一致。
        /// </summary>
        static Dictionary<string, string> CollectSources()
        {
            Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in LevelSeedSources.Folders)
            {
                string absolute = Path.GetFullPath(folder);
                if (!Directory.Exists(absolute)) continue;

                string[] files = Directory.GetFiles(absolute, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    if (LevelSeedSources.IsNotLevel(name)) continue;

                    if (sources.ContainsKey(name))
                    {
                        Debug.LogWarning("[BitFlow] 内置关卡包：跳过重名的 " + file
                            + "（已采用 " + sources[name] + "）");
                        continue;
                    }

                    sources[name] = file;
                }
            }

            return sources;
        }

        /// <summary>内容和目标一致时<b>不碰文件</b>：避免每次出包都把整目录的 .meta 时间戳刷一遍。</summary>
        static bool IsSameContent(string destination, string text)
        {
            try
            {
                return File.Exists(destination)
                    && string.Equals(File.ReadAllText(destination), text, StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 内置关卡包：读不了旧的 " + destination + "，" + exception.Message);
                return false;
            }
        }

        /// <summary>目标目录里这一版不需要的 <c>.json</c> 全删掉（<c>DeleteAsset</c> 连 <c>.meta</c> 一起带走）。</summary>
        static int RemoveStale(string targetAbsolute, HashSet<string> keep)
        {
            if (!Directory.Exists(targetAbsolute)) return 0;

            string[] files = Directory.GetFiles(targetAbsolute, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int removed = 0;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (keep.Contains(name)) continue;

                if (AssetDatabase.DeleteAsset(TargetFolder + "/" + name))
                {
                    removed++;
                }
                else
                {
                    Debug.LogWarning("[BitFlow] 内置关卡包：删不掉过期文件 " + file);
                }
            }

            return removed;
        }

        /// <summary>用运行时那套解析验一遍（和进关、导入完全同一个判定）。</summary>
        static bool IsReadable(string json, out string reason)
        {
            reason = null;

            if (!LevelJsonLibrary.TryParseJson(json, out LevelSO probe, out List<string> problems))
            {
                reason = string.Join("；", problems);
                return false;
            }

            // 只是验货，读出来的临时资产当场回收
            LevelJsonLibrary.DestroyLevel(probe);
            return true;
        }

        static void EnsureAssetFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;

            string absolute = Path.GetFullPath(assetFolder);
            if (!Directory.Exists(absolute)) Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }
    }

    /// <summary>
    /// 出包前自动刷一次内置关卡包：改了 <c>LevelJson/</c> 忘了跑菜单，也不会打出一个旧包。
    /// 写资源 + 同步导入都在 <see cref="LevelSeedBundler.Bundle"/> 里做完，出包读到的就是新内容。
    /// </summary>
    sealed class LevelSeedBuildHook : IPreprocessBuildWithReport
    {
        /// <summary>先于其它预处理跑：别的钩子若要读这几份 JSON，看到的就是这一版。</summary>
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            LevelSeedBundler.Report result = LevelSeedBundler.Bundle();
            Debug.Log("[BitFlow] 出包前已刷新内置关卡包：" + result
                + "（目标平台 " + report.summary.platform + "）");
        }
    }
}
