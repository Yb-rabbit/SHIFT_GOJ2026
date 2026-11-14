using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ShiftGo.BitFlow.Core;

namespace ShiftGoParity
{
    /// <summary>
    /// 对拍工装（C# 侧）：把 tests/fixtures-*.json 里的每一关交给**游戏同一份** Core 代码跑，
    /// 把构建结果、问题列表、tick 数、结算、自检意见导出成 expected-csharp.json。
    ///
    /// JS 侧的 tests/parity.js --check 会拿这份输出逐字段比对 —— 这才是"编辑器里测通 = 游戏里能过"的证据。
    /// </summary>
    internal static class Program
    {
        sealed class FixtureFile
        {
            public List<Fixture> fixtures = new List<Fixture>();
        }

        sealed class Fixture
        {
            public string name = string.Empty;
            public string note = string.Empty;
            public LevelDocument document;
        }

        static readonly string[] FixtureFiles = { "fixtures-core.json", "fixtures-edge.json" };

        static int Main(string[] args)
        {
            string testsDir = FindTestsDirectory();
            string outputPath = args.Length > 0 ? args[0] : Path.Combine(testsDir, "expected-csharp.json");

            LevelTypeCatalog catalog = LoadCatalog();

            List<Dictionary<string, object>> cases = new List<Dictionary<string, object>>();
            foreach (string fileName in FixtureFiles)
            {
                string fullPath = Path.Combine(testsDir, fileName);
                FixtureFile file = Deserialize<FixtureFile>(File.ReadAllText(fullPath));
                foreach (Fixture fixture in file.fixtures)
                {
                    cases.Add(Summarize(fixture, catalog));
                }
            }

            Dictionary<string, object> output = new Dictionary<string, object>
            {
                ["engine"] = "ShiftGo Core (C#)",
                ["schema"] = LevelDocument.CurrentSchema,
                ["cases"] = cases
            };

            File.WriteAllText(outputPath, JsonSerializer.Serialize(output, SerializerOptions()) + "\n");
            Console.WriteLine($"C# 侧跑完 {cases.Count} 个 fixture → {outputPath}");
            return 0;
        }

        static string FindTestsDirectory()
        {
            string current = AppContext.BaseDirectory;
            for (int i = 0; i < 6 && current != null; i++)
            {
                string candidate = Path.Combine(current, "fixtures-core.json");
                if (File.Exists(candidate)) return current;
                current = Directory.GetParent(current)?.FullName;
            }
            throw new DirectoryNotFoundException("找不到 tests/fixtures-core.json");
        }

        static LevelTypeCatalog LoadCatalog()
        {
            string current = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && current != null; i++)
            {
                string candidate = Path.Combine(current, "LevelStudio", "web-src", "catalog.json");
                if (File.Exists(candidate)) return Deserialize<LevelTypeCatalog>(File.ReadAllText(candidate));
                candidate = Path.Combine(current, "web-src", "catalog.json");
                if (File.Exists(candidate)) return Deserialize<LevelTypeCatalog>(File.ReadAllText(candidate));
                current = Directory.GetParent(current)?.FullName;
            }
            return new LevelTypeCatalog();
        }

        static JsonSerializerOptions SerializerOptions()
        {
            return new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }

