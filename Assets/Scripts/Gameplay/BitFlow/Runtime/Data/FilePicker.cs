using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 「从磁盘上挑一个文件」的系统对话框 —— 选关面板的 <c>IMPORT · FROM FILE</c> 用它。
    ///
    /// 为什么需要它：玩家拿到的关卡 JSON 可能躺在下载目录、聊天软件的文件夹、U 盘里，
    /// 而运行时关卡目录在 <c>AppData\LocalLow\...</c>（Windows 默认隐藏）。
    /// 只给「打开目录 + 自己把文件拖进去」等于要求玩家先会找隐藏目录。
    ///
    /// 三条实现路径，按平台选：
    /// - <b>编辑器</b>：<c>UnityEditor.EditorUtility.OpenFilePanel</c>，一行就够；
    /// - <b>Windows 独立版</b>：P/Invoke <c>comdlg32!GetOpenFileNameW</c>（系统自带的打开对话框，
    ///   不引 System.Windows.Forms，也就不需要额外的 DLL）；
    /// - <b>其它平台</b>：<see cref="IsSupported"/> 为 false，调用方退回「分享目录」那条路。
    ///
    /// 对话框是模态的：Unity 主线程会停在那里直到玩家选完或取消。这是系统对话框的固有行为，
    /// 所以只在玩家主动点按钮时才调，绝不在 Update 里调。
    /// </summary>
    public static class FilePicker
    {
        /// <summary>这个平台能不能弹系统文件对话框。</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR
                return true;
#elif UNITY_STANDALONE_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 让玩家挑一个 <c>.json</c>。<b>取消 / 不支持 / 出错</b>一律返回 null ——
        /// 调用方只需要判空，不必区分「玩家点了取消」和「系统不给我这个能力」。
        /// </summary>
        /// <param name="title">对话框标题（纯 ASCII，系统标题栏不需要像素字体）。</param>
        /// <param name="startFolder">初始目录（不存在就退回分享目录）。</param>
        public static string PickJson(string title, string startFolder)
        {
            if (string.IsNullOrEmpty(title)) title = "OPEN LEVEL JSON";

            if (string.IsNullOrEmpty(startFolder) || !Directory.Exists(startFolder))
            {
                startFolder = LevelJsonLibrary.ShareFolder;
            }

            // 分享目录也可能建不出来（装在 Program Files、文档目录被重定向等）：
            // 这时退回「我的文档」，至少让对话框有地方落脚，而不是整条 IMPORT 路都不可用。
            if (string.IsNullOrEmpty(startFolder) || !Directory.Exists(startFolder))
            {
                startFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

#if UNITY_EDITOR
            string picked = UnityEditor.EditorUtility.OpenFilePanel(title, startFolder, "json");
            return string.IsNullOrEmpty(picked) ? null : picked;
#elif UNITY_STANDALONE_WIN
            return PickWindows(title, startFolder);
#else
            return null;
#endif
        }

        // ------------------------------------------------------------------ Windows

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN

        /// <summary>OPENFILENAMEW 里路径缓冲区的字符数（MAX_PATH 的 4 倍，长路径也放得下）。</summary>
        const int PathBufferChars = 1024;

        const int OfnNoChangeDir = 0x00000008;
        const int OfnPathMustExist = 0x00000800;
        const int OfnFileMustExist = 0x00001000;

        /// <summary>
        /// Win32 <c>OPENFILENAMEW</c>。字段顺序 / 宽度必须和头文件逐字对齐 ——
        /// 结构体错一个字段，native 那边就会往错误的位置写，表现是「点了没反应」或者直接崩。
        ///
        /// <b>lpstrFile / lpstrFileTitle 必须是指针（<see cref="IntPtr"/>），不能是定长 <c>char[]</c>。</b>
        /// 用 <c>ByValArray</c> 会把上千个字符内联进结构体：<c>lStructSize</c> 变成 1408（正确值 152 / x64），
        /// <c>GetOpenFileNameW</c> 直接以 <c>CDERR_STRUCTSIZE</c> 失败，玩家看到的就是「点了没反应」。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;

            /// <summary>lpstrFilter：双 <c>'\0'</c> 结尾的过滤器表，见 <see cref="AllocDoubleNullString"/>。</summary>
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;

            /// <summary>lpstrFile：指向调用方分配的路径缓冲区（见 <see cref="PickWindows"/>）。</summary>
            public IntPtr file;
            public int maxFile;

            /// <summary>lpstrFileTitle：这里不需要短文件名，传 <see cref="IntPtr.Zero"/>、maxFileTitle = 0。</summary>
            public IntPtr fileTitle;
            public int maxFileTitle;

            [MarshalAs(UnmanagedType.LPWStr)] public string initialDir;
            [MarshalAs(UnmanagedType.LPWStr)] public string titleText;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            [MarshalAs(UnmanagedType.LPWStr)] public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            [MarshalAs(UnmanagedType.LPWStr)] public string templateName;
            public IntPtr reservedPointer;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW(ref OpenFileName openFileName);

        /// <summary>系统对话框失败时给原因；返回 0 表示玩家主动取消。</summary>
        [DllImport("comdlg32.dll")]
        static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        /// <summary>
        /// 弹系统打开对话框。路径缓冲区用 <see cref="Marshal.AllocHGlobal"/> 自己分配：
        /// 结构体里只放指针，布局才和原生 <c>OPENFILENAMEW</c> 一致；
        /// 系统写回来的内容在调用后从缓冲区读成字符串，最后在 <c>finally</c> 里释放。
        /// </summary>
        static string PickWindows(string title, string startFolder)
        {
            OpenFileName dialog = new OpenFileName();
            IntPtr filter = IntPtr.Zero;
            IntPtr fileBuffer = IntPtr.Zero;

            try
            {
                dialog.structSize = Marshal.SizeOf(typeof(OpenFileName));
                dialog.owner = GetActiveWindow();

                // 过滤器表：显示名 '\0' 通配符 '\0' …… 两个 '\0' 收尾。
                // 手工分配缓冲区，内嵌 '\0' 才不会被任何脚本后端截断。
                filter = AllocDoubleNullString("Level JSON (*.json)\0*.json\0All files (*.*)\0*.*\0");
                dialog.filter = filter;

                // 路径缓冲区：系统会往这里写玩家选中的路径（UTF-16）
                fileBuffer = Marshal.AllocHGlobal(PathBufferChars * sizeof(char));
                dialog.file = fileBuffer;
                dialog.maxFile = PathBufferChars;

                // 不需要短文件名：lpstrFileTitle 传 NULL 是文档允许的写法
                dialog.fileTitle = IntPtr.Zero;
                dialog.maxFileTitle = 0;

                dialog.initialDir = startFolder;
                dialog.titleText = title;
                dialog.defaultExtension = "json";

                // NoChangeDir：选完不把进程的当前目录改到玩家刚才翻到的文件夹
                dialog.flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir;

                // 系统只在玩家真的选了文件时才写缓冲区，必须先清零，否则会读到垃圾
                ZeroBuffer(fileBuffer, PathBufferChars);

                if (!GetOpenFileNameW(ref dialog))
                {
                    int code = CommDlgExtendedError();
                    if (code == 0) return null;   // 玩家点了取消 / 关掉窗口，不需要吵

                    UnityEngine.Debug.LogWarning(
                        "[BitFlow] 打不开文件对话框（CommDlgExtendedError=0x" + code.ToString("X4")
                        + "），请改用 IMPORT · FROM SHARE FOLDER。");
                    return null;
                }

                return Marshal.PtrToStringUni(fileBuffer);
            }
            catch (Exception exception)
            {
                // 缺 DLL / 系统太老：退回分享目录那条路，但要把原因写进 Console
                UnityEngine.Debug.LogWarning("[BitFlow] 打不开文件对话框，请改用 IMPORT · FROM SHARE FOLDER：" + exception.Message);
                return null;
            }
            finally
            {
                if (filter != IntPtr.Zero) Marshal.FreeHGlobal(filter);
                if (fileBuffer != IntPtr.Zero) Marshal.FreeHGlobal(fileBuffer);
            }
        }

        /// <summary>
        /// 分配一段以 <c>'\0'</c> 结尾的 UTF-16 非托管字符串，<b>保留内嵌的 <c>'\0'</c></b>
        /// （<c>OPENFILENAME</c> 的过滤器表正是靠内嵌 <c>'\0'</c> 分隔每一项）。
        /// 调用方负责 <see cref="Marshal.FreeHGlobal"/>。
        /// </summary>
        static IntPtr AllocDoubleNullString(string value)
        {
            char[] chars = value.ToCharArray();
            int bytes = (chars.Length + 1) * sizeof(char);

            IntPtr pointer = Marshal.AllocHGlobal(bytes);
            Marshal.Copy(chars, 0, pointer, chars.Length);
            Marshal.WriteInt16(pointer, chars.Length * sizeof(char), 0);
            return pointer;
        }

        /// <summary>把一段非托管缓冲区清零（<see cref="Marshal.AllocHGlobal(int)"/> 不保证是 0）。</summary>
        static void ZeroBuffer(IntPtr buffer, int charCount)
        {
            int bytes = charCount * sizeof(char);
            Marshal.Copy(new byte[bytes], 0, buffer, bytes);
        }

#endif
    }
}
