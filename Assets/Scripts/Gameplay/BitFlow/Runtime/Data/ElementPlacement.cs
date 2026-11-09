using System;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 一个元件在关卡里的摆放：引用类型配置 + 坐标 + 旋转态 + 本格参数覆盖。
    ///
    /// 类型资产负责「这是什么元件、长什么样、默认什么参数」；
    /// 「这一格到底发几位、什么序列、要收什么」属于关卡设计，存在这里。
    /// 以前这些参数只能跟着类型资产走，想在一关里放两个节奏不同的发射器就得
    /// 新建变体资产；现在选中格子直接填即可。
    /// </summary>
    [Serializable]
    public sealed class ElementPlacement
    {
        [Tooltip("类型配置资产（提供 Kind / 外观 / 默认参数）")]
        public ElementTypeSO type;

        [Tooltip("网格坐标：x 向右、y 向上")]
        public Vector2Int coord;

        [Tooltip("整体旋转：0/1/2/3 对应顺时针 0°/90°/180°/270°")]
        [Range(0, 3)] public int rotationSteps;

        [Tooltip("允许玩家搬动 / 拾回这一格。默认关掉 = 关卡预设件（墙、发射器、接收器都别让玩家碰）")]
        public bool playerEditable;

        [Header("本格参数（不勾 = 完全跟随类型资产）")]
        [Tooltip("勾上 = 这一格用自己的序列，不影响别的关卡 / 别的格子")]
        public bool overrideSequence;

        [Tooltip("本格序列：发射器是吐位节奏，接收器是期望序列。例：1011")]
        public string sequence = "1";

        [Tooltip("勾上 = 这一格用自己的次数")]
        public bool overrideCount;

        [Tooltip("本格次数：发射器是「一共发几位」，接收器是「一共收几位」")]
        [Min(0)] public int count = 1;

        [Tooltip("接收器专用：勾上 = 这一格自己决定「四面可收还是要认进料面」")]
        public bool overrideFace;

        [Tooltip("接收器专用：true（默认）= 四面都能收；false = 只认进料面，其余面撞上销毁")]
        public bool acceptAnyFace = true;

        [Tooltip("过滤器专用：勾上 = 这一格自己决定放行哪个值（JSON 的 kind=Filter + pass 就落在这里）")]
        public bool overridePassValue;

        [Tooltip("过滤器专用：放行的值（0 或 1）")]
        public byte passValue = 1;

        /// <summary>这一格有没有改过参数（= 和类型资产的默认值不一样）。</summary>
        public bool HasParamOverride => overrideSequence || overrideCount || overrideFace || overridePassValue;

        /// <summary>
        /// 把本格参数套到生成好的逻辑定义上。只在勾了 override 的字段上动手，
        /// 所以老关卡（override 全 false）走纯资产路径，行为一个字节都不变。
        /// </summary>
        public void ApplyOverrides(ElementDefinition definition)
        {
            if (definition == null || !HasParamOverride) return;

            switch (definition)
            {
                case EmitterDefinition emitter:
                    if (overrideSequence) emitter.Sequence = sequence;
                    if (overrideCount) emitter.EmitCount = Math.Max(0, count);
                    break;

                case ReceiverDefinition receiver:
                    if (overrideSequence) receiver.TargetSequence = sequence;
                    if (overrideCount) receiver.TargetCount = Math.Max(0, count);
                    if (overrideFace) receiver.AcceptAnyFace = acceptAnyFace;
                    break;

                case FilterDefinition filter:
                    if (overridePassValue) filter.PassValue = (byte)(passValue == 0 ? 0 : 1);
                    break;
            }
        }

        /// <summary>
        /// 把本格参数套到可编辑草稿件上，保证「作者在关卡编辑器里看到的」和
        /// 「玩家进关后拿到的」永远是同一份参数。
        /// </summary>
        public void ApplyOverrides(EditableElement element)
        {
            if (element == null || !HasParamOverride) return;

            switch (element.Kind)
            {
                case CellKind.Emitter:
                case CellKind.Receiver:
                    if (overrideSequence) element.Sequence = sequence;
                    if (overrideCount) element.Count = Math.Max(0, count);
                    break;

                case CellKind.Filter:
                    if (overridePassValue) element.PassValue = passValue == 0 ? (byte)0 : (byte)1;
                    break;
            }
        }

        /// <summary>编辑器里显示本格参数的短文本（"1011 · 8" / "跟随资产"）。</summary>
        public string ParamsSummary()
        {
            if (type == null) return string.Empty;
            if (!HasParamOverride) return "跟随资产";

            string seq = overrideSequence ? sequence : "(资产序列)";
            string cnt = overrideCount ? count.ToString() : "(资产次数)";
            string face = overrideFace ? (acceptAnyFace ? " · 四面可收" : " · 只认进料面") : string.Empty;
            string pass = overridePassValue ? " · 放行 " + (passValue == 0 ? 0 : 1) : string.Empty;

            // 只改了过滤器放行值时，别把「序列 / 次数」那堆无关字段也糊上来
            if (overridePassValue && !overrideSequence && !overrideCount && !overrideFace)
            {
                return "放行 " + (passValue == 0 ? 0 : 1);
            }

            return seq + " · " + cnt + face + pass;
        }
    }
}
