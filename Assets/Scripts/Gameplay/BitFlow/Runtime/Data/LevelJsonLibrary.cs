using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 选关面板上的一条 JSON 关卡。<b>不持有</b> <see cref="LevelSO"/>：
    /// 面板只是列个名字，真正进关时再读一遍文件 —— 玩家手改了 JSON，重开面板就能看到最新内容，
    /// 也不会出现「面板缓存了一份、关卡跑的是另一份」这种所有权扯不清的情况。
    /// </summary>
    public sealed class LevelJsonEntry
    {
        public readonly string Path;
        public readonly string DisplayName;
        public readonly int Width;
        public readonly int Height;

        public LevelJsonEntry(string path, string displayName, int width, int height)
        {
            Path = path;
            DisplayName = string.IsNullOrEmpty(displayName)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : displayName;
            Width = width;
            Height = height;
        }

        /// <summary>「文件名 · 宽x高」，面板拿它当副标题。</summary>
        public string Subtitle => System.IO.Path.GetFileName(Path) + "  " + Width + "x" + Height;
    }

    /// <summary>
    /// 运行时关卡 JSON 的落盘目录与读取入口 —— <b>打包后也有</b>。
    ///
    /// 目录固定为 <c>Application.persistentDataPath/LevelJson</c>，不放 Assets：
    /// 打包后的项目里没有 Assets，而 persistentDataPath 是 Player 唯一
    /// 「作者/玩家能直接扔文件进去」的地方。编辑器里同一个路径照样能读写，
    /// 所以三个环境（Play / EditMode / Player）共用这一份代码，不用 #if UNITY_EDITOR。
    ///
    /// 编辑器侧另有一个菜单把工程里的 <c>LevelJson/</c> 同步过来，见 LevelJsonRuntimeSetup。
    /// </summary>
    public static class LevelJsonLibrary
    {
        /// <summary>persistentDataPath 下的关卡目录名。</summary>
        public const string FolderName = "LevelJson";

        /// <summary>关卡 JSON 目录的绝对路径（可能还不存在）。</summary>
        public static string Folder => System.IO.Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>建目录（已存在就是空操作）。</summary>
        public static void EnsureFolder()
        {
            string folder = Folder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }

        /// <summary>剪贴板导入落盘时的文件名前缀：<c>IMPORT_01.json</c>。</summary>
        public const string ImportPrefix = "IMPORT_";

        /// <summary>导出当前关卡时的文件名前缀：<c>EXPORT_01.json</c>。</summary>
        public const string ExportPrefix = "EXPORT_";

        /// <summary>
        /// <see cref="TryWriteNew"/> 最多试到第几个号。99 关够玩家用一辈子，
        /// 又不会在目录被塞满时卡在循环里。
        /// </summary>
        const int MaxAutoNames = 99;

        /// <summary>
        /// 把一段文本<b>原样</b>写进关卡目录的新文件（<c>PREFIX_NN.json</c>，不覆盖任何已有文件）。
        ///
        /// 「原样」是重点：导入只是搬运别人给的字节，不重新序列化 ——
        /// 作者写的字段顺序、缩进、多余的空格都留着，玩家回看时不会觉得「怎么和发来的不一样」。
        /// 合法性由调用方先用 <see cref="TryParseJson"/> 把关。
        /// </summary>
        public static bool TryWriteNew(string prefix, string json, out string path, out string error)
        {
            path = null;
            error = null;

            if (json == null)
            {
                error = "没有内容";
                return false;
            }

            if (string.IsNullOrEmpty(prefix)) prefix = "LEVEL_";

            EnsureFolder();
            for (int n = 1; n <= MaxAutoNames; n++)
            {
                string candidate = System.IO.Path.Combine(Folder, prefix + n.ToString("00") + ".json");
                if (File.Exists(candidate)) continue;

                try
                {
                    File.WriteAllText(candidate, json);
                }
                catch (Exception exception)
                {
                    error = "写不了文件：" + exception.Message;
                    return false;
                }

                path = candidate;
                return true;
            }

            error = "文件名用完了（" + prefix + "01 ~ " + prefix + MaxAutoNames + "）";
            return false;
        }

        /// <summary>
        /// 删掉一份关卡 JSON（选关面板的 DELETE）。
        ///
        /// 只肯删<b>这个目录里</b>的 .json：路径先用 <see cref="Folder"/> 的绝对路径框一遍。
        /// 面板上一个 bug 就删掉玩家机器上任意一个文件，是这类工具最不该有的失手。
        /// </summary>
        public static bool TryDelete(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "没有路径";
                return false;
            }

            string folder;
            string full;
            try
            {
                folder = Path.GetFullPath(Folder);
                full = Path.GetFullPath(path);
            }
            catch (Exception exception)
            {
                error = "路径看不懂：" + exception.Message;
                return false;
            }

            if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, folder, StringComparison.OrdinalIgnoreCase))
            {
                error = "只能删关卡目录里的 .json";
                return false;
            }

            try
            {
                if (!File.Exists(full))
                {
                    error = "文件已经不在了";
                    return false;
                }

                File.Delete(full);
                return true;
            }
            catch (Exception exception)
            {
                error = "删不掉：" + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 校验一段 JSON <b>文本</b>（不落盘）：能读通就给出一个临时关卡，调用方负责
        /// <see cref="DestroyLevel"/>。剪贴板导入、以及「这份 JSON 到底能不能跑」都走它。
        ///
        /// 判定标准和 <see cref="TryLoad"/> 完全一致（同一段 <see cref="Parse"/>）：
        /// 有错就当读不通 —— 导入进来的东西到不了「能玩」那一步，就不该落盘。
        /// </summary>
        public static bool TryParseJson(string json, out LevelSO level, out List<string> problems)
        {
            level = null;
            problems = new List<string>();

            if (string.IsNullOrEmpty(json))
            {
                problems.Add("没有内容");
                return false;
            }

            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            if (registry == null)
            {
                problems.Add("没有类型注册表（Resources/" + LevelTypeRegistrySO.ResourcePath + "）");
                return false;
            }

            return Parse(json, "剪贴板", new LevelTypeRegistryResolver(registry), out level, out problems);
        }

        /// <summary>
        /// 列出目录里所有能读通的 <c>*.json</c> 关卡（按文件名排序，大小写不敏感）。
        /// 读不出来的文件不会静默消失：原因都塞进 <paramref name="problems"/>，交给面板/日志显示。
        /// </summary>
        public static List<LevelJsonEntry> Scan(out List<string> problems)
        {
            problems = new List<string>();
            List<LevelJsonEntry> entries = new List<LevelJsonEntry>();

            string folder = Folder;
            if (!Directory.Exists(folder))
            {
                problems.Add("还没有关卡目录：" + folder);
                return entries;
            }

            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            if (registry == null)
            {
                problems.Add("没有类型注册表（Resources/" + LevelTypeRegistrySO.ResourcePath + "）");
                return entries;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.json");
            }
            catch (Exception exception)
            {
                problems.Add("列目录失败：" + exception.Message);
                return entries;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            LevelTypeRegistryResolver resolver = new LevelTypeRegistryResolver(registry);
            foreach (string file in files)
            {
                // 下划线开头的是模板 / 草稿，不该出现在选关列表里
                if (System.IO.Path.GetFileName(file).StartsWith("_", StringComparison.Ordinal)) continue;

                if (!TryRead(file, resolver, out LevelSO level, out List<string> fileProblems))
                {
                    foreach (string problem in fileProblems)
                    {
                        problems.Add(System.IO.Path.GetFileName(file) + "：" + problem);
                    }
                    continue;
                }

                entries.Add(new LevelJsonEntry(file, level.displayName, level.width, level.height));

                // 面板只要元数据：临时资产立刻回收，免得每开一次面板堆一堆 LevelSO
                DestroyLevel(level);
            }

            return entries;
        }

        /// <summary>读一个 JSON 文件成一个<b>临时</b> <see cref="LevelSO"/>（调用方负责用完销毁）。</summary>
        public static bool TryLoad(string path, out LevelSO level, out List<string> problems)
        {
            level = null;
            problems = new List<string>();

            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            if (registry == null)
            {
                problems.Add("没有类型注册表（Resources/" + LevelTypeRegistrySO.ResourcePath + "）");
                return false;
            }

            return TryRead(path, new LevelTypeRegistryResolver(registry), out level, out problems);
        }

        static bool TryRead(string path, ILevelJsonTypeResolver resolver, out LevelSO level, out List<string> problems)
        {
            level = null;
            problems = new List<string>();

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                problems.Add("读不了文件：" + exception.Message);
                return false;
            }

            return Parse(json, System.IO.Path.GetFileName(path), resolver, out level, out problems);
        }

        /// <summary>
        /// 文本 → 临时关卡。<see cref="TryLoad"/> 与 <see cref="TryParseJson"/> 共用这一份判定，
        /// 免得「从文件读」和「从剪贴板读」哪天对不上（一个收、一个不收）。
        /// </summary>
        static bool Parse(string json, string source, ILevelJsonTypeResolver resolver, out LevelSO level, out List<string> problems)
        {
            level = null;
            problems = new List<string>();

            LevelJsonReadResult result = LevelJsonSerializer.Read(json, resolver);
            if (!result.HasLevel)
            {
                problems.AddRange(result.Errors);
                if (problems.Count == 0) problems.Add("读不出关卡");
                return false;
            }

            if (result.Errors.Count > 0)
            {
                // 有错 = 这份文件还不完整，不拿半成品去跑关（否则玩家会看到一关莫名其妙的东西）
                problems.AddRange(result.Errors);
                DestroyLevel(result.Level);
                return false;
            }

            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning("[BitFlow] " + source + "：" + warning);
            }

            level = result.Level;
            return true;
        }

        /// <summary>回收运行时造出来的临时关卡资产（播放中 Destroy、编辑器里 DestroyImmediate）。</summary>
        public static void DestroyLevel(LevelSO level)
        {
            if (level == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(level);
            else UnityEngine.Object.DestroyImmediate(level);
        }
    }
}
