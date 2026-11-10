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
    /// 导入一份外部 JSON 的结果。UI 必须按这里分情况说话：
    /// <see cref="NameTaken"/> 绝不能悄悄覆盖同名关卡，<see cref="Unchanged"/>
    /// 也不该被报成「又导入了一关」。
    /// </summary>
    public enum ImportOutcome
    {
        /// <summary>复制进正式目录了。</summary>
        Imported,

        /// <summary>源文件本来就在正式目录里，列表里已经有它。</summary>
        AlreadyInLibrary,

        /// <summary>正式目录已有同名且内容完全相同的文件，无需写入。</summary>
        Unchanged,

        /// <summary>正式目录已有同名但内容不同，调用方没有授权覆盖。</summary>
        NameTaken,

        /// <summary>文件不是能跑的关卡（含保留文件名 / 非 .json）。</summary>
        Invalid,

        /// <summary>读写失败，细节看 <c>error</c> 与 Console。</summary>
        Failed,
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

        /// <summary>
        /// 「收件箱」目录名：玩家 / 作者把待导入的 json 丢进 <see cref="IncomingFolder"/>，
        /// 面板上一按 <c>IMPORT · FROM INBOX</c> 就搬进正式目录。
        ///
        /// 为什么不直接读正式目录：正式目录里躺着的是「已经在列表里的关卡」，
        /// 玩家往那儿丢文件再点导出的按钮，会得到一份重名副本（<c>MY_LEVEL_01_02.json</c>）。
        /// 收件箱是单向的：进去 → 验货 → 搬到正式目录，语义不含糊。
        /// </summary>
        public const string IncomingFolderName = "incoming";

        /// <summary>收件箱目录的绝对路径（可能还不存在）。</summary>
        public static string IncomingFolder => System.IO.Path.Combine(Folder, IncomingFolderName);

        /// <summary>建目录（已存在就是空操作）。收件箱与说明文件一起备好。</summary>
        public static void EnsureFolder()
        {
            string folder = Folder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string incoming = IncomingFolder;
            if (!Directory.Exists(incoming)) Directory.CreateDirectory(incoming);

            WriteReadmeIfMissing(folder);
        }

        /// <summary>
        /// 目录说明文件名。以 <c>README</c> 开头、不是 <c>.json</c>，所以绝不会被
        /// <see cref="Scan"/> 当成关卡读。
        /// </summary>
        public const string ReadmeName = "README.txt";

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

        // ------------------------------------------------------------------ 外部 JSON 导入

        /// <summary>往关卡目录里放一份说明（只在缺失时写，不覆盖玩家改过的版本）。</summary>
        static void WriteReadmeIfMissing(string folder)
        {
            try
            {
                string path = System.IO.Path.Combine(folder, ReadmeName);
                if (File.Exists(path)) return;

                File.WriteAllText(path,
                    "SHIFT GO — 关卡 JSON 目录\r\n"
                    + "========================================\r\n\r\n"
                    + "这里放的就是「选关面板 → MY LEVELS」里列出来的关卡。\r\n"
                    + "每个 .json 就是一关，文件名不重要，关卡名写在文件里的 displayName。\r\n\r\n"
                    + "怎么加关（任选一条）：\r\n"
                    + "  1. 游戏里：选关面板 → FILES → IMPORT → FROM FILE（挑一个 json）\r\n"
                    + "  2. 游戏里：把 json 放进本目录下的 incoming\\ ，再点 IMPORT → FROM INBOX\r\n"
                    + "  3. 直接复制进本目录，然后点 FILES → REFRESH（或重开面板）\r\n\r\n"
                    + "怎么删关：选关面板 → MY LEVELS 里那一行右边的 ... → DELETE → 3 秒内再点 DELETE。\r\n"
                    + "怎么改关：同一行 ... → EDIT，在工坊里按 NAME 改名，DONE 写回原文件。\r\n"
                    + "同名但内容不同的 json 不会被悄悄覆盖；请先改文件名再导入。\r\n\r\n"
                    + "以 _ 开头的文件（_template.json 之类）是模板/草稿，不会被当成关卡。\r\n"
                    + "incoming\\ 里是待导入的批次，导入成功后正式目录会多一份同名副本，\r\n"
                    + "收件箱里那份可以自己删掉。\r\n\r\n"
                    + "路径：\r\n  " + folder + "\r\n");
            }
            catch (Exception exception)
            {
                // 写说明失败不影响读写关卡：这是纯帮助文本
                Debug.LogWarning("[BitFlow] 写不了关卡目录说明：" + exception.Message);
            }
        }

        /// <summary>
        /// 程序所在目录。<c>Application.dataPath</c> 在 Player 里是 <c>&lt;exe&gt;\&lt;产品名&gt;_Data</c>、
        /// 编辑器里是 <c>&lt;工程&gt;\Assets</c>，两者的父目录都是「玩家一眼能找到的地方」。
        /// </summary>
        static string ProgramFolder
        {
            get
            {
                try
                {
                    DirectoryInfo data = new DirectoryInfo(Application.dataPath);
                    return data.Parent != null ? data.Parent.FullName : Application.dataPath;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 「外部投放目录」：按优先级排列的、玩家可以自己找到并丢文件的目录。
        ///
        /// 第一个永远是正式目录下的 <see cref="IncomingFolder"/>（<c>SHARE FOLDER</c> 打开的就是它的上级），
        /// 后面是**程序旁边**的 <c>LevelJson</c> / <c>LevelJson\incoming</c>：
        /// 独立版里那是 exe 同级目录（完全不隐藏），编辑器里就是工程根目录的 <c>LevelJson\</c>
        /// （和 <c>Tools/LevelStudio</c> 存盘、Unity 同步菜单用的是同一处）。
        /// </summary>
        public static List<string> DropFolders()
        {
            List<string> folders = new List<string> { IncomingFolder };

            string program = ProgramFolder;
            if (!string.IsNullOrEmpty(program))
            {
                folders.Add(System.IO.Path.Combine(program, FolderName));
                folders.Add(System.IO.Path.Combine(program, FolderName, IncomingFolderName));
            }

            List<string> unique = new List<string>();
            foreach (string folder in folders)
            {
                bool duplicate = false;
                foreach (string existing in unique)
                {
                    if (string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase)) duplicate = true;
                }
                if (!duplicate && Directory.Exists(folder)) unique.Add(folder);
            }

            return unique;
        }

        /// <summary>
        /// 文件名是不是「不能作为关卡出现」的保留名：
        /// - 下划线开头是模板 / 草稿，<see cref="Scan"/> 会跳过；
        /// - <c>catalog.json</c> 是目录索引，不是关卡。
        ///
        /// 导入时必须先拦下来，否则会「提示导入成功、列表里却找不到」。
        /// </summary>
        static bool IsReservedLevelFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            if (fileName.StartsWith("_", StringComparison.Ordinal)) return true;
            return string.Equals(fileName, "catalog.json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 把一个外部 <c>.json</c> 导进正式关卡目录（<b>先验货、再落盘</b>）。
        ///
        /// 为什么复制而不移动：源文件可能在玩家的下载目录里，也可能被别的关卡引用着；
        /// 复制是幂等的、可后悔的。字节原样搬运（不重新序列化），作者写的缩进和字段顺序都留着。
        ///
        /// 已在正式目录里的文件视为「已经导入过」：直接返回它的路径，不再造一份 <c>_02</c> 副本。
        /// 正式目录里已有同名文件时：内容相同返回 <see cref="ImportOutcome.Unchanged"/>；
        /// 内容不同且 <paramref name="overwrite"/> 为 false 返回 <see cref="ImportOutcome.NameTaken"/>，
        /// <b>一个字节都不写</b>。只有调用方明确传 true 才覆盖。
        /// 下划线开头 / <c>catalog.json</c> 这类不会出现在列表里的文件名会被明确拒绝。
        /// </summary>
        public static ImportOutcome TryImportFile(string sourcePath, bool overwrite, out string path, out string error)
        {
            path = null;
            error = null;

            if (string.IsNullOrEmpty(sourcePath))
            {
                error = "没有文件";
                return ImportOutcome.Invalid;
            }

            if (!File.Exists(sourcePath))
            {
                error = "文件不在了：" + sourcePath;
                return ImportOutcome.Invalid;
            }

            string sourceName = System.IO.Path.GetFileName(sourcePath);
            if (!sourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                error = "只导入 .json 文件";
                return ImportOutcome.Invalid;
            }

            if (IsReservedLevelFileName(sourceName))
            {
                error = sourceName.StartsWith("_", StringComparison.Ordinal)
                    ? "文件名不能以下划线开头（这类文件不会显示在选关列表里）"
                    : "catalog.json 是目录索引，不是关卡";
                return ImportOutcome.Invalid;
            }

            string json;
            try
            {
                json = File.ReadAllText(sourcePath);
            }
            catch (Exception exception)
            {
                error = "读不了文件：" + exception.Message;
                return ImportOutcome.Failed;
            }

            // 读不通的东西一个字节都不落盘：否则列表里会多一关，点进去是黑的
            if (!TryParseJson(json, out LevelSO probe, out List<string> problems))
            {
                error = "不是能跑的关卡：" + string.Join("；", problems);
                return ImportOutcome.Invalid;
            }
            DestroyLevel(probe);

            if (IsInsideLibrary(sourcePath))
            {
                path = System.IO.Path.GetFullPath(sourcePath);
                return ImportOutcome.AlreadyInLibrary;
            }

            EnsureFolder();

            // 目标名 = 源文件名：重复导入同一份文件就是覆盖它自己，不会在列表里长出 _02、_03
            string destination = System.IO.Path.Combine(Folder, System.IO.Path.GetFileName(sourcePath));

            if (File.Exists(destination))
            {
                string existing;
                try
                {
                    existing = File.ReadAllText(destination);
                }
                catch (Exception exception)
                {
                    error = "读不了目标文件：" + exception.Message;
                    return ImportOutcome.Failed;
                }

                // 内容一模一样 = 这份文件早就导过了。不写盘，也不把它算成一次新导入。
                if (string.Equals(existing, json, StringComparison.Ordinal))
                {
                    path = destination;
                    return ImportOutcome.Unchanged;
                }

                if (!overwrite)
                {
                    path = destination;
                    error = "正式目录里已有同名但内容不同的关卡：" + System.IO.Path.GetFileName(destination);
                    return ImportOutcome.NameTaken;
                }
            }

            try
            {
                File.WriteAllText(destination, json);
            }
            catch (Exception exception)
            {
                error = "写不进关卡目录：" + exception.Message;
                return ImportOutcome.Failed;
            }

            path = destination;
            return ImportOutcome.Imported;
        }

        /// <summary>
        /// 把 <see cref="DropFolders"/> 里所有能读通的 json 导进正式目录，返回<b>新写入</b>的个数。
        ///
        /// 同名以「先出现的目录」为准（收件箱优先），并且<b>绝不覆盖</b>正式目录里已有且内容不同的文件：
        /// 那种情况记进 <paramref name="problems"/>，让玩家自己改名后再导入。
        /// 内容完全相同的重复文件算 <paramref name="unchanged"/>，不反复重写。
        /// </summary>
        public static int ImportFromDropFolders(out List<string> problems)
        {
            return ImportFromDropFolders(out problems, out _);
        }

        /// <summary>同 <see cref="ImportFromDropFolders(out List{string})"/>，顺带报出「已经是最新」的个数。</summary>
        public static int ImportFromDropFolders(out List<string> problems, out int unchanged)
        {
            problems = new List<string>();
            unchanged = 0;
            int imported = 0;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in DropFolders())
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(folder, "*.json");
                }
                catch (Exception exception)
                {
                    problems.Add(folder + "：" + exception.Message);
                    continue;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    string name = System.IO.Path.GetFileName(file);
                    if (IsReservedLevelFileName(name)) continue;   // 模板 / 草稿 / 目录索引

                    // 已经在正式目录里的文件不算「导入」：它就是列表里那一关
                    if (!seen.Add(name) || IsInsideLibrary(file)) continue;

                    ImportOutcome outcome = TryImportFile(file, false, out string path, out string error);

                    if (outcome == ImportOutcome.Imported)
                    {
                        imported++;
                        Debug.Log("[BitFlow] 已导入关卡 JSON：" + file + " → " + path);
                    }
                    else if (outcome == ImportOutcome.Unchanged || outcome == ImportOutcome.AlreadyInLibrary)
                    {
                        unchanged++;
                    }
                    else
                    {
                        problems.Add(name + "：" + error);
                    }
                }
            }

            return imported;
        }

        /// <summary>路径是不是就在正式关卡目录里（不含收件箱）。</summary>
        public static bool IsInsideLibrary(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                string folder = System.IO.Path.GetFullPath(Folder);
                string full = System.IO.Path.GetFullPath(path);
                if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;

                // 收件箱在正式目录下面，但那不是「已经在列表里」
                string incoming = System.IO.Path.GetFullPath(IncomingFolder);
                return !full.StartsWith(incoming, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
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
                // 下划线开头的是模板 / 草稿，catalog.json 是目录索引，都不该出现在选关列表里
                if (IsReservedLevelFileName(System.IO.Path.GetFileName(file))) continue;

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
