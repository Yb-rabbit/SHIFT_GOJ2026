using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 延迟器类型：位从任意面进入后进本格的 FIFO 队列，比直通元件多停 <see cref="delayTicks"/> 个 tick 后
    /// 从"进入方向的对面"原样穿出。没有出口朝向，也没有旋转档位（四面等价）。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/DelayGate", fileName = "Type_DelayGate")]
    public sealed class DelayGateTypeSO : ElementTypeSO
    {
        [Header("延迟器")]
        [Tooltip("延迟 tick 数：N = 比直通元件多停 N 拍（1 = 晚 1 tick，4 = 晚 4 tick），最大 4")]
        [Range(1, 4)] public int delayTicks = 1;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new DelayGateDefinition
            {
                Id = id,
                Coord = coord,
                // 延迟器不旋转：rotationSteps 故意忽略，四档转出来的行为必须完全一样
                DelayTicks = Mathf.Clamp(delayTicks, 1, 4),
            };
        }

        public override string Describe()
        {
            return "DELAY " + label + "\nHOLD " + Mathf.Clamp(delayTicks, 1, 4) + " TICK  THROUGH";
        }
    }
}
