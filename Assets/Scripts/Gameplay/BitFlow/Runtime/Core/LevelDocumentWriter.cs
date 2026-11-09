using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// <see cref="LevelDefinition"/> → <see cref="LevelDocument"/>（纯 C#）。
    ///
    /// 输出的格式刻意选「kind 模式 + 参数全写」：这样这份 JSON 完全自描述，
    /// 不依赖任何类型资产就能被 <see cref="LevelDocumentBuilder"/> 还原成一模一样的关卡。
    /// 外部编辑器（Tools/LevelStudio）存盘、以及测试里做「资产 → JSON → 关卡」无损校验都走它。
    /// </summary>
    public static class LevelDocumentWriter
    {
        public static LevelDocument FromDefinition(LevelDefinition definition, List<string> problems = null)
        {
            LevelDocument document = new LevelDocument();
            if (definition == null) return document;

            int width = Math.Max(1, definition.Width);
            int height = Math.Max(1, definition.Height);

            document.displayName = definition.DisplayName ?? string.Empty;
            document.width = width;
            document.height = height;
            document.tickInterval = definition.TickInterval;

            List<ElementDefinition> ordered = new List<ElementDefinition>();
            foreach (ElementDefinition element in definition.Elements)
            {
                if (element != null) ordered.Add(element);
            }

            // 和 LevelSO.ToDefinition 一样按 (y, x) 排；墙单独收进 wallRows，其余写进 elements
            ordered.Sort(CompareElements);

            HashSet<GridPoint> occupied = new HashSet<GridPoint>();
            bool anyWall = false;
            char[,] wallGrid = new char[height, width];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) wallGrid[y, x] = '.';
            }

            foreach (ElementDefinition element in ordered)
            {
                if (element.Kind == CellKind.Wall)
                {
                    if (element.Coord.X < 0 || element.Coord.Y < 0) continue;
                    if (element.Coord.X >= width || element.Coord.Y >= height) continue;
                    if (occupied.Contains(element.Coord)) continue;

                    wallGrid[element.Coord.Y, element.Coord.X] = '#';
                    anyWall = true;
                    continue;
                }

                LevelDocumentCell cell = ToCell(element);
                if (cell == null)
                {
                    Add(problems, "元件 " + element + " 没法写回文档");
                    continue;
                }

                occupied.Add(element.Coord);
                document.elements.Add(cell);
            }

            if (anyWall)
            {
                document.wallRows = new string[height];
                char[] buffer = new char[width];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++) buffer[x] = wallGrid[y, x];
                    document.wallRows[y] = new string(buffer);
                }
            }

            WriteSupplies(definition, document, problems);
            document.allowBuild = definition.AllowBuild;

            return document;
        }

        /// <summary>
        /// 手牌写回文档。
        ///
        /// 同时写 <c>type</c> 与 <c>kind</c>：<c>type</c> 是权威（读回来直接命中那一个类型资产），
        /// <c>kind</c> 是「资产改名/删除以后还能按种类找到替身」的兜底 —— 和 elements 的写法一致。
        /// </summary>
        static void WriteSupplies(LevelDefinition definition, LevelDocument document, List<string> problems)
        {
            if (definition.Supplies == null || definition.Supplies.Count == 0) return;

            foreach (LevelSupplyEntry supply in definition.Supplies)
            {
                if (supply == null) continue;

                int count = supply.Count;
                if (count <= 0)
                {
                    // 0 张等于「不给这张牌」，读的一方也会跳过它 —— 直接不写，省得存档里出现迷惑条目
                    Add(problems, "手牌 " + supply.TypeKey + " 的张数是 " + count + "，已跳过");
                    continue;
                }

                document.supplies.Add(new LevelDocumentSupply
                {
                    type = supply.TypeKey ?? string.Empty,
                    kind = supply.Kind.ToString(),
                    count = count,
                });
            }
        }

        static int CompareElements(ElementDefinition left, ElementDefinition right)
        {
            if (left.Coord.Y != right.Coord.Y) return left.Coord.Y.CompareTo(right.Coord.Y);
            return left.Coord.X.CompareTo(right.Coord.X);
        }

        static LevelDocumentCell ToCell(ElementDefinition element)
        {
            LevelDocumentCell cell = new LevelDocumentCell
            {
                kind = element.Kind.ToString(),
                x = element.Coord.X,
                y = element.Coord.Y,
                type = string.Empty,
                rot = 0,

                // 作者在工坊里给这一格打的「固定 / 自由」标记。不写这一行，
                // 存出来的每一件都会退回默认的 false —— 也就是"全部锁死"，
                // 玩家下一局进来一个都搬不动（这正是之前手牌关卡搬不动元件的原因）。
                playerEditable = element.PlayerEditable,
            };

            switch (element)
            {
                case EmitterDefinition emitter:
                    cell.dir = emitter.Facing.ToString();
                    cell.sequence = emitter.Sequence ?? "1";
                    cell.count = Math.Max(0, emitter.EmitCount);
                    break;

                case ReceiverDefinition receiver:
                    cell.dir = receiver.FeedFace.ToString();
                    cell.sequence = receiver.TargetSequence ?? "1";
                    cell.count = Math.Max(0, receiver.TargetCount);
                    cell.face = receiver.AcceptAnyFace ? "any" : "in";
                    break;

                case SplitterDefinition splitter:
                    cell.dir = splitter.OneArm.ToString();
                    cell.dir2 = splitter.ZeroArm.ToString();
                    break;

                case NotGateDefinition notGate:
                    cell.dir = notGate.OutputDir.ToString();
                    break;

                case FilterDefinition filter:
                    cell.dir = filter.OutputDir.ToString();
                    cell.pass = filter.PassValue == 0 ? 0 : 1;
                    break;

                default:
                    return null;
            }

            return cell;
        }

        static void Add(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
