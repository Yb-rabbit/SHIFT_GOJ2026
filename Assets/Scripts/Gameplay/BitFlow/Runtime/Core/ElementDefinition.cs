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

        /// <summary>
        /// 延迟器（D）：位从任意面进入后先进本格的 FIFO 队列，比直通元件多停 N 个 tick，
        /// 再从进入方向的对面原样穿出（不改值、不改向）。追加在枚举末尾，老存档的数值不变。
        /// </summary>
        DelayGate = 6,
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

        /// <summary>
        /// 来源类型资产的稳定标识（<see cref="Label"/> 是给人看的短名，这个是给代码认的）。
        /// 空字符串表示「没有来源资产」（例如纯代码搭的演示关）。
        /// 视图层靠它把程序化几何换成自定义模型/预制体，玩家手牌也靠它把拾回的元件还回正确的牌堆，
        /// 因此它必须跟资产同生共死，不要随手改。
        /// </summary>
        public string TypeKey = string.Empty;

        /// <summary>
        /// 这一格玩家能不能搬动（对应 JSON 的 <c>playerEditable</c>）。
        ///
        /// false = 关卡预设件：玩家局里搬不动、拾不回、也转不了（发射器 / 接收器 / 墙通常是这一类）；
        /// true  = 作者留给玩家自由改的件（地板上的门、可以挪位置的分流器之类）。
        ///
        /// 默认 false 是刻意的：手写 JSON 忘了写这个字段时，宁愿锁着 ——
        /// 一进关就能把作者的关卡拆了，比"想搬搬不动"糟糕得多。
        /// </summary>
        public bool PlayerEditable;

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
    /// 发射器：朝 Facing 方向吐位。Sequence 按"从字符串末字符往前"弹栈组成循环单元，
    /// 再循环填充到 <see cref="EmitCount"/> 位。
    /// 例：Sequence="1001" 的循环单元是 1,0,0,1；EmitCount=6 → 实发 1,0,0,1,1,0。
    /// 若正前方被占用则本 tick 顺延（不丢失）。
    /// </summary>
    public sealed class EmitterDefinition : ElementDefinition
    {
        public Dir Facing = Dir.Right;
        public string Sequence = "1";

        /// <summary>
        /// 发射总位数（把 Sequence 的循环单元循环填充到这个长度）。0 = 不工作。
        /// 这是「填入发射数」真正生效的字段：想要 6 位就填 6，不必被序列长度整除。
        /// 旧语义是"重复轮数"，长度只能是序列长度的整数倍，做不出 6 位这种关卡。
        /// </summary>
        public int EmitCount = 1;

        public override CellKind Kind => CellKind.Emitter;

        public override IReadOnlyList<Dir> ExitDirs => new[] { Facing };

        /// <summary>
        /// 序列的循环单元（末字符先弹）。只收 0 与 1，其他字符忽略；空序列返回空表。
        /// 编辑器靠它显示"这个序列几位一循环、里面几个 1"。
        /// </summary>
        public List<byte> BuildCycle()
        {
            List<byte> cycle = new List<byte>();
            if (string.IsNullOrEmpty(Sequence)) return cycle;

            for (int i = Sequence.Length - 1; i >= 0; i--)
            {
                char c = Sequence[i];
                if (c == '1') cycle.Add(1);
                else if (c == '0') cycle.Add(0);
            }
            return cycle;
        }

        /// <summary>循环单元长度（= 序列里有效 0/1 的个数）。</summary>
        public int CycleLength => BuildCycle().Count;

        /// <summary>展开后的实际发射顺序，长度恒等于 <see cref="EmitCount"/>。</summary>
        public List<byte> BuildEmitOrder()
        {
            List<byte> order = new List<byte>();
            if (EmitCount <= 0) return order;

            List<byte> cycle = BuildCycle();
            if (cycle.Count == 0) return order;

            for (int i = 0; i < EmitCount; i++) order.Add(cycle[i % cycle.Count]);
            return order;
        }
    }

    /// <summary>
    /// 接收器：默认**四面都能收**（位从任何方向撞进来都算数），这样"墙围一圈只留一个口"
    /// 这类布局不用再为每一面单独调朝向。要严格限制只从一面进，把 <see cref="AcceptAnyFace"/>
    /// 关掉即可，此时只认 <see cref="FeedFace"/> 那一面，其余面撞上销毁。
    /// TargetSequence 按循环比对，TargetCount 是期望收到的总个数。
    /// </summary>
    public sealed class ReceiverDefinition : ElementDefinition
    {
        /// <summary>进料面：位必须以这个面的反方向进入（FeedFace=Down 时位要朝上走进去）。</summary>
        public Dir FeedFace = Dir.Down;

        /// <summary>
        /// true（默认）= 四面都能收，<see cref="FeedFace"/> 只作为显示/建议朝向；
        /// false = 严格模式，只有 <see cref="FeedFace"/> 这一面能进，其余面撞上即销毁。
        /// </summary>
        public bool AcceptAnyFace = true;

        public string TargetSequence = "1";
        public int TargetCount = 1;

        public override CellKind Kind => CellKind.Receiver;

        /// <summary>
        /// <paramref name="entryFace"/> 是位撞上的那一面（= 位前进方向的反面）。
        /// 四面可收时恒为 true；严格模式下只有正好撞在进料面上才收。
        /// </summary>
        public bool AcceptsEntry(Dir entryFace) => AcceptAnyFace || entryFace == FeedFace;

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

    /// <summary>
    /// 延迟器（D）：位进入后不占格、不打转，直接进本格的 FIFO 队列；
    /// 比直通元件多等 N 个 tick 之后，才从"进入方向的对面"原样穿出（值、朝向都不变），
    /// 所以它只改<b>时间</b>，不改路由。
    ///
    /// 时间线（以 delay=1 为例，T = 位撞进延迟器的那个 tick）：
    ///   T     进队；T+1 它在队列里（场上没有这个位）；T+2 放行出口、当 tick 迈一步；T+3 抵达下游。
    /// 也就是说 delay=N 恰好比"直通元件"晚 N 个 tick —— 填 1 就一定看得出慢了。
    ///
    /// 为什么是 FIFO 且容量有限：它要能当"蓄水池"用（前面堵住时先按住若干个位），
    /// 但无限蓄水会让关卡失去时间压力，所以队列上限是 <see cref="Capacity"/>。
    /// 无方向、不可旋转：四个面等价，旋转它不会有任何可见变化。
    /// </summary>
    public sealed class DelayGateDefinition : ElementDefinition
    {
        /// <summary>队列容量。5 个位连续涌入是关卡里的常见峰值，留到 8 才不容易"莫名丢位"。</summary>
        public const int Capacity = 8;

        /// <summary>延迟 tick 数：N = 比直通元件多停 N 拍（1 就是最轻的延迟，仍然比直通晚 1 tick）。</summary>
        public int DelayTicks = 1;

        public override CellKind Kind => CellKind.DelayGate;

        /// <summary>延迟器四面都能进，没有"出口面逆行"的概念。</summary>
        public override IReadOnlyList<Dir> ExitDirs => System.Array.Empty<Dir>();

        /// <summary>把配置夹到合法区间，模拟器与 UI 共用这一份判定。</summary>
        public int SafeDelayTicks
        {
            get
            {
                if (DelayTicks < 1) return 1;
                return DelayTicks > 4 ? 4 : DelayTicks;
            }
        }
    }
}
