using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 一份「类型资产」的快照（纯 C#）。
    ///
    /// 类型资产（<c>ElementTypeSO</c>）是 UnityEngine.Object，Unity 之外拿不到；
    /// 但外部编辑器要打开已有导出关（那些关的元件只写了 <c>type</c>）就必须知道
    /// 那个类型资产的默认参数。于是 Unity 侧把它导成 JSON（菜单「导出类型目录」），
    /// 外部编辑器读这份快照 —— 参数值是从真实的 <c>CreateDefinition</c> 读出来的，
    /// 所以两边不会各写一套默认值。
    /// </summary>
    [Serializable]
    public sealed class LevelTypeSpec
    {
        /// <summary>稳定标识 = 类型资产名（与 <c>ElementTypeSO.Key</c> 同源）。</summary>
        public string key = string.Empty;

        /// <summary>Wall / Emitter / Receiver / Splitter / NotGate / Filter。</summary>
        public string kind = string.Empty;

        public string label = string.Empty;

        /// <summary>悬停提示文本（= 类型资产的 Describe()）。</summary>
        public string hint = string.Empty;

        /// <summary>主朝向（发射器=吐位方向，接收器=进料面，非门/过滤器=输出，分流器=OneArm）。</summary>
        public string dir = "Right";

        /// <summary>分流器 ZeroArm。</summary>
        public string dir2 = "Down";

        public string sequence = "1";

        /// <summary>发射总位数 / 接收总个数（已经换算过，不是资产上的「轮数」）。</summary>
        public int count = 1;

        /// <summary>接收器：true = 四面可收。</summary>
        public bool acceptAnyFace = true;

        public int pass = 1;

        /// <summary>延迟器的延迟 tick 数（1~4）。</summary>
        public int delay = 1;

        public CellKind KindOrDefault()
        {
            return LevelDocumentBuilder.TryParseKind(kind, out CellKind parsed) ? parsed : CellKind.Wall;
        }

        /// <summary>按本快照生成一份逻辑定义（朝向按 rotationSteps 旋转）。</summary>
        public ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            Dir primary = ParseDirOr(dir, Dir.Right);
            Dir secondary = ParseDirOr(dir2, Dir.Down);

            switch (KindOrDefault())
            {
                case CellKind.Emitter:
                    return new EmitterDefinition
                    {
                        Id = id,
                        Coord = coord,
                        Facing = LevelDocumentBuilder.Rotate(primary, rotationSteps),
                        Sequence = string.IsNullOrEmpty(sequence) ? "1" : sequence,
                        EmitCount = Math.Max(0, count),
                    };

                case CellKind.Receiver:
                    return new ReceiverDefinition
                    {
                        Id = id,
                        Coord = coord,
                        FeedFace = LevelDocumentBuilder.Rotate(primary, rotationSteps),
                        AcceptAnyFace = acceptAnyFace,
                        TargetSequence = string.IsNullOrEmpty(sequence) ? "1" : sequence,
                        TargetCount = Math.Max(0, count),
                    };

                case CellKind.Splitter:
                    return new SplitterDefinition
                    {
                        Id = id,
                        Coord = coord,
                        OneArm = LevelDocumentBuilder.Rotate(primary, rotationSteps),
                        ZeroArm = LevelDocumentBuilder.Rotate(secondary, rotationSteps),
                    };

                case CellKind.NotGate:
                    return new NotGateDefinition
                    {
                        Id = id,
                        Coord = coord,
                        OutputDir = LevelDocumentBuilder.Rotate(primary, rotationSteps),
                    };

                case CellKind.Filter:
                    return new FilterDefinition
                    {
                        Id = id,
                        Coord = coord,
                        OutputDir = LevelDocumentBuilder.Rotate(primary, rotationSteps),
                        PassValue = (byte)(pass == 0 ? 0 : 1),
                    };

                case CellKind.DelayGate:
                    return new DelayGateDefinition
                    {
                        Id = id,
                        Coord = coord,
                        DelayTicks = delay < 1 ? 1 : (delay > 4 ? 4 : delay),
                    };

                default:
                    return new WallDefinition { Id = id, Coord = coord };
            }
        }

        static Dir ParseDirOr(string text, Dir fallback)
        {
            return LevelDocumentBuilder.TryParseDir(text, out Dir dir) ? dir : fallback;
        }
    }

    /// <summary>类型资产快照表。外部编辑器的 <c>typeCatalog.json</c> 就是它的序列化形态。</summary>
    [Serializable]
    public sealed class LevelTypeCatalog
    {
        public int schema = LevelDocument.CurrentSchema;

        public List<LevelTypeSpec> types = new List<LevelTypeSpec>();

        Dictionary<string, LevelTypeSpec> index;

        public void InvalidateIndex()
        {
            index = null;
        }

        public bool TryGet(string key, out LevelTypeSpec spec)
        {
            spec = null;
            if (string.IsNullOrEmpty(key)) return false;

            if (index == null)
            {
                index = new Dictionary<string, LevelTypeSpec>(StringComparer.Ordinal);
                if (types != null)
                {
                    foreach (LevelTypeSpec candidate in types)
                    {
                        if (candidate == null || string.IsNullOrEmpty(candidate.key)) continue;
                        index[candidate.key] = candidate;
                    }
                }
            }

            return index.TryGetValue(key, out spec);
        }

        /// <summary>按种类找第一份快照（墙层、kind 模式没写参数时的参照）。</summary>
        public LevelTypeSpec FindByKind(CellKind kind)
        {
            if (types == null) return null;

            foreach (LevelTypeSpec candidate in types)
            {
                if (candidate != null && candidate.KindOrDefault() == kind) return candidate;
            }
            return null;
        }
    }
}
