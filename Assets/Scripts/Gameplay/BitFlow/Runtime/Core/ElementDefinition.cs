using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>网格单元的类型（墙也算一种单元）。</summary>
    public enum CellKind
    {
        Wall = 0,
        Emitter = 1,
        Receiver = 2,
        Splitter = 3,
        NotGate = 4,
        Filter = 5,
    }

    /// <summary>
    /// 元件的"数据定义"：只管类型配置与它所在的坐标，不含任何运行时状态。
    /// 运行时状态（已发射数量、接收到的位等）一律放在 BitFlowSimulation 内部，
    /// 这样 ScriptableObject 永远不会被写脏。
    /// </summary>
    public abstract class ElementDefinition
    {
        /// <summary>关卡内唯一 Id（结算面板与调试日志用）。</summary>
        public int Id;

        /// <summary>所在网格坐标。</summary>
        public GridPoint Coord;

        /// <summary>UI / 提示框里显示的短名（如 E1、L1），由类型资产带过来。</summary>
        public string Label = string.Empty;

        /// <summary>悬停提示文本（由类型资产生成）。</summary>
        public string Hint = string.Empty;

        public abstract CellKind Kind { get; }

        /// <summary>该元件的出口朝向集合；位从这些面逆行撞入视为违规。</summary>
        public virtual IReadOnlyList<Dir> ExitDirs => System.Array.Empty<Dir>();

        public override string ToString() => Kind + "#" + Id + Coord;
    }

    /// <summary>障碍墙：位撞上即销毁。</summary>
    public sealed class WallDefinition : ElementDefinition
    {
        public override CellKind Kind => CellKind.Wall;
    }

    /// <summary>
    /// 发射器：朝 Facing 方向吐位。位序列按"从字符串末字符往前"弹栈，并重复 RepeatCount 轮。
    /// 例：Sequence="1001"、RepeatCount=2 → 弹出顺序 1,0,0,1,1,0,0,1。
    /// 若正前方被占用则本 tick 顺延（不丢失）。
    /// </summary>
    public sealed class EmitterDefinition : ElementDefinition
    {
        public Dir Facing = Dir.Right;
        public string Sequence = "1";
        public int RepeatCount = 1;

        public override CellKind Kind => CellKind.Emitter;

        public override IReadOnlyList<Dir> ExitDirs => new[] { Facing };

        /// <summary>展开后的实际发射顺序。</summary>
        public List<byte> BuildEmitOrder()
        {
            List<byte> order = new List<byte>();
            if (string.IsNullOrEmpty(Sequence) || RepeatCount <= 0) return order;

            for (int repeat = 0; repeat < RepeatCount; repeat++)
            {
                for (int i = Sequence.Length - 1; i >= 0; i--)
                {
                    char c = Sequence[i];
                    if (c == '1') order.Add(1);
                    else if (c == '0') order.Add(0);
                }
            }
            return order;
        }
    }

    /// <summary>
    /// 接收器：位只能从进料面朝入。FeedFace 表示进料口"朝向哪一面"，
    /// 也就是位必须以 FeedFace 的反方向进入（FeedFace=Down 时位要朝上走进去）。
    /// TargetSequence 按循环比对，TargetCount 是期望收到的总个数。
    /// </summary>
    public sealed class ReceiverDefinition : ElementDefinition
    {
        public Dir FeedFace = Dir.Down;
        public string TargetSequence = "1";
        public int TargetCount = 1;

        public override CellKind Kind => CellKind.Receiver;

        /// <summary>第 index 个位（从 0 起）期望的取值。</summary>
        public byte ExpectedValueAt(int index)
        {
            if (string.IsNullOrEmpty(TargetSequence)) return 1;
            char c = TargetSequence[index % TargetSequence.Length];
            return c == '0' ? (byte)0 : (byte)1;
        }
    }

    /// <summary>
    /// 分流器：两臂互成 90°。位从任意非出口面进入时，值 1 走 OneArm、值 0 走 ZeroArm。
    /// 从任一出口面逆行撞入 = 销毁。
    /// </summary>
    public sealed class SplitterDefinition : ElementDefinition
    {
        public Dir OneArm = Dir.Up;
        public Dir ZeroArm = Dir.Right;

        public override CellKind Kind => CellKind.Splitter;

        public override IReadOnlyList<Dir> ExitDirs => new[] { OneArm, ZeroArm };

        public Dir ArmFor(byte value) => value == 1 ? OneArm : ZeroArm;
    }

    /// <summary>非门：从任意非输出面进入时把 0/1 取反，并从 OutputDir 射出。逆行 = 销毁。</summary>
    public sealed class NotGateDefinition : ElementDefinition
    {
        public Dir OutputDir = Dir.Right;

        public override CellKind Kind => CellKind.NotGate;

        public override IReadOnlyList<Dir> ExitDirs => new[] { OutputDir };
    }

    /// <summary>过滤器：只放行指定值，且从 OutputDir 射出；不匹配的值销毁。</summary>
    public sealed class FilterDefinition : ElementDefinition
    {
        public Dir OutputDir = Dir.Right;

        /// <summary>放行的取值：0 或 1。</summary>
        public byte PassValue = 1;

        public override CellKind Kind => CellKind.Filter;

        public override IReadOnlyList<Dir> ExitDirs => new[] { OutputDir };
    }
}
