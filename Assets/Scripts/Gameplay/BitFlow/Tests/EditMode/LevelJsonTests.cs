using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 关卡 JSON 通道测试：导出是否幂等、往返是否无损、两种写法（type / kind）能不能混用、
    /// 覆盖参数与手牌是否照字面落地、坏数据会不会变成可读的警告而不是异常。
    ///
    /// 为什么测试自己实现一个 <see cref="ILevelJsonTypeResolver"/>：正式的解析器要造/改类型资产，
    /// 属于编辑器工具（Assembly-CSharp-Editor，测试程序集引用不到）。序列化核心放在 Runtime，
    /// 正是为了让这一层能被这里直接驱动。
    /// </summary>
    public class LevelJsonTests
    {
        const string LevelAssetPath = "Assets/Data/Levels/Level_01.asset";

        readonly List<UnityEngine.Object> spawned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object item in spawned)
            {
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            }
            spawned.Clear();
        }

        // ------------------------------------------------------------------ 导出 / 往返

        [Test]
        public void Export_Import_Export_IsIdentical()
        {
            LevelSO source = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(source, "缺少关卡资产：" + LevelAssetPath);

            string first = LevelJsonSerializer.ToJson(source);
            Assert.IsFalse(string.IsNullOrEmpty(first), "导出的 JSON 不该是空的");

            LevelJsonReadResult read = ReadOrFail(first);
            string second = LevelJsonSerializer.ToJson(read.Level);

            Assert.AreEqual(first, second, "导出 → 导入 → 再导出必须字节一致（否则 git diff 会噪声不断）");
        }

        [Test]
        public void Export_KeepsEveryElementSemantics()
        {
            LevelSO source = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(source, "缺少关卡资产：" + LevelAssetPath);

            LevelJsonReadResult read = ReadOrFail(LevelJsonSerializer.ToJson(source));

            LevelDefinition expected = source.ToDefinition();
            LevelDefinition actual = read.Level.ToDefinition();

            Assert.AreEqual(expected.Width, actual.Width);
            Assert.AreEqual(expected.Height, actual.Height);
            Assert.AreEqual(expected.TickInterval, actual.TickInterval, 0.0001f);

            // 导入时统一按 (y, x) 重排了顺序，所以按坐标配对来比，而不是按下标
            Dictionary<GridPoint, ElementDefinition> byCoord = actual.BuildLookup();
            Assert.AreEqual(expected.Elements.Count, byCoord.Count, "元件总数（含墙）必须一致");

            foreach (ElementDefinition expectedElement in expected.Elements)
            {
                Assert.IsTrue(byCoord.TryGetValue(expectedElement.Coord, out ElementDefinition actualElement),
                    "往返后丢了坐标 " + expectedElement.Coord + " 的元件");
                Assert.AreEqual(expectedElement.Kind, actualElement.Kind, "坐标 " + expectedElement.Coord + " 的种类");

                if (expectedElement is EmitterDefinition expectedEmitter)
                {
                    EmitterDefinition actualEmitter = actualElement as EmitterDefinition;
                    Assert.IsNotNull(actualEmitter);
                    Assert.AreEqual(expectedEmitter.Facing, actualEmitter.Facing);
                    Assert.AreEqual(expectedEmitter.Sequence, actualEmitter.Sequence);
                    Assert.AreEqual(expectedEmitter.EmitCount, actualEmitter.EmitCount);
                }
                else if (expectedElement is ReceiverDefinition expectedReceiver)
                {
                    ReceiverDefinition actualReceiver = actualElement as ReceiverDefinition;
                    Assert.IsNotNull(actualReceiver);
                    Assert.AreEqual(expectedReceiver.FeedFace, actualReceiver.FeedFace);
                    Assert.AreEqual(expectedReceiver.TargetSequence, actualReceiver.TargetSequence);
                    Assert.AreEqual(expectedReceiver.TargetCount, actualReceiver.TargetCount);
                    Assert.AreEqual(expectedReceiver.AcceptAnyFace, actualReceiver.AcceptAnyFace);
                }
                else if (expectedElement is SplitterDefinition expectedSplitter)
                {
                    SplitterDefinition actualSplitter = actualElement as SplitterDefinition;
                    Assert.IsNotNull(actualSplitter);
                    Assert.AreEqual(expectedSplitter.OneArm, actualSplitter.OneArm);
                    Assert.AreEqual(expectedSplitter.ZeroArm, actualSplitter.ZeroArm);
                }
                else if (expectedElement is FilterDefinition expectedFilter)
                {
                    FilterDefinition actualFilter = actualElement as FilterDefinition;
                    Assert.IsNotNull(actualFilter);
                    Assert.AreEqual(expectedFilter.OutputDir, actualFilter.OutputDir);
                    Assert.AreEqual(expectedFilter.PassValue, actualFilter.PassValue);
                }
            }
        }

        // ------------------------------------------------------------------ 墙层 / 覆盖参数 / 手牌

        [Test]
        public void WallRows_And_WallMap_ProduceTheSameWalls()
        {
            const string rowsJson =
                "{ \"width\": 4, \"height\": 3, \"wallRows\": [ \"#..#\", \"....\", \"####\" ] }";
            const string mapJson =
                "{ \"width\": 4, \"height\": 3, \"wallMap\": \"#..#\\n....\\n####\" }";

            LevelSO fromRows = ReadOrFail(rowsJson).Level;
            LevelSO fromMap = ReadOrFail(mapJson).Level;

            List<Vector2Int> rows = new List<Vector2Int>(fromRows.ParseWalls());
            List<Vector2Int> map = new List<Vector2Int>(fromMap.ParseWalls());

            Assert.AreEqual(6, rows.Count, "3 行字符画里一共 6 个 #");
            CollectionAssert.AreEquivalent(map, rows, "两种写法必须画出同一批墙");
            Assert.IsNotNull(fromRows.wallType, "有墙就必须带上墙类型资产，否则墙会被静默忽略");
        }

        [Test]
        public void Overrides_AreRecordedEvenWhenTheValueIsZero()
        {
            const string json = "{ \"width\": 5, \"height\": 1, \"elements\": ["
                + "{ \"kind\": \"Emitter\", \"x\": 0, \"y\": 0, \"dir\": \"Right\", \"sequence\": \"1011\", \"count\": 0 }"
                + "] }";

            LevelSO level = ReadOrFail(json).Level;
            Assert.AreEqual(1, level.placements.Count);

            ElementPlacement placement = level.placements[0];
            Assert.IsTrue(placement.overrideSequence, "写了 sequence 就该开启覆盖");
            Assert.AreEqual("1011", placement.sequence);
            Assert.IsTrue(placement.overrideCount, "count = 0 是合法值，不能被当成「没写」");
            Assert.AreEqual(0, placement.count);

            EmitterDefinition emitter = (EmitterDefinition)level.ToDefinition().Elements[0];
            Assert.AreEqual(0, emitter.EmitCount);

            StringAssert.Contains("\"count\": 0", LevelJsonSerializer.ToJson(level),
                "0 值覆盖也必须导出，否则往返会丢信息");
        }

        [Test]
        public void Supplies_StackUpPerType()
        {
            const string json = "{ \"width\": 5, \"height\": 3,"
                + " \"elements\": [ { \"kind\": \"Filter\", \"x\": 1, \"y\": 1, \"dir\": \"Right\", \"pass\": 0 } ],"
                + " \"supplies\": ["
                + "   { \"kind\": \"Filter\", \"count\": 2 },"
                + "   { \"kind\": \"Filter\", \"count\": 3 }"
                + " ] }";

            LevelSO level = ReadOrFail(json).Level;
            Assert.AreEqual(1, level.placements.Count);
            Assert.AreEqual(2, level.supplies.Count, "JSON 里写了几条就几条，导出时也不会被合并");

            // 同一个类型写两条 = 再多给几张（LevelSO.CreatePlayerDraft 的累加语义）
            LevelDraft draft = level.CreatePlayerDraft();
            Assert.IsTrue(draft.StockEnabled);
            Assert.AreEqual(5, draft.Remaining(level.supplies[0].type.Key));
        }

        // ------------------------------------------------------------------ 宽松写法 / 诊断

        [Test]
        public void KindMode_CarriesDirectionSequenceAndCounting()
        {
            const string json = "{ \"width\": 6, \"height\": 4, \"elements\": ["
                + "{ \"kind\": \"Receiver\", \"x\": 5, \"y\": 2, \"dir\": \"Left\", \"sequence\": \"110\", \"count\": 3, \"face\": \"in\" },"
                + "{ \"kind\": \"Splitter\", \"x\": 2, \"y\": 2, \"dir\": \"Up\", \"dir2\": \"Left\" }"
                + "] }";

            LevelSO level = ReadOrFail(json).Level;
            Assert.AreEqual(2, level.placements.Count);

            Dictionary<GridPoint, ElementDefinition> byCoord = level.ToDefinition().BuildLookup();

            ReceiverDefinition receiver = byCoord[new GridPoint(5, 2)] as ReceiverDefinition;
            Assert.IsNotNull(receiver, "(5,2) 应是接收器");
            Assert.AreEqual(Dir.Left, receiver.FeedFace);
            Assert.AreEqual("110", receiver.TargetSequence);
            Assert.AreEqual(3, receiver.TargetCount);
            Assert.IsFalse(receiver.AcceptAnyFace, "face=in 表示只认进料面");

            SplitterDefinition splitter = byCoord[new GridPoint(2, 2)] as SplitterDefinition;
            Assert.IsNotNull(splitter, "(2,2) 应是分流器");
            Assert.AreEqual(Dir.Up, splitter.OneArm);
            Assert.AreEqual(Dir.Left, splitter.ZeroArm);
        }

        [Test]
        public void TypeRef_WinsOverKind()
        {
            LevelSO asset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(asset);
            Assert.Greater(asset.placements.Count, 0);

            ElementTypeSO expected = asset.placements[0].type;
            string json = "{ \"width\": 9, \"height\": 8, \"elements\": ["
                + "{ \"type\": \"" + expected.Key + "\", \"kind\": \"Wall\", \"x\": 3, \"y\": 0, \"rot\": 1 }"
                + "] }";

            LevelSO level = ReadOrFail(json).Level;
            Assert.AreEqual(expected, level.placements[0].type, "写了 type 就该用 type，不能被 kind 抢走");
            Assert.AreEqual(1, level.placements[0].rotationSteps);
            Assert.IsFalse(level.placements[0].playerEditable, "playerEditable 缺省 false = 关卡预设件");
        }

        [Test]
        public void PlayerEditable_RoundTrips()
        {
            LevelSO asset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(asset);

            string json = "{ \"width\": 9, \"height\": 8, \"elements\": ["
                + "{ \"type\": \"" + asset.placements[0].type.Key + "\", \"x\": 3, \"y\": 0, \"playerEditable\": true }"
                + "] }";

            LevelSO level = ReadOrFail(json).Level;
            Assert.IsTrue(level.placements[0].playerEditable);
            StringAssert.Contains("\"playerEditable\": true", LevelJsonSerializer.ToJson(level));
        }

        [Test]
        public void BadCells_BecomeDiagnostics_NotExceptions()
        {
            const string json = "{ \"width\": 3, \"height\": 3, \"elements\": ["
                + "{ \"kind\": \"Emitter\", \"x\": 99, \"y\": 0 },"
                + "{ \"type\": \"没有这个资产\", \"x\": 0, \"y\": 0 },"
                + "{ \"kind\": \"Emitter\", \"x\": 1, \"y\": 1, \"count\": 2 },"
                + "{ \"kind\": \"Emitter\", \"x\": 1, \"y\": 1 },"
                + "{ \"kind\": \"NotAThing\", \"x\": 2, \"y\": 2 }"
                + "] }";

            LevelJsonReadResult read = LevelJsonSerializer.Read(json, new TestResolver());
            spawned.Add(read.Level);

            Assert.IsFalse(read.Ok);
            Assert.AreEqual(2, read.Errors.Count, "未知 type、未知 kind 各记一条错误");
            Assert.IsTrue(read.Warnings.Exists(text => text.Contains("越界")), "越界应是警告而不是崩溃");
            Assert.IsTrue(read.Warnings.Exists(text => text.Contains("重复")), "同格写两次应被挡掉");
            Assert.AreEqual(1, read.Level.placements.Count, "只有 (1,1) 那格活下来");
        }

        [Test]
        public void BrokenJson_ReturnsErrorInsteadOfThrowing()
        {
            LevelJsonReadResult read = LevelJsonSerializer.Read("{ \"width\": ", new TestResolver());

            Assert.IsFalse(read.HasLevel);
            Assert.IsNotEmpty(read.Errors);
        }

        // ------------------------------------------------------------------ 测试用解析器

        LevelJsonReadResult ReadOrFail(string json)
        {
            LevelJsonReadResult read = LevelJsonSerializer.Read(json, new TestResolver(item => spawned.Add(item)));

            spawned.Add(read.Level);
            Assert.IsNotNull(read.Level, "JSON 没读出关卡：\n" + string.Join("\n", read.Errors));
            return read;
        }

        /// <summary>
        /// 测试替身：type 走真实资产（按 Key 查），kind 直接现造一份内存类型资产。
        /// 正式的 kind 逻辑（先复用现成资产、没有才造变体）在编辑器工具里，这里只验证序列化层的接线。
        /// </summary>
        sealed class TestResolver : ILevelJsonTypeResolver
        {
            readonly Dictionary<string, ElementTypeSO> byKey = new Dictionary<string, ElementTypeSO>();
            readonly System.Action<UnityEngine.Object> track;
            readonly WallTypeSO assetWall;

            WallTypeSO wallType;

            public TestResolver(System.Action<UnityEngine.Object> track = null)
            {
                this.track = track;

                foreach (string guid in AssetDatabase.FindAssets("t:ElementTypeSO"))
                {
                    ElementTypeSO type = AssetDatabase.LoadAssetAtPath<ElementTypeSO>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (type == null || string.IsNullOrEmpty(type.Key)) continue;

                    byKey[type.Key] = type;

                    // 工程里真有墙类型资产就复用它：往返导出才对得上 Type_Wall 这个名字
                    if (assetWall == null && type is WallTypeSO wallAsset) assetWall = wallAsset;
                }
            }

            public ElementTypeSO Resolve(LevelJsonSerializer.LevelJsonCell cell, out int rotationSteps)
            {
                rotationSteps = 0;
                if (cell == null) return null;

                rotationSteps = ((cell.rot % 4) + 4) % 4;

                if (!string.IsNullOrEmpty(cell.type))
                {
                    return byKey.TryGetValue(cell.type, out ElementTypeSO exact) ? exact : null;
                }

                if (string.IsNullOrEmpty(cell.kind)) return null;
                if (!LevelJsonSerializer.TryParseKind(cell.kind, out CellKind kind)) return null;

                return Build(kind, cell);
            }

            public WallTypeSO ResolveWall()
            {
                if (assetWall != null) return assetWall;
                if (wallType != null) return wallType;

                wallType = ScriptableObject.CreateInstance<WallTypeSO>();
                wallType.name = "Test_Wall";
                track?.Invoke(wallType);
                return wallType;
            }

            ElementTypeSO Build(CellKind kind, LevelJsonSerializer.LevelJsonCell cell)
            {
                Dir dir = Dir.Right;
                LevelJsonSerializer.TryParseDir(cell.dir, out dir);

                Dir dir2 = dir.RotateClockwise();
                LevelJsonSerializer.TryParseDir(cell.dir2, out dir2);

                string sequence = string.IsNullOrEmpty(cell.sequence) ? "1" : cell.sequence;
                int count = cell.count >= 0 ? cell.count : 1;
                bool acceptAnyFace = !string.Equals(cell.face, "in", System.StringComparison.OrdinalIgnoreCase);

                ElementTypeSO type;
                switch (kind)
                {
                    case CellKind.Emitter:
                        EmitterTypeSO emitter = ScriptableObject.CreateInstance<EmitterTypeSO>();
                        emitter.facing = dir;
                        emitter.sequence = sequence;
                        emitter.repeatCount = 1;
                        type = emitter;
                        break;

                    case CellKind.Receiver:
                        ReceiverTypeSO receiver = ScriptableObject.CreateInstance<ReceiverTypeSO>();
                        receiver.feedFace = dir;
                        receiver.acceptAnyFace = acceptAnyFace;
                        receiver.targetSequence = sequence;
                        receiver.targetCount = Mathf.Max(1, count);
                        type = receiver;
                        break;

                    case CellKind.Splitter:
                        SplitterTypeSO splitter = ScriptableObject.CreateInstance<SplitterTypeSO>();
                        splitter.oneArm = dir;
                        splitter.zeroArm = dir2;
                        type = splitter;
                        break;

                    case CellKind.NotGate:
                        NotGateTypeSO notGate = ScriptableObject.CreateInstance<NotGateTypeSO>();
                        notGate.outputDir = dir;
                        type = notGate;
                        break;

                    case CellKind.Filter:
                        FilterTypeSO filter = ScriptableObject.CreateInstance<FilterTypeSO>();
                        filter.outputDir = dir;
                        filter.passValue = cell.pass == 0 ? (byte)0 : (byte)1;
                        type = filter;
                        break;

                    default:
                        WallTypeSO wall = ScriptableObject.CreateInstance<WallTypeSO>();
                        type = wall;
                        break;
                }

                type.name = "Test_" + kind;
                track?.Invoke(type);
                return type;
            }
        }
    }
}
