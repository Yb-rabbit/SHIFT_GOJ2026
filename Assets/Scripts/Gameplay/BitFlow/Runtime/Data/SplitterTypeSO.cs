using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>L 分流器类型：两臂互成 90°，值 1 走臂 A、值 0 走臂 B。</summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Splitter", fileName = "Type_Splitter")]
    public sealed class SplitterTypeSO : ElementTypeSO
    {
        [Header("L 分流器")]
        [Tooltip("值 1 的出口臂（基准朝向）")]
        public Dir oneArm = Dir.Up;

        [Tooltip("值 0 的出口臂（基准朝向，应与 1-臂互成 90°）")]
        public Dir zeroArm = Dir.Right;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new SplitterDefinition
            {
                Id = id,
                Coord = coord,
                OneArm = Rotate(oneArm, rotationSteps),
                ZeroArm = Rotate(zeroArm, rotationSteps),
            };
        }

        public override string Describe()
        {
            return "SPLITTER " + label + "\n1 " + DirArrow(oneArm) + "   0 " + DirArrow(zeroArm);
        }
    }
}
