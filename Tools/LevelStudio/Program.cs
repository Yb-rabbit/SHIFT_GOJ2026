using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace ShiftGo.LevelStudio
{
    /// <summary>
    /// 外部关卡编辑器的本地宿主：起一个只监听 127.0.0.1 的 HttpListener，
    /// 把 wwwroot 里的单页编辑器发给浏览器，并给前端提供「用真引擎校验」的接口。
    ///
    /// 为什么要有这一层而不是纯前端：校验必须跑 Unity 里那份 BitFlowSimulation，
    /// 重写一份 JS 版迟早会和游戏跑出两个结果。这里直接编译 Core 的源文件，零漂移。
    /// </summary>
    public static class Program
    {
        const int FirstPort = 5199;
        const int LastPort = 5209;

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string root = StudioPaths.ProjectRoot;
            string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(webRoot))
            {
                webRoot = Path.Combine(StudioPaths.ToolDirectory, "wwwroot");
            }

            if (!Directory.Exists(webRoot))
            {
                Console.Error.WriteLine("找不到 wwwroot（编辑器界面文件）。");
                return 2;
            }

            HttpListener listener = StartListener(out int port);
            if (listener == null)
            {
                Console.Error.WriteLine("端口 " + FirstPort + "-" + LastPort + " 都被占用，起不来。");
                return 3;
            }

            string url = "http://127.0.0.1:" + port + "/";
            Console.WriteLine();
            Console.WriteLine("  SHIFT GO 关卡编辑器");
            Console.WriteLine("  " + url);
            Console.WriteLine("  工程根目录：" + root);
            Console.WriteLine("  （关掉这个窗口 = 关闭编辑器）");
            Console.WriteLine();

            if (!HasFlag(args, "--no-browser")) OpenBrowser(url);

            Api api = new Api(root, webRoot);

            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                ThreadPool.QueueUserWorkItem(_ => api.Handle(context));
            }

            return 0;
        }

        static HttpListener StartListener(out int port)
        {
            for (int candidate = FirstPort; candidate <= LastPort; candidate++)
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + candidate + "/");

                try
                {
                    listener.Start();
                    port = candidate;
                    return listener;
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                }
            }

            port = 0;
            return null;
        }

        static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                Console.WriteLine("（自动打开浏览器失败，手动访问上面的地址：" + exception.Message + "）");
            }
        }

        static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;

            foreach (string argument in args)
            {
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
