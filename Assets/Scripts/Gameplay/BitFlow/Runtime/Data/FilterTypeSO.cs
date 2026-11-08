using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>过滤器类型：只放行指定值，其余销毁；放行的值从箭头方向离开。</summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Filter", fileName = "Type_Filter")]
    public sealed class FilterTypeSO : ElementTypeSO
    {
        [Header("过滤器")]
        [Tooltip("箭头（出口）方向，基准朝向")]
        public Dir outputDir = Dir.Right;

        [Tooltip("允许通过的值：0 或 1")]
        public byte passValue = 1;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new FilterDefinition
            {
                Id = id,
                Coord = coord,
                OutputDir = Rotate(outputDir, rotationSteps),
                PassValue = (byte)(passValue == 0 ? 0 : 1),
            };
        }

        public override string Describe()
        {
            return "FILTER " + label + "\nPASS " + (passValue == 0 ? 0 : 1) + "  OUT " + DirArrow(outputDir);
        }
    }
}
