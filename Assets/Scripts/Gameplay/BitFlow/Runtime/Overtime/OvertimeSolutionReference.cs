using System.Collections.Generic;
using System.Text;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 把固定 OVERTIME 题库导出成人类可读的解题参考。
    ///
    /// 运行时只负责拼 Markdown，不碰文件系统；编辑器菜单
    /// <c>Tools/BitFlow/Export OVERTIME Solutions</c> 负责写到 <c>docs/OVERTIME_SOLUTIONS.md</c>。
    /// </summary>
    public static class OvertimeSolutionReference
    {
        public static string BuildMarkdown()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# OVERTIME Solutions");
            builder.AppendLine();
            builder.AppendLine("Generated from `OvertimeCollisionCatalog`. " + OvertimeConfig.MaxProblems + " fixed problems.");
            builder.AppendLine();

            for (int index = OvertimeConfig.FirstProblem; index <= OvertimeConfig.LastProblem; index++)
            {
                AppendProblem(builder, OvertimeCatalog.Build(index));
            }

            return builder.ToString();
        }

        static void AppendProblem(StringBuilder builder, OvertimeLayout layout)
        {
            builder.Append("## ").Append(layout.Index.ToString("00")).Append(" · ").Append(layout.Title).AppendLine();
            builder.AppendLine();
            builder.Append("- File: `").Append(OvertimeConfig.FileName(layout.Index)).AppendLine("`");
            builder.Append("- Grid: ").Append(layout.Width).Append('x').Append(layout.Height)
                .Append(" · Emitters: ").Append(layout.Emitters.Count)
                .Append(" · Receivers: ").Append(layout.Receivers.Count)
                .Append(" · Slots: ").Append(layout.Slots.Count)
                .Append(" · Expected conflicts: ").Append(layout.ExpectedConflictPairs).AppendLine(" pair(s)");
            builder.AppendLine();

            AppendBoard(builder, layout);
            builder.AppendLine();

            builder.AppendLine("| Slot | Coord | Piece | Rotation |");
            builder.AppendLine("| ---: | :---: | :--- | ---: |");
            foreach (OvertimePlacement placement in layout.IntendedPlacements)
            {
                OvertimePiece piece = layout.Pieces[placement.PieceIndex];
                GridPoint coord = layout.Slots[placement.SlotIndex].Coord;
                builder.Append("| ").Append(placement.SlotIndex + 1)
                    .Append(" | ").Append(coord.X).Append(',').Append(coord.Y)
                    .Append(" | ").Append(PieceName(piece))
                    .Append(" | ").Append(placement.Rotation)
                    .AppendLine(" |");
            }
            builder.AppendLine();

            builder.AppendLine("Hand order:");
            builder.AppendLine();
            foreach (OvertimePlacement placement in layout.IntendedPlacements)
            {
                OvertimePiece piece = layout.Pieces[placement.PieceIndex];
                GridPoint coord = layout.Slots[placement.SlotIndex].Coord;
                builder.Append("- ").Append(PieceName(piece))
                    .Append(" → (").Append(coord.X).Append(',').Append(coord.Y).Append(')')
                    .Append(" rot ").AppendLine(placement.Rotation.ToString());
            }
            builder.AppendLine();
        }

        static void AppendBoard(StringBuilder builder, OvertimeLayout layout)
        {
            Dictionary<GridPoint, string> marks = new Dictionary<GridPoint, string>();
            foreach (GridPoint wall in layout.Walls) marks[wall] = "#";
            foreach (GridPoint path in layout.PathCells) marks[path] = ".";
            for (int i = 0; i < layout.Slots.Count; i++) marks[layout.Slots[i].Coord] = "+";
            foreach (OvertimeEmitterSpec emitter in layout.Emitters) marks[emitter.Coord] = "E";
            foreach (OvertimeReceiverSpec receiver in layout.Receivers) marks[receiver.Coord] = "R";

            builder.AppendLine("```");
            builder.Append("    ");
            for (int x = 0; x < layout.Width; x++) builder.Append((x % 10).ToString());
            builder.AppendLine();

            for (int y = 0; y < layout.Height; y++)
            {
                builder.Append(y.ToString("00")).Append("  ");
                for (int x = 0; x < layout.Width; x++)
                {
                    marks.TryGetValue(new GridPoint(x, y), out string mark);
                    builder.Append(string.IsNullOrEmpty(mark) ? " " : mark);
                }
                builder.AppendLine();
            }

            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine("Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall");
        }

        static string PieceName(OvertimePiece piece)
        {
            switch (piece.Kind)
            {
                case CellKind.NotGate: return "NOT";
                case CellKind.Filter: return piece.PassValue == 1 ? "FILTER 1" : "FILTER 0";
                case CellKind.Splitter: return "SPLITTER";
                case CellKind.DelayGate: return "DELAY " + piece.DelayTicks;
                default: return piece.Kind.ToString();
            }
        }
    }
}
