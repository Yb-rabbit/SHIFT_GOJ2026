using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// <see cref="ILevelJsonTypeResolver"/> 的<b>运行时</b>实现：把 JSON 里的 <c>type</c>（类型资产 Key）
    /// 或 <c>kind</c>（宽松简写）翻译成 <see cref="ElementTypeSO"/>。
    ///
    /// 和编辑器侧的 LevelJsonTool 的差别只有「资产从哪来」：
    /// 这边只查 <see cref="LevelTypeRegistrySO"/>（Resources 里的清单），不碰 AssetDatabase。
    /// 所以编辑器 Play、EditMode 测试、打包后的 Player 跑的是<b>同一段</b>解析代码。
    ///
    /// kind 模式的匹配规则（和编辑器侧一致）：在清单里找同种类的资产，逐个试 0~3 档旋转，
    /// 取第一个「转完朝向正好对得上 JSON」的 —— 命中就不必造变体资产，JSON 和资产名也就解耦了。
    /// </summary>
    public sealed class LevelTypeRegistryResolver : ILevelJsonTypeResolver
    {
        readonly LevelTypeRegistrySO registry;

        public LevelTypeRegistryResolver(LevelTypeRegistrySO registry)
        {
            this.registry = registry;
        }

        public ElementTypeSO Resolve(LevelJsonSerializer.LevelJsonCell cell, out int rotationSteps)
        {
            rotationSteps = 0;
            if (cell == null || registry == null) return null;

            // 精确模式：type 已经是资产 Key / 资产名，旋转档照抄
            if (!string.IsNullOrEmpty(cell.type))
            {
                ElementTypeSO exact = registry.Find(cell.type);
                if (exact == null) return null;

                rotationSteps = ((cell.rot % 4) + 4) % 4;
                return exact;
            }

            if (string.IsNullOrEmpty(cell.kind)) return null;
            if (!LevelJsonSerializer.TryParseKind(cell.kind, out CellKind kind)) return null;

            return MatchByKind(kind, cell, out rotationSteps);
        }

        public WallTypeSO ResolveWall()
        {
            return registry != null ? registry.FindWall(null) : null;
        }

        ElementTypeSO MatchByKind(CellKind kind, LevelJsonSerializer.LevelJsonCell cell, out int rotationSteps)
        {
            rotationSteps = 0;

            bool hasDir = LevelJsonSerializer.TryParseDir(cell.dir, out Dir wantedDir);
            bool hasDir2 = LevelJsonSerializer.TryParseDir(cell.dir2, out Dir wantedDir2);

            foreach (ElementTypeSO candidate in registry.OfKind(kind))
            {
                // 墙靠 wallRows 铺，不该出现在 elements 里；真写了也轮不到墙来顶
                if (candidate is WallTypeSO) continue;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    ElementDefinition definition = candidate.CreateDefinition(new GridPoint(0, 0), attempt, 0);
                    if (definition == null || definition.Kind != kind) continue;

                    EditableElement shape = EditableElement.FromDefinition(definition);
                    if (shape == null) continue;

                    if (hasDir && shape.PrimaryDir != wantedDir) continue;
                    if (hasDir2 && kind == CellKind.Splitter && shape.SecondaryDir != wantedDir2) continue;

                    rotationSteps = attempt;
                    return candidate;
                }
            }

            return null;
        }
    }
}
