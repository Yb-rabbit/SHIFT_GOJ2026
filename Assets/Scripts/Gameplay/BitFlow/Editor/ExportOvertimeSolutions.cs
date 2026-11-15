using System.IO;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Overtime;

namespace ShiftGo.BitFlow.Editor
{
    /// <summary>
    /// 把固定 OVERTIME 题库的棋盘、槽位、发牌顺序与冲突数导出成 docs 参考文档。
    /// 文档是生成物：改题面后重跑菜单，不要手改 Markdown。
    /// </summary>
    public static class ExportOvertimeSolutions
    {
        const string RelativePath = "docs/OVERTIME_SOLUTIONS.md";

        [MenuItem("Tools/BitFlow/Export OVERTIME Solutions")]
        public static void Export()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllText(path, OvertimeSolutionReference.BuildMarkdown());
            AssetDatabase.Refresh();
            Debug.Log("[BitFlow] OVERTIME 解决方案已导出：" + path);
        }
    }
}
