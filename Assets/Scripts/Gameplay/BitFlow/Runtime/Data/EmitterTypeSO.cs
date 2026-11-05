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

        [Tooltip("重复轮数，0 表示该发射器不工作")]
        [Min(0)] public int repeatCount = 1;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new EmitterDefinition
            {
                Id = id,
                Coord = coord,
                Facing = Rotate(facing, rotationSteps),
                Sequence = sequence,
                RepeatCount = repeatCount,
            };
        }

        public override string Describe()
        {
            return "发射器 " + label + "\n朝 " + DirName(facing) + "，序列 \"" + sequence + "\" ×" + repeatCount;
        }
    }
}
