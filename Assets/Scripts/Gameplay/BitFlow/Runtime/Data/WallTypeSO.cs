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
            // 墙上本来就没有可调参数，写一行名字就够 —— 之前那句「位撞上即销毁」是在解释规则，
            // 而规则在第一次撞墙时就会被玩家看见。
            return "WALL";
        }
    }
}
