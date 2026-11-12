using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.LevelStudio
{
    /// <summary>
    /// 给浏览器界面用的本地接口。全部只监听 127.0.0.1，不对外网开放。
    ///
    /// 接口清单：
    ///  GET  /api/state                 路径信息（工程目录 / 玩家关卡目录 / 有没有类型目录）
    ///  GET  /api/catalog               类型资产快照（没导出过就给一份内置默认）
    ///  GET  /api/levels?where=...      列关卡文件
    ///  GET  /api/level?where=..&amp;name=..  读一份关卡的原始文本
    ///  POST /api/check                 用真引擎跑一遍，返回可不可解
    ///  POST /api/save                  存盘（先构建一遍，坏数据不落盘）
    ///  POST /api/open-folder           在资源管理器里打开某个关卡目录
    /// </summary>
    public sealed class Api
    {
        readonly string root;
        readonly string webRoot;

        static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        };

        static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            // LevelDocument / LevelTypeCatalog 的成员全是 public 字段（为了给 Unity 的 JsonUtility 直读），
            // System.Text.Json 默认只序列化属性 —— 少这一行，兜底路径的 /api/catalog 会只剩 schema、types 全丢。
            IncludeFields = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public Api(string projectRoot, string webRoot)
        {
            root = projectRoot;
            this.webRoot = webRoot;
        }

        public void Handle(HttpListenerContext context)
        {
            try
            {
                Route(context);
            }
            catch (Exception exception)
            {
                TrySend(context, 500, "application/json; charset=utf-8",
                    JsonSerializer.Serialize(new { error = exception.Message }, WriteOptions));
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        void Route(HttpListenerContext context)
        {
            string path = (context.Request.Url?.AbsolutePath ?? "/").ToLowerInvariant();

            if (context.Request.HttpMethod == "POST")
            {
                switch (path)
                {
                    // 只留 /api/check 一个入口。以前这里还挂着 /api/solve 别名，
                    // 但它和 check 走的是同一条实现（把摆好的布局跑一遍），
                    // 名字承诺了"求解"却不求解 —— 谁调谁误会。
                    case "/api/check": HandleCheck(context); return;
                    case "/api/save": HandleSave(context); return;
                    case "/api/open-folder": HandleOpenFolder(context); return;
                }
            }
            else
            {
                switch (path)
                {
                    case "/api/state": HandleState(context); return;
                    case "/api/catalog": HandleCatalog(context); return;
                    case "/api/levels": HandleLevels(context); return;
                    case "/api/level": HandleLevel(context); return;
                }
            }

            ServeStatic(context, path);
        }

        // ------------------------------------------------------------------ 读接口

        void HandleState(HttpListenerContext context)
        {
            string player = StudioPaths.PlayerLevelsDirectory;

            SendJson(context, new
            {
                projectRoot = root,
                exportedDir = StudioPaths.ExportedDirectory,
                incomingDir = StudioPaths.IncomingDirectory,
                playerDir = player,
                playerDirExists = player != null && Directory.Exists(player),
                hasCatalog = File.Exists(StudioPaths.TypeCatalogFile),
                schema = LevelDocument.CurrentSchema,
            });
        }

        void HandleCatalog(HttpListenerContext context)
        {
            string file = StudioPaths.TypeCatalogFile;
            if (File.Exists(file))
            {
                SendText(context, 200, "application/json; charset=utf-8", File.ReadAllText(file, Encoding.UTF8));
                return;
            }

            // 没导出过类型目录：给一份内置默认，够画 kind 模式的关（参数全写进 JSON，不需要资产）
            LevelTypeCatalog catalog = new LevelTypeCatalog();
            CellKind[] kinds =
            {
                CellKind.Wall, CellKind.Emitter, CellKind.Receiver,
                CellKind.Splitter, CellKind.NotGate, CellKind.Filter, CellKind.DelayGate,
            };

            foreach (CellKind kind in kinds)
            {
                EditableElement defaults = EditableElement.CreateDefault(kind, new GridPoint(0, 0));
                catalog.types.Add(new LevelTypeSpec
                {
                    key = string.Empty,
                    kind = kind.ToString(),
                    label = kind.ToString(),
                    hint = defaults.Summary(),
                    dir = defaults.PrimaryDir.ToString(),
                    dir2 = defaults.SecondaryDir.ToString(),
                    sequence = defaults.Sequence,
                    count = defaults.Count,
                    pass = defaults.PassValue,
                    delay = defaults.DelayTicks,
                });
            }

            SendJson(context, catalog);
        }

        void HandleLevels(HttpListenerContext context)
        {
            string where = context.Request.QueryString["where"] ?? "incoming";
            if (!TryResolveDirectory(where, out string directory) || directory == null || !Directory.Exists(directory))
            {
                SendJson(context, new { where = where, missing = true, files = Array.Empty<object>() });
                return;
            }

            List<object> files = new List<object>();
            foreach (string file in Directory.GetFiles(directory, "*.json"))
            {
                FileInfo info = new FileInfo(file);

                // Unity 选关面板显示的是 JSON 里的 displayName，而 Studio 列表以前只认文件名 ——
                // 于是「文件叫 MY_LEVEL_02_01、游戏里叫 111」这种不一致，在作者眼里是隐形的。
                // 现在反过来了：名字是唯一身份，expectedName 是按规则该有的文件名，
                // 对不上就说明这份文件不是本编辑器存出来的（或被人手工改过名）。
                string displayName = ReadDisplayName(file);

                files.Add(new
                {
                    name = Path.GetFileNameWithoutExtension(file),
                    file = Path.GetFileName(file),
                    displayName = displayName,
                    expectedName = string.IsNullOrEmpty(displayName) ? null : LevelNaming.BaseNameOf(displayName),

                    size = info.Length,
                    modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                });
            }

            SendJson(context, new { where = where, directory = directory, missing = false, files = files.ToArray() });
        }

        void HandleLevel(HttpListenerContext context)
        {
            string where = context.Request.QueryString["where"] ?? "incoming";
            string name = context.Request.QueryString["name"] ?? string.Empty;

            if (!TryResolveDirectory(where, out string directory) || directory == null || !Directory.Exists(directory))
            {
                SendJson(context, new { error = "目录不存在：" + where });
                return;
            }

            string file = Path.Combine(directory, SafeFileName(name) + ".json");
            if (!File.Exists(file))
            {
                SendJson(context, new { error = "找不到文件：" + Path.GetFileName(file) });
                return;
            }

            SendText(context, 200, "application/json; charset=utf-8", File.ReadAllText(file, Encoding.UTF8));
        }

        // ------------------------------------------------------------------ 写接口

        void HandleCheck(HttpListenerContext context)
        {
            string body = ReadBody(context);

            LevelDocument document;
            try
            {
                document = JsonSerializer.Deserialize<LevelDocument>(body, ReadOptions);
            }
            catch (JsonException exception)
            {
                // 这两条早退走 error 通道：它们是"接口收不了这份东西"，不是"检查结论"。
                // 以前混在 pass/report 里返回，前端会把它当成一次正常校验显示绿框。
                SendJson(context, new { error = "JSON 语法错误：" + exception.Message });
                return;
            }

            if (document == null)
            {
                SendJson(context, new { error = "JSON 顶层必须是一个 { } 对象" });
                return;
            }

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, LoadCatalog(), problems);

            // 跑的就是 Unity 里那一份规则：LevelCheck + BitFlowSimulation
            string report = LevelCheck.Run(definition, out bool success);

            // 「预设布局跑不通」和「这一关有毛病」是两件事，必须分开报：
            // - 有手牌时，预设布局本来就该跑不通（留给玩家补），跑通了才是"手牌是摆设"；
            // - 判断这一关好不好的唯一依据，是存盘时同一份 RunForSave 的六条规则。
            // 以前只把上面的 success 丢给前端，前端写成「✗ 不可解」——
            // 一个设计正确的关会被作者当成坏关。
            //
            // RunForSave 内部会再跑一次模拟；这个量级（一次几百 tick）是亚毫秒的，
            // 换来 report 和六条规则各归其位，不值得为它做缓存。
            List<string> saveProblems = LevelCheck.RunForSave(definition);

            SendJson(context, new
            {
                pass = success,
                report = report,
                problems = problems.ToArray(),
                saveProblems = saveProblems.ToArray(),
                // 「有手牌」这个事实由后端说了算：前端自己去数 doc.supplies 会算错
                // —— 比如 count 为 0 的条目，导入时就已被丢掉，前端手上那份还留着。
                hasSupplies = definition.HasSupplies,
                elementCount = definition.Elements.Count,
            });
        }

        void HandleSave(HttpListenerContext context)
        {
            string body = ReadBody(context);

            SaveRequest request;
            try
            {
                request = JsonSerializer.Deserialize<SaveRequest>(body, ReadOptions);
            }
            catch (JsonException exception)
            {
                SendJson(context, new { ok = false, error = "请求格式不对：" + exception.Message });
                return;
            }

            if (request == null || string.IsNullOrEmpty(request.json))
            {
                SendJson(context, new { ok = false, error = "没有收到关卡内容" });
                return;
            }

            LevelDocument document;
            try
            {
                document = JsonSerializer.Deserialize<LevelDocument>(request.json, ReadOptions);
            }
            catch (JsonException exception)
            {
                SendJson(context, new { ok = false, error = "JSON 语法错误：" + exception.Message });
                return;
            }

            if (document == null)
            {
                SendJson(context, new { ok = false, error = "JSON 顶层必须是一个 { } 对象" });
                return;
            }

            // 落盘前先构建一遍：Build 会把「坐标越界 / 找不到类型」这类读不了的格子跳过，
            // 但它同时也会报「墙被元件盖掉」「类型目录里没有，按默认值处理」——
            // 那是**正常的编辑情况**，不是坏数据。两种混在同一个 problems 列表里，
            // 没法凭条数判断该不该拒。所以这里只收集、交给响应（/api/check 用它标红），
            // 不当存盘闸门：误拒一关正常作品，比放行一份读不出来的文件更糟 ——
            // 后者还有 LevelJsonLibrary.TryLoad 在导入时兜底，前者没有任何补救。
            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, LoadCatalog(), problems);

            // 「引擎读不了」和「设计上还差什么」是两件事，前者拦、后者只说。
            // 后者不拦是因为新建的空白关卡第一步就撞两条（没有接收器 / 没有发射器）——
            // 草稿得存得下，否则作者连半成品都留不住。
            // 规则用的是 Core 里同一份 RunForSave：游戏内工坊存盘前跑的就是它，不存在第二套。
            List<string> saveProblems = LevelCheck.RunForSave(definition);

            if (!TryResolveDirectory(request.where, out string directory) || directory == null)
            {
                SendJson(context, new { ok = false, error = "不认识的目录：" + request.where });
                return;
            }

            Directory.CreateDirectory(directory);

            // 名字只有一个来源：JSON 里的 displayName。
            // 客户端不再能指定文件名 —— 文件名由 LevelNaming 从名字派生，和游戏内工坊、选关面板、
            // 存档路径（SaveStore 按路径记通关）用的是同一条规则。这条规则就在 Core 里，
            // 本工程编的是同一份源码，不是抄来的副本。
            string display = LevelNaming.Sanitize(document.displayName);
            if (string.IsNullOrEmpty(display))
            {
                SendJson(context, new
                {
                    ok = false,
                    error = "关卡名不能为空。只能用 A-Z、0-9、- _ 和空格，最多 " + LevelNaming.MaxLength + " 个字。",
                });
                return;
            }

            string baseName = LevelNaming.BaseNameOf(display);

            // 名字撞车不许静默覆盖：游戏侧 NameTaken 也会拦 ——
            // 两个文件在选关面板上显示同一个名字，玩家根本分不清谁是谁。
            string clash = FindNameClash(directory, display, baseName, request.oldName);
            if (clash != null)
            {
                SendJson(context, new
                {
                    ok = false,
                    error = "这个名字已经有人用了：" + clash + "。换个名字再存。",
                });
                return;
            }

            string file = Path.Combine(directory, baseName + ".json");

            // 「改名」= 把原来那一份搬过去，不是再写一份新的。
            // 以前打开 MY_LEVEL_02_01.json（关卡名 111）改完存，磁盘上会多一个 111.json，
            // 原文件还留着 —— 同一关两份，谁都不知道该删哪个。
            // oldName 只认本目录里真实存在的文件，越界的名字直接当没有（见 ResolveInside）。
            string renamedFrom = null;
            string previous = ResolveInside(directory, request.oldName);
            if (previous != null && !string.Equals(previous, file, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(file))
                {
                    SendJson(context, new
                    {
                        ok = false,
                        error = "已经有一份 " + baseName + ".json，改名会盖掉它。换个名字。",
                    });
                    return;
                }

                try
                {
                    File.Move(previous, file);
                    renamedFrom = Path.GetFileNameWithoutExtension(previous);
                }
                catch (IOException exception)
                {
                    SendJson(context, new { ok = false, error = "改名失败：" + exception.Message });
                    return;
                }
            }

            // 「这次是覆盖还是在磁盘上又生出一份新的」必须说清楚。
            bool existed = File.Exists(file);

            File.WriteAllText(file, request.json, new UTF8Encoding(false));

            SendJson(context, new
            {
                ok = true,
                file = file,
                name = baseName,
                displayName = display,
                existed = existed,
                renamedFrom = renamedFrom,
                problems = problems.ToArray(),
                // 「这关还差什么」随存盘结果一起回去：作者存完不必再点一次「跑一遍真引擎」
                // 就知道自己还欠几笔。前端只转述，不据此拒绝保存。
                saveProblems = saveProblems.ToArray(),
            });
        }

        void HandleOpenFolder(HttpListenerContext context)
        {
            string body = ReadBody(context);

            string where = "incoming";
            try
            {
                OpenRequest request = JsonSerializer.Deserialize<OpenRequest>(body, ReadOptions);
                if (request != null && !string.IsNullOrEmpty(request.where)) where = request.where;
            }
            catch (JsonException)
            {
            }

            TryResolveDirectory(where, out string directory);
            if (directory == null)
            {
                SendJson(context, new { ok = false, error = "不认识的目录：" + where });
                return;
            }

            Directory.CreateDirectory(directory);
            bool opened = TryOpenDirectory(directory);
            SendJson(context, new { ok = opened, directory = directory });
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>
        /// 读一份关卡的 displayName，只为列表显示。
        ///
        /// 读不通就返回 null，<b>绝不抛</b>：半坏的 JSON 也得能在列表里被看见 ——
        /// 作者得先看到它，才谈得上去修它；一个坏文件不该让整个列表变成 500。
        /// </summary>
        static string ReadDisplayName(string file)
        {
            try
            {
                LevelDocument document = JsonSerializer.Deserialize<LevelDocument>(
                    File.ReadAllText(file, Encoding.UTF8), ReadOptions);

                return document == null ? null : document.displayName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 这个名字是否已被<b>别的</b>文件占了。对齐游戏内 <c>BitFlowWorkshop.NameTaken</c>：
        /// 既比派生出来的文件名，也比文件里写的 displayName —— 只要有一项撞上，
        /// 两个关卡在选关面板上就会看着一模一样。
        ///
        /// 例外：目标文件名和 displayName 都对得上，那就是同一关（覆盖自己），放行。
        /// </summary>
        static string FindNameClash(string directory, string display, string baseName, string oldName)
        {
            string self = ResolveInside(directory, oldName);

            foreach (string other in Directory.GetFiles(directory, "*.json"))
            {
                string otherFull = Path.GetFullPath(other);
                if (self != null && string.Equals(otherFull, self, StringComparison.OrdinalIgnoreCase)) continue;

                bool sameName = string.Equals(LevelNaming.Sanitize(ReadDisplayName(other)), display,
                    StringComparison.OrdinalIgnoreCase);
                bool sameFile = string.Equals(Path.GetFileNameWithoutExtension(other), baseName,
                    StringComparison.OrdinalIgnoreCase);

                // 完全同一关（文件名与名字都对得上）：允许覆盖
                if (sameName && sameFile) continue;

                if (sameName || sameFile) return Path.GetFileName(other);
            }

            return null;
        }

        /// <summary>
        /// 把请求里的文件名解析成目录内的真实路径。越出目录、或文件不存在，一律返回 null。
        ///
        /// 调用方据此直接跳过「改名」，而不是拿着一个 <c>../</c> 的路径去 <c>File.Move</c>。
        /// <see cref="SafeFileName"/> 已经把分隔符换成了下划线，这里的 StartsWith 是第二道闸。
        /// </summary>
        static string ResolveInside(string directory, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                string stem = SafeFileName(name.Trim());
                // 前端给的是不带扩展名的基名；手工调接口的人常带着 .json 一起发，别因此找不到文件
                if (stem.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 5);

                string full = Path.GetFullPath(Path.Combine(directory, stem + ".json"));
                string inside = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!full.StartsWith(inside + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;

                return File.Exists(full) ? full : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        LevelTypeCatalog LoadCatalog()
        {
            string file = StudioPaths.TypeCatalogFile;
            if (!File.Exists(file)) return null;

            try
            {
                return JsonSerializer.Deserialize<LevelTypeCatalog>(File.ReadAllText(file, Encoding.UTF8), ReadOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        bool TryResolveDirectory(string where, out string directory)
        {
            switch ((where ?? "incoming").ToLowerInvariant())
            {
                case "exported":
                    directory = StudioPaths.ExportedDirectory;
                    return true;

                case "incoming":
                    directory = StudioPaths.IncomingDirectory;
                    return true;

                case "player":
                    directory = StudioPaths.PlayerLevelsDirectory;
                    return directory != null;

                default:
                    directory = null;
                    return false;
            }
        }

        static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            StringBuilder builder = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (char c in name.Trim())
            {
                bool bad = Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || c == ':';
                builder.Append(bad ? '_' : c);
            }

            return builder.ToString().Trim();
        }

        static string ReadBody(HttpListenerContext context)
        {
            using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        void SendJson(HttpListenerContext context, object payload)
        {
            SendText(context, 200, "application/json; charset=utf-8",
                JsonSerializer.Serialize(payload, WriteOptions));
        }

        static void SendText(HttpListenerContext context, int status, string contentType, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);

            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        static void TrySend(HttpListenerContext context, int status, string contentType, string text)
        {
            try
            {
                SendText(context, status, contentType, text);
            }
            catch (Exception)
            {
            }
        }

        void ServeStatic(HttpListenerContext context, string path)
        {
            string relative = path == "/" || path.Length == 0 ? "index.html" : path.TrimStart('/');
            relative = relative.Replace('/', Path.DirectorySeparatorChar);

            string file = Path.GetFullPath(Path.Combine(webRoot, relative));
            string rootFull = Path.GetFullPath(webRoot);

            // 防目录穿越：只要真实路径不在 wwwroot 里就拒绝
            if (!file.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            {
                SendText(context, 404, "text/plain; charset=utf-8", "404 " + relative);
                return;
            }

            SendText(context, 200, ContentTypeOf(file), File.ReadAllText(file, Encoding.UTF8));
        }

        static string ContentTypeOf(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                default: return "application/octet-stream";
            }
        }

        static bool TryOpenDirectory(string directory)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo("open", "\"" + directory + "\"") { UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdg-open", "\"" + directory + "\"") { UseShellExecute = false });
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>POST /api/save 的请求体。</summary>
    public sealed class SaveRequest
    {
        public string where = "incoming";

        /// <summary>
        /// 已弃用：文件名现在由 JSON 里的 displayName 派生，客户端说的不算。
        /// 留着字段只为不让老请求直接 400。
        /// </summary>
        public string name = string.Empty;

        /// <summary>
        /// 当前打开的那份文件的文件名（不含 .json）。只用于「改名」：
        /// 名字改了就把原来那一份搬过去，而不是再写一份新的。空 = 新文档。
        /// </summary>
        public string oldName = string.Empty;

        public string json = string.Empty;
    }

    /// <summary>POST /api/open-folder 的请求体。</summary>
    public sealed class OpenRequest
    {
        public string where = "incoming";
    }
}
