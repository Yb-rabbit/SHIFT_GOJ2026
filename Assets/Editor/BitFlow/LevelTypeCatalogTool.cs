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

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/导出类型目录（给外部编辑器）", false, 44)]
        static void ExportCatalogMenu()
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

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/打开外部关卡编辑器", false, 45)]
        static void LaunchLevelStudio()
        {
            string bat = Path.Combine(ProjectRoot, "Tools", "LevelStudio", "启动关卡编辑器.bat");
            if (!File.Exists(bat))
            {
                EditorUtility.DisplayDialog("外部关卡编辑器",
                    "没找到：\n" + bat + "\n\n（Tools/LevelStudio 这个目录被删或没同步过来？）", "好");
                return;
            }

            // 先把类型目录刷一遍，免得编辑器里打开导出关时参数对不上
            ExportCatalog(out string catalog);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bat)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(bat),
            });

            Debug.Log("[BitFlow] 已启动外部关卡编辑器（类型目录：" + catalog + "）");
        }
    }
}
