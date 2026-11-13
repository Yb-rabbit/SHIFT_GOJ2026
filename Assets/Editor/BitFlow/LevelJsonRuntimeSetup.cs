using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 把「工程里的关卡 JSON」搬到「运行时可读的位置」，并维护运行时唯一的类型清单。
    ///
    /// 为什么必须搬一次：运行时加载走 <see cref="LevelJsonLibrary"/>，它只认
    /// <c>Application.persistentDataPath/LevelJson</c>。工程根目录的 <c>LevelJson/</c> 是协作中转站
    /// （见 <see cref="LevelJsonTool"/>），打包后根本不存在；如果两边各读一处，
    /// 就会出现「编辑器里能跑的关、打包后跑不了」。所以这里只做<b>单向复制</b>，
    /// 运行时那段代码在编辑器 Play / EditMode 测试 / 打包 Player 里完全一致。
    ///
    /// 复制是「不删」的：运行时目录里玩家/AI 自己扔进去的关卡会保留，这里只覆盖同名文件。
    /// </summary>
    public static class LevelJsonRuntimeSetup
    {
        /// <summary>注册表落点（Resources 下，运行时 <c>Resources.Load</c> 直接取）。</summary>
        const string RegistryFolder = "Assets/Resources/BitFlow";
        const string RegistryAssetPath = RegistryFolder + "/LevelTypeRegistry.asset";

        /// <summary>
        /// 同步来源：根目录 = 当前在做的关；incoming = 设计侧交上来的批次。
        /// <c>exported/</c> 不同步 —— 那是 Unity 侧导出的参照样例，重复进选关列表只会更乱。
        /// 「哪些文件算关卡」这套规则和内置关卡包（<see cref="LevelSeedBundler"/>）共用一份。
        /// </summary>
        static readonly string[] SourceFolders = LevelSeedSources.Folders;

        /// <summary>不是关卡的文件：模板、关卡顺序表、类型快照，同步过去只会让面板报错。</summary>
        static readonly string[] NotLevels = LevelSeedSources.NotLevels;

        // ------------------------------------------------------------------ 菜单

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表", false, 40)]
        static void RefreshRegistryMenu()
        {
            int count = RefreshRegistry();

            EditorUtility.DisplayDialog("类型注册表",
                count > 0
                    ? "已写入 " + count + " 个元件类型：\n" + RegistryAssetPath
                        + "\n\n新增或改名类型资产后要再刷一次，否则 JSON 里的 type 会断链。"
                    : "工程里没有可注册的元件类型资产，注册表没动。",
                "好");
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/同步到运行时目录", false, 41)]
        static void SyncRuntimeMenu()
        {
            int copied = SyncRuntimeLevels();

            EditorUtility.DisplayDialog("同步关卡 JSON",
                copied > 0
                    ? "已同步 " + copied + " 个关卡到：\n" + LevelJsonLibrary.Folder
                    : "没有新的关卡 JSON 可同步。\n\n" + Path.GetFullPath(LevelJsonTool.RootFolder)
                        + "\n" + Path.GetFullPath(LevelJsonTool.IncomingFolder)
                        + "\n\n（exported/ 是导出参照，不参与同步）",
                "好");

            EditorUtility.RevealInFinder(LevelJsonLibrary.Folder);
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/打开运行时关卡目录", false, 42)]
        static void OpenRuntimeFolder()
        {
            LevelJsonLibrary.EnsureFolder();
            EditorUtility.RevealInFinder(LevelJsonLibrary.Folder);
        }

        /// <summary>
        /// 一条命令补齐「JSON 关卡能跑」的全部前置：注册表有清单、运行时目录有文件。
        /// 新拉一份工程后跑这个就够了。
        /// </summary>
        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/一键：刷新注册表并同步", false, 43)]
        static void SetupEverything()
        {
            int types = RefreshRegistry();
            int copied = SyncRuntimeLevels();

            EditorUtility.DisplayDialog("关卡 JSON 运行时准备",
                "类型注册表：" + (types > 0 ? types + " 个类型 → " + RegistryAssetPath : "没写入（工程里没有类型资产）")
                + "\n\n已同步关卡：" + copied + " 个 → " + LevelJsonLibrary.Folder
                + "\n\n失败的文件会在 Console 里列出原因。",
                "好");

            EditorUtility.RevealInFinder(LevelJsonLibrary.Folder);
        }

        // ------------------------------------------------------------------ 目录迁移 / 总览

        /// <summary>
        /// 改过 ProjectSettings 的 companyName 之后，旧目录会留在原地：
        /// persistentDataPath 是 <c>LocalLow/&lt;company&gt;/&lt;product&gt;</c>，公司名一换整棵树就换位置。
        /// 这个菜单把旧树里的 <c>*.json</c>（关卡 / 存档 / 草稿）复制过来，<b>只复制、不删除</b>，
        /// 同名一律跳过 —— 宁可少搬一个，也不覆盖新目录里已有的东西。
        /// </summary>
        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/迁移旧关卡目录（DefaultCompany → 当前公司名）", false, 44)]
        static void MigrateOldCompanyFolder()
        {
            const string oldCompany = "DefaultCompany";

            string current = Application.persistentDataPath;      // .../LocalLow/<company>/<product>
            DirectoryInfo product = new DirectoryInfo(current);
            DirectoryInfo company = product.Parent;
            if (company == null || company.Parent == null)
            {
                EditorUtility.DisplayDialog("迁移旧关卡目录", "定位不到 LocalLow 目录：\n" + current, "好");
                return;
            }

            string oldRoot = Path.Combine(company.Parent.FullName, oldCompany, product.Name);

            if (string.Equals(oldRoot, current, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("迁移旧关卡目录",
                    "当前公司名就是 " + oldCompany + "，两个路径是同一个：\n" + current, "好");
                return;
            }

            if (!Directory.Exists(oldRoot))
            {
                EditorUtility.DisplayDialog("迁移旧关卡目录", "没有旧目录可迁移（本来就没生成过）：\n" + oldRoot, "好");
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(oldRoot, "*.json", SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("迁移旧关卡目录", "读不了旧目录：\n" + exception.Message, "好");
                return;
            }

            List<string> copied = new List<string>();
            List<string> skipped = new List<string>();
            List<string> failed = new List<string>();

            foreach (string source in files)
            {
                string relative = source.Substring(oldRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                string destination = Path.Combine(current, relative);

                try
                {
                    if (File.Exists(destination))
                    {
                        skipped.Add(relative);
                        continue;
                    }

                    string folder = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    File.Copy(source, destination);
                    copied.Add(relative);
                }
                catch (Exception exception)
                {
                    failed.Add(relative + "：" + exception.Message);
                }
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("旧目录（保留不动）：" + oldRoot);
            report.AppendLine("新目录：" + current);
            report.AppendLine("复制 " + copied.Count + " 个：");
            foreach (string name in copied) report.AppendLine("  " + name);
            if (skipped.Count > 0) report.AppendLine("跳过 " + skipped.Count + " 个（新目录已有同名）");
            if (failed.Count > 0)
            {
                report.AppendLine("失败 " + failed.Count + " 个：");
                foreach (string name in failed) report.AppendLine("  " + name);
            }

            Debug.Log("[BitFlow] 关卡目录迁移\n" + report);
            EditorUtility.DisplayDialog("迁移旧关卡目录", report.ToString(), "好");
            EditorUtility.RevealInFinder(current);
        }

        /// <summary>
        /// 一次打印所有落点。路径分散在 AppData（运行时库）、程序旁边（分享）、文档（兜底）三处，
        /// 光读代码或文档很容易记错 —— 这个菜单是唯一的事实来源。
        /// </summary>
        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/显示关卡目录总览", false, 45)]
        static void ShowFolderOverview()
        {
            StringBuilder report = new StringBuilder();

            report.AppendLine("[运行时库]     " + LevelJsonLibrary.Folder);
            report.AppendLine("  可写状态     " + WriteState(LevelJsonLibrary.Folder));
            report.AppendLine("[旧收件箱]     " + LevelJsonLibrary.IncomingFolder);

            bool shareOk = LevelJsonLibrary.EnsureShareFolder(out string shareError);
            report.AppendLine("[分享目录]     " + LevelJsonLibrary.ShareFolder);
            report.AppendLine("  可写状态     " + (shareOk ? WriteState(LevelJsonLibrary.ShareFolder) : "✗ " + shareError));

            List<string> drops = LevelJsonLibrary.DropFolders();
            report.AppendLine("[批量导入扫描] " + drops.Count + " 个目录");
            foreach (string folder in drops) report.AppendLine("               " + folder);

            report.AppendLine("[草稿存档]     " + LevelDraftSerializer.DefaultPath);
            report.AppendLine("  可写状态     " + WriteState(Path.GetDirectoryName(LevelDraftSerializer.DefaultPath)));
            report.AppendLine("[通关存档]     " + SaveStore.FilePath);
            report.AppendLine("  可写状态     " + WriteState(Path.GetDirectoryName(SaveStore.FilePath)));

            Debug.Log("[BitFlow] 关卡目录总览\n" + report);
            EditorUtility.DisplayDialog("关卡目录总览", report.ToString(), "好");
        }

        /// <summary>目录能不能写：真写一个探针文件再删掉，比看权限位靠谱（UAC 虚拟化会骗人）。</summary>
        static string WriteState(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "—";
            if (!Directory.Exists(folder)) return "✗ 不存在";

            try
            {
                string probe = Path.Combine(folder, ".write_probe");
                File.WriteAllText(probe, "SHIFT GO");
                File.Delete(probe);
                return "✓ 可写";
            }
            catch (Exception)
            {
                return "✗ 只读";
            }
        }

        // ------------------------------------------------------------------ 注册表

        /// <summary>
        /// 按工程里现有的全部 <see cref="ElementTypeSO"/> 重写注册表，返回写入的类型数量（0 = 没资产）。
        /// 清单顺序按资产名排，保证同一份工程刷出来的注册表完全一致（kind 模式的匹配结果也就稳定）。
        /// </summary>
        internal static int RefreshRegistry()
        {
            List<ElementTypeSO> pool = LevelImportTool.LoadTypePool();
            if (pool.Count == 0)
            {
                Debug.LogWarning("[BitFlow] 类型注册表没刷新：工程里一个 ElementTypeSO 都没有。");
                return 0;
            }

            pool.Sort((left, right) => string.CompareOrdinal(left.name, right.name));

            LevelTypeRegistrySO registry = AssetDatabase.LoadAssetAtPath<LevelTypeRegistrySO>(RegistryAssetPath);
            bool created = registry == null;
            if (created)
            {
                EnsureFolder(RegistryFolder);
                registry = ScriptableObject.CreateInstance<LevelTypeRegistrySO>();
                AssetDatabase.CreateAsset(registry, RegistryAssetPath);
            }

            // types 是私有 [SerializeField]：用 SerializedObject 写，脏标记与保存才走得通
            SerializedObject serialized = new SerializedObject(registry);
            SerializedProperty types = serialized.FindProperty("types");
            types.arraySize = pool.Count;
            for (int i = 0; i < pool.Count; i++)
            {
                types.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            // 运行时那份缓存（Resources.Load 的结果）必须清掉，否则本次会话还在用旧清单
            LevelTypeRegistrySO.ClearCache();

            Debug.Log("[BitFlow] 类型注册表已刷新（" + (created ? "新建" : "覆盖") + "）："
                + RegistryAssetPath + "，共 " + pool.Count + " 个类型");

            return pool.Count;
        }

        // ------------------------------------------------------------------ 同步

        /// <summary>
        /// 把工程的关卡 JSON 复制进 <see cref="LevelJsonLibrary.Folder"/>，返回复制的文件数。
        /// 同名文件以「先到先得」为准：根目录里的正式关不会被 incoming 里的同批次文件盖掉。
        /// </summary>
        internal static int SyncRuntimeLevels()
        {
            string target = LevelJsonLibrary.Folder;
            LevelJsonLibrary.EnsureFolder();

            Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in SourceFolders)
            {
                string absolute = Path.GetFullPath(folder);
                if (!Directory.Exists(absolute)) continue;

                foreach (string file in Directory.GetFiles(absolute, "*.json"))
                {
                    string name = Path.GetFileName(file);
                    if (IsNotLevel(name)) continue;

                    if (sources.ContainsKey(name))
                    {
                        Debug.LogWarning("[BitFlow] 同步关卡 JSON：跳过重名的 " + file
                            + "（已采用 " + sources[name] + "）");
                        continue;
                    }

                    sources[name] = file;
                }
            }

            // 没有注册表就读不出关卡，验证只会刷一屏「没有类型注册表」，干脆只搬不验
            bool canValidate = LevelTypeRegistrySO.Load() != null;
            if (!canValidate)
            {
                Debug.LogWarning("[BitFlow] 同步关卡 JSON：还没有类型注册表，文件照搬但没法验证。"
                    + "先跑菜单 SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表。");
            }

            int copied = 0;
            foreach (KeyValuePair<string, string> pair in sources)
            {
                string destination = Path.Combine(target, pair.Key);
                try
                {
                    File.Copy(pair.Value, destination, true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[BitFlow] 同步关卡 JSON 失败：" + pair.Value + " → "
                        + destination + "，" + exception.Message);
                    continue;
                }

                copied++;
                if (canValidate) WarnIfUnreadable(destination);
            }

            Debug.Log("[BitFlow] 已同步 " + copied + " 个关卡 JSON 到 " + target);
            return copied;
        }

        /// <summary>
        /// 复制完立刻用运行时那套代码读一遍：能读通才算「面板上点得进去」。
        /// 读出来的临时关卡资产当场销毁，不留内存垃圾。
        /// </summary>
        static void WarnIfUnreadable(string path)
        {
            if (LevelJsonLibrary.TryLoad(path, out LevelSO level, out List<string> problems))
            {
                LevelJsonLibrary.DestroyLevel(level);
                return;
            }

            Debug.LogWarning("[BitFlow] 同步过去的关卡读不通：" + Path.GetFileName(path)
                + " → " + string.Join("；", problems));
        }

        static bool IsNotLevel(string fileName)
        {
            foreach (string skip in NotLevels)
            {
                if (string.Equals(fileName, skip, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string absolute = Path.GetFullPath(folder);
            if (!Directory.Exists(absolute)) Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }
    }
}
