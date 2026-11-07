using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>发射器类型：固定单向吐位，序列按"末字符先弹"的栈序输出。</summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Emitter", fileName = "Type_Emitter")]
    public sealed class EmitterTypeSO : ElementTypeSO
    {
        [Header("发射器")]
        [Tooltip("吐位方向（基准朝向，可再被关卡里的旋转态旋转）")]
        public Dir facing = Dir.Right;

        [Tooltip("位序列，只会取其中的 0 与 1；“末字符先弹”。例：1011 → 1,1,0,1")]
        public string sequence = "1";

        [Tooltip("重复轮数：× 序列有效位数 = 发射总位数（1011 ×2 = 8 位）。0 表示该发射器不工作。" +
                 "关卡里想要 6 位这种非整除长度，在关卡编辑器里选中格子直接填「发射数」，不用改这里")]
        [Min(0)] public int repeatCount = 1;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            int bits = CountBits(sequence);
            return new EmitterDefinition
            {
                Id = id,
                Coord = coord,
                Facing = Rotate(facing, rotationSteps),
                Sequence = sequence,
                // 资产上填的是"轮数"，这里换算成模拟器要的"总位数"（1011 ×2 → 8 位）。
                // 换算而不是改字段名：已经做好的类型资产不需要迁移，行为逐位不变。
                EmitCount = repeatCount * bits,
            };
        }

        /// <summary>序列里有效 0/1 的个数（其他字符会被忽略，和 EmitterDefinition.BuildCycle 保持一致）。</summary>
        internal static int CountBits(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '0' || value[i] == '1') count++;
            }
            return count;
        }

        public override string Describe()
        {
            return "发射器 " + label + "\n朝 " + DirName(facing) + "，序列 \"" + sequence + "\" ×" + repeatCount
                + "（共 " + repeatCount * CountBits(sequence) + " 位）";
        }
    }
}
