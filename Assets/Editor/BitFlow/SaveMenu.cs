using System.IO;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 存档相关的编辑器菜单：清掉本轮自测攒下的进度、把存档文件夹翻出来看看。
    ///
    /// 为什么需要它：通关会在 <c>persistentDataPath</c> 里留下记录，选关面板据此打勾。
    /// 在 Play 里自测几关之后再想验"新玩家看到的样子"，就得手动去 LocalLow 里删 json，
    /// 而路径里还带着公司名/产品名。菜单替你做这件事，顺便清掉内存里的静态缓存
    /// （不点菜单、只删文件的话，编辑器进程里那份 <see cref="SaveStore"/> 数据还在）。
    ///
    /// 注意：编辑器用的是 <c>save_editor.json</c>，和打包后的 <c>save.json</c> 分开，
    /// 所以这里清完不会影响已经发出去的版本。
    /// </summary>
    public static class SaveMenu
    {
        [MenuItem("SHIFT GO/BitFlow/清空本轮存档（通关打勾 + 元件首见）", false, 60)]
        internal static void ClearSave()
        {
            bool hadFile = File.Exists(SaveStore.FilePath);

            SaveStore.ClearAll();

            string message = hadFile
                ? "[BitFlow] 存档已清空：" + SaveStore.FilePath
                : "[BitFlow] 本来就没有存档文件，内存缓存已重置：" + SaveStore.FilePath;

            Debug.Log(message);

            // 本机跑过构建版的话，工作目录里还躺着一份打包存档 —— 编辑器不读它，
            // 但它会让「构建出来的 exe」一直打勾。这里只提醒，不擅自删别人机器的进度。
            if (File.Exists(SaveStore.PlayerFilePath))
            {
                Debug.Log("[BitFlow] 另外还有一份打包存档（编辑器不读、构建版会读）：" + SaveStore.PlayerFilePath);
            }
        }

        [MenuItem("SHIFT GO/BitFlow/在资源管理器里显示存档", false, 61)]
        internal static void RevealSave()
        {
            string path = SaveStore.FilePath;
            string folder = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Debug.Log("[BitFlow] 存档目录还不存在（这一轮还没写过存档）：" + path);
                return;
            }

            EditorUtility.RevealInFinder(File.Exists(path) ? path : folder);
        }
    }
}
