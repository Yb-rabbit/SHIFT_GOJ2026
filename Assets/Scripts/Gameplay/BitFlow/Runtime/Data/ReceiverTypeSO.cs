using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>接收器类型：只有一个进料面，逐位比对 + 累计计数。</summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Receiver", fileName = "Type_Receiver")]
    public sealed class ReceiverTypeSO : ElementTypeSO
    {
        [Header("接收器")]
        [Tooltip("进料面朝向：Down 表示位要从下方朝上走进去")]
        public Dir feedFace = Dir.Down;

        [Tooltip("期望的位序列，按循环逐位比对；例：1 表示每位都必须是 1")]
        public string targetSequence = "1";

        [Tooltip("期望收到的位总数；超过这个数即算超量判负")]
        [Min(1)] public int targetCount = 1;

        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new ReceiverDefinition
            {
                Id = id,
                Coord = coord,
                FeedFace = Rotate(feedFace, rotationSteps),
                TargetSequence = targetSequence,
                TargetCount = targetCount,
            };
        }

        public override string Describe()
        {
            return "接收器 " + label + "\n进料面朝 " + DirName(feedFace) + "，需要 \"" + targetSequence + "\" ×" + targetCount;
        }
    }
}
