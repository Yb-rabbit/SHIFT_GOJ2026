using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>某一刻某个位的位置快照（View 层每 tick 结束后重建表现用）。</summary>
    public readonly struct BitSnapshot
    {
        public readonly int BitId;
        public readonly byte Value;
        public readonly GridPoint Coord;
        public readonly Dir Facing;

        public BitSnapshot(int bitId, byte value, GridPoint coord, Dir facing)
        {
            BitId = bitId;
            Value = value;
            Coord = coord;
            Facing = facing;
        }
    }

    /// <summary>
    /// 位流路由的纯逻辑核心：不依赖 UnityEngine，可被 EditMode 单元测试直接驱动。
    ///
    /// 每个 tick 严格三步：
    ///   1) 发射：各发射器尝试吐出下一个位（前方被占则顺延；多发射器抢同一格 → 一起销毁）；
    ///      吐出的位当 tick 就前进一步，因此发射器可以每个 tick 连续吐位
    ///   2) 前进：所有位按当前朝向尝试走 1 格。允许"跟随前进"——前方位同 tick 会离开时自己就能补位；
    ///      撞墙/出界/进错接收器面 → 销毁；两位抢同一格或对穿 → 全部销毁
    ///   3) 裁决：本 tick 刚进入元件格的位，按元件规则改向/改值/销毁
    /// 位进入元件格后会"停 1 tick"（裁决只改朝向），真正离开元件格要等下一个 tick。
    /// </summary>
    public sealed class BitFlowSimulation
    {
        sealed class RuntimeBit
        {
            public int Id;
            public byte Value;
            public GridPoint Coord;
            public Dir Facing;

            /// <summary>本 tick 刚进入元件格，等第 3 阶段裁决。</summary>
            public bool PendingResolve;
        }

        sealed class EmitterState
        {
            public EmitterDefinition Def;
            public List<byte> Order;
            public int Cursor;

            public bool Done => Cursor >= Order.Count;

            public bool TryPeek(out byte value)
            {
                if (Done)
                {
                    value = 0;
                    return false;
                }
                value = Order[Cursor];
                return true;
            }

            public void Pop() => Cursor++;
        }

        sealed class ReceiverState
        {
            public ReceiverDefinition Def;
            public readonly List<byte> Received = new List<byte>();
            public readonly List<bool> Matched = new List<bool>();

            public bool Overflow => Received.Count > Def.TargetCount;
        }

        readonly struct PendingMove
        {
            public readonly RuntimeBit Bit;
            public readonly GridPoint Target;

            public PendingMove(RuntimeBit bit, GridPoint target)
            {
                Bit = bit;
                Target = target;
            }
        }

        readonly LevelDefinition level;
        readonly Dictionary<GridPoint, ElementDefinition> lookup;
        readonly List<EmitterState> emitters = new List<EmitterState>();
        readonly List<ReceiverState> receivers = new List<ReceiverState>();
        readonly Dictionary<int, EmitterState> emittersById = new Dictionary<int, EmitterState>();
        readonly Dictionary<int, ReceiverState> receiversById = new Dictionary<int, ReceiverState>();
        readonly List<RuntimeBit> bits = new List<RuntimeBit>();
        readonly Dictionary<GridPoint, RuntimeBit> occupancy = new Dictionary<GridPoint, RuntimeBit>();

        int nextBitId = 1;
        bool finished;

        public BitFlowSimulation(LevelDefinition definition)
        {
            level = definition ?? throw new ArgumentNullException(nameof(definition));
            lookup = level.BuildLookup();
            Reset();
        }

        public LevelDefinition Level => level;

        /// <summary>已经推进的 tick 数。</summary>
        public int TickIndex { get; private set; }

        /// <summary>模拟是否结束（发射器吐完且场上无位）。</summary>
        public bool IsFinished => finished;

        /// <summary>场上现存的位数。</summary>
        public int ActiveBitCount => bits.Count;

        /// <summary>安全上限，避免关卡配置错误时无限推进。</summary>
        public int SafetyTickLimit { get; set; } = 5000;

        /// <summary>回到初始状态（重播用）。</summary>
        public void Reset()
        {
            TickIndex = 0;
            finished = false;
            nextBitId = 1;
            bits.Clear();
            occupancy.Clear();
            emitters.Clear();
            receivers.Clear();
            emittersById.Clear();
            receiversById.Clear();

            foreach (ElementDefinition element in level.Elements)
            {
                if (element is EmitterDefinition emitterDef)
                {
                    EmitterState state = new EmitterState { Def = emitterDef, Order = emitterDef.BuildEmitOrder() };
                    emitters.Add(state);
                    emittersById[emitterDef.Id] = state;
                }
                else if (element is ReceiverDefinition receiverDef)
                {
                    ReceiverState state = new ReceiverState { Def = receiverDef };
                    receivers.Add(state);
                    receiversById[receiverDef.Id] = state;
                }
            }
        }

        /// <summary>推进一个 tick，并返回这一 tick 发生的事。</summary>
        public TickReport Step()
        {
            TickReport report = new TickReport(TickIndex + 1);
            if (finished)
            {
                report.IsFinished = true;
                return report;
            }

            TickIndex++;

            EmitPhase(report);
            AdvancePhase(report);
            ResolvePhase(report);

            if ((AllEmittersDone() && bits.Count == 0) || TickIndex >= SafetyTickLimit)
            {
                finished = true;
                report.IsFinished = true;
            }

            return report;
        }

        // ---------------------------------------------------------------- 阶段 1：发射

        void EmitPhase(TickReport report)
        {
            RebuildOccupancy();

            // 先把"想吐到哪一格"全部收集起来，再统一提交，
            // 这样两个发射器同 tick 对准同一格时才能判定为"双销毁"（验收标准 4）。
            List<(EmitterState Emitter, byte Value, GridPoint Target)> intents =
                new List<(EmitterState, byte, GridPoint)>();

            foreach (EmitterState emitter in emitters)
            {
                if (!emitter.TryPeek(out byte value)) continue;

                GridPoint target = emitter.Def.Coord + emitter.Def.Facing.ToOffset();
                if (!level.InBounds(target))
                {
                    // 出口对着网格外：视为配置错误，本 tick 顺延（LevelValidator 会提前报警）
                    report.StalledEmitters++;
                    continue;
                }

                if (occupancy.ContainsKey(target))
                {
                    // 正前方有位置：本 tick 不吐，顺延
                    report.StalledEmitters++;
                    continue;
                }

                ElementDefinition occupant = GetElement(target);
                if (occupant is WallDefinition || occupant is EmitterDefinition)
                {
                    // 正前方是墙 / 发射器机身：同样顺延
                    report.StalledEmitters++;
                    continue;
                }

                intents.Add((emitter, value, target));
            }

            if (intents.Count == 0) return;

            Dictionary<GridPoint, int> intentCount = new Dictionary<GridPoint, int>();
            foreach ((EmitterState Emitter, byte Value, GridPoint Target) intent in intents)
            {
                intentCount.TryGetValue(intent.Target, out int count);
                intentCount[intent.Target] = count + 1;
            }

            foreach ((EmitterState Emitter, byte Value, GridPoint Target) intent in intents)
            {
                intent.Emitter.Pop();

                if (intentCount[intent.Target] > 1)
                {
                    // 同格争抢：全部销毁（红闪），谁的位也没真正出现
                    report.Destroyed.Add(new BitDestroyed(0, intent.Value, intent.Target, DestroyReason.Conflict));
                    continue;
                }

                RuntimeBit bit = SpawnBit(intent.Value, intent.Target, intent.Emitter.Def.Facing);
                report.Spawns.Add(new BitSpawned(bit.Id, bit.Value, bit.Coord, bit.Facing, intent.Emitter.Def.Id));
            }
        }

        // ---------------------------------------------------------------- 阶段 2：前进

        void AdvancePhase(TickReport report)
        {
            RebuildOccupancy();

            RuntimeBit[] snapshot = bits.ToArray();

            // 第一轮：先把"绝对进不去"的位处理掉（出界 / 墙 / 发射器机身 / 接收器错误进料面）
            List<PendingMove> candidates = new List<PendingMove>();
            foreach (RuntimeBit bit in snapshot)
            {
                // 刚被吐进元件格、还没裁决的位：本 tick 停在原地，等第 3 阶段由元件改向
                if (bit.PendingResolve)
                {
                    report.Moves.Add(new BitMoved(bit.Id, bit.Value, bit.Coord, bit.Coord, false));
                    continue;
                }

                GridPoint target = bit.Coord + bit.Facing.ToOffset();

                if (!level.InBounds(target))
                {
                    DestroyBit(bit, DestroyReason.OutOfBounds, report);
                    continue;
                }

                ElementDefinition element = GetElement(target);
                if (element is WallDefinition)
                {
                    DestroyBit(bit, DestroyReason.Wall, report);
                    continue;
                }
                if (element is EmitterDefinition)
                {
                    // 发射器机身不可穿过
                    DestroyBit(bit, DestroyReason.EmitterBody, report);
                    continue;
                }
                if (element is ReceiverDefinition receiverDef && bit.Facing.Opposite() != receiverDef.FeedFace)
                {
                    // 接收器只有进料面能进，其余面撞上即销毁
                    DestroyBit(bit, DestroyReason.ReceiverWrongFace, report);
                    continue;
                }

                candidates.Add(new PendingMove(bit, target));
            }

            if (candidates.Count == 0) return;

            // 第二轮：链式阻塞。前方格里的位如果本 tick 也走不掉，自己就被顶住；
            // 反之（前方位同 tick 会离开）允许"跟着前进"，这样位流才是连续的一股，而不是一格一跳。
            HashSet<int> blocked = FindBlocked(candidates);

            List<PendingMove> intents = new List<PendingMove>();
            foreach (PendingMove move in candidates)
            {
                if (blocked.Contains(move.Bit.Id))
                {
                    report.Moves.Add(new BitMoved(move.Bit.Id, move.Bit.Value, move.Bit.Coord, move.Bit.Coord, false));
                    continue;
                }
                intents.Add(move);
            }

            if (intents.Count == 0) return;

            HashSet<int> conflicted = FindConflicts(intents);

            foreach (PendingMove move in intents)
            {
                if (conflicted.Contains(move.Bit.Id))
                {
                    DestroyBit(move.Bit, DestroyReason.Conflict, report);
                    continue;
                }

                GridPoint from = move.Bit.Coord;
                move.Bit.Coord = move.Target;
                report.Moves.Add(new BitMoved(move.Bit.Id, move.Bit.Value, from, move.Target, true));

                ElementDefinition element = GetElement(move.Target);
                if (element is ReceiverDefinition receiver)
                {
                    Deliver(receiver, move.Bit, report);
                }
                else if (element != null)
                {
                    move.Bit.PendingResolve = true;
                }
            }
        }

        /// <summary>
        /// 迭代到收敛地求"被顶住"的位集合：只有当前方格里的位本 tick 也动不了，自己才动不了。
        /// 迭代次数上界与位数量同阶，规模极小，可忽略。
        /// </summary>
        HashSet<int> FindBlocked(List<PendingMove> candidates)
        {
            HashSet<int> blocked = new HashSet<int>();
            bool changed = true;

            while (changed)
            {
                changed = false;
                foreach (PendingMove move in candidates)
                {
                    if (blocked.Contains(move.Bit.Id)) continue;
                    if (!occupancy.TryGetValue(move.Target, out RuntimeBit other)) continue;
                    if (other == move.Bit) continue;

                    if (blocked.Contains(other.Id))
                    {
                        blocked.Add(move.Bit.Id);
                        changed = true;
                    }
                }
            }

            return blocked;
        }

        /// <summary>找出"抢同一格"与"两两对穿"的位（都会被销毁）。</summary>
        static HashSet<int> FindConflicts(List<PendingMove> intents)
        {
            HashSet<int> conflicted = new HashSet<int>();

            Dictionary<GridPoint, List<PendingMove>> byTarget = new Dictionary<GridPoint, List<PendingMove>>();
            Dictionary<GridPoint, PendingMove> bySource = new Dictionary<GridPoint, PendingMove>();

            foreach (PendingMove move in intents)
            {
                if (!byTarget.TryGetValue(move.Target, out List<PendingMove> list))
                {
                    list = new List<PendingMove>();
                    byTarget[move.Target] = list;
                }
                list.Add(move);
                bySource[move.Bit.Coord] = move;
            }

            foreach (KeyValuePair<GridPoint, List<PendingMove>> pair in byTarget)
            {
                if (pair.Value.Count > 1)
                {
                    foreach (PendingMove move in pair.Value)
                    {
                        conflicted.Add(move.Bit.Id);
                    }
                }
            }

            // 对穿（A 去 B 的格、B 去 A 的格）：两颗一起销毁
            foreach (PendingMove move in intents)
            {
                if (bySource.TryGetValue(move.Target, out PendingMove other) &&
                    other.Target == move.Bit.Coord &&
                    other.Bit.Id != move.Bit.Id)
                {
                    conflicted.Add(move.Bit.Id);
                    conflicted.Add(other.Bit.Id);
                }
            }

            return conflicted;
        }

        // ---------------------------------------------------------------- 阶段 3：元件裁决

        void ResolvePhase(TickReport report)
        {
            foreach (RuntimeBit bit in bits.ToArray())
            {
                if (!bit.PendingResolve) continue;
                bit.PendingResolve = false;

                ElementDefinition element = GetElement(bit.Coord);
                if (element == null) continue;

                // 位"从哪一面进来的" = 当前朝向的反面
                Dir entryFace = bit.Facing.Opposite();

                switch (element)
                {
                    case SplitterDefinition splitter:
                        if (splitter.ExitDirs.Contains(entryFace))
                        {
                            DestroyBit(bit, DestroyReason.SplitterBackflow, report);
                            break;
                        }
                        bit.Facing = splitter.ArmFor(bit.Value);
                        break;

                    case NotGateDefinition notGate:
                        if (notGate.ExitDirs.Contains(entryFace))
                        {
                            DestroyBit(bit, DestroyReason.NotGateBackflow, report);
                            break;
                        }
                        bit.Value = (byte)(1 - bit.Value);
                        bit.Facing = notGate.OutputDir;
                        break;

                    case FilterDefinition filter:
                        if (filter.ExitDirs.Contains(entryFace))
                        {
                            DestroyBit(bit, DestroyReason.FilterBackflow, report);
                            break;
                        }
                        if (bit.Value != filter.PassValue)
                        {
                            DestroyBit(bit, DestroyReason.FilterRejected, report);
                            break;
                        }
                        bit.Facing = filter.OutputDir;
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- 内部工具

        RuntimeBit SpawnBit(byte value, GridPoint coord, Dir facing)
        {
            RuntimeBit bit = new RuntimeBit
            {
                Id = nextBitId++,
                Value = value,
                Coord = coord,
                Facing = facing,
                // 直接吐进元件格（比如发射器正对分流器）时，本 tick 就要裁决
                PendingResolve = GetElement(coord) != null,
            };
            bits.Add(bit);
            occupancy[coord] = bit;
            return bit;
        }

        void DestroyBit(RuntimeBit bit, DestroyReason reason, TickReport report)
        {
            bits.Remove(bit);
            occupancy.Remove(bit.Coord);
            report.Destroyed.Add(new BitDestroyed(bit.Id, bit.Value, bit.Coord, reason));
        }

        void Deliver(ReceiverDefinition receiverDef, RuntimeBit bit, TickReport report)
        {
            if (!receiversById.TryGetValue(receiverDef.Id, out ReceiverState state)) return;

            int receivedIndex = state.Received.Count;
            bool overflow = receivedIndex >= receiverDef.TargetCount;
            bool matched = !overflow && bit.Value == receiverDef.ExpectedValueAt(receivedIndex);

            state.Received.Add(bit.Value);
            state.Matched.Add(matched);

            bits.Remove(bit);
            occupancy.Remove(bit.Coord);

            report.Deliveries.Add(new DeliveryEvent(receiverDef.Id, bit.Value, matched, receivedIndex, overflow));
        }

        void RebuildOccupancy()
        {
            occupancy.Clear();
            foreach (RuntimeBit bit in bits)
            {
                occupancy[bit.Coord] = bit;
            }
        }

        ElementDefinition GetElement(GridPoint coord)
        {
            return lookup.TryGetValue(coord, out ElementDefinition element) ? element : null;
        }

        bool AllEmittersDone()
        {
            foreach (EmitterState emitter in emitters)
            {
                if (!emitter.Done) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- 给 View / UI 的只读查询

        /// <summary>当前所有位的位置（每 tick 结束后 View 用它对齐表现）。</summary>
        public List<BitSnapshot> GetBits()
        {
            List<BitSnapshot> snapshots = new List<BitSnapshot>(bits.Count);
            foreach (RuntimeBit bit in bits)
            {
                snapshots.Add(new BitSnapshot(bit.Id, bit.Value, bit.Coord, bit.Facing));
            }
            return snapshots;
        }

        /// <summary>各发射器的吐位进度（View 用来点亮/变暗元件上的序列点阵）。</summary>
        public List<EmitterProgress> GetEmitterProgress()
        {
            List<EmitterProgress> progress = new List<EmitterProgress>(emitters.Count);
            foreach (EmitterState state in emitters)
            {
                progress.Add(new EmitterProgress
                {
                    EmitterId = state.Def.Id,
                    EmittedCount = state.Cursor,
                    TotalCount = state.Order.Count,
                });
            }
            return progress;
        }

        /// <summary>各接收器的收料进度（UI 上显示 "3/6"）。</summary>
        public List<ReceiverProgress> GetReceiverProgress()
        {
            List<ReceiverProgress> progress = new List<ReceiverProgress>(receivers.Count);
            foreach (ReceiverState state in receivers)
            {
                bool stillValid = true;
                for (int i = 0; i < state.Matched.Count; i++)
                {
                    if (!state.Matched[i])
                    {
                        stillValid = false;
                        break;
                    }
                }

                progress.Add(new ReceiverProgress
                {
                    ReceiverId = state.Def.Id,
                    TargetCount = state.Def.TargetCount,
                    ReceivedCount = state.Received.Count,
                    IsStillValid = stillValid,
                });
            }
            return progress;
        }

        /// <summary>生成结算结果（模拟结束或中途查看都可用）。</summary>
        public LevelResult BuildResult()
        {
            LevelResult result = new LevelResult
            {
                DisplayName = level.DisplayName,
                TickCount = TickIndex,
            };

            bool allSuccess = receivers.Count > 0;
            foreach (ReceiverState state in receivers)
            {
                ReceiverResult receiverResult = new ReceiverResult
                {
                    ReceiverId = state.Def.Id,
                    TargetCount = state.Def.TargetCount,
                    ReceivedCount = state.Received.Count,
                };
                receiverResult.Received.AddRange(state.Received);
                receiverResult.Matched.AddRange(state.Matched);

                result.Receivers.Add(receiverResult);
                allSuccess &= receiverResult.IsSuccess;
            }

            result.IsSuccess = allSuccess;
            return result;
        }
    }
}
