using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>位被销毁的原因（View 层据此决定红闪特效与提示文案）。</summary>
    public enum DestroyReason
    {
        /// <summary>走到网格外。</summary>
        OutOfBounds = 0,

        /// <summary>撞墙。</summary>
        Wall = 1,

        /// <summary>撞上发射器本体（发射器机身不可穿过）。</summary>
        EmitterBody = 2,

        /// <summary>从错误的进料面进入接收器。</summary>
        ReceiverWrongFace = 3,

        /// <summary>过滤器不接受该值。</summary>
        FilterRejected = 4,

        /// <summary>从过滤器出口逆行撞入。</summary>
        FilterBackflow = 5,

        /// <summary>从分流器出口逆行撞入。</summary>
        SplitterBackflow = 6,

        /// <summary>从非门输出面逆行撞入。</summary>
        NotGateBackflow = 7,

        /// <summary>两个（或更多）位同 tick 争抢同一格，全部销毁。</summary>
        Conflict = 8,
    }

    /// <summary>本 tick 里某个位的移动（Moved=false 表示被挡住、原地等待）。</summary>
    public readonly struct BitMoved
    {
        public readonly int BitId;
        public readonly byte Value;
        public readonly GridPoint From;
        public readonly GridPoint To;
        public readonly bool Moved;

        public BitMoved(int bitId, byte value, GridPoint from, GridPoint to, bool moved)
        {
            BitId = bitId;
            Value = value;
            From = from;
            To = to;
            Moved = moved;
        }
    }

    /// <summary>本 tick 新出现的位。</summary>
    public readonly struct BitSpawned
    {
        public readonly int BitId;
        public readonly byte Value;
        public readonly GridPoint Coord;
        public readonly Dir Facing;
        public readonly int EmitterId;

        public BitSpawned(int bitId, byte value, GridPoint coord, Dir facing, int emitterId)
        {
            BitId = bitId;
            Value = value;
            Coord = coord;
            Facing = facing;
            EmitterId = emitterId;
        }
    }

    /// <summary>本 tick 被销毁的位。</summary>
    public readonly struct BitDestroyed
    {
        public readonly int BitId;
        public readonly byte Value;
        public readonly GridPoint Coord;
        public readonly DestroyReason Reason;

        public BitDestroyed(int bitId, byte value, GridPoint coord, DestroyReason reason)
        {
            BitId = bitId;
            Value = value;
            Coord = coord;
            Reason = reason;
        }
    }

    /// <summary>本 tick 有位置进入接收器。</summary>
    public readonly struct DeliveryEvent
    {
        public readonly int ReceiverId;
        public readonly byte Value;
        public readonly bool Matched;
        public readonly int ReceivedIndex;
        public readonly bool Overflow;

        public DeliveryEvent(int receiverId, byte value, bool matched, int receivedIndex, bool overflow)
        {
            ReceiverId = receiverId;
            Value = value;
            Matched = matched;
            ReceivedIndex = receivedIndex;
            Overflow = overflow;
        }
    }

    /// <summary>
    /// 一个 tick 的完整结果。View 层只消费这份报告做插值/特效，不回头读模拟器内部状态，
    /// 这样"逻辑推进"与"动画播放"解耦，单步、暂停、回放都很自然。
    /// </summary>
    public sealed class TickReport
    {
        /// <summary>这是第几个 tick（从 1 开始）。</summary>
        public readonly int Tick;

        public readonly List<BitSpawned> Spawns = new List<BitSpawned>();
        public readonly List<BitMoved> Moves = new List<BitMoved>();
        public readonly List<BitDestroyed> Destroyed = new List<BitDestroyed>();
        public readonly List<DeliveryEvent> Deliveries = new List<DeliveryEvent>();

        /// <summary>本 tick 有发射器因正前方被占而顺延的数量。</summary>
        public int StalledEmitters;

        /// <summary>模拟在这一 tick 结束后进入结算态。</summary>
        public bool IsFinished;

        public TickReport(int tick)
        {
            Tick = tick;
        }

        public bool HasClip
        {
            get
            {
                return Spawns.Count > 0 || Moves.Count > 0 || Destroyed.Count > 0 || Deliveries.Count > 0;
            }
        }
    }
}
