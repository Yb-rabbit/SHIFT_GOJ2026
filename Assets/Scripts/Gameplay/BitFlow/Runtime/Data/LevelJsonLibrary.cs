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

        // ---------------------------------------------------------------- 存储后端
        //
        // 浏览器沙箱里没有可写的本地目录：WebGL 的 System.IO 是 stub，碰一下就抛。
        // 关卡库真正需要的只是「路径 → 文本」这么几个动作，所以全部收敛到这里：
        // 桌面端原样调 System.IO，WebGL 交给 BrowserFileSystem（PlayerPrefs / IndexedDB）。
        // 上层（选关面板、工坊）不需要任何 #if。

        /// <summary>本平台能不能落真正的文件（WebGL 不能，关卡存在浏览器存储里）。</summary>
        public static bool HasLocalFiles => !BrowserFileSystem.IsActive;

        /// <summary>文件是否存在（含浏览器存储里的关卡）。</summary>
        public static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return BrowserFileSystem.IsActive ? BrowserFileSystem.Exists(path) : File.Exists(path);
        }

        /// <summary>读 UTF-8 文本；文件不存在时抛异常，语义与 <c>File.ReadAllText</c> 一致。</summary>
        public static string ReadAllText(string path)
        {
            return BrowserFileSystem.IsActive ? BrowserFileSystem.ReadAllText(path) : File.ReadAllText(path);
        }

        /// <summary>写 UTF-8 文本（浏览器里存进 PlayerPrefs）。</summary>
        public static void WriteAllText(string path, string text)
        {
            if (BrowserFileSystem.IsActive) BrowserFileSystem.WriteAllText(path, text);
            else File.WriteAllText(path, text);
        }

        /// <summary>删一个文件（浏览器里删掉对应的存储键）。</summary>
        public static void DeleteFile(string path)
        {
            if (BrowserFileSystem.IsActive) BrowserFileSystem.Delete(path);
            else File.Delete(path);
        }

        /// <summary>
        /// 把路径归一化成绝对形式，好让「在不在关卡目录里」这类判断可靠。
        ///
        /// 浏览器里<b>不能</b>用 <c>Path.GetFullPath</c>：它依赖 <c>CurrentDirectory</c>，
        /// 而 WebGL 下那是 stub。好在 <see cref="Folder"/> 本身就已经是绝对形式，
        /// 上层传进来的也都是 <c>Path.Combine(Folder, ...)</c> 拼出来的，原样返回即可。
        /// </summary>
        static string NormalizePath(string path)
        {
            return BrowserFileSystem.IsActive ? path : System.IO.Path.GetFullPath(path);
        }

        /// <summary>目录是否存在。浏览器里「关卡目录」恒存在，免得面板报「还没有关卡目录」。</summary>
        static bool DirectoryExists(string directory)
        {
            return BrowserFileSystem.IsActive || Directory.Exists(directory);
        }

        /// <summary>建目录。浏览器里没有真实目录，空操作。</summary>
        static void CreateDirectory(string directory)
        {
            if (!BrowserFileSystem.IsActive) Directory.CreateDirectory(directory);
        }

        /// <summary>列目录里的 <c>*.json</c>。浏览器里来自存储索引。</summary>
        static string[] GetJsonFiles(string folder)
        {
            return BrowserFileSystem.IsActive
                ? BrowserFileSystem.GetFiles(folder, "*.json")
                : Directory.GetFiles(folder, "*.json");
        }

        /// <summary>
        /// 旧版「收件箱」目录名。仍保留在批量导入的扫描列表里，兼容以前把 json 丢进
        /// <see cref="IncomingFolder"/> 的玩家；新流程请用 <see cref="ShareFolder"/>。
        /// </summary>
        public const string IncomingFolderName = "incoming";

        /// <summary>收件箱目录的绝对路径（可能还不存在）。</summary>
        public static string IncomingFolder => System.IO.Path.Combine(Folder, IncomingFolderName);

        /// <summary>
        /// 玩家可见的分享 / 导入目录名。放在程序旁边的 <c>LevelJson/share</c>，
        /// 而不是 <see cref="Folder"/> 那个 Windows 默认隐藏的 AppData 目录。
        /// </summary>
        public const string ShareFolderName = "share";

        static string shareFolder;

        /// <summary>
        /// 玩家可见的分享目录（可能还没建）。优先程序旁边的 <c>LevelJson/share</c>；
        /// 程序目录不可写时，<see cref="EnsureShareFolder"/> 会改用「文档/SHIFT GO/Levels」。
        /// </summary>
        public static string ShareFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(shareFolder)) return shareFolder;

                string program = ProgramFolder;
                if (!string.IsNullOrEmpty(program))
                {
                    return System.IO.Path.Combine(program, FolderName, ShareFolderName);
                }

                return System.IO.Path.Combine(Folder, ShareFolderName);
            }
        }

        /// <summary>建目录（已存在就是空操作）。收件箱与说明文件一起备好。</summary>
        public static void EnsureFolder()
        {
            string folder = Folder;
            if (!DirectoryExists(folder)) CreateDirectory(folder);

            string incoming = IncomingFolder;
            if (!DirectoryExists(incoming)) CreateDirectory(incoming);

            WriteReadmeIfMissing(folder);

            // 内置的作者关卡：走的是和玩家导入完全相同的写盘路径（同一个目录、同一份文件格式），
            // 所以第一次开面板时它们就已经躺在 MY LEVELS 里，没有任何「特殊关卡」分支。
            BundledLevels.InstallIfNeeded();
        }

        /// <summary>
        /// 建「玩家看得见」的分享目录，并把实际落点缓存进 <see cref="ShareFolder"/>。
        /// 程序目录通常是可写的；装在 Program Files 之类只读位置时退到文档目录。
        /// </summary>
        public static bool EnsureShareFolder(out string error)
        {
            error = null;

            // 浏览器里没有「程序旁边的目录」这回事，分享 / 批量导入一律不可用
            if (BrowserFileSystem.IsActive)
            {
                error = "浏览器版本没有分享目录，请用 EXPORT · COPY JSON 复制关卡";
                return false;
            }

            string preferred = ShareFolder;
            if (TryEnsureShareDirectory(preferred, out error))
            {
                shareFolder = preferred;
                WriteShareReadmeIfMissing(preferred);
                return true;
            }

            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(documents))
                {
                    string fallback = System.IO.Path.Combine(documents, "SHIFT GO", "Levels");
                    if (TryEnsureShareDirectory(fallback, out string fallbackError))
                    {
                        shareFolder = fallback;
                        WriteShareReadmeIfMissing(fallback);
                        error = null;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(fallbackError)) error = fallbackError;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 找不到文档目录：" + exception.Message);
            }

            string last = System.IO.Path.Combine(Folder, ShareFolderName);
            if (TryEnsureShareDirectory(last, out string lastError))
            {
                shareFolder = last;
                WriteShareReadmeIfMissing(last);
                error = null;
                return true;
            }

            if (!string.IsNullOrEmpty(lastError)) error = lastError;
            return false;
        }

        static bool TryEnsureShareDirectory(string folder, out string error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                // 目录存在 ≠ 可写（Program Files 下的空目录就是典型）：真写一个探针再删掉。
                string probe = System.IO.Path.Combine(folder, ".write_probe");
                File.WriteAllText(probe, "SHIFT GO");
                File.Delete(probe);
                return true;
            }
            catch (Exception exception)
            {
                error = "分享目录不可写：" + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 目录说明的首行版本戳。文案改了就改这里：已有 README（包括旧版、带本机绝对路径的）
        /// 会在下次 <see cref="EnsureFolder"/> / <see cref="EnsureShareFolder"/> 时被重写，
        /// 免得发出去的包里那份旧说明永远留在玩家机器上。
        /// </summary>
        public const string ReadmeStamp = "SHIFT GO README v2";

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
                if (FileExists(candidate)) continue;

                try
                {
                    WriteAllText(candidate, json);
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
        /// 把一段 JSON 写进<b>玩家看得见</b>的分享目录（<c>PREFIX_NN.json</c>，不覆盖已有文件）。
        /// 用途只有一个：让玩家把自己的关卡文件发给别人，而不是在面板里展示原文。
        /// </summary>
        public static bool TryWriteShare(string prefix, string json, out string path, out string error)
        {
            path = null;
            error = null;

            if (json == null)
            {
                error = "没有内容";
                return false;
            }

            if (string.IsNullOrEmpty(prefix)) prefix = "SHARED_";

            if (!EnsureShareFolder(out error)) return false;

            string folder = ShareFolder;
            for (int n = 1; n <= MaxAutoNames; n++)
            {
                string candidate = System.IO.Path.Combine(folder, prefix + n.ToString("00") + ".json");

                if (FileExists(candidate))
                {
                    try
                    {
                        // 同一份内容已经在分享目录里 = 已经分享过，不再堆一份副本
                        if (string.Equals(ReadAllText(candidate), json, StringComparison.Ordinal))
                        {
                            path = candidate;
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        // 读不了已有文件就当它不同，继续找下一个号
                    }

                    continue;
                }

                try
                {
                    WriteAllText(candidate, json);
                }
                catch (Exception exception)
                {
                    error = "写不进分享目录：" + exception.Message;
                    return false;
                }

                path = candidate;
                return true;
            }

            error = "分享文件名用完了（" + prefix + "01 ~ " + prefix + MaxAutoNames + "）";
            return false;
        }

        /// <summary>把关卡库里的一个 JSON 文件复制到分享目录（<c>SHARE · SAVE COPY</c>）。</summary>
        public static bool TryCopyToShare(string sourcePath, out string path, out string error)
        {
            path = null;
            error = null;

            if (string.IsNullOrEmpty(sourcePath) || !FileExists(sourcePath))
            {
                error = "文件不在了：" + sourcePath;
                return false;
            }

            string json;
            try
            {
                json = ReadAllText(sourcePath);
            }
            catch (Exception exception)
            {
                error = "读不了文件：" + exception.Message;
                return false;
            }

            string prefix = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            if (string.IsNullOrEmpty(prefix)) prefix = "SHARED";
            return TryWriteShare(prefix + "_", json, out path, out error);
        }

        // ------------------------------------------------------------------ 外部 JSON 导入

        /// <summary>
        /// 往关卡目录里放一份说明。只在「缺失 / 版本戳不对」时写 ——
        /// 玩家自己改过的同版本说明不会被覆盖，旧版（可能带绝对路径）会被自动纠正。
        /// </summary>
        static void WriteReadmeIfMissing(string folder)
        {
            WriteReadmeIfOutdated(folder,
                ReadmeStamp + " · 关卡 JSON 目录\r\n"
                + "========================================\r\n\r\n"
                + "这里放的就是「选关面板 → MY LEVELS」里列出来的关卡。\r\n"
                + "每个 .json 就是一关，文件名不重要，关卡名写在文件里的 displayName。\r\n\r\n"
                + "怎么加关（任选一条）：\r\n"
                + "  1. 游戏里：选关面板 → FILES → IMPORT → FROM FILE（挑一个 json）\r\n"
                + "  2. 游戏里：把 json 放进 SHARE FOLDER，再点 IMPORT → FROM SHARE FOLDER\r\n"
                + "  3. 直接复制进本目录，然后点 FILES → REFRESH（或重开面板）\r\n\r\n"
                + "怎么删关：选关面板 → MY LEVELS 里那一行右边的 ... → DELETE → 3 秒内再点 DELETE。\r\n"
                + "怎么改关：同一行 ... → EDIT，在工坊里按 NAME 改名，DONE 写回原文件。\r\n"
                + "怎么分享：同一行 ... → SHARE · SAVE COPY，副本会写进玩家可见的 SHARE FOLDER。\r\n"
                + "同名但内容不同的 json 不会被悄悄覆盖；请先改文件名再导入。\r\n\r\n"
                + "以 _ 开头的文件（_template.json 之类）是模板/草稿，不会被当成关卡。\r\n"
                + "本文件所在的目录就是游戏的运行时关卡目录；不同电脑上的路径不一样，这里不写死。\r\n");
        }

        /// <summary>
        /// 给分享目录放一份说明。分享目录里的文件<b>不会自动</b>出现在 MY LEVELS ——
        /// 必须先走 IMPORT，所以说明里要把这条写死，免得玩家以为丢进去就能玩。
        /// 说明里<b>不写绝对路径</b>：这份文件会被随关卡一起转发，写死路径等于把发布者的机器目录带给别人。
        /// </summary>
        static void WriteShareReadmeIfMissing(string folder)
        {
            WriteReadmeIfOutdated(folder,
                ReadmeStamp + " · 关卡分享 / 导入目录\r\n"
                + "========================================\r\n\r\n"
                + "这个目录是给玩家之间传关卡用的：\r\n"
                + "  1. 别人发来的 .json 放进这里；\r\n"
                + "  2. 游戏里：选关面板 → IMPORT → FROM SHARE FOLDER · BATCH IMPORT；\r\n"
                + "  3. 你自己的关卡：MY LEVELS 那一行 ... → SHARE · SAVE COPY 也会存到这里。\r\n\r\n"
                + "注意：这里的文件不会自动出现在 MY LEVELS，导入成功后才会进游戏自己的关卡库。\r\n"
                + "导出 / 分享只是把文件复制到这里，不会覆盖已有的同名文件。\r\n\r\n"
                + "本文件所在的目录就是分享目录（游戏里 SHARE FOLDER 打开的就是它）；\r\n"
                + "不同电脑上的路径不一样，所以说明里不写死路径。\r\n");
        }

        /// <summary>
        /// 只有「文件不存在 / 首行版本戳对不上」时才写说明。
        /// 这样玩家自己改过的同版本说明会被保留，而旧版说明
        /// （包括早期版本写死开发者本机路径的那些）会在下一次启动时自动纠正。
        /// </summary>
        static void WriteReadmeIfOutdated(string folder, string content)
        {
            // 浏览器里没有真实目录，README 无从谈起（也别往关卡存储里塞非关卡文本）
            if (BrowserFileSystem.IsActive) return;

            try
            {
                string path = System.IO.Path.Combine(folder, ReadmeName);
                if (IsCurrentReadme(path)) return;

                File.WriteAllText(path, content);
            }
            catch (Exception exception)
            {
                // 写说明失败不影响读写关卡：这是纯帮助文本
                Debug.LogWarning("[BitFlow] 写不了目录说明：" + exception.Message);
            }
        }

        /// <summary>说明文件存在且首行就是当前版本的戳。</summary>
        static bool IsCurrentReadme(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                using (StreamReader reader = new StreamReader(path))
                {
                    string firstLine = reader.ReadLine();
                    return firstLine != null
                        && firstLine.StartsWith(ReadmeStamp, StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                // 读不了就当它是旧的：下次写入会尝试修好
                return false;
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
        /// 第一个是**玩家可见**的 <see cref="ShareFolder"/>（程序旁边 <c>LevelJson/share</c>），
        /// 然后是正式目录下的 <see cref="IncomingFolder"/>（旧版收件箱），
        /// 最后是程序旁边的 <c>LevelJson</c> / <c>LevelJson\incoming</c>：
        /// 独立版里那是 exe 同级目录（完全不隐藏），编辑器里就是工程根目录的 <c>LevelJson\</c>
        /// （和 <c>Tools/LevelStudio</c> 存盘、Unity 同步菜单用的是同一处）。
        /// </summary>
        public static List<string> DropFolders()
        {
            // 浏览器里没有「玩家能自己丢文件的目录」，批量导入无从谈起
            if (BrowserFileSystem.IsActive) return new List<string>();

            List<string> folders = new List<string> { ShareFolder, IncomingFolder };

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

            if (!FileExists(sourcePath))
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
                json = ReadAllText(sourcePath);
            }
            catch (Exception exception)
            {
                error = "读不了文件：" + exception.Message;
                return ImportOutcome.Failed;
            }

            // 正式目录里已有同一份文件 = 已经导入过，不再造第二份
            if (IsInsideLibrary(sourcePath))
            {
                path = NormalizePath(sourcePath);
                return ImportOutcome.AlreadyInLibrary;
            }

            return WriteImport(sourceName, json, overwrite, out path, out error);
        }

        /// <summary>
        /// 同 <see cref="TryImportFile"/>，但源是按文本给的。
        /// 浏览器里没有可读的路径：文件选择器只交回「文件名 + 正文」，
        /// 所以落盘规则必须和文件那条路共用一份实现（<see cref="WriteImport"/>）；
        /// 「是不是已经在正式目录里」这个问题浏览器答不了，一律当作新导入。
        /// </summary>
        public static ImportOutcome TryImportJson(string fileName, string json, bool overwrite, out string path, out string error)
        {
            path = null;
            error = null;

            string sourceName = System.IO.Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrEmpty(sourceName)) sourceName = "IMPORT.json";
            if (!sourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) sourceName += ".json";

            if (IsReservedLevelFileName(sourceName))
            {
                error = sourceName.StartsWith("_", StringComparison.Ordinal)
                    ? "文件名不能以下划线开头（这类文件不会显示在选关列表里）"
                    : "catalog.json 是目录索引，不是关卡";
                return ImportOutcome.Invalid;
            }

            return WriteImport(sourceName, json, overwrite, out path, out error);
        }

        /// <summary>
        /// 验货 + 落盘。文件名规则、重名规则、「不覆盖」规则只写在这里一份，
        /// 文件导入（<see cref="TryImportFile"/>）和浏览器导入（<see cref="TryImportJson"/>）共用，
        /// 免得两边慢慢长出不一样的语义。
        /// </summary>
        static ImportOutcome WriteImport(string fileName, string json, bool overwrite, out string path, out string error)
        {
            path = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "文件是空的";
                return ImportOutcome.Invalid;
            }

            // 读不通的东西一个字节都不落盘：否则列表里会多一关，点进去是黑的
            if (!TryParseJson(json, out LevelSO probe, out List<string> problems))
            {
                error = "不是能跑的关卡：" + string.Join("；", problems);
                return ImportOutcome.Invalid;
            }
            DestroyLevel(probe);

            EnsureFolder();

            // 目标名 = 源文件名：重复导入同一份文件就是覆盖它自己，不会在列表里长出 _02、_03
            string destination = System.IO.Path.Combine(Folder, fileName);

            if (FileExists(destination))
            {
                string existing;
                try
                {
                    existing = ReadAllText(destination);
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
                    error = "正式目录里已有同名但内容不同的关卡：" + fileName;
                    return ImportOutcome.NameTaken;
                }
            }

            try
            {
                WriteAllText(destination, json);
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
                string folder = NormalizePath(Folder);
                string full = NormalizePath(path);
                if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;

                // 收件箱在正式目录下面，但那不是「已经在列表里」
                string incoming = NormalizePath(IncomingFolder);
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
                folder = NormalizePath(Folder);
                full = NormalizePath(path);
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
                if (!FileExists(full))
                {
                    error = "文件已经不在了";
                    return false;
                }

                DeleteFile(full);
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
            if (!DirectoryExists(folder))
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
                files = GetJsonFiles(folder);
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
                json = ReadAllText(path);
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
