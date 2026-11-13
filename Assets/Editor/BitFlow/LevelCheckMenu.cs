using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 批量自检菜单：把关卡逐关丢给模拟器跑一遍，一次性告诉你
    /// 「哪一关能通 / 卡在哪个接收器的第几位 / 发射器到底发了什么」。
    ///
    /// 为什么需要它：关卡是纯数据，改一个发射序列或接收数就可能让整关从能通变成不能通，
    /// 而 Play 一关要等动画、要点鼠标。这个菜单一次跑完所有关，一秒出结论。
    /// 它和游戏内 BUILD MODE 的试跑、玩家通关共用同一个 <see cref="BitFlowSimulation"/>，
    /// 所以这里通过 = 玩家按同一份数据也能通。
    /// </summary>
    public static class LevelCheckMenu
    {
        const string CatalogPath = "Assets/Data/Levels/LevelCatalog.asset";

        [MenuItem("SHIFT GO/BitFlow/试跑关卡表", false, 22)]
        internal static void CheckCatalog()
        {
            LevelCatalogSO catalog = AssetDatabase.LoadAssetAtPath<LevelCatalogSO>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[BitFlow] 没找到 " + CatalogPath + "，改用「试跑所有关卡（扫全盘）」");
                CheckEveryLevelOnDisk();
                return;
            }

            int nullEntries = 0;
            List<LevelSO> levels = new List<LevelSO>();
            for (int i = 0; i < catalog.levels.Count; i++)
            {
                LevelSO level = catalog.levels[i];
                if (level == null) { nullEntries++; continue; }
                levels.Add(level);
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("关卡表共 " + catalog.levels.Count + " 项（其中空引用 " + nullEntries + " 项）");

            int passed = Report(levels, builder, out int puzzles);

            builder.AppendLine("合计：" + passed + "/" + levels.Count + " 关预设布局可通"
                + (puzzles > 0 ? "（其中 " + puzzles + " 关有手牌，预设不通是预期的 —— 玩家要靠牌解）" : ""));
            if (nullEntries > 0)
            {
                builder.AppendLine("⚠ 关卡表里有 " + nullEntries + " 个空引用（删掉的关卡留下了洞），"
                    + "跑「清理关卡表空引用」可以修掉");
            }

            Debug.Log("[BitFlow] 关卡表试跑\n" + builder);
            EditorUtility.DisplayDialog("关卡表试跑", builder.ToString(), "好");
        }

        [MenuItem("SHIFT GO/BitFlow/试跑所有关卡（扫全盘）", false, 23)]
        internal static void CheckEveryLevelOnDisk()
        {
            List<LevelSO> levels = new List<LevelSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelSO"))
            {
                LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (level != null) levels.Add(level);
            }

            levels.Sort((left, right) => string.CompareOrdinal(left.name, right.name));

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("扫描到 " + levels.Count + " 个关卡资产（不看关卡表）");

            int passed = Report(levels, builder, out int puzzles);
            builder.AppendLine("合计：" + passed + "/" + levels.Count + " 关预设布局可通"
                + (puzzles > 0 ? "（其中 " + puzzles + " 关有手牌，预设不通是预期的 —— 玩家要靠牌解）" : ""));

            Debug.Log("[BitFlow] 全盘关卡试跑\n" + builder);
            EditorUtility.DisplayDialog("全盘关卡试跑", builder.ToString(), "好");
        }

        /// <summary>把关卡表里指向已删资产的那些洞吃掉（否则遍历关卡表会撞 null）。</summary>
        [MenuItem("SHIFT GO/BitFlow/清理关卡表空引用", false, 24)]
        internal static void RemoveNullEntries()
        {
            LevelCatalogSO catalog = AssetDatabase.LoadAssetAtPath<LevelCatalogSO>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[BitFlow] 没找到 " + CatalogPath);
                return;
            }

            int removed = 0;
            for (int i = catalog.levels.Count - 1; i >= 0; i--)
            {
                if (catalog.levels[i] != null) continue;
                catalog.levels.RemoveAt(i);
                removed++;
            }

            if (removed == 0)
            {
                Debug.Log("[BitFlow] 关卡表里没有空引用，不用清理");
                return;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[BitFlow] 关卡表已清掉 " + removed + " 个空引用，现在 " + catalog.levels.Count + " 关");
        }

        /// <summary>
        /// 跑一批关卡。<paramref name="puzzles"/> = 有手牌、且预设布局跑不通的关数 ——
        /// 那些不是失败（规则见 <see cref="LevelCheck.RunForSave"/>），不能算进"可通"也不该当事故报。
        /// </summary>
        static int Report(List<LevelSO> levels, StringBuilder builder, out int puzzles)
        {
            int passed = 0;
            puzzles = 0;
            foreach (LevelSO level in levels)
            {
                LevelDefinition definition = level.ToDefinition();

                string report = LevelCheck.Run(definition, level.HasSupplies, out bool success);
                if (success) passed++;
                else if (level.HasSupplies) puzzles++;

                builder.AppendLine();
                builder.AppendLine(level.name + "   " + definition.Elements.Count + " 个元件"
                    + (level.HasSupplies ? "，手牌 " + level.supplies.Count + " 摞" : "，无手牌"));
                builder.AppendLine("  " + report.Replace("\n", "\n  "));
                AppendEmitterSummary(definition, builder);
            }
            return passed;
        }

        /// <summary>顺带列出每个发射器实际会发什么 —— 作者核对"发 6 位"时最常看的就是这行。</summary>
        static void AppendEmitterSummary(LevelDefinition definition, StringBuilder builder)
        {
            foreach (ElementDefinition element in definition.Elements)
            {
                if (!(element is EmitterDefinition emitter)) continue;

                builder.Append("    E").Append(emitter.Id).Append(" 序列 \"").Append(emitter.Sequence)
                    .Append("\" 共 ").Append(emitter.EmitCount).Append(" 位 → ")
                    .AppendLine(LevelCheck.BitsText(emitter.BuildEmitOrder()));
            }
        }
    }
}