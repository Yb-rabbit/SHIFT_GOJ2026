using System;
using System.Collections.Generic;
using System.IO;
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
