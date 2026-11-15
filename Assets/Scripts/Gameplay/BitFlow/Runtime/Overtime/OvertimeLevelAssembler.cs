using System.Collections.Generic;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 把 <see cref="OvertimeLayout"/> 变成真正的 <see cref="LevelDefinition"/>。
    ///
    /// 两个入口：
    /// - <see cref="BuildPlayerLevel"/>：玩家看到的题面，转角格为空、手牌是标准解里的件；
    /// - <see cref="BuildSolvedLevel"/>：把标准解 / 求解器候选摆上去，交给模拟器判定。
    /// </summary>
    public static class OvertimeLevelAssembler
    {
        public static LevelDefinition BuildPlayerLevel(OvertimeLayout layout, int index)
        {
            LevelDefinition definition = BuildBase(layout, OvertimeConfig.DisplayName(index));

            Dictionary<string, LevelSupplyEntry> supplies = new Dictionary<string, LevelSupplyEntry>();
            foreach (OvertimePiece piece in layout.Pieces)
            {
                if (piece == null || string.IsNullOrEmpty(piece.TypeKey)) continue;

                if (!supplies.TryGetValue(piece.TypeKey, out LevelSupplyEntry entry))
                {
                    entry = new LevelSupplyEntry
                    {
                        TypeKey = piece.TypeKey,
                        Kind = piece.Kind,
                        Count = 0,
                    };
                    supplies[piece.TypeKey] = entry;
                    definition.Supplies.Add(entry);
                }

                entry.Count++;
            }

            definition.AllowBuild = definition.Supplies.Count > 0;
            return definition;
        }

        public static LevelDefinition BuildSolvedLevel(OvertimeLayout layout, int[] slotPieces, int[] slotRotations)
        {
            LevelDefinition definition = BuildBase(layout, OvertimeConfig.DisplayName(1));
            if (slotPieces == null) return definition;

            int nextId = definition.Elements.Count + 1;
            for (int slot = 0; slot < layout.Slots.Count; slot++)
            {
                int pieceIndex = slotPieces[slot];
                if (pieceIndex < 0 || pieceIndex >= layout.Pieces.Count) continue;

                int rotation = slotRotations != null && slot < slotRotations.Length ? slotRotations[slot] : 0;
                OvertimePiece piece = layout.Pieces[pieceIndex];
                ElementDefinition element = piece.CreateDefinition(layout.Slots[slot].Coord, nextId, rotation);
                if (element == null) continue;

                element.PlayerEditable = true;
                definition.Elements.Add(element);
                nextId++;
            }

            return definition;
        }

        public static LevelDefinition BuildBase(OvertimeLayout layout, string displayName)
        {
            LevelDefinition definition = new LevelDefinition
            {
                DisplayName = displayName,
                Width = layout.Width,
                Height = layout.Height,
                TickInterval = OvertimeConfig.TickInterval,
            };

            int id = 1;

            if (layout.Emitters.Count > 0)
            {
                foreach (OvertimeEmitterSpec emitter in layout.Emitters)
                {
                    definition.Elements.Add(new EmitterDefinition
                    {
                        Id = id++,
                        Coord = emitter.Coord,
                        Facing = emitter.Facing,
                        Sequence = string.IsNullOrEmpty(emitter.Sequence) ? "1" : emitter.Sequence,
                        EmitCount = emitter.EmitCount,
                        PlayerEditable = false,
                        TypeKey = string.IsNullOrEmpty(emitter.TypeKey) ? "Type_Emitter" : emitter.TypeKey,
                    });
                }
            }
            else
            {
                definition.Elements.Add(new EmitterDefinition
                {
                    Id = id++,
                    Coord = layout.EmitterCoord,
                    Facing = layout.EmitterFacing,
                    Sequence = layout.StartValue.ToString(),
                    EmitCount = 1,
                    PlayerEditable = false,
                    TypeKey = "Type_Emitter",
                });
            }

            if (layout.Receivers.Count > 0)
            {
                foreach (OvertimeReceiverSpec receiver in layout.Receivers)
                {
                    definition.Elements.Add(new ReceiverDefinition
                    {
                        Id = id++,
                        Coord = receiver.Coord,
                        FeedFace = receiver.FeedFace,
                        TargetSequence = string.IsNullOrEmpty(receiver.TargetSequence) ? "1" : receiver.TargetSequence,
                        TargetCount = receiver.TargetCount,
                        AcceptAnyFace = receiver.AcceptAnyFace,
                        PlayerEditable = false,
                        TypeKey = string.IsNullOrEmpty(receiver.TypeKey) ? "Type_Receiver" : receiver.TypeKey,
                    });
                }
            }
            else
            {
                definition.Elements.Add(new ReceiverDefinition
                {
                    Id = id++,
                    Coord = layout.ReceiverCoord,
                    FeedFace = layout.ReceiverFeedFace,
                    TargetSequence = layout.TargetValue.ToString(),
                    TargetCount = 1,
                    AcceptAnyFace = true,
                    PlayerEditable = false,
                    TypeKey = "Type_Receiver",
                });
            }

            foreach (GridPoint wall in layout.Walls)
            {
                definition.Elements.Add(new WallDefinition
                {
                    Id = id++,
                    Coord = wall,
                    PlayerEditable = false,
                    TypeKey = "Type_Wall",
                });
            }

            return definition;
        }
    }
}