        static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions());
        }

        static Dictionary<string, object> Summarize(Fixture fixture, LevelTypeCatalog catalog)
        {
            List<string> problems = new List<string>();
            LevelDefinition definition = LevelDocumentBuilder.Build(fixture.document, catalog, problems);

            // 与 LevelCheck.Run 完全相同的推进方式：上限取两者较大值。
            BitFlowSimulation simulation = new BitFlowSimulation(definition);
            int safety = Math.Max(LevelCheck.SafetyTicks(definition), simulation.SafetyTickLimit);
            int ticks = 0;
            while (!simulation.IsFinished && ticks < safety)
            {
                simulation.Step();
                ticks++;
            }

            LevelResult result = simulation.BuildResult();
            string report = LevelCheck.Run(definition, out bool success);
            List<LevelCheck.SaveIssue> issues = LevelCheck.RunForSaveDetailed(definition);

            List<Dictionary<string, object>> elements = new List<Dictionary<string, object>>();
            foreach (ElementDefinition element in definition.Elements) elements.Add(ElementSummary(element));

            List<Dictionary<string, object>> supplies = new List<Dictionary<string, object>>();
            foreach (LevelSupplyEntry supply in definition.Supplies)
            {
                supplies.Add(new Dictionary<string, object>
                {
                    ["typeKey"] = supply.TypeKey ?? string.Empty,
                    ["kind"] = supply.Kind.ToString(),
                    ["count"] = supply.Count
                });
            }

            List<Dictionary<string, object>> receivers = new List<Dictionary<string, object>>();
            foreach (ReceiverResult receiver in result.Receivers)
            {
                receivers.Add(new Dictionary<string, object>
                {
                    ["id"] = receiver.ReceiverId,
                    ["targetCount"] = receiver.TargetCount,
                    ["receivedCount"] = receiver.ReceivedCount,
                    ["received"] = new List<byte>(receiver.Received),
                    ["matched"] = new List<bool>(receiver.Matched),
                    ["isSuccess"] = receiver.IsSuccess
                });
            }

            List<Dictionary<string, object>> issueSummaries = new List<Dictionary<string, object>>();
            foreach (LevelCheck.SaveIssue issue in issues)
            {
                issueSummaries.Add(new Dictionary<string, object>
                {
                    ["severity"] = issue.Severity.ToString(),
                    ["message"] = issue.Message
                });
            }

            return new Dictionary<string, object>
            {
                ["name"] = fixture.name,
                ["width"] = definition.Width,
                ["height"] = definition.Height,
                ["tickInterval"] = Math.Round(definition.TickInterval, 3),
                ["displayName"] = definition.DisplayName,
                ["allowBuild"] = definition.AllowBuild,
                ["elements"] = elements,
                ["supplies"] = supplies,
                ["problems"] = problems,
                ["ticks"] = ticks,
                ["success"] = success,
                ["finished"] = simulation.IsFinished,
                ["report"] = report,
                ["receivers"] = receivers,
                ["issues"] = issueSummaries
            };
        }

        static Dictionary<string, object> ElementSummary(ElementDefinition element)
        {
            Dictionary<string, object> summary = new Dictionary<string, object>
            {
                ["kind"] = element.Kind.ToString(),
                ["id"] = element.Id,
                ["x"] = element.Coord.X,
                ["y"] = element.Coord.Y,
                ["dir"] = null,
                ["dir2"] = null,
                ["sequence"] = null,
                ["count"] = null,
                ["acceptAnyFace"] = null,
                ["pass"] = null,
                ["delay"] = null,
                ["playerEditable"] = element.PlayerEditable,
                ["typeKey"] = element.TypeKey ?? string.Empty,
                ["label"] = element.Label ?? string.Empty
            };

            switch (element)
            {
                case EmitterDefinition emitter:
                    summary["dir"] = (int)emitter.Facing;
                    summary["sequence"] = emitter.Sequence;
                    summary["count"] = emitter.EmitCount;
                    break;

                case ReceiverDefinition receiver:
                    summary["dir"] = (int)receiver.FeedFace;
                    summary["sequence"] = receiver.TargetSequence;
                    summary["count"] = receiver.TargetCount;
                    summary["acceptAnyFace"] = receiver.AcceptAnyFace;
                    break;

                case SplitterDefinition splitter:
                    summary["dir"] = (int)splitter.OneArm;
                    summary["dir2"] = (int)splitter.ZeroArm;
                    break;

                case NotGateDefinition notGate:
                    summary["dir"] = (int)notGate.OutputDir;
                    break;

                case FilterDefinition filter:
                    summary["dir"] = (int)filter.OutputDir;
                    summary["pass"] = filter.PassValue;
                    break;

                case DelayGateDefinition delayGate:
                    summary["delay"] = delayGate.DelayTicks;
                    break;
            }

            return summary;
        }
    }
}

