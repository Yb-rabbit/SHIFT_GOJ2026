using System;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 一个元件在关卡里的摆放：引用类型配置 + 坐标 + 旋转态。
    /// 这样同一个 EmitterTypeSO 能被多个关卡复用，关卡数据也不会把坐标写死在类型资产里。
    /// </summary>
    [Serializable]
    public sealed class ElementPlacement
    {
        [Tooltip("类型配置资产")]
        public ElementTypeSO type;

        [Tooltip("网格坐标：x 向右、y 向上")]
        public Vector2Int coord;

        [Tooltip("整体旋转：0/1/2/3 对应顺时针 0°/90°/180°/270°")]
        [Range(0, 3)] public int rotationSteps;
    }
}
