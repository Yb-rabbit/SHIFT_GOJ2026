using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>墙类型：纯占位障碍，位撞上即销毁。</summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Wall", fileName = "Type_Wall")]
    public sealed class WallTypeSO : ElementTypeSO
    {
        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new WallDefinition { Id = id, Coord = coord };
        }

        public override string Describe()
        {
            return "墙 " + label + "\n位撞上即销毁";
        }
    }
}
