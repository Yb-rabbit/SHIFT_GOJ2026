using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 自制模型装配的回归测试，守三条底线：
    /// 1. 尺寸只允许一次**等比**归一化（水平方向正好一格），不许为了塞进格子把模型压扁；
    /// 2. 位置必须落地 + 居中，朝向必须跟元件实际朝向一致；
    /// 3. 模型自己的材质不能被换掉（美术的颜色 / 贴图不能被染色），并且缺模型时元件能退回几何体拼装。
    /// </summary>
    public class ElementModelLibraryTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            root = null;
        }

        GameObject NewParent()
        {
            if (root == null) root = new GameObject("model-fit-root");
            GameObject parent = new GameObject("elem");
            parent.transform.SetParent(root.transform, false);
            return parent;
        }

        /// <summary>合并所有渲染器的世界包围盒；没有渲染器时返回 false。</summary>
        static bool TryMeasure(Transform t, out Bounds bounds)
        {
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
            bounds = default;
            bool has = false;

            foreach (Renderer renderer in renderers)
            {
                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return has;
        }

        [Test]
        public void EmitterFit_SitsOnGroundCentered_WithOneUniformScale()
        {
            GameObject parent = NewParent();

            bool built = ElementModelLibrary.TryBuild(ElementModelLibrary.EmitterModel,
                ElementModelLibrary.EmitterYawOffset, Dir.Right, parent.transform,
                ElementModelLibrary.EmitterModelScale, out ElementModelLibrary.Fit fit);

            if (!built) Assert.Ignore("没找到 " + ElementModelLibrary.EmitterModel + " 模型，跳过装配测试");

            Assert.IsNotNull(fit.Root, "装配成功必须给出模型根节点");
            Assert.AreEqual("model", fit.Root.name, "模型根固定叫 model，测试和调试都靠它找节点");
            Assert.IsTrue(TryMeasure(fit.Root.transform, out Bounds bounds), "模型里应有可渲染的网格");

            // 只做一次等比归一化：三轴共用一个系数（造型比例不能被破坏），水平方向占满 CellFootprint 格
            Assert.AreEqual(fit.RawSize.x * fit.Scale, bounds.size.x, 0.01f, "模型三轴必须共用一个缩放系数（X）");
            Assert.AreEqual(fit.RawSize.y * fit.Scale, bounds.size.y, 0.01f, "模型三轴必须共用一个缩放系数（Y）");
            Assert.AreEqual(fit.RawSize.z * fit.Scale, bounds.size.z, 0.01f, "模型三轴必须共用一个缩放系数（Z）");

            float widest = Mathf.Max(bounds.size.x, bounds.size.z);
            Assert.AreEqual(ElementModelLibrary.CellFootprint, widest, 0.02f,
                "模型水平方向应刚好占满一格（超过就会压到邻格，太小又显得模型被缩没了）");

            // 只做对齐：水平居中 + 底面贴地
            Assert.AreEqual(0f, bounds.center.x, 0.03f, "模型应水平居中到格心（X）");
            Assert.AreEqual(0f, bounds.center.z, 0.03f, "模型应水平居中到格心（Z）");
            Assert.AreEqual(ElementModelLibrary.GroundY, bounds.min.y, 0.02f, "模型底面应贴在格子面上（略微抬起防 z-fighting）");

            // 顶面高度回填给箭头 / 点阵，否则它们会埋进模型里
            Assert.AreEqual(bounds.max.y, fit.TopY, 0.02f, "TopY 必须等于模型顶面高度，否则箭头会穿模");
        }

        [Test]
        public void ReceiverFit_IsTurnedTowardFacingDirection()
        {
            GameObject parent = NewParent();

            bool built = ElementModelLibrary.TryBuild(ElementModelLibrary.ReceiverModel,
                ElementModelLibrary.ReceiverYawOffset, Dir.Up, parent.transform,
                ElementModelLibrary.ReceiverModelScale, out ElementModelLibrary.Fit fit);

            if (!built) Assert.Ignore("没找到 " + ElementModelLibrary.ReceiverModel + " 模型，跳过装配测试");

            Vector3 expected = GridMetrics.DirToWorld(Dir.Up);
            float dot = Vector3.Dot(fit.Root.transform.forward, expected);
            Assert.Greater(dot, 0.99f, "模型正面应朝向元件的进料面（朝向对了玩家才看得出料往哪走）");
        }

        /// <summary>
        /// 诊断 + 守门：把模型的原始尺寸、归一化后的尺寸打出来，并检查归一化结果。
        /// 原始尺寸可以比格子大（美术按自己舒服的比例建），但归一化之后必须正好占一格。
        /// </summary>
        [Test]
        public void ModelNormalization_PrintsRawAndFittedSizes()
        {
            string[] models = { ElementModelLibrary.EmitterModel, ElementModelLibrary.ReceiverModel };

            foreach (string model in models)
            {
                GameObject parent = NewParent();

                bool built = ElementModelLibrary.TryBuild(model, 0f, Dir.Up, parent.transform, 1f,
                    out ElementModelLibrary.Fit fit);

                if (!built) Assert.Ignore("没找到 " + model + " 模型，跳过比例检查");

                Vector3 fitted = fit.RawSize * fit.Scale;
                Debug.Log("[model-size] " + model + " raw=" + fit.RawSize.ToString("F3")
                    + " fitted=" + fitted.ToString("F3") + " (一格 = 1 × 1 米)");

                Assert.Greater(fit.RawSize.y, 0.02f, model + " 高度几乎为 0，模型可能没有网格");
                Assert.Less(fit.RawSize.y, 50f, model + " 高过 50 米，建模单位基本可以断定不是米");
                Assert.AreEqual(ElementModelLibrary.CellFootprint, Mathf.Max(fitted.x, fitted.z), 0.02f,
                    model + " 归一化后水平方向应正好占满一格");
            }
        }

        [Test]
        public void ReceiverFit_ReportsItsRealTop()
        {
            GameObject parent = NewParent();

            bool built = ElementModelLibrary.TryBuild(ElementModelLibrary.ReceiverModel,
                ElementModelLibrary.ReceiverYawOffset, Dir.Down, parent.transform,
                ElementModelLibrary.ReceiverModelScale, out ElementModelLibrary.Fit fit);

            if (!built) Assert.Ignore("没找到 " + ElementModelLibrary.ReceiverModel + " 模型，跳过装配测试");

            Assert.AreEqual(ElementModelLibrary.GroundY + fit.RawSize.y * fit.Scale, fit.TopY, 0.02f,
                "TopY 必须是模型的真实顶面，进度条 / 点阵靠它定位");
            Assert.Greater(fit.HalfWidth, 0f, "装配后的水平半径应为正数");
            Assert.Greater(fit.HalfDepth, 0f, "装配后的水平半径应为正数");
        }

        /// <summary>
        /// 元件视图必须两选一：要么用上自制模型，要么退回方块。
        /// 两者都不存在说明装配链路断了（最典型的是 fbx 缺失后异常被静默吞掉）。
        /// </summary>
        [Test]
        public void ElementView_HasExactlyOneBody_ModelOrBlock()
        {
            ElementDefinition[] elements =
            {
                new EmitterDefinition { Id = 11, Coord = new GridPoint(1, 1), Facing = Dir.Right, Sequence = "1", EmitCount = 1 },
                new ReceiverDefinition { Id = 12, Coord = new GridPoint(1, 1), FeedFace = Dir.Down, TargetSequence = "1", TargetCount = 1 },
            };

            foreach (ElementDefinition element in elements)
            {
                LevelDefinition level = new LevelDefinition { Width = 3, Height = 3 };
                level.Elements.Add(element);

                root = new GameObject("view-root");
                LevelBuilder builder = root.AddComponent<LevelBuilder>();
                builder.Build(null, level);

                ElementView view = builder.GetElement(element.Id);
                Assert.IsNotNull(view, "没找到元件视图：" + element);

                bool hasModel = view.transform.Find("model") != null;
                bool hasBlock = view.transform.Find("block") != null;

                Assert.IsTrue(hasModel || hasBlock, element.Kind + " 既没有模型也没有方块，本体整个消失了");
                Assert.IsFalse(hasModel && hasBlock, element.Kind + " 同时有模型和方块，等于叠了两个本体");

                Object.DestroyImmediate(root);
                root = null;
            }
        }

        [Test]
        public void ElementView_KeepsTheModelsOwnMaterials()
        {
            LevelDefinition level = new LevelDefinition { Width = 4, Height = 4 };
            level.Elements.Add(new EmitterDefinition
                { Id = 21, Coord = new GridPoint(1, 1), Facing = Dir.Right, Sequence = "1", EmitCount = 1 });
            level.Elements.Add(new ReceiverDefinition
                { Id = 22, Coord = new GridPoint(2, 1), FeedFace = Dir.Down, TargetSequence = "1", TargetCount = 1 });

            root = new GameObject("view-root");
            LevelBuilder builder = root.AddComponent<LevelBuilder>();
            builder.Build(null, level);

            int[] ids = { 21, 22 };
            foreach (int id in ids)
            {
                ElementView view = builder.GetElement(id);
                Assert.IsNotNull(view, "没找到元件视图：" + id);

                Transform model = view.transform.Find("model");
                if (model == null) Assert.Ignore("没找到自制模型，跳过材质检查（此时本体是方块，不涉及美术材质）");

                // 模型是美术资产：材质必须还是它自己的（本体材质叫 M_Elem_<id>，出现在模型上就说明被染色了）
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.sharedMaterial == null) continue;
                    Assert.IsFalse(renderer.sharedMaterial.name.StartsWith("M_Elem_"),
                        "模型被换成了元件本体材质，美术的颜色 / 贴图被覆盖了");
                }

                // 反馈不染模型，就必须有替代载体，否则红闪 / 达标 / 悬停在整个元件上都没有视觉变化
                Assert.IsNotNull(view.transform.Find("feedback_ring"),
                    "有模型时应有脚下的反馈环（feedback_ring）");
            }
        }

        [Test]
        public void CardArtLibrary_CoversEveryPlayableKind()
        {
            CellKind[] kinds =
            {
                CellKind.Emitter, CellKind.Receiver, CellKind.Splitter, CellKind.NotGate, CellKind.Filter,
            };

            foreach (CellKind kind in kinds)
            {
                Sprite icon = CardArtLibrary.Icon(kind);
                Assert.IsNotNull(icon, "手牌缺少卡面贴图：" + kind
                    + "（应在 Assets/Arts/Resources/Cards 下有同名方形 PNG，且导入类型为 Sprite）");
                Assert.Greater(icon.rect.width, 0f, "卡面贴图尺寸异常：" + kind);
            }
        }
    }
}
