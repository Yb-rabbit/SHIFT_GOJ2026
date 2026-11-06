using System;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 玩家手牌里的一摞卡：某种元件类型 + 本关给几张。
    /// 关卡里 <see cref="LevelSO.placements"/> 是"给定布局"（通常 locked），这里才是"玩家能做到的事"，
    /// 两者合起来才是一个有限元件解谜关。
    /// </summary>
    [Serializable]
    public sealed class ElementSupply
    {
        [Tooltip("手牌对应的元件类型资产")]
        public ElementTypeSO type;

        [Tooltip("本关提供几张（含已放下的）")]
        [Min(0)] public int count = 1;
    }
}
