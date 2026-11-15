using System;
using System.Collections.Generic;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>唯一解证明的结论。TimedOut 为 true 时绝不能把题写盘。</summary>
    public sealed class OvertimeSolveResult
    {
        public int SolutionCount;
        public bool TimedOut;
        public int NodeCount;
        public int Ticks;
        public int[] SolutionPieces;
        public int[] SolutionRotations;

        public bool IsUnique => !TimedOut && SolutionCount == 1;
        public bool HasSolution => SolutionCount > 0;
    }

    /// <summary>
    /// 在生成器给定的“转角格 + 手牌”上穷举所有放置与旋转，用同一个
    /// <see cref="BitFlowSimulation"/> 跑通关，最多找到两个解。
    ///
    /// 为什么可以只枚举转角格：走廊之外的格子是固定墙，路径上的空格不能放件成功
    /// （少一个转角，位会撞墙）。碰撞题也一样：作者只把必须填的空位留给玩家，
    /// 其余格子全是墙；求解器枚举这些空位的排列 + 旋转，用同一个模拟器验证
    /// "两条位流在该撞的 tick 撞掉、该到的位全部到齐"。
    /// </summary>
    public static class OvertimeSolver
    {
        public static OvertimeSolveResult Solve(OvertimeLayout layout, int maxSolutions = 2, int maxNodes = 200000)
        {
            OvertimeSolveResult result = new OvertimeSolveResult();
            if (layout == null || layout.Slots.Count == 0 || layout.Pieces.Count != layout.Slots.Count)
            {
                result.TimedOut = false;
                return result;
            }

            int count = layout.Slots.Count;
            int[] slotPieces = new int[count];
            int[] slotRotations = new int[count];
            bool[] used = new bool[layout.Pieces.Count];
            for (int i = 0; i < count; i++) slotPieces[i] = -1;

            HashSet<string> successful = new HashSet<string>();
            int nodes = 0;
            int wanted = Math.Max(2, maxSolutions);
            bool timedOut = false;
            int[] firstPieces = null;
            int[] firstRotations = null;
            int firstTicks = 0;

            Search(0);

            result.NodeCount = nodes;
            result.TimedOut = timedOut;
            result.SolutionCount = successful.Count;
            result.Ticks = firstTicks;
            result.SolutionPieces = firstPieces;
            result.SolutionRotations = firstRotations;
            return result;

            void Search(int pieceIndex)
            {
                if (timedOut || successful.Count >= wanted) return;
                if (nodes >= maxNodes)
                {
                    timedOut = true;
                    return;
                }

                if (pieceIndex >= layout.Pieces.Count)
                {
                    string key = AssignmentKey(layout, slotPieces, slotRotations);
                    if (successful.Contains(key)) return;

                    LevelDefinition definition = OvertimeLevelAssembler.BuildSolvedLevel(layout, slotPieces, slotRotations);
                    if (!Run(definition, out int ticks)) return;

                    successful.Add(key);
                    if (firstPieces == null)
                    {
                        firstPieces = (int[])slotPieces.Clone();
                        firstRotations = (int[])slotRotations.Clone();
                        firstTicks = ticks;
                    }
                    return;
                }

                OvertimePiece piece = layout.Pieces[pieceIndex];
                int previousSameSlot = -1;
                if (pieceIndex > 0 && SamePiece(piece, layout.Pieces[pieceIndex - 1]))
                {
                    for (int search = 0; search < count; search++)
                    {
                        if (slotPieces[search] == pieceIndex - 1)
                        {
                            previousSameSlot = search;
                            break;
                        }
                    }
                }

                for (int slot = 0; slot < count; slot++)
                {
                    if (used[pieceIndex] || slotPieces[slot] >= 0) continue;
                    if (previousSameSlot >= 0 && slot <= previousSameSlot) continue;

                    for (int rotation = 0; rotation < piece.RotationCount; rotation++)
                    {
                        if (nodes >= maxNodes)
                        {
                            timedOut = true;
                            return;
                        }
                        nodes++;

                        slotPieces[slot] = pieceIndex;
                        slotRotations[slot] = rotation;
                        used[pieceIndex] = true;
                        Search(pieceIndex + 1);
                        used[pieceIndex] = false;
                        slotPieces[slot] = -1;
                        slotRotations[slot] = 0;

                        if (timedOut || successful.Count >= wanted) return;
                    }
                }
            }
        }

        static bool Run(LevelDefinition definition, out int ticks)
        {
            ticks = 0;
            BitFlowSimulation simulation = new BitFlowSimulation(definition);
            int safety = Math.Max(LevelCheck.SafetyTicks(definition), simulation.SafetyTickLimit);

            while (!simulation.IsFinished && ticks < safety)
            {
                simulation.Step();
                ticks++;
            }

            return simulation.BuildResult().IsSuccess;
        }

        static bool SamePiece(OvertimePiece left, OvertimePiece right)
        {
            if (left == null || right == null) return false;
            return left.Kind == right.Kind
                && string.Equals(left.TypeKey, right.TypeKey, StringComparison.Ordinal)
                && left.PassValue == right.PassValue
                && left.DelayTicks == right.DelayTicks;
        }

        static string AssignmentKey(OvertimeLayout layout, int[] slotPieces, int[] slotRotations)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int slot = 0; slot < slotPieces.Length; slot++)
            {
                int pieceIndex = slotPieces[slot];
                if (pieceIndex < 0)
                {
                    builder.Append("-;");
                    continue;
                }

                OvertimePiece piece = layout.Pieces[pieceIndex];
                builder.Append(piece.TypeKey).Append(':').Append(slotRotations[slot]).Append(';');
            }
            return builder.ToString();
        }
    }
}
