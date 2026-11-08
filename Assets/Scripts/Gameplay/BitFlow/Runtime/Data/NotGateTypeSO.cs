using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>非门类型：从任意非出口面进入即取反，并从出口方向离开。</summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/NotGate", fileName = "Type_NotGate")]
    public sealed class NotGateTypeSO : ElementTypeSO
    {
        [Header("非门")]
        [Tooltip("出口方向（基准朝向）")]
        public Dir outputDir = Dir.Right;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new NotGateDefinition
            {
                Id = id,
                Coord = coord,
                OutputDir = Rotate(outputDir, rotationSteps),
            };
        }

        public override string Describe()
        {
            return "NOT " + label + "\nINVERT  OUT " + DirArrow(outputDir);
        }
    }
}
