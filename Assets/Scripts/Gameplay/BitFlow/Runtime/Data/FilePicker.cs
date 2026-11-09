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
    /// - <b>其它平台</b>：<see cref="IsSupported"/> 为 false，调用方退回「收件箱目录」那条路。
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
        /// <param name="startFolder">初始目录（不存在就退回关卡目录）。</param>
        public static string PickJson(string title, string startFolder)
        {
            if (string.IsNullOrEmpty(title)) title = "OPEN LEVEL JSON";

            if (string.IsNullOrEmpty(startFolder) || !Directory.Exists(startFolder))
            {
                startFolder = LevelJsonLibrary.Folder;
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
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            [MarshalAs(UnmanagedType.LPWStr)] public string filter;
            [MarshalAs(UnmanagedType.LPWStr)] public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PathBufferChars)] public char[] file;
            public int maxFile;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public char[] fileTitle;
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

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        /// <summary>
        /// 弹系统打开对话框。用 <c>char[]</c> 收路径而不是 <c>string</c>：
        /// <c>string</c> 字段在 struct 里只保证「送进去」，native 写回来的内容拿不到；
        /// 定长 <c>char[]</c> 才会被 marshal 回托管侧。
        /// </summary>
        static string PickWindows(string title, string startFolder)
        {
            OpenFileName dialog = new OpenFileName();
            dialog.structSize = Marshal.SizeOf(typeof(OpenFileName));
            dialog.owner = GetActiveWindow();

            // 过滤串用 '\0' 分段、两个 '\0' 结尾。LPWStr 会按字符串长度整段拷进去，内嵌 '\0' 不会丢
            dialog.filter = "Level JSON (*.json)\0*.json\0All files (*.*)\0*.*\0\0";
            dialog.file = new char[PathBufferChars];
            dialog.maxFile = PathBufferChars;
            dialog.fileTitle = new char[256];
            dialog.maxFileTitle = 256;
            dialog.initialDir = startFolder;
            dialog.titleText = title;
            dialog.defaultExtension = "json";

            // NoChangeDir：选完不把进程的当前目录改到玩家刚才翻到的文件夹
            dialog.flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir;

            try
            {
                if (!GetOpenFileNameW(ref dialog)) return null;   // 取消 / 关掉窗口
            }
            catch (Exception exception)
            {
                // 缺 DLL / 系统太老：静默退回，玩家还能走「收件箱目录」
                UnityEngine.Debug.LogWarning("[BitFlow] 打不开文件对话框，请改用 IMPORT · FROM INBOX：" + exception.Message);
                return null;
            }

            return BufferToString(dialog.file);
        }

        /// <summary>定长字符缓冲区 → 字符串（截到第一个 '\0'）。</summary>
        static string BufferToString(char[] buffer)
        {
            if (buffer == null) return null;

            int length = Array.IndexOf(buffer, '\0');
            if (length < 0) length = buffer.Length;
            if (length <= 0) return null;

            return new string(buffer, 0, length);
        }

#endif
    }
}
