using System.Collections.Generic;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 把类型资产拍成纯 C# 的快照（<see cref="LevelTypeCatalog"/>）。
    ///
    /// 为什么需要：Unity 之外的关卡编辑器（Tools/LevelStudio）打不开 <see cref="ElementTypeSO"/>，
    /// 但它要「打开一份 Unity 导出的关」就必须知道每个 type 的默认参数。
    /// 这里从**真实的** <c>CreateDefinition</c> 读参数（而不是在别处再写一套默认值），
    /// 所以工具里算出来的关卡和游戏里跑的是同一份配置。
    ///
    /// 快照文件由菜单 <c>SHIFT GO/BitFlow/关卡 JSON/导出类型目录</c> 生成。
    /// </summary>
    public static class LevelTypeCatalogSource
    {
        public static LevelTypeCatalog Build(LevelTypeRegistrySO registry)
        {
            LevelTypeCatalog catalog = new LevelTypeCatalog();
            if (registry == null || registry.Types == null) return catalog;

            foreach (ElementTypeSO type in registry.Types)
            {
                LevelTypeSpec spec = FromType(type);
                if (spec != null) catalog.types.Add(spec);
            }

            catalog.InvalidateIndex();
            return catalog;
        }

        /// <summary>一份类型资产 → 一份快照；传入 null 返回 null。</summary>
        public static LevelTypeSpec FromType(ElementTypeSO type)
        {
            if (type == null) return null;

            ElementDefinition definition = type.CreateDefinition(new GridPoint(0, 0), 0, 0);
            if (definition == null) return null;

            LevelTypeSpec spec = new LevelTypeSpec
            {
                key = type.Key,
                kind = definition.Kind.ToString(),
                label = type.label ?? string.Empty,
                hint = type.Describe(),
                dir = "Right",
                dir2 = "Down",
                sequence = "1",
                count = 1,
                acceptAnyFace = true,
                pass = 1,
                tool = type.IsTool,
            };

            // 参数一律从「0 号定义」里读：朝向、序列、次数都是元件自己的默认脸
            switch (definition)
            {
                case EmitterDefinition emitter:
                    spec.dir = emitter.Facing.ToString();
                    spec.sequence = emitter.Sequence;
                    spec.count = emitter.EmitCount;
                    break;

                case ReceiverDefinition receiver:
                    spec.dir = receiver.FeedFace.ToString();
                    spec.sequence = receiver.TargetSequence;
                    spec.count = receiver.TargetCount;
                    spec.acceptAnyFace = receiver.AcceptAnyFace;
                    break;

                case SplitterDefinition splitter:
                    spec.dir = splitter.OneArm.ToString();
                    spec.dir2 = splitter.ZeroArm.ToString();
                    break;

                case NotGateDefinition notGate:
                    spec.dir = notGate.OutputDir.ToString();
                    break;

                case FilterDefinition filter:
                    spec.dir = filter.OutputDir.ToString();
                    spec.pass = filter.PassValue == 0 ? 0 : 1;
                    break;

                case DelayGateDefinition delayGate:
                    spec.delay = delayGate.SafeDelayTicks;
                    break;
            }

            return spec;
        }

        /// <summary>把定义里的朝向/序列读成一份 kind 模式的最小描述（外部编辑器新建元件时当默认值）。</summary>
        public static LevelTypeSpec FromDefinition(ElementDefinition definition, string key)
        {
            if (definition == null) return null;

            LevelTypeSpec spec = new LevelTypeSpec
            {
                key = key ?? string.Empty,
                kind = definition.Kind.ToString(),
                label = definition.Label,
                hint = definition.Hint,
                dir = "Right",
                dir2 = "Down",
                sequence = "1",
                count = 1,
                acceptAnyFace = true,
                pass = 1,
            };

            if (definition is EmitterDefinition emitter)
            {
                spec.dir = emitter.Facing.ToString();
                spec.sequence = emitter.Sequence;
                spec.count = emitter.EmitCount;
            }
            else if (definition is ReceiverDefinition receiver)
            {
                spec.dir = receiver.FeedFace.ToString();
                spec.sequence = receiver.TargetSequence;
                spec.count = receiver.TargetCount;
                spec.acceptAnyFace = receiver.AcceptAnyFace;
            }
            else if (definition is SplitterDefinition splitter)
            {
                spec.dir = splitter.OneArm.ToString();
                spec.dir2 = splitter.ZeroArm.ToString();
            }
            else if (definition is NotGateDefinition notGate)
            {
                spec.dir = notGate.OutputDir.ToString();
            }
            else if (definition is FilterDefinition filter)
            {
                spec.dir = filter.OutputDir.ToString();
                spec.pass = filter.PassValue == 0 ? 0 : 1;
            }
            else if (definition is DelayGateDefinition delayGate)
            {
                spec.delay = delayGate.SafeDelayTicks;
            }

            return spec;
        }

        /// <summary>工程里没有注册表时的兜底：按种类给一份内置默认（kind 模式够用）。</summary>
        public static LevelTypeCatalog BuildFallback()
        {
            LevelTypeCatalog catalog = new LevelTypeCatalog();
            CellKind[] kinds =
            {
                CellKind.Wall, CellKind.Emitter, CellKind.Receiver,
                CellKind.Splitter, CellKind.NotGate, CellKind.Filter,
                CellKind.DelayGate,
            };

            List<LevelTypeSpec> specs = new List<LevelTypeSpec>();
            foreach (CellKind kind in kinds)
            {
                specs.Add(FromDefinition(EditableElement.CreateDefault(kind, new GridPoint(0, 0)).ToDefinition(0), string.Empty));
            }

            catalog.types = specs;
            catalog.InvalidateIndex();
            return catalog;
        }
    }
}
