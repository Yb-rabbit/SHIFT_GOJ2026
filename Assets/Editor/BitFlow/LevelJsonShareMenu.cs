using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 发布前的分享目录自检。
    ///
    /// 为什么需要它：分享目录里的 <c>README.txt</c> 是<b>运行时</b>生成的
    /// （见 <see cref="LevelJsonLibrary.WriteShareReadmeIfMissing"/>）。早期版本的说明会把
    /// <c>D:\...</c> 这种本机绝对路径写进去，跟着关卡一起被转发就泄漏了开发机的目录结构。
    /// 现在说明只写相对描述、首行带版本戳；这个菜单负责在打包前把本机那份顺手刷新一遍，
    /// 并提醒「整个 LevelJson 文件夹不要跟 exe 一起压缩发布」。
    /// </summary>
    public static class LevelJsonShareMenu
    {
        [MenuItem("SHIFT GO/BitFlow/关卡 JSON/发布前自检：本机分享目录", false, 43)]
        static void CheckShareFolder()
        {
            if (!LevelJsonLibrary.EnsureShareFolder(out string error))
            {
                EditorUtility.DisplayDialog("分享目录自检", "分享目录建不出来：\n" + error, "好");
                return;
            }

            string folder = LevelJsonLibrary.ShareFolder;
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();

            StringBuilder report = new StringBuilder();
            report.AppendLine("分享目录：" + folder);
            report.AppendLine("文件数：" + files.Length);
            foreach (string file in files)
            {
                report.AppendLine("  " + Path.GetFileName(file));
            }
            report.AppendLine();
            report.AppendLine("说明已刷新到 " + LevelJsonLibrary.ReadmeStamp + "（只写相对描述，不含绝对路径）。");
            report.AppendLine("提醒：Levels 是玩家侧的中转目录，不要跟 exe 一起打包 / 压缩发布。");

            Debug.Log("[BitFlow] 分享目录自检\n" + report);

            bool reveal = EditorUtility.DisplayDialog("分享目录自检", report.ToString(), "在资源管理器里打开", "关闭");
            if (reveal) EditorUtility.RevealInFinder(folder);
        }
    }
}
