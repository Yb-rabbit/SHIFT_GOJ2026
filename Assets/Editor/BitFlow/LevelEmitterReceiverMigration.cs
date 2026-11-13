using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 一次性数据迁移：把「发射器 / 接收器」的节奏参数从<b>类型资产</b>搬到<b>关卡放置</b>上。
    ///
    /// 为什么要搬：类型资产以前既是「长什么样」又是「这一关发什么」，于是想在同一关里放两个
    /// 节奏不同的发射器就得新建变体资产（工程里堆了 E1–E5 / R1–R6）。参数落到
    /// <see cref="ElementPlacement"/> 之后，两个种类各留一份模板就够，作者在关卡编辑器里选中格子直接填。
    ///
    /// 安全性：每一步都能单独跑、单独验证。
    /// 1) 预览：只算不写，把 24 行的旧值 / 新值 / 等价结果写成报告。
    /// 2) 写入放置参数：给每个 E/R 放置打开 override 并填上<b>当前实际值</b>，
    ///    类型资产此时一个字节都没动 —— 落盘后立刻进游戏就应该和迁移前完全一致。
    /// 3) 归一模板并清理：把模板改名 / 改默认值，把旧变体的放置重指到模板，删掉冗余资产，
    ///    再刷注册表 + 导出类型目录。
    ///
    /// 等价断言的比较对象是「旧路径算出来的 <see cref="ElementDefinition"/>」（类型资产 +
    /// <see cref="ElementPlacement.ApplyOverrides(ElementDefinition)"/>）与新路径的结果，
    /// 只要任一字段不等，整批不落盘。
    /// </summary>
    public static class LevelEmitterReceiverMigration
    {
        const string EmitterFolder = "Assets/Data/Elements/Emitter";
        const string ReceiverFolder = "Assets/Data/Elements/Receiver";
        const string EmitterTemplatePath = EmitterFolder + "/Type_Emitter_E1.asset";
        const string ReceiverTemplatePath = ReceiverFolder + "/Type_Receiver_R1.asset";
        const string EmitterTemplateName = "Type_Emitter";
        const string ReceiverTemplateName = "Type_Receiver";
        const string ReportFile = LevelJsonTool.ExportedFolder + "/_migration_preview.txt";

        // 模板默认值按作者裁定（选 2）：沿用 E1 的实测参数；接收器的 targetCount 改成 4。
        const string TemplateEmitterLabel = "E";
        const string TemplateEmitterSequence = "1011";
        const int TemplateEmitterRepeat = 2;
        const Dir TemplateEmitterFacing = Dir.Right;

        const string TemplateReceiverLabel = "R";
        const string TemplateReceiverSequence = "1";
        const int TemplateReceiverCount = 4;
        const bool TemplateReceiverAnyFace = true;
        const Dir TemplateReceiverFeedFace = Dir.Down;

        /// <summary>一行预览 / 一次迁移的计划。</summary>
        sealed class Plan
        {
            public LevelSO level;
            public ElementPlacement placement;
            public bool isEmitter;
            public string oldKey;
            public string oldParams;
            public string newParams;
            public int oldRotation;
            public int newRotation;
            public bool equivalent;
            public string diff;

            // 要写进放置的值
            public string sequence;
            public int count;
            public bool acceptAnyFace;
        }

        // ------------------------------------------------------------------ 菜单

        [MenuItem("SHIFT GO/BitFlow/数据迁移/1 预览：发射器·接收器参数 → 放置", false, 60)]
        internal static void PreviewMenu()
        {
            List<Plan> plans = Collect(out string error);
            if (plans == null)
            {
                EditorUtility.DisplayDialog("数据迁移", error, "好");
                return;
            }

            string report = BuildReport(plans);
            SaveText(ReportFile, report);
            Debug.Log(report);

            int bad = CountInequivalent(plans);
            EditorUtility.DisplayDialog("数据迁移 · 预览",
                "共 " + plans.Count + " 处发射器 / 接收器放置。\n"
                    + "等价： " + (plans.Count - bad) + " 处\n"
                    + "不等价：" + bad + " 处\n\n"
                    + "明细已写到：\n" + Path.GetFullPath(ReportFile)
                    + (bad > 0 ? "\n\n有不等价项，先别看下一步。" : "\n\n可以执行第 2 步。"),
                "好");
            EditorUtility.RevealInFinder(Path.GetFullPath(ReportFile));
        }

        [MenuItem("SHIFT GO/BitFlow/数据迁移/2 写入放置参数（类型资产不动）", false, 61)]
        internal static void ApplyOverridesMenu()
        {
            List<Plan> plans = Collect(out string error);
            if (plans == null)
            {
                EditorUtility.DisplayDialog("数据迁移", error, "好");
                return;
            }

            int bad = CountInequivalent(plans);
            if (bad > 0)
            {
                EditorUtility.DisplayDialog("数据迁移",
                    "有 " + bad + " 处不等价，已中止（一个字节都没写）。\n先跑第 1 步看报告。", "好");
                return;
            }

            if (!EditorUtility.DisplayDialog("数据迁移 · 写入放置参数",
                "将给 " + plans.Count + " 处放置打开 override 并填上当前实际值。\n"
                    + "类型资产不会改动，落盘后行为应与迁移前完全一致。\n\n继续？", "写入", "取消"))
            {
                return;
            }

            string result = ApplyOverridesNow();
            Debug.Log("[BitFlow] " + result);
            EditorUtility.DisplayDialog("数据迁移", result, "好");
        }

        /// <summary>写入放置参数（不弹窗，供菜单与批处理共用）。返回摘要，失败时以 "ERROR: " 开头。</summary>
        internal static string ApplyOverridesNow()
        {
            List<Plan> plans = Collect(out string error);
            if (plans == null) return "ERROR: " + error;

            int bad = CountInequivalent(plans);
            if (bad > 0) return "ERROR: 有 " + bad + " 处不等价，一个字节都没写。";

            int levels = WritePlans(plans);
            return "已写入 " + plans.Count + " 处放置参数（" + levels + " 个关卡）。";
        }

        static int WritePlans(List<Plan> plans)
        {
            var touched = new HashSet<LevelSO>();
            foreach (Plan plan in plans)
            {
                LevelSO level = plan.level;
                if (touched.Add(level)) Undo.RecordObject(level, "迁移发射器·接收器参数");

                ElementPlacement placement = plan.placement;
                placement.rotationSteps = plan.newRotation;
                placement.overrideSequence = true;
                placement.sequence = plan.sequence;
                placement.overrideCount = true;
                placement.count = plan.count;

                if (!plan.isEmitter)
                {
                    placement.overrideFace = true;
                    placement.acceptAnyFace = plan.acceptAnyFace;
                }

                EditorUtility.SetDirty(level);
            }

            AssetDatabase.SaveAssets();
            return touched.Count;
        }

        [MenuItem("SHIFT GO/BitFlow/数据迁移/3 归一模板并清理冗余类型", false, 62)]
        internal static void NormalizeMenu()
        {
            EmitterTypeSO emitter = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterTemplatePath);
            ReceiverTypeSO receiver = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverTemplatePath);

            // 已经跑过一次时按新名字找模板，避免重复执行误报
            if (emitter == null)
                emitter = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterFolder + "/" + EmitterTemplateName + ".asset");
            if (receiver == null)
                receiver = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverFolder + "/" + ReceiverTemplateName + ".asset");

            if (emitter == null || receiver == null)
            {
                EditorUtility.DisplayDialog("数据迁移",
                    "找不到模板资产：\n" + EmitterTemplatePath + "\n" + ReceiverTemplatePath
                        + "\n\n（已经执行过第 3 步？那先看 Console 里的结果。）", "好");
                return;
            }

            if (!EditorUtility.DisplayDialog("数据迁移 · 归一模板",
                "1) 把所有发射器 / 接收器放置重指到模板\n"
                    + "2) 模板改名并设定默认值（接收器 targetCount = 4）\n"
                    + "3) 删除 E2–E5 / R2–R6 / Generated Auto* 冗余资产\n"
                    + "4) 刷新类型注册表 + 导出类型目录\n\n继续？", "执行", "取消"))
            {
                return;
            }

            string result = NormalizeNow();
            Debug.Log("[BitFlow] " + result);
            EditorUtility.DisplayDialog("数据迁移 · 归一", result, "好");
        }

        /// <summary>归一模板并清理冗余资产（不弹窗）。返回摘要，失败时以 "ERROR: " 开头。</summary>
        internal static string NormalizeNow()
        {
            EmitterTypeSO emitter = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterTemplatePath);
            ReceiverTypeSO receiver = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverTemplatePath);

            if (emitter == null)
                emitter = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterFolder + "/" + EmitterTemplateName + ".asset");
            if (receiver == null)
                receiver = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverFolder + "/" + ReceiverTemplateName + ".asset");

            if (emitter == null || receiver == null) return "ERROR: 找不到模板资产。";

            int repointed = RepointAll(emitter, receiver);
            ApplyTemplateValues(emitter, receiver);

            string error = RenameTemplate(EmitterTemplatePath, EmitterFolder, EmitterTemplateName);
            if (error == null) error = RenameTemplate(ReceiverTemplatePath, ReceiverFolder, ReceiverTemplateName);
            if (error != null) return "ERROR: 改名失败：" + error;

            emitter = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterFolder + "/" + EmitterTemplateName + ".asset");
            receiver = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverFolder + "/" + ReceiverTemplateName + ".asset");
            if (emitter == null || receiver == null) return "ERROR: 改名后找不到模板。";

            int removed = DeleteRedundant(emitter, receiver);
            int types = LevelJsonRuntimeSetup.RefreshRegistry();
            LevelTypeCatalogTool.ExportCatalog(out string catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return "重指 " + repointed + " 处，删除 " + removed + " 个冗余类型，"
                + "注册表现在 " + types + " 个类型，目录 → " + catalog;
        }


        // ------------------------------------------------------------------ 计划 / 断言

        /// <summary>
        /// 遍历全部 <see cref="LevelSO"/>，为每个发射器 / 接收器放置算一份迁移计划。
        /// 失败返回 null（<paramref name="error"/> 里是原因）。
        /// </summary>
        static List<Plan> Collect(out string error)
        {
            error = null;

            EmitterTypeSO emitterTemplate = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterTemplatePath);
            ReceiverTypeSO receiverTemplate = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverTemplatePath);

            if (emitterTemplate == null)
                emitterTemplate = AssetDatabase.LoadAssetAtPath<EmitterTypeSO>(EmitterFolder + "/" + EmitterTemplateName + ".asset");
            if (receiverTemplate == null)
                receiverTemplate = AssetDatabase.LoadAssetAtPath<ReceiverTypeSO>(ReceiverFolder + "/" + ReceiverTemplateName + ".asset");

            if (emitterTemplate == null || receiverTemplate == null)
            {
                error = "找不到模板资产（迁移前需要 Type_Emitter_E1 / Type_Receiver_R1）。";
                return null;
            }

            var plans = new List<Plan>();
            foreach (LevelSO level in LoadLevels())
            {
                foreach (ElementPlacement placement in level.placements)
                {
                    Plan plan = BuildPlan(level, placement, emitterTemplate, receiverTemplate);
                    if (plan != null) plans.Add(plan);
                }
            }

            if (plans.Count == 0)
            {
                error = "没扫到任何发射器 / 接收器放置（已经迁移过？）。";
                return null;
            }

            return plans;
        }

        static List<LevelSO> LoadLevels()
        {
            var levels = new List<LevelSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var level = AssetDatabase.LoadAssetAtPath<LevelSO>(path);
                if (level != null) levels.Add(level);
            }

            levels.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            return levels;
        }

        static Plan BuildPlan(LevelSO level, ElementPlacement placement,
            EmitterTypeSO emitterTemplate, ReceiverTypeSO receiverTemplate)
        {
            if (placement == null || placement.type == null) return null;

            var coord = new GridPoint(placement.coord.x, placement.coord.y);
            ElementDefinition oldDefinition = placement.type.CreateDefinition(coord, placement.rotationSteps, 1);
            if (oldDefinition == null) return null;

            // 旧路径的「真实行为」：类型资产 + 放置上已有的 override
            placement.ApplyOverrides(oldDefinition);

            var plan = new Plan
            {
                level = level,
                placement = placement,
                oldKey = placement.type.name,
                oldRotation = placement.rotationSteps,
            };

            ElementDefinition candidate;

            if (oldDefinition is EmitterDefinition oldEmitter && placement.type is EmitterTypeSO)
            {
                plan.isEmitter = true;
                plan.sequence = oldEmitter.Sequence;
                plan.count = oldEmitter.EmitCount;
                plan.newRotation = Mod((int)oldEmitter.Facing - (int)emitterTemplate.facing, 4);

                ElementPlacement probe = Probe(emitterTemplate, placement.coord, plan.newRotation,
                    plan.sequence, plan.count, true, true);
                candidate = emitterTemplate.CreateDefinition(coord, plan.newRotation, 1);
                probe.ApplyOverrides(candidate);

                var built = (EmitterDefinition)candidate;
                plan.oldParams = "SEQ " + oldEmitter.Sequence + " / EMIT " + oldEmitter.EmitCount
                    + " / FACE " + oldEmitter.Facing + " / ROT " + plan.oldRotation;
                plan.newParams = "SEQ " + plan.sequence + " / EMIT " + plan.count
                    + " / FACE " + built.Facing + " / ROT " + plan.newRotation;
            }
            else if (oldDefinition is ReceiverDefinition oldReceiver && placement.type is ReceiverTypeSO)
            {
                plan.isEmitter = false;
                plan.sequence = oldReceiver.TargetSequence;
                plan.count = oldReceiver.TargetCount;
                plan.acceptAnyFace = oldReceiver.AcceptAnyFace;
                plan.newRotation = Mod((int)oldReceiver.FeedFace - (int)receiverTemplate.feedFace, 4);

                ElementPlacement probe = Probe(receiverTemplate, placement.coord, plan.newRotation,
                    plan.sequence, plan.count, plan.acceptAnyFace, false);
                candidate = receiverTemplate.CreateDefinition(coord, plan.newRotation, 1);
                probe.ApplyOverrides(candidate);

                var built = (ReceiverDefinition)candidate;
                plan.oldParams = "SEQ " + oldReceiver.TargetSequence + " / NEED " + oldReceiver.TargetCount
                    + " / FACE " + oldReceiver.FeedFace + (oldReceiver.AcceptAnyFace ? " ANY" : " STRICT")
                    + " / ROT " + plan.oldRotation;
                plan.newParams = "SEQ " + plan.sequence + " / NEED " + plan.count
                    + " / FACE " + built.FeedFace + (built.AcceptAnyFace ? " ANY" : " STRICT")
                    + " / ROT " + plan.newRotation;
            }
            else
            {
                return null;
            }

            plan.equivalent = SameDefinition(oldDefinition, candidate, out plan.diff);
            return plan;
        }

        /// <summary>构造一份「落盘后会长成这样」的临时放置，走和 <see cref="LevelSO.ToDefinition"/> 完全相同的路径。</summary>
        static ElementPlacement Probe(ElementTypeSO type, Vector2Int coord, int rotationSteps,
            string sequence, int count, bool acceptAnyFace, bool isEmitter)
        {
            return new ElementPlacement
            {
                type = type,
                coord = coord,
                rotationSteps = rotationSteps,
                overrideSequence = true,
                sequence = sequence,
                overrideCount = true,
                count = count,
                overrideFace = !isEmitter,
                acceptAnyFace = acceptAnyFace,
            };
        }

        static bool SameDefinition(ElementDefinition oldDefinition, ElementDefinition candidate, out string diff)
        {
            var notes = new List<string>();

            if (oldDefinition.Kind != candidate.Kind)
                notes.Add("Kind " + oldDefinition.Kind + "→" + candidate.Kind);

            if (oldDefinition is EmitterDefinition oldEmitter && candidate is EmitterDefinition newEmitter)
            {
                if (oldEmitter.Facing != newEmitter.Facing) notes.Add("Facing " + oldEmitter.Facing + "→" + newEmitter.Facing);
                if (oldEmitter.Sequence != newEmitter.Sequence) notes.Add("Sequence " + oldEmitter.Sequence + "→" + newEmitter.Sequence);
                if (oldEmitter.EmitCount != newEmitter.EmitCount) notes.Add("EmitCount " + oldEmitter.EmitCount + "→" + newEmitter.EmitCount);
            }
            else if (oldDefinition is ReceiverDefinition oldReceiver && candidate is ReceiverDefinition newReceiver)
            {
                if (oldReceiver.FeedFace != newReceiver.FeedFace) notes.Add("FeedFace " + oldReceiver.FeedFace + "→" + newReceiver.FeedFace);
                if (oldReceiver.AcceptAnyFace != newReceiver.AcceptAnyFace) notes.Add("AcceptAnyFace " + oldReceiver.AcceptAnyFace + "→" + newReceiver.AcceptAnyFace);
                if (oldReceiver.TargetSequence != newReceiver.TargetSequence) notes.Add("TargetSequence " + oldReceiver.TargetSequence + "→" + newReceiver.TargetSequence);
                if (oldReceiver.TargetCount != newReceiver.TargetCount) notes.Add("TargetCount " + oldReceiver.TargetCount + "→" + newReceiver.TargetCount);
            }
            else if (notes.Count == 0)
            {
                notes.Add("定义类型不一致（" + oldDefinition.GetType().Name + " / " + candidate.GetType().Name + "）");
            }

            diff = string.Join("; ", notes);
            return notes.Count == 0;
        }

        static int Mod(int value, int modulus)
        {
            int remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }

        static int CountInequivalent(List<Plan> plans)
        {
            int count = 0;
            foreach (Plan plan in plans)
            {
                if (!plan.equivalent) count++;
            }
            return count;
        }

        static string BuildReport(List<Plan> plans)
        {
            var builder = new StringBuilder();
            builder.AppendLine("BitFlow 发射器 / 接收器参数迁移预览");
            builder.AppendLine("生成时间：" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("模板：发射器沿用它原本的参数，接收器 targetCount = " + TemplateReceiverCount);
            builder.AppendLine();
            builder.AppendLine("序号 | 关卡 | 格 | 旧类型 | 旧参数 | 新参数 | rotationSteps | 等价");
            builder.AppendLine("-----|------|----|--------|--------|--------|---------------|-----");

            int index = 0;
            foreach (Plan plan in plans)
            {
                index++;
                builder.Append(index).Append(" | ")
                    .Append(plan.level.name).Append(" | ")
                    .Append("(").Append(plan.placement.coord.x).Append(",").Append(plan.placement.coord.y).Append(") | ")
                    .Append(plan.oldKey).Append(" | ")
                    .Append(plan.oldParams).Append(" | ")
                    .Append(plan.newParams).Append(" | ")
                    .Append(plan.oldRotation).Append(" → ").Append(plan.newRotation).Append(" | ")
                    .Append(plan.equivalent ? "OK" : "DIFF: " + plan.diff)
                    .AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("合计 " + plans.Count + " 处，等价 " + (plans.Count - CountInequivalent(plans))
                + " 处，不等价 " + CountInequivalent(plans) + " 处。");
            return builder.ToString();
        }

        static void SaveText(string relativePath, string text)
        {
            string full = Path.GetFullPath(relativePath);
            string directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ 归一

        /// <summary>把旧变体（E2–E5 / R2–R6）的放置全部指向模板，返回改了几处。</summary>
        static int RepointAll(EmitterTypeSO emitterTemplate, ReceiverTypeSO receiverTemplate)
        {
            int count = 0;
            foreach (LevelSO level in LoadLevels())
            {
                bool dirty = false;

                foreach (ElementPlacement placement in level.placements)
                {
                    if (placement == null || placement.type == null) continue;

                    bool emitter = placement.type is EmitterTypeSO && placement.type != emitterTemplate;
                    bool receiver = placement.type is ReceiverTypeSO && placement.type != receiverTemplate;
                    if (!emitter && !receiver) continue;

                    // 第一次改动这个关卡前记一次快照（placement 是普通类，只能整份 SO 记录）
                    if (!dirty) Undo.RecordObject(level, "重指发射器·接收器模板");

                    placement.type = emitter ? (ElementTypeSO)emitterTemplate : receiverTemplate;
                    count++;
                    dirty = true;
                }

                if (dirty) EditorUtility.SetDirty(level);
            }

            AssetDatabase.SaveAssets();
            return count;
        }

        /// <summary>模板的默认值（新建放置时的初值；已有关卡靠放置上的 override，不受影响）。</summary>
        static void ApplyTemplateValues(EmitterTypeSO emitter, ReceiverTypeSO receiver)
        {
            Undo.RecordObject(emitter, "归一发射器模板");
            emitter.label = TemplateEmitterLabel;
            emitter.sequence = TemplateEmitterSequence;
            emitter.repeatCount = TemplateEmitterRepeat;
            emitter.facing = TemplateEmitterFacing;
            EditorUtility.SetDirty(emitter);

            Undo.RecordObject(receiver, "归一接收器模板");
            receiver.label = TemplateReceiverLabel;
            receiver.acceptAnyFace = TemplateReceiverAnyFace;
            receiver.feedFace = TemplateReceiverFeedFace;
            receiver.targetSequence = TemplateReceiverSequence;
            receiver.targetCount = TemplateReceiverCount;
            EditorUtility.SetDirty(receiver);

            AssetDatabase.SaveAssets();
        }

        /// <summary>改名（保留 guid，所有引用自动跟着走）。已经是目标名字时返回 null。</summary>
        static string RenameTemplate(string path, string folder, string newName)
        {
            var type = AssetDatabase.LoadAssetAtPath<ElementTypeSO>(path);
            if (type == null)
            {
                // 重复执行时原路径已经不存在，说明上一轮就改好了
                return AssetDatabase.LoadAssetAtPath<ElementTypeSO>(folder + "/" + newName + ".asset") != null
                    ? null
                    : "找不到：" + path;
            }

            if (type.name == newName) return null;

            string error = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(error)) return error;

            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ElementTypeSO>(folder + "/" + newName + ".asset") == null
                ? "改名后找不到：" + folder + "/" + newName + ".asset"
                : null;
        }

        /// <summary>删掉模板之外的所有发射器 / 接收器类型资产，顺带清掉空掉的 Generated 目录。</summary>
        static int DeleteRedundant(EmitterTypeSO emitterTemplate, ReceiverTypeSO receiverTemplate)
        {
            var doomed = new List<string>();

            foreach (ElementTypeSO type in LevelImportTool.LoadTypePool())
            {
                if (type == null || type == emitterTemplate || type == receiverTemplate) continue;

                CellKind kind = LevelTypeRegistrySO.KindOf(type);

                // BUFFER 借墙的形态，Kind 是 Wall，不会落进这里
                if (kind != CellKind.Emitter && kind != CellKind.Receiver) continue;

                string path = AssetDatabase.GetAssetPath(type);
                if (!string.IsNullOrEmpty(path)) doomed.Add(path);
            }

            foreach (string path in doomed)
            {
                Debug.Log("[BitFlow] 删除冗余类型：" + path);
                AssetDatabase.DeleteAsset(path);
            }

            const string generated = "Assets/Data/Elements/Generated";
            if (AssetDatabase.IsValidFolder(generated) && Directory.GetFiles(generated).Length == 0
                && Directory.GetDirectories(generated).Length == 0)
            {
                AssetDatabase.DeleteAsset(generated);
            }

            AssetDatabase.SaveAssets();
            return doomed.Count;
        }
    }
}
