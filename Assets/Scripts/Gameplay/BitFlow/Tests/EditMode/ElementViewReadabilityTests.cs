using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 可读性回归测试：箭头带箭头头、序列点阵、分流器臂配色、非门三面矮栏、过滤器闸门配色……
    /// 这些"玩家一眼看到的东西"就是关卡可读性本身，重构 ElementView 时不能悄悄弄丢。
    /// </summary>
    public class ElementViewReadabilityTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            root = null;
        }

        ElementView BuildView(ElementDefinition element)
        {
            LevelDefinition level = new LevelDefinition { Width = 3, Height = 3 };
            level.Elements.Add(element);

            root = new GameObject("readability-root");
            LevelBuilder builder = root.AddComponent<LevelBuilder>();
            builder.Build(null, level);

            ElementView view = builder.GetElement(element.Id);
            Assert.IsNotNull(view, "没找到元件视图：" + element);
            return view;
        }

        static Transform Child(ElementView view, string name)
        {
            Transform found = view.transform.Find(name);
            Assert.IsNotNull(found, "缺少子物体 '" + name + "'，元件：" + view.name);
            return found;
        }

        /// <summary>按名字（精确）收集直接子物体：矮栏、封口这类零件天然是多块同名的。</summary>
        static List<Transform> Children(ElementView view, string name)
        {
            List<Transform> found = new List<Transform>();
            foreach (Transform child in view.transform)
            {
                if (child.name == name) found.Add(child);
            }
            return found;
        }

        static int CountChildren(ElementView view, string name)
        {
            return Children(view, name).Count;
        }

        /// <summary>按前缀数零件（"gate_0/1/2" 这类带序号的零件用）。</summary>
        static int CountChildrenStartingWith(ElementView view, string prefix)
        {
            int count = 0;
            foreach (Transform child in view.transform)
            {
                if (child.name.StartsWith(prefix)) count++;
            }
            return count;
        }

        static Color MaterialColor(Transform transform)
        {
            Renderer renderer = transform.GetComponent<Renderer>();
            Assert.IsNotNull(renderer, "缺少 Renderer：" + transform.name);

            Material material = renderer.sharedMaterial;
            Assert.IsNotNull(material, "缺少材质：" + transform.name);

            return material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        }

        static Color BitColor(byte value) => value == 1 ? BitActorView.ColorOne : BitActorView.ColorZero;

        static float Brightness(Color color) => color.r + color.g + color.b;

        static void AssertSameColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 0.02f, message + "（R）");
            Assert.AreEqual(expected.g, actual.g, 0.02f, message + "（G）");
            Assert.AreEqual(expected.b, actual.b, 0.02f, message + "（B）");
        }

        [Test]
        public void EmitterView_DotsFollowRealEmitOrder()
        {
            // Sequence "1011" 是从末字符开始弹的，真实发射顺序是 1,1,0,1
            EmitterDefinition emitter = new EmitterDefinition
            {
                Id = 1,
                Coord = new GridPoint(1, 1),
                Facing = Dir.Right,
                Sequence = "1011",
                EmitCount = 4,
            };

            ElementView view = BuildView(emitter);

            byte[] expectedOrder = { 1, 1, 0, 1 };
            for (int i = 0; i < expectedOrder.Length; i++)
            {
                Transform dot = Child(view, "seqdot_" + i);
                AssertSameColor(BitColor(expectedOrder[i]), MaterialColor(dot),
                    "发射器点阵第 " + i + " 颗应等于发射顺序里的第 " + i + " 位");
            }

            Assert.IsNull(view.transform.Find("seqdot_" + expectedOrder.Length), "点阵数量应与序列长度一致");
        }

        [Test]
        public void EmitterView_LongSequenceIsCappedButNeverEmpty()
        {
            EmitterDefinition emitter = new EmitterDefinition
            {
                Id = 2,
                Coord = new GridPoint(1, 1),
                Facing = Dir.Up,
                Sequence = "101010101100",
                EmitCount = 12,
            };

            ElementView view = BuildView(emitter);

            Assert.IsNotNull(Child(view, "seqdot_0"), "长序列至少要有第一颗点");
            Assert.IsNull(view.transform.Find("seqdot_8"), "点阵最多铺 8 颗，避免挤出格子");
        }

        [Test]
        public void EmitterView_ProgressDimsEmittedDotsOnly()
        {
            EmitterDefinition emitter = new EmitterDefinition
            {
                Id = 3,
                Coord = new GridPoint(1, 1),
                Facing = Dir.Right,
                Sequence = "1111",
                EmitCount = 4,
            };

            ElementView view = BuildView(emitter);
            view.SetEmitterProgress(2);

            float sentBrightness = Brightness(MaterialColor(Child(view, "seqdot_0")));
            float pendingBrightness = Brightness(MaterialColor(Child(view, "seqdot_3")));

            Assert.Less(sentBrightness, pendingBrightness, "已发出的点应变暗，未发出的保持明亮");
        }

        [Test]
        public void SplitterView_ArmsUseBitValueColors()
        {
            SplitterDefinition splitter = new SplitterDefinition
            {
                Id = 4,
                Coord = new GridPoint(1, 1),
                OneArm = Dir.Up,
                ZeroArm = Dir.Right,
            };

            ElementView view = BuildView(splitter);

            AssertSameColor(BitColor(1), MaterialColor(Child(view, "arrow_Up")), "走 1 的臂应为青色（与位 1 同色）");
            AssertSameColor(BitColor(0), MaterialColor(Child(view, "arrow_Right")), "走 0 的臂应为橙色（与位 0 同色）");
            Assert.IsNotNull(view.transform.Find("arrowhead_Up"), "方向必须有箭头头，否则玩家容易看反");
            Assert.IsNotNull(view.transform.Find("arrowhead_Right"), "方向必须有箭头头，否则玩家容易看反");
        }

        [Test]
        public void ReceiverView_ShowsTargetSequenceDots()
        {
            ReceiverDefinition receiver = new ReceiverDefinition
            {
                Id = 6,
                Coord = new GridPoint(1, 1),
                FeedFace = Dir.Down,
                TargetSequence = "01",
                TargetCount = 4,
            };

            ElementView view = BuildView(receiver);

            AssertSameColor(BitColor(0), MaterialColor(Child(view, "seqdot_0")), "接收器点阵第一位应为 0 的颜色");
            AssertSameColor(BitColor(1), MaterialColor(Child(view, "seqdot_1")), "接收器点阵第二位应为 1 的颜色");
            Assert.IsNotNull(view.transform.Find("progress"), "接收器必须保留顶部进度条");
        }

        [Test]
        public void NotGateView_OpensOnOneSideOnly()
        {
            // 非门在模拟里「除出口面外的三面都能进」（只有逆行才会 NotGateBackflow），
            // 所以外观必须把这件事说出来：三道矮栏＝三个入口，剩下那一面敞开＝唯一出口。
            // 老版本是 45° 菱形，只说明"我和别人不一样"，完全没交代能从哪进来。
            NotGateDefinition notGate = new NotGateDefinition
            {
                Id = 7,
                Coord = new GridPoint(1, 1),
                OutputDir = Dir.Right,
            };

            ElementView view = BuildView(notGate);

            Assert.AreEqual(3, CountChildren(view, "rail"),
                "非门应立三道矮栏（除出口面外都能进），并把出口那一面留空");
            Assert.IsNotNull(view.transform.Find("invert_mark"),
                "非门需要「翻转」标记，否则和三面开口的过滤器分不开");

            // 缺口本身就是出口，不再画出口箭头：箭头 y=0.30 正好浮在矮栏顶（0.22）上方，
            // 3/4 视角下看着像一根悬空的把手，和缺口重复表达同一件事。
            Assert.IsNull(view.transform.Find("arrow_Right"),
                "非门不该有出口箭头 —— 中间那道缺口已经把方向说完了");
            Assert.AreEqual(0, CountChildrenStartingWith(view, "arrow"), "非门整格都不该出现箭头");

            // 开口检查：出口朝右，所以 +x 那一面不能有矮栏挡着
            foreach (Transform rail in Children(view, "rail"))
            {
                Assert.LessOrEqual(rail.localPosition.x, 0.1f,
                    "出口朝右时右侧那一面必须敞开，矮栏不能挡在 +x 上");
            }
        }

        [Test]
        public void FilterView_ShowsPassValueAsGateColor()
        {
            // 过滤器同样是三面进：开口处立一道闸门，闸门的颜色就是它放行的值。
            // 闸门站在开口上，所以也不画出口箭头 —— 它就立在那个缺口里，本身已是"这里出去"的告示牌。
            foreach (byte passValue in new byte[] { 0, 1 })
            {
                FilterDefinition filter = new FilterDefinition
                {
                    Id = 5,
                    Coord = new GridPoint(1, 1),
                    OutputDir = Dir.Left,
                    PassValue = passValue,
                };

                ElementView view = BuildView(filter);

                Assert.AreEqual(3, CountChildren(view, "rail"), "过滤器应立三道矮栏，留出口那一面开口");
                Assert.AreEqual(3, CountChildrenStartingWith(view, "gate"), "闸门应是三根细条（中间留缝，才像筛子而不是一堵墙）");
                Assert.AreEqual(0, CountChildrenStartingWith(view, "arrow"),
                    "过滤器不该有出口箭头 —— 闸门已经站在缺口上，方向不用再说第二遍");

                for (int i = 0; i < 3; i++)
                {
                    AssertSameColor(BitColor(passValue), MaterialColor(Child(view, "gate_" + i)),
                        "闸门颜色必须等于放行值（放行 " + passValue + "）");
                }

                Object.DestroyImmediate(root);
                root = null;
            }
        }

        [Test]
        public void MultiInputViews_UseRailsOnlyForProcessors()
        {
            // 「多个面能进」在两个元件家族里长得不同，这是刻意的：
            // 处理器（非门/过滤器）= 三面连续矮栏 + 一面开口 = 三面进一面出；
            // 接收器 = 四根角柱 = 四面都进、但不吐。
            // 两边一旦都改用同一种零件，玩家就只能靠颜色猜谁是谁了。
            ReceiverDefinition receiver = new ReceiverDefinition
            {
                Id = 9,
                Coord = new GridPoint(1, 1),
                FeedFace = Dir.Down,
                TargetSequence = "1",
                TargetCount = 1,
            };

            ElementView receiverView = BuildView(receiver);
            Assert.AreEqual(0, CountChildren(receiverView, "rail"),
                "接收器应该用四角柱表达四面可收，不该改用处理器那套矮栏");
        }

        [Test]
        public void ReceiverView_NeverLooksLikeAnEmitter()
        {
            // 接收器是"收"，绝不能画朝外的箭头 —— 发射器就是"矮块 + 朝外箭头"，
            // 两边都往外指的话玩家只能靠进度条猜谁是收、谁是发（四支白箭头还会把本体糊成一朵白花）。
            Dir[] dirs = (Dir[])System.Enum.GetValues(typeof(Dir));
            foreach (bool acceptAnyFace in new[] { true, false })
            {
                ReceiverDefinition receiver = new ReceiverDefinition
                {
                    Id = 7,
                    Coord = new GridPoint(1, 1),
                    FeedFace = Dir.Left,
                    TargetSequence = "1",
                    TargetCount = 2,
                    AcceptAnyFace = acceptAnyFace,
                };

                ElementView view = BuildView(receiver);

                foreach (Dir dir in dirs)
                {
                    Assert.IsNull(view.transform.Find("arrow_" + dir),
                        "接收器（AcceptAnyFace=" + acceptAnyFace + "）不该有朝外的箭杆，会跟发射器撞脸");
                    Assert.IsNull(view.transform.Find("arrowhead_" + dir),
                        "接收器（AcceptAnyFace=" + acceptAnyFace + "）不该有朝外的箭头头，会跟发射器撞脸");
                }

                Object.DestroyImmediate(root);
                root = null;
            }
        }

        [Test]
        public void ReceiverView_StrictModeSealsTheOtherThreeSides()
        {
            // 收几面靠"形状"表达：全向=四个口全开，严格=堵掉三个只留进料面。
            // 所以严格模式应该正好多出三块封口 —— 比让玩家去数箭头指哪边快得多。
            int openSides = CountBodyBlocks(true);
            int strictSides = CountBodyBlocks(false);

            Assert.AreEqual(openSides + 3, strictSides,
                "严格模式应比全向模式多堵三块（另外三个口），否则玩家看不出只有一边能进");
        }

        int CountBodyBlocks(bool acceptAnyFace)
        {
            ReceiverDefinition receiver = new ReceiverDefinition
            {
                Id = 8,
                Coord = new GridPoint(1, 1),
                FeedFace = Dir.Left,
                TargetSequence = "1",
                TargetCount = 2,
                AcceptAnyFace = acceptAnyFace,
            };

            ElementView view = BuildView(receiver);
            int count = 0;
            foreach (Transform child in view.transform)
            {
                if (child.name == "block") count++;
            }

            Object.DestroyImmediate(root);
            root = null;
            return count;
        }

        [Test]
        public void Simulation_ReportsEmitterProgress()
        {
            LevelDefinition level = new LevelDefinition { Width = 6, Height = 1 };
            level.Elements.Add(new EmitterDefinition
            {
                Id = 8,
                Coord = new GridPoint(0, 0),
                Facing = Dir.Right,
                Sequence = "1001",
                EmitCount = 8,
            });

            BitFlowSimulation simulation = new BitFlowSimulation(level);
            var progress = simulation.GetEmitterProgress();

            Assert.AreEqual(1, progress.Count, "每个发射器都应有一条进度");
            Assert.AreEqual(8, progress[0].TotalCount, "序列 4 位 × 重复 2 轮 = 8 个位");
            Assert.AreEqual(0, progress[0].EmittedCount, "刚构造时还没发射");

            simulation.Step();
            progress = simulation.GetEmitterProgress();
            Assert.Greater(progress[0].EmittedCount, 0, "跑过一 tick 后应已吐出至少一个位");
        }
    }
}
