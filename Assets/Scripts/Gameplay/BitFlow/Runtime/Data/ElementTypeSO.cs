using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 元件"类型配置"资产基类：只描述这个元件是什么、基准朝向如何，
    /// 坐标与整体旋转放在 <see cref="LevelSO"/> 的 placements 里，因此同一种元件能被多个关卡复用。
    /// </summary>
    public abstract class ElementTypeSO : ScriptableObject
    {
        [Tooltip("编辑器里显示用的短名，例如 E1 / L1 / R2（留空则用资产名）")]
        public string label = string.Empty;

        /// <summary>
        /// 稳定标识：默认取资产名（Type_Emitter_E1 之类），写进 <see cref="ElementDefinition.TypeKey"/>。
        /// 想改名又不破坏存档/手牌对应关系时，在子类里覆写成固定字符串。
        /// </summary>
        public virtual string Key => string.IsNullOrEmpty(name) ? GetType().Name : name;

        /// <summary>该类型的元件在 UI 提示里的说明文本。</summary>
        public abstract string Describe();

        /// <summary>按坐标 + 旋转态生成纯逻辑定义。id 由关卡在收集 placements 时统一分配。</summary>
        public abstract ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id);

        /// <summary>
        /// 按本资产的配置（朝向 / 序列 / 数量…）生成一份可编辑草稿，玩家手牌放置与关卡预设件都用它，
        /// 这样「同一个类型资产」在关卡里、在手牌里、在草稿里始终是同一份配置，不会各写一套默认值。
        /// </summary>
        public EditableElement CreateEditable(GridPoint coord, int rotationSteps = 0)
        {
            ElementDefinition definition = CreateDefinition(coord, rotationSteps, 0);
            if (definition == null) return null;

            definition.TypeKey = Key;
            EditableElement element = EditableElement.FromDefinition(definition);
            element.TypeKey = Key;
            return element;
        }

        /// <summary>把基准朝向按旋转态（0-3，顺时针 90°/档）换算成实际朝向。</summary>
        protected static Dir Rotate(Dir baseDir, int rotationSteps)
        {
            int steps = ((rotationSteps % 4) + 4) % 4;
            Dir result = baseDir;
            for (int i = 0; i < steps; i++) result = result.RotateClockwise();
            return result;
        }

        /// <summary>
        /// 朝向的箭头写法，给提示框和开场黑幕的元件说明用。
        ///
        /// 以前这里返回「上 / 右 / 下 / 左」—— 但 UI 字体换成了像素字体（<see cref="View.PixelFont"/>），
        /// <b>没有中文字形</b>，中文在编辑器里靠系统字体 fallback 还能看，打包后全是方块。
        /// 具体实现放在 <see cref="Core.DirUtil.ToMark"/>，场景里那套兜底说明也用同一份。
        /// </summary>
        protected static string DirArrow(Dir dir)
        {
            return dir.ToMark();
        }
    }
}
