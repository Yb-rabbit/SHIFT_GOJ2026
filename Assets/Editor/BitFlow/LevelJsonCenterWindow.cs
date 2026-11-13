using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 关卡 JSON 的两个日常动作：<b>做关</b>（和 <c>Tools/LevelStudio</c> 往返）与<b>出包</b>（把关卡塞进 Resources）。
    ///
    /// 这里只有按钮，没有规则：每个按钮调的就是原来那条菜单方法，弹窗和日志一字未改。
    /// 运行时同步、注册表、关卡表试跑、存档这些低频动作仍然在 <c>SHIFT GO/BitFlow/</c> 的菜单里。
    /// </summary>
    public sealed class LevelJsonCenterWindow : EditorWindow
    {
        enum Tab { Author, Ship }

        static readonly string[] TabNames = { "做关", "出包" };

        const string TabKey = "BitFlow.LevelJsonCenter.Tab";

        [SerializeField] Tab tab = Tab.Author;

        // 只在打开窗口 / 点「刷新」时碰磁盘，OnGUI 里不读文件
        int sourceCount;
        int bundledCount;
        int incomingCount;

        Vector2 scroll;

        [MenuItem("SHIFT GO/BitFlow/关卡 JSON 中心", false, 18)]
        static void Open()
        {
            LevelJsonCenterWindow window = GetWindow<LevelJsonCenterWindow>("关卡 JSON");
            window.minSize = new Vector2(540f, 320f);
            window.Show();
        }

        void OnEnable()
        {
            tab = (Tab)Mathf.Clamp(EditorPrefs.GetInt(TabKey, (int)Tab.Author), 0, TabNames.Length - 1);
            RefreshStatus();
        }

        void OnDisable()
        {
            EditorPrefs.SetInt(TabKey, (int)tab);
        }

        void OnGUI()
        {
            DrawTabs();
            DrawStatus();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            switch (tab)
            {
                case Tab.Author: DrawAuthor(); break;
                case Tab.Ship: DrawShip(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawTabs()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                for (int i = 0; i < TabNames.Length; i++)
                {
                    bool active = (int)tab == i;
                    if (GUILayout.Toggle(active, TabNames[i], EditorStyles.toolbarButton, GUILayout.Width(72f)) && !active)
                    {
                        tab = (Tab)i;
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                {
                    RefreshStatus();
                }
            }
        }

        /// <summary>两条进包路径常驻：作者最常问的就是「我这个文件到底会不会进包」。点一下打开目录。</summary>
        void DrawStatus()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("① 作者源　" + sourceCount + " 关", EditorStyles.miniButton))
                {
                    Reveal(LevelJsonTool.RootFolder);
                }

                if (GUILayout.Button("② 内置包　" + bundledCount + " 关", EditorStyles.miniButton))
                {
                    Reveal(LevelSeedBundler.TargetFolder);
                }

                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------------ 做关

        void DrawAuthor()
        {
            EditorGUILayout.Space();

            // 和菜单 SHIFT GO/BitFlow/关卡编辑器 是同一条路：
            // 主通道就是浏览器版 LevelStudio，Unity 内的网格窗口只在 LevelSO 检视器里。
            if (GUILayout.Button("打开关卡编辑器（浏览器版）", GUILayout.Height(36f)))
            {
                LevelTypeCatalogTool.LaunchLevelStudio();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("导出到 " + LevelJsonTool.ExportedFolder, EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导出选中关卡", GUILayout.Height(26f))) LevelJsonTool.ExportSelected();
                if (GUILayout.Button("导出全部关卡", GUILayout.Height(26f))) LevelJsonTool.ExportAll();
                if (GUILayout.Button("导出空白模板", GUILayout.Height(26f))) LevelJsonTool.ExportTemplate();
            }

            if (GUILayout.Button("导出类型目录（给外部编辑器）", GUILayout.Height(24f)))
            {
                LevelTypeCatalogTool.ExportCatalogMenu();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("收进来的关", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("校验 JSON（不落盘）", GUILayout.Height(26f))) LevelJsonTool.ValidateJson();
                if (GUILayout.Button("检查 type 断链", GUILayout.Height(26f))) LevelJsonTool.CheckBrokenTypes();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导入为新关卡", GUILayout.Height(26f)))
                {
                    LevelJsonTool.ImportAsNewLevel();
                    RefreshStatus();
                }

                if (GUILayout.Button("覆盖选中关卡", GUILayout.Height(26f)))
                {
                    LevelJsonTool.OverwriteSelected();
                    RefreshStatus();
                }
            }

            if (GUILayout.Button("批量导入 incoming（" + incomingCount + " 个待导入）", GUILayout.Height(26f)))
            {
                LevelJsonTool.ImportIncomingFolder();
                RefreshStatus();
            }
        }

        // ------------------------------------------------------------------ 出包

        void DrawShip()
        {
            EditorGUILayout.Space();

            if (GUILayout.Button("生成内置关卡包", GUILayout.Height(34f)))
            {
                LevelSeedBundler.BundleMenu();
                RefreshStatus();
            }

            if (GUILayout.Button("打包前自检（关卡）", GUILayout.Height(34f)))
            {
                LevelBundlePreflight.RunMenu();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("发布前自检：本机分享目录", GUILayout.Height(26f)))
            {
                LevelJsonShareMenu.CheckShareFolder();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "exported/ 里的文件不会自动进包：要挪到 ① 或 incoming/ 再生成。",
                MessageType.Info);
        }

        // ------------------------------------------------------------------ 状态

        void RefreshStatus()
        {
            sourceCount = CountLevels(LevelJsonTool.RootFolder);
            bundledCount = CountLevels(LevelSeedBundler.TargetFolder);
            incomingCount = CountLevels(LevelJsonTool.IncomingFolder);
            Repaint();
        }

        /// <summary>数一个目录里「真算关卡」的 json —— 和同步 / 打包共用同一套保留名规则。</summary>
        static int CountLevels(string folder)
        {
            string absolute = Path.GetFullPath(folder);
            if (!Directory.Exists(absolute)) return 0;

            int count = 0;
            foreach (string file in Directory.GetFiles(absolute, "*.json"))
            {
                if (LevelSeedSources.IsNotLevel(Path.GetFileName(file))) continue;
                count++;
            }

            return count;
        }

        static void Reveal(string folder)
        {
            string absolute = Path.GetFullPath(folder);
            if (Directory.Exists(absolute)) EditorUtility.RevealInFinder(absolute);
            else Debug.LogWarning("[BitFlow] 目录还不存在：" + absolute);
        }
    }
}
