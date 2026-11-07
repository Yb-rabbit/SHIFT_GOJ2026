using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 接收器类型：默认四面都能收（<see cref="acceptAnyFace"/>），逐位比对 + 累计计数。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Receiver", fileName = "Type_Receiver")]
    public sealed class ReceiverTypeSO : ElementTypeSO
    {
        [Header("接收器")]
        [Tooltip("勾上（默认）= 四面都能收，位从任何方向进来都算；下面是建议朝向，只用于显示。\n取消勾选 = 严格模式，只认进料面这一面，其余面撞上销毁")]
        public bool acceptAnyFace = true;

        [Tooltip("进料面朝向：Down 表示位要从下方朝上走进去（四面可收时只作建议朝向）")]
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
                AcceptAnyFace = acceptAnyFace,
                TargetSequence = targetSequence,
                TargetCount = targetCount,
            };
        }

        public override string Describe()
        {
            string face = acceptAnyFace ? "四面都能进" : "只从" + DirName(feedFace) + "面进";
            return "接收器 " + label + "\n" + face + "，需要 \"" + targetSequence + "\" ×" + targetCount;
        }
    }
}
