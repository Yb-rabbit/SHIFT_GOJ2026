using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 关卡 JSON 双端通道：把 <see cref="LevelSO"/> 导成人能读、能手改的 JSON，
    /// 也能把手写的 JSON 校验后落成/覆盖关卡资产。
    ///
    /// 为什么放在工程根目录的 <c>LevelJson/</c> 而不是 Assets 里：这些文件是**协作中转站**
    /// （设计侧给 JSON、Unity 侧出关卡资产与诊断），不是运行时资源，进 Assets 只会让
    /// Unity 多导一份 TextAsset、还容易被误当成资源引用。
    ///
    /// 目录约定：
    /// - <c>LevelJson/exported/</c>  Unity 侧导出的样例，给设计侧当参考
    /// - <c>LevelJson/incoming/</c>  设计侧交上来的 JSON，等 Unity 侧批量导入
    /// - <c>LevelJson/reports/</c>   每次校验 / 导入落盘的诊断文本（设计侧看不到 Console）
    /// </summary>
    public static class LevelJsonTool
    {
        const string CatalogPath = "Assets/Data/Levels/LevelCatalog.asset";

        internal const string RootFolder = "LevelJson";
        internal const string ExportedFolder = RootFolder + "/exported";
        internal const string IncomingFolder = RootFolder + "/incoming";
        internal const string ReportFolder = RootFolder + "/reports";
        internal const string TemplateFile = RootFolder + "/_template.json";

        // ------------------------------------------------------------------ 导出

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/导出选中关卡", false, 25)]
        static void ExportSelected()
        {
            LevelSO level = Selection.activeObject as LevelSO;
            if (level == null)
            {
                EditorUtility.DisplayDialog("导出关卡 JSON",
                    "先在 Project 面板里选中一个关卡资产（Assets/Data/Levels/Level_xx.asset），再点这个菜单。", "好");
                return;
            }

            string path = ExportLevel(level);
            Debug.Log("[BitFlow] 已导出关卡 JSON：" + path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/导出全部关卡", false, 26)]
        static void ExportAll()
        {
            List<LevelSO> levels = CollectAllLevels();
            if (levels.Count == 0)
            {
                EditorUtility.DisplayDialog("导出关卡 JSON", "工程里一个 LevelSO 都没有。", "好");
                return;
            }

            EnsureFolder(ExportedFolder);
            StringBuilder builder = new StringBuilder();
            builder.Append("{\n  \"levels\": [\n");

            for (int i = 0; i < levels.Count; i++)
            {
                ExportLevel(levels[i]);
                builder.Append("    ").Append(Quote(levels[i].name));
                if (i < levels.Count - 1) builder.Append(',');
                builder.Append('\n');
            }
            builder.Append("  ]\n}\n");

            // 顺带记一份关卡顺序：设计侧看这张表就知道哪一关先上，不用问 Unity 侧
            File.WriteAllText(Path.GetFullPath(ExportedFolder + "/catalog.json"), builder.ToString());

            Debug.Log("[BitFlow] 已导出 " + levels.Count + " 个关卡 JSON 到 " + Path.GetFullPath(ExportedFolder));
            EditorUtility.RevealInFinder(Path.GetFullPath(ExportedFolder));
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/导出空白模板", false, 27)]
        static void ExportTemplate()
        {
            EnsureFolder(RootFolder);

            StringBuilder builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"_readme\": [\n");
            builder.Append("    \"关卡 JSON 模板：完整字段说明见 docs/LEVEL_JSON.md\",\n");
            builder.Append("    \"wallRows 一行对应一个 y（第一行是 y=0，从下往上画），# = 墙，. = 空\",\n");
            builder.Append("    \"元件两种写法：kind + dir 直接手写；type 精确引用类型资产（= 资产名，导出时自动带上）\",\n");
            builder.Append("    \"sequence / count / face 出现即表示这一格覆盖类型资产；count 是发射总位数 / 接收总个数\",\n");
            builder.Append("    \"_readme 与 _example 这两段会被导入器忽略，随便删\"\n");
            builder.Append("  ],\n");
            builder.Append("  \"_example\": [\n");
            builder.Append("    { \"kind\": \"Emitter\", \"x\": 0, \"y\": 3, \"dir\": \"Right\", \"sequence\": \"1011\", \"count\": 6 },\n");
            builder.Append("    { \"kind\": \"Receiver\", \"x\": 8, \"y\": 3, \"dir\": \"Left\", \"sequence\": \"101110\", \"count\": 6 },\n");
            builder.Append("    { \"kind\": \"Splitter\", \"x\": 4, \"y\": 3, \"dir\": \"Up\", \"dir2\": \"Down\" }\n");
            builder.Append("  ],\n");
            builder.Append("  \"schema\": ").Append(LevelJsonSerializer.SchemaVersion).Append(",\n");
            builder.Append("  \"displayName\": \"NEW LEVEL\",\n");
            builder.Append("  \"width\": 9,\n");
            builder.Append("  \"height\": 8,\n");
            builder.Append("  \"tickInterval\": 0.5,\n");
            builder.Append("  \"wallRows\": [\n");
            builder.Append("    \".........\",\n    \".........\",\n    \".........\",\n    \".........\",\n");
            builder.Append("    \".........\",\n    \".........\",\n    \".........\",\n    \".........\"\n");
            builder.Append("  ],\n");
            builder.Append("  \"elements\": [],\n");
            builder.Append("  \"supplies\": []\n");
            builder.Append("}\n");

            string path = Path.GetFullPath(TemplateFile);
            File.WriteAllText(path, builder.ToString());
            Debug.Log("[BitFlow] 已生成空白模板：" + path);
            EditorUtility.RevealInFinder(path);
        }

        // ------------------------------------------------------------------ 校验 / 导入

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/校验 JSON（不落盘）", false, 28)]
        static void ValidateJson()
        {
            string path = PickJson("选择要校验的关卡 JSON");
            if (string.IsNullOrEmpty(path)) return;

            JsonRun run = Run(path, true);
            WriteReport(run);
            EditorUtility.DisplayDialog("校验完成", run.ToText(), "好");
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/导入为新关卡", false, 29)]
        static void ImportAsNewLevel()
        {
            string path = PickJson("选择要导入的关卡 JSON");
            if (string.IsNullOrEmpty(path)) return;

            // 先干跑一遍：错误多的时候提醒一声，也避免用户取消前就把变体资产落进工程
            JsonRun probe = Run(path, true);
            if (!probe.Result.HasLevel)
            {
                WriteReport(probe);
                EditorUtility.DisplayDialog("导入失败", probe.ToText(), "好");
                return;
            }

            if (!probe.Result.Ok && !EditorUtility.DisplayDialog("有错误，仍要导入吗",
                    probe.ToText() + "\n\n确定要跳过这些格子继续导入吗？", "仍要导入", "先不改"))
            {
                return;
            }

            JsonRun run = Run(path, false);
            string assetPath = CreateLevelAsset(run.Result.Level);
            LevelSO created = AssetDatabase.LoadAssetAtPath<LevelSO>(assetPath);
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);

            string summary = "已生成 " + assetPath + "\n" + run.ToText();
            WriteReport(run);
            Debug.Log("[BitFlow] 关卡 JSON 导入完成：" + summary);
            EditorUtility.DisplayDialog("导入完成", summary, "好");
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/覆盖选中关卡", false, 30)]
        static void OverwriteSelected()
        {
            LevelSO target = Selection.activeObject as LevelSO;
            if (target == null)
            {
                EditorUtility.DisplayDialog("覆盖选中关卡",
                    "先在 Project 面板里选中要被覆盖的关卡资产（Assets/Data/Levels/Level_xx.asset）。", "好");
                return;
            }

            string path = PickJson("选择用来覆盖「" + target.name + "」的关卡 JSON");
            if (string.IsNullOrEmpty(path)) return;

            JsonRun probe = Run(path, true);
            if (!probe.Result.HasLevel)
            {
                WriteReport(probe);
                EditorUtility.DisplayDialog("覆盖失败", probe.ToText(), "好");
                return;
            }

            string message = "即将用 JSON 覆盖 " + target.name + "：\n"
                + "  现在：" + DescribeLevel(target) + "\n"
                + "  JSON：" + DescribeLevel(probe.Result.Level) + "\n\n"
                + (probe.Result.Ok ? "校验通过。" : "⚠ 有 " + probe.Result.Errors.Count + " 个错误格会被跳过。")
                + "\n覆盖后可以用 Ctrl+Z 撤回。";

            if (!EditorUtility.DisplayDialog("确认覆盖", message, "覆盖", "取消")) return;

            JsonRun run = Run(path, false);
            LevelSO source = run.Result.Level;

            Undo.RecordObject(target, "导入关卡 JSON");
            CopyInto(target, source);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            // 覆盖不新建资产：run 的试跑结论就是对覆盖后目标关的结论
            string summary = "已覆盖 " + target.name + "\n" + run.ToText();
            WriteReport(run);
            Debug.Log("[BitFlow] 关卡 JSON 覆盖完成：" + summary);
            EditorUtility.DisplayDialog("覆盖完成", summary, "好");
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/批量导入 incoming 目录", false, 31)]
        static void ImportIncomingFolder()
        {
            string folder = Path.GetFullPath(IncomingFolder);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                EditorUtility.DisplayDialog("批量导入",
                    "目录已经建好了，把设计侧交上来的 JSON 放进去再点一次：\n" + folder, "好");
                EditorUtility.RevealInFinder(folder);
                return;
            }

            string[] files = Directory.GetFiles(folder, "*.json");
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("批量导入", "目录里没有 .json 文件：\n" + folder, "好");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("批量导入 " + folder);
            int imported = 0;
            int failed = 0;

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("_", StringComparison.Ordinal)) continue; // _template.json 之类

                JsonRun run = Run(file, false);
                WriteReport(run);

                if (!run.Result.HasLevel)
                {
                    failed++;
                    builder.AppendLine();
                    builder.AppendLine("✗ " + name + "  读不出来");
                    builder.AppendLine(Indent(run.ToText()));
                    continue;
                }

                string assetPath = CreateLevelAsset(run.Result.Level);
                imported++;
                builder.AppendLine();
                builder.AppendLine("✓ " + name + " → " + assetPath + "（" + DescribeLevel(run.Result.Level) + "）");
                if (!run.Result.Ok) builder.AppendLine("  ⚠ " + run.Result.Errors.Count + " 个错误格被跳过");
                builder.AppendLine("  " + run.CheckText.Replace("\n", "\n  "));
            }

            builder.AppendLine();
            builder.AppendLine("合计：导入 " + imported + " 个，失败 " + failed + " 个");

            SaveText(ReportFolder + "/_batch.txt", builder.ToString());
            Debug.Log("[BitFlow] 批量导入关卡 JSON\n" + builder);
            EditorUtility.DisplayDialog("批量导入完成", builder.ToString(), "好");
        }

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/检查 type 断链", false, 32)]
        static void CheckBrokenTypes()
        {
            List<ElementTypeSO> pool = LevelImportTool.LoadTypePool();
            StringBuilder builder = new StringBuilder();
            int broken = 0;
            int scanned = 0;

            foreach (string file in CollectJsonFiles())
            {
                scanned++;
                List<string> errors = new List<string>();
                LevelJsonSerializer.LevelJsonFile data =
                    LevelJsonSerializer.FromJson(File.ReadAllText(file), errors);
                if (data == null) continue;

                string name = Path.GetFileName(file);

                if (data.elements != null)
                {
                    for (int i = 0; i < data.elements.Count; i++)
                    {
                        LevelJsonSerializer.LevelJsonCell cell = data.elements[i];
                        if (cell == null || string.IsNullOrEmpty(cell.type)) continue;
                        if (LevelImportTool.FindByKey(pool, cell.type) != null) continue;

                        broken++;
                        builder.AppendLine(name + " elements[" + i + "] type=\"" + cell.type + "\" 找不到对应资产");
                    }
                }

                if (data.supplies != null)
                {
                    for (int i = 0; i < data.supplies.Count; i++)
                    {
                        LevelJsonSerializer.LevelJsonSupply supply = data.supplies[i];
                        if (supply == null || string.IsNullOrEmpty(supply.type)) continue;
                        if (LevelImportTool.FindByKey(pool, supply.type) != null) continue;

                        broken++;
                        builder.AppendLine(name + " supplies[" + i + "] type=\"" + supply.type + "\" 找不到对应资产");
                    }
                }
            }

            string message = broken == 0
                ? "扫了 " + scanned + " 个 JSON，type 引用都在。"
                : "扫了 " + scanned + " 个 JSON，发现 " + broken + " 条断链（改过资产名或删过资产就会这样）：\n\n"
                    + builder + "\n改回资产名，或者把 type 换成 kind 写法。";

            SaveText(ReportFolder + "/_broken_types.txt", message);
            Debug.Log("[BitFlow] " + message);
            EditorUtility.DisplayDialog("type 断链检查", message, "好");
        }

        // ------------------------------------------------------------------ JSON → 类型资产

        /// <summary>
        /// 把 JSON 的一格落到真实类型资产上：
        /// <c>type</c> 精确找资产；<c>kind</c> 走 <see cref="LevelImportTool.ResolveType"/> 那套
        /// 「先复用、没有才造变体」的逻辑（和导入 custom_level.json 共享同一份实现）。
        /// 干跑时造的变体只在内存里，退出时销毁。
        /// </summary>
        sealed class AssetResolver : ILevelJsonTypeResolver
        {
            readonly List<ElementTypeSO> pool = LevelImportTool.LoadTypePool();
            readonly Dictionary<string, ElementTypeSO> variants = new Dictionary<string, ElementTypeSO>();
            readonly List<ElementTypeSO> temporary = new List<ElementTypeSO>();
            readonly bool dryRun;

            /// <summary>这一份 JSON 会新建哪些变体 —— 直接写进报告，提醒作者「其实你可以复用现成资产」。</summary>
            public readonly List<string> WouldCreate = new List<string>();

            public AssetResolver(bool dryRun)
            {
                this.dryRun = dryRun;
            }

            public ElementTypeSO Resolve(LevelJsonSerializer.LevelJsonCell cell, out int rotationSteps)
            {
                rotationSteps = 0;
                if (cell == null) return null;

                // 1) 精确模式：type = 类型资产 Key。找不到就直接报错，不猜 —— 猜错比报错更难查
                if (!string.IsNullOrEmpty(cell.type))
                {
                    ElementTypeSO exact = LevelImportTool.FindByKey(pool, cell.type);
                    if (exact == null) return null;

                    rotationSteps = ((cell.rot % 4) + 4) % 4;
                    return exact;
                }

                // 2) 宽松模式：kind + 绝对朝向
                if (string.IsNullOrEmpty(cell.kind)) return null;
                if (!LevelJsonSerializer.TryParseKind(cell.kind, out CellKind kind)) return null;

                EditableElement draft = BuildDraft(kind, cell);
                LevelImportTool.Resolution resolution = LevelImportTool.ResolveType(draft, pool, variants, !dryRun);
                if (resolution.Type == null) return null;

                rotationSteps = resolution.Steps;
                if (resolution.Created)
                {
                    WouldCreate.Add(resolution.Type.name);
                    if (dryRun) temporary.Add(resolution.Type);
                }
                return resolution.Type;
            }

            public WallTypeSO ResolveWall()
            {
                foreach (ElementTypeSO candidate in pool)
                {
                    if (candidate is WallTypeSO wall) return wall;
                }
                return null;
            }

            static EditableElement BuildDraft(CellKind kind, LevelJsonSerializer.LevelJsonCell cell)
            {
                EditableElement draft = EditableElement.CreateDefault(kind, new GridPoint(cell.x, cell.y));

                if (LevelJsonSerializer.TryParseDir(cell.dir, out Dir dir)) draft.PrimaryDir = dir;
                if (LevelJsonSerializer.TryParseDir(cell.dir2, out Dir dir2)) draft.SecondaryDir = dir2;

                if (!string.IsNullOrEmpty(cell.sequence)) draft.Sequence = cell.sequence.Trim();
                if (cell.count >= 0) draft.Count = cell.count;
                if (cell.pass >= 0) draft.PassValue = cell.pass == 0 ? (byte)0 : (byte)1;

                // 宽松模式不认识资产名，从零开始找等价资产
                draft.TypeKey = string.Empty;
                return draft;
            }

            public void Dispose()
            {
                foreach (ElementTypeSO type in temporary)
                {
                    if (type != null) UnityEngine.Object.DestroyImmediate(type);
                }
                temporary.Clear();
            }
        }

        // ------------------------------------------------------------------ 跑一份 JSON

        /// <summary>一份 JSON 的处理结果（诊断 + 试跑结论）。</summary>
        sealed class JsonRun
        {
            public string Path;
            public string FileName;
            public LevelJsonReadResult Result;
            public string CheckText = string.Empty;
            public bool Success;

            /// <summary>解析器给的附加信息（会新建哪些变体）。</summary>
            public readonly List<string> Notes = new List<string>();

            public string ToText()
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("关卡 JSON：" + FileName);

                if (Result != null && Result.Errors.Count > 0)
                {
                    builder.AppendLine("错误 " + Result.Errors.Count + " 条");
                    foreach (string error in Result.Errors) builder.AppendLine("  ✗ " + error);
                }

                if (Result != null && Result.Warnings.Count > 0)
                {
                    builder.AppendLine("警告 " + Result.Warnings.Count + " 条");
                    foreach (string warning in Result.Warnings) builder.AppendLine("  ⚠ " + warning);
                }

                foreach (string note in Notes) builder.AppendLine("  · " + note);

                if (Result != null && Result.HasLevel)
                {
                    builder.AppendLine(DescribeLevel(Result.Level));
                    builder.AppendLine("试跑：" + CheckText);
                }
                else
                {
                    builder.AppendLine("没读出任何关卡。");
                }

                return builder.ToString();
            }
        }

        static JsonRun Run(string path, bool dryRun)
        {
            JsonRun run = new JsonRun { Path = path, FileName = Path.GetFileName(path) };
            run.Result = new LevelJsonReadResult();

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                run.Result.Errors.Add("读不了文件：" + exception.Message);
                return run;
            }

            AssetResolver resolver = new AssetResolver(dryRun);
            try
            {
                run.Result = LevelJsonSerializer.Read(json, resolver);

                if (run.Result.HasLevel)
                {
                    run.CheckText = LevelCheck.Run(run.Result.Level.ToDefinition(), out bool success);
                    run.Success = success;
                }
            }
            catch (Exception exception)
            {
                run.Result.Errors.Add("解析时出错：" + exception.Message);
            }
            finally
            {
                string prefix = dryRun ? "将新建类型变体：" : "新建类型变体：";
                foreach (string name in resolver.WouldCreate) run.Notes.Add(prefix + name);
                resolver.Dispose();
            }

            return run;
        }

        /// <summary>把内存关卡落成资产并追加进关卡表，返回资产路径。</summary>
        static string CreateLevelAsset(LevelSO level)
        {
            string assetPath = LevelImportTool.NextLevelPath();
            AssetDatabase.CreateAsset(level, assetPath);
            EditorUtility.SetDirty(level);

            int catalogIndex = LevelImportTool.AppendToCatalog(level);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BitFlow] 关卡 JSON → " + assetPath
                + (catalogIndex >= 0
                    ? "（关卡表第 " + (catalogIndex + 1) + " 项，索引 0 是开局第一关）"
                    : "（没找到 " + CatalogPath + "，需要手动把关卡拖进关卡表）"));

            return assetPath;
        }

        static void CopyInto(LevelSO target, LevelSO source)
        {
            target.displayName = source.displayName;
            target.width = source.width;
            target.height = source.height;
            target.tickInterval = source.tickInterval;
            target.placements = source.placements;
            target.wallType = source.wallType;
            target.wallMap = source.wallMap;
            target.supplies = source.supplies;
        }

        static string DescribeLevel(LevelSO level)
        {
            if (level == null) return "(空)";

            int walls = 0;
            foreach (Vector2Int _ in level.ParseWalls()) walls++;

            int placements = level.placements != null ? level.placements.Count : 0;
            int supplies = level.supplies != null ? level.supplies.Count : 0;

            return level.width + "×" + level.height + "，元件 " + placements + " 个，墙 " + walls + " 面，手牌 " + supplies + " 摞";
        }

        // ------------------------------------------------------------------ 杂项

        static string PickJson(string title)
        {
            string folder = Path.GetFullPath(IncomingFolder);
            EnsureFolder(IncomingFolder);
            return EditorUtility.OpenFilePanel(title, folder, "json");
        }

        /// <summary>关卡表顺序在前（= 玩家实际顺序），关卡表没收录的资产按名字排在后面。</summary>
        static List<LevelSO> CollectAllLevels()
        {
            List<LevelSO> levels = new List<LevelSO>();
            HashSet<LevelSO> seen = new HashSet<LevelSO>();

            LevelCatalogSO catalog = AssetDatabase.LoadAssetAtPath<LevelCatalogSO>(CatalogPath);
            if (catalog != null && catalog.levels != null)
            {
                foreach (LevelSO level in catalog.levels)
                {
                    if (level == null || !seen.Add(level)) continue;
                    levels.Add(level);
                }
            }

            List<LevelSO> rest = new List<LevelSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelSO"))
            {
                LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || !seen.Add(level)) continue;
                rest.Add(level);
            }

            rest.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            levels.AddRange(rest);
            return levels;
        }

        static IEnumerable<string> CollectJsonFiles()
        {
            string[] folders = { ExportedFolder, IncomingFolder };
            foreach (string folder in folders)
            {
                string absolute = Path.GetFullPath(folder);
                if (!Directory.Exists(absolute)) continue;

                foreach (string file in Directory.GetFiles(absolute, "*.json")) yield return file;
            }
        }

        static string ExportLevel(LevelSO level)
        {
            EnsureFolder(ExportedFolder);

            string path = Path.GetFullPath(ExportedFolder + "/" + Sanitize(level.name) + ".json");
            File.WriteAllText(path, LevelJsonSerializer.ToJson(level));
            return path;
        }

        static void EnsureFolder(string relative)
        {
            string absolute = Path.GetFullPath(relative);
            if (!Directory.Exists(absolute)) Directory.CreateDirectory(absolute);
        }

        /// <summary>把诊断落盘到 LevelJson/reports —— 设计侧看不到 Unity Console，报告是唯一回传通道。</summary>
        static void WriteReport(JsonRun run)
        {
            SaveText(ReportFolder + "/" + Sanitize(run.FileName) + ".report.txt", run.ToText());
        }

        static void SaveText(string relative, string text)
        {
            try
            {
                EnsureFolder(Path.GetDirectoryName(relative) ?? RootFolder);
                File.WriteAllText(Path.GetFullPath(relative), text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 写报告失败：" + exception.Message);
            }
        }

        static string Indent(string text)
        {
            return "  " + text.Replace("\n", "\n  ");
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString();
        }

        static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }





    }
}
