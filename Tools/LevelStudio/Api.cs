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
                    case "/api/check":
                    case "/api/solve": HandleCheck(context); return;
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
                CellKind.Splitter, CellKind.NotGate, CellKind.Filter,
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
                files.Add(new
                {
                    name = Path.GetFileNameWithoutExtension(file),
                    file = Path.GetFileName(file),
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
                SendJson(context, new { pass = false, report = "JSON 语法错误：" + exception.Message, problems = Array.Empty<string>() });
                return;
            }

            if (document == null)
            {
                SendJson(context, new { pass = false, report = "JSON 顶层必须是一个 { } 对象", problems = Array.Empty<string>() });
                return;
            }

            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(document, LoadCatalog(), problems);

            // 跑的就是 Unity 里那一份规则：LevelCheck + BitFlowSimulation
            string report = LevelCheck.Run(definition, out bool success);

            SendJson(context, new
            {
                pass = success,
                report = report,
                problems = problems.ToArray(),
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

            // 落盘前先构建一遍：类型/坐标有硬伤就别写文件，免得游戏侧读到半坏的关
            List<string> problems = new List<string>();
            LevelDocumentBuilder.Build(document, LoadCatalog(), problems);

            if (!TryResolveDirectory(request.where, out string directory) || directory == null)
            {
                SendJson(context, new { ok = false, error = "不认识的目录：" + request.where });
                return;
            }

            Directory.CreateDirectory(directory);

            string name = SafeFileName(string.IsNullOrEmpty(request.name) ? document.displayName : request.name);
            if (string.IsNullOrEmpty(name)) name = "level";

            string file = Path.Combine(directory, name + ".json");
            File.WriteAllText(file, request.json, new UTF8Encoding(false));

            SendJson(context, new { ok = true, file = file, problems = problems.ToArray() });
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
        public string name = string.Empty;
        public string json = string.Empty;
    }

    /// <summary>POST /api/open-folder 的请求体。</summary>
    public sealed class OpenRequest
    {
        public string where = "incoming";
    }
}
