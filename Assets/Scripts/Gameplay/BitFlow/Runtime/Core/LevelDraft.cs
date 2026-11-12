using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 游戏内编辑器里一格的"可改配置"。
    /// 各种元件的差异字段（朝向、序列、数量、放行值）都收进同一个对象，
    /// 这样放置/旋转/删除/存档都能在纯 C# 层完成，也方便 EditMode 单测直接驱动。
    /// 它只是编辑期的草稿，<see cref="ToDefinition"/> 之后才变成模拟器认识的 <see cref="ElementDefinition"/>。
    /// </summary>
    public sealed class EditableElement
    {
        public CellKind Kind = CellKind.Wall;
        public GridPoint Coord;

        /// <summary>主朝向：发射器的吐位方向 / 接收器的进料面 / 非门与过滤器的输出 / 分流器的 OneArm。</summary>
        public Dir PrimaryDir = Dir.Right;

        /// <summary>副朝向：只给分流器的 ZeroArm 用。</summary>
        public Dir SecondaryDir = Dir.Down;

        /// <summary>发射器的序列 / 接收器的目标序列。</summary>
        public string Sequence = "1";

        /// <summary>发射器的发射总位数 / 接收器的期望个数。</summary>
        public int Count = 1;

        /// <summary>
        /// 接收器专用：true（默认）= 四面都能收，<see cref="PrimaryDir"/> 只作为显示朝向；
        /// false = 严格模式，只有进料面能收。发射器/其它件忽略这个字段。
        /// </summary>
        public bool AcceptAnyFace = true;

        /// <summary>过滤器放行的取值（0 或 1）。</summary>
        public byte PassValue = 1;

        /// <summary>延迟器的延迟 tick 数（1~4）。比直通元件多停 N 拍，其它元件忽略这个字段。</summary>
        public int DelayTicks = 1;

        /// <summary>
        /// 这种元件旋转有没有意义：延迟器四面等价，转它既看不出变化、也不改行为，
        /// 所以手牌 / 游戏内编辑器的 R 键要跳过它（避免玩家以为"转了就会换向"）。
        /// </summary>
        public bool SupportsRotation => Kind != CellKind.DelayGate;

        /// <summary>
        /// 来源类型资产的标识（与 ElementTypeSO.Key 同源）。空字符串表示手工摆的、没有归属的元件。
        /// 玩家拾回元件时靠它把牌还回正确地那摞，不靠类型猜。
        /// </summary>
        public string TypeKey = string.Empty;

        /// <summary>按类型给一份能直接玩的默认配置。</summary>
        public static EditableElement CreateDefault(CellKind kind, GridPoint coord)
        {
            EditableElement element = new EditableElement
            {
                Kind = kind,
                Coord = coord,
            };

            switch (kind)
            {
                case CellKind.Emitter:
                    element.PrimaryDir = Dir.Right;
                    element.Sequence = "1";
                    element.Count = 1;
                    break;

                case CellKind.Receiver:
                    element.PrimaryDir = Dir.Down; // 进料面：位要朝上走进去
                    element.Sequence = "1";
                    element.Count = 1;
                    element.AcceptAnyFace = true;  // 默认四面可收：围一圈只留一个口是最好上手的关卡
                    break;

                case CellKind.Splitter:
                    element.PrimaryDir = Dir.Up;      // OneArm
                    element.SecondaryDir = Dir.Right; // ZeroArm
                    break;

                case CellKind.NotGate:
                case CellKind.Filter:
                    element.PrimaryDir = Dir.Right;
                    break;

                case CellKind.DelayGate:
                    element.DelayTicks = 1;
                    break;
            }

            return element;
        }

        /// <summary>把逻辑定义读回可编辑配置（进入编辑模式时用来载入现有关卡）。</summary>
        public static EditableElement FromDefinition(ElementDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            EditableElement element = CreateDefault(definition.Kind, definition.Coord);
            element.TypeKey = definition.TypeKey;

            switch (definition)
            {
                case EmitterDefinition emitter:
                    element.PrimaryDir = emitter.Facing;
                    element.Sequence = emitter.Sequence;
                    element.Count = emitter.EmitCount;
                    break;

                case ReceiverDefinition receiver:
                    element.PrimaryDir = receiver.FeedFace;
                    element.Sequence = receiver.TargetSequence;
                    element.Count = receiver.TargetCount;
                    element.AcceptAnyFace = receiver.AcceptAnyFace;
                    break;

                case SplitterDefinition splitter:
                    element.PrimaryDir = splitter.OneArm;
                    element.SecondaryDir = splitter.ZeroArm;
                    break;

                case NotGateDefinition notGate:
                    element.PrimaryDir = notGate.OutputDir;
                    break;

                case FilterDefinition filter:
                    element.PrimaryDir = filter.OutputDir;
                    element.PassValue = filter.PassValue;
                    break;

                case DelayGateDefinition delayGate:
                    element.DelayTicks = delayGate.SafeDelayTicks;
                    break;
            }

            return element;
        }

        public EditableElement Clone()
        {
            return new EditableElement
            {
                Kind = Kind,
                Coord = Coord,
                PrimaryDir = PrimaryDir,
                SecondaryDir = SecondaryDir,
                Sequence = Sequence,
                Count = Count,
                AcceptAnyFace = AcceptAnyFace,
                PassValue = PassValue,
                DelayTicks = DelayTicks,
                TypeKey = TypeKey,
            };
        }

        /// <summary>整体顺时针转 90°（主/副朝向一起转，与关卡里的 rotationSteps 语义一致）。</summary>
        public void Rotate()
        {
            PrimaryDir = PrimaryDir.RotateClockwise();
            SecondaryDir = SecondaryDir.RotateClockwise();
        }

        /// <summary>生成模拟器用的定义。id 由草稿统一分配，保证同一份草稿每次重建都拿到同样的 id。</summary>
        public ElementDefinition ToDefinition(int id)
        {
            switch (Kind)
            {
                case CellKind.Emitter:
                    return new EmitterDefinition
                    {
                        Id = id,
                        Coord = Coord,
                        Facing = PrimaryDir,
                        Sequence = NormalizeSequence(Sequence),
                        EmitCount = Math.Max(0, Count),
                    };

                case CellKind.Receiver:
                    return new ReceiverDefinition
                    {
                        Id = id,
                        Coord = Coord,
                        FeedFace = PrimaryDir,
                        AcceptAnyFace = AcceptAnyFace,
                        TargetSequence = NormalizeSequence(Sequence),
                        TargetCount = Math.Max(0, Count),
                    };

                case CellKind.Splitter:
                    return new SplitterDefinition
                    {
                        Id = id,
                        Coord = Coord,
                        OneArm = PrimaryDir,
                        ZeroArm = SecondaryDir,
                    };

                case CellKind.NotGate:
                    return new NotGateDefinition
                    {
                        Id = id,
                        Coord = Coord,
                        OutputDir = PrimaryDir,
                    };

                case CellKind.Filter:
                    return new FilterDefinition
                    {
                        Id = id,
                        Coord = Coord,
                        OutputDir = PrimaryDir,
                        PassValue = PassValue == 0 ? (byte)0 : (byte)1,
                    };

                case CellKind.DelayGate:
                    return new DelayGateDefinition
                    {
                        Id = id,
                        Coord = Coord,
                        DelayTicks = Math.Max(1, Math.Min(4, DelayTicks)),
                    };

                default:
                    return new WallDefinition { Id = id, Coord = Coord };
            }
        }

        /// <summary>提示框里显示的简短配置说明。</summary>
        public string Summary()
        {
            switch (Kind)
            {
                case CellKind.Emitter:
                    return "发射器  朝" + PrimaryDir.ToChineseName() + "  序列 " + NormalizeSequence(Sequence) + " 共 " + Count + " 位";
                case CellKind.Receiver:
                    return "接收器  进料面" + PrimaryDir.ToChineseName() + (AcceptAnyFace ? "（四面可收）" : "（只认进料面）")
                        + "  需要 " + NormalizeSequence(Sequence) + " 共 " + Count + " 位";
                case CellKind.Splitter:
                    return "分流器  1→" + PrimaryDir.ToChineseName() + "  0→" + SecondaryDir.ToChineseName();
                case CellKind.NotGate:
                    return "非门  取反后向" + PrimaryDir.ToChineseName() + "输出";
                case CellKind.Filter:
                    return "过滤器  只放行 " + PassValue;
                case CellKind.DelayGate:
                    return "延迟器  延迟 " + Math.Max(1, Math.Min(4, DelayTicks)) + " tick  直行穿出";
                default:
                    return "墙";
            }
        }

        /// <summary>空序列会让模拟器当作空元件，这里兜底成 "1"，避免出现"看不见的哑元件"。</summary>
        static string NormalizeSequence(string sequence)
        {
            if (string.IsNullOrEmpty(sequence)) return "1";

            for (int i = 0; i < sequence.Length; i++)
            {
                if (sequence[i] == '0' || sequence[i] == '1') return sequence;
            }
            return "1";
        }
    }

    /// <summary>
    /// 可编辑的关卡草稿：一格最多一个元件，支持放置/替换/删除/旋转，最后转成 <see cref="LevelDefinition"/>。
    /// 纯 C#、不依赖 ScriptableObject，所以游戏内改完只影响内存，不会把 Assets/Data 下的资产改脏。
    /// </summary>
    public sealed partial class LevelDraft
    {
        readonly Dictionary<GridPoint, EditableElement> cells = new Dictionary<GridPoint, EditableElement>();

        public string DisplayName = "CUSTOM LEVEL";
        public float TickInterval = 0.35f;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public LevelDraft(int width, int height, float tickInterval = 0.35f)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            TickInterval = tickInterval <= 0f ? 0.35f : tickInterval;
        }

        /// <summary>从一份现有关卡载入（进入编辑模式时用）。</summary>
        public static LevelDraft FromDefinition(LevelDefinition level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            LevelDraft draft = new LevelDraft(level.Width, level.Height, level.TickInterval)
            {
                DisplayName = level.DisplayName,
            };

            foreach (ElementDefinition element in level.Elements)
            {
                draft.Place(EditableElement.FromDefinition(element));
            }

            // 关卡带手牌就一起载入：工坊改一关「有限元件」的关时，发牌数字不能丢
            foreach (LevelSupplyEntry supply in level.Supplies)
            {
                if (supply == null || string.IsNullOrEmpty(supply.TypeKey)) continue;
                draft.SetStock(supply.TypeKey, supply.Kind, supply.Count);
            }

            return draft;
        }

        public int Count => cells.Count;

        public IEnumerable<EditableElement> Cells => cells.Values;

        public bool InBounds(GridPoint coord)
        {
            return coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Height;
        }

        public EditableElement GetAt(GridPoint coord)
        {
            return cells.TryGetValue(coord, out EditableElement element) ? element : null;
        }

        /// <summary>在格子上放一个默认配置的元件；越界返回 null，该格已有元件则整体替换。</summary>
        public EditableElement PlaceDefault(CellKind kind, GridPoint coord)
        {
            if (!InBounds(coord)) return null;

            EditableElement element = EditableElement.CreateDefault(kind, coord);
            PushHistory();
            cells[coord] = element;
            return element;
        }

        /// <summary>放入一份现成配置；越界返回 false。</summary>
        public bool Place(EditableElement element)
        {
            return Place(element, false);
        }

        /// <summary>
        /// 放入一份现成配置，并标记它是不是关卡预设件。
        /// locked=true 的格子玩家搬不动也删不掉（关卡给定的墙/发射器/接收器都走这条）。
        /// </summary>
        public bool Place(EditableElement element, bool locked)
        {
            if (element == null || !InBounds(element.Coord)) return false;

            PushHistory();
            cells[element.Coord] = element;
            if (locked) lockedCells.Add(element.Coord);
            else lockedCells.Remove(element.Coord);
            return true;
        }

        /// <summary>删除格子里的元件；关卡预设件拒绝删除（作者模式下照删）。</summary>
        public bool Remove(GridPoint coord)
        {
            if (Blocked(coord)) return false;
            if (!cells.ContainsKey(coord)) return false;

            PushHistory();
            lockedCells.Remove(coord);   // 删掉之后那格的锁没有意义了，留着会挡住下一件的放置
            return cells.Remove(coord);
        }

        /// <summary>把格子里的元件顺时针转 90°；空格或关卡预设件返回 false（作者模式下照转）。</summary>
        public bool Rotate(GridPoint coord)
        {
            if (Blocked(coord)) return false;
            if (!cells.TryGetValue(coord, out EditableElement element)) return false;
            // 延迟器四面等价：转了既不改行为也没有视觉变化，直接当作"无需旋转"
            if (!element.SupportsRotation) return false;

            PushHistory();
            element.Rotate();
            return true;
        }

        /// <summary>
        /// 就地改一格的参数（朝向 / 序列 / 位数 / 接收条件），并把这一步压进撤销栈。
        ///
        /// 为什么不把字段直接给 UI 改：没有 <c>PushHistory</c> 的改动撤不回来，
        /// 而玩家改错一位序列时最需要的就是 Ctrl+Z。改完记得让 runner 重建一次舞台
        /// （手牌盘的 <c>CommitDraft</c>），否则玩家看到的还是旧配置。
        /// </summary>
        public bool Edit(GridPoint coord, Action<EditableElement> mutate)
        {
            if (mutate == null) return false;
            if (Blocked(coord)) return false;
            if (!cells.TryGetValue(coord, out EditableElement element)) return false;

            PushHistory();
            mutate(element);
            return true;
        }

        public int CountOf(CellKind kind)
        {
            int count = 0;
            foreach (EditableElement element in cells.Values)
            {
                if (element.Kind == kind) count++;
            }
            return count;
        }

        /// <summary>
        /// 转成模拟器用的关卡。id 按 (y, x) 排序后从 1 递增分配，
        /// 因此同一份草稿无论重建多少次，坐标与 id 的对应关系都完全一致（HUD 读数靠它）。
        /// </summary>
        public LevelDefinition ToDefinition()
        {
            LevelDefinition level = new LevelDefinition
            {
                DisplayName = string.IsNullOrEmpty(DisplayName) ? "CUSTOM LEVEL" : DisplayName,
                Width = Width,
                Height = Height,
                TickInterval = TickInterval,
            };

            List<EditableElement> ordered = new List<EditableElement>(cells.Values);
            ordered.Sort(CompareByCoord);

            int nextId = 1;
            foreach (EditableElement element in ordered)
            {
                ElementDefinition definition = element.ToDefinition(nextId);
                definition.TypeKey = element.TypeKey;

                // 锁定标记 = 玩家搬不动。整份草稿里 lockedCells 是唯一权威，
                // 存档时把它写回 playerEditable，读回来才还是同一把锁。
                definition.PlayerEditable = !lockedCells.Contains(element.Coord);

                level.Elements.Add(definition);
                nextId++;
            }

            // 手牌：用「发过多少张」而不是「还剩多少张」——
            // 作者一边摆预设件一边数牌时，库存会被扣掉，存档里要的是他填的那个数字。
            foreach (KeyValuePair<string, int> pair in granted)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value <= 0) continue;

                level.Supplies.Add(new LevelSupplyEntry
                {
                    TypeKey = pair.Key,
                    Kind = KindOfStock(pair.Key),
                    Count = pair.Value,
                });
            }

            // 有手牌就必须让玩家能动手，否则卡摆在屏幕上却放不下去
            level.AllowBuild = level.Supplies.Count > 0;

            return level;
        }

        static int CompareByCoord(EditableElement left, EditableElement right)
        {
            if (left.Coord.Y != right.Coord.Y) return left.Coord.Y.CompareTo(right.Coord.Y);
            return left.Coord.X.CompareTo(right.Coord.X);
        }
    }
}
