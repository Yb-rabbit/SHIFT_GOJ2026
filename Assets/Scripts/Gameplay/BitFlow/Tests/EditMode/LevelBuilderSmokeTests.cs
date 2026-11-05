using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 视图层冒烟测试：在 EditMode 里直接把整关搭出来，确认每个元件都有视图、坐标正确、
    /// 提示文本非空、碰撞体可被悬停射线命中。不需要进 Play 模式。
    /// </summary>
    public class LevelBuilderSmokeTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            root = null;
        }

        [Test]
        public void LevelBuilder_CreatesOneViewPerElement()
        {
            root = new GameObject("smoke-root");
            LevelBuilder builder = root.AddComponent<LevelBuilder>();

            LevelDefinition level = DemoLevels.CreateLevel01();
            builder.Build(null, level);

            List<ElementView> views = new List<ElementView>(builder.ElementViews);
            Assert.AreEqual(level.Elements.Count, views.Count, "每个元件都应有对应视图");

            foreach (ElementDefinition element in level.Elements)
            {
                ElementView view = builder.GetElement(element.Id);
                Assert.IsNotNull(view, "缺少元件视图：" + element);
                Assert.IsFalse(string.IsNullOrEmpty(view.Description), "元件提示文本不应为空：" + element);

                Vector3 expected = GridMetrics.CellToWorld(element.Coord);
                Assert.AreEqual(expected.x, view.transform.position.x, 0.001f, "视图 X 坐标错：" + element);
                Assert.AreEqual(expected.z, view.transform.position.z, 0.001f, "视图 Z 坐标错：" + element);

                Assert.AreSame(view, builder.GetElementAt(element.Coord), "坐标索引应能反查回同一个视图");
                Assert.IsNotNull(view.GetComponent<Collider>(), "元件需要碰撞体才能被鼠标悬停命中");
            }

            Assert.IsNotNull(builder.BitsRoot, "位流的父节点应已创建");
        }

        [Test]
        public void LevelBuilder_ClearRemovesEverything()
        {
            root = new GameObject("smoke-root");
            LevelBuilder builder = root.AddComponent<LevelBuilder>();
            builder.Build(null, DemoLevels.CreateLevel01());

            builder.Clear();

            Assert.AreEqual(0, new List<ElementView>(builder.ElementViews).Count);
            Assert.AreEqual(0, builder.StageChildCount, "Clear 后舞台下不应残留任何生成物");
            Assert.AreEqual(1, root.transform.childCount, "只应留下 Stage 容器本身");
            Assert.IsNull(builder.Definition);
        }

        /// <summary>
        /// 回归测试：LevelBuilder / BitFlowRunner / BitFlowHud 会挂在同一个 GameObject 上，
        /// Clear 绝不能顺手清掉兄弟物体（HUD_Canvas），否则会抛 MissingReferenceException。
        /// </summary>
        [Test]
        public void LevelBuilder_ClearKeepsSiblingObjects()
        {
            root = new GameObject("smoke-root");
            LevelBuilder builder = root.AddComponent<LevelBuilder>();

            GameObject hud = new GameObject("HUD");
            hud.transform.SetParent(root.transform, false);

            builder.Build(null, DemoLevels.CreateLevel01());
            builder.Clear();

            Assert.IsTrue(hud != null, "Clear 不应销毁同一 GameObject 下的兄弟物体");
            Assert.AreEqual(0, hud.transform.childCount);

            // 重播路径：再次 Build 后舞台内容应完整重建
            builder.Build(null, DemoLevels.CreateLevel01());
            Assert.AreEqual(DemoLevels.CreateLevel01().Elements.Count, new List<ElementView>(builder.ElementViews).Count);
            Assert.IsTrue(hud != null);
        }

        [Test]
        public void GridMetrics_MapsCoordToWorldConsistently()
        {
            Assert.AreEqual(new Vector3(0f, 0f, 0f), GridMetrics.CellToWorld(new GridPoint(0, 0)));
            Assert.AreEqual(new Vector3(3f, 0f, 0f), GridMetrics.CellToWorld(new GridPoint(3, 0)));
            Assert.AreEqual(new Vector3(0f, 0f, 5f), GridMetrics.CellToWorld(new GridPoint(0, 5)));
            Assert.AreEqual(Vector3.forward, GridMetrics.DirToWorld(Dir.Up));
            Assert.AreEqual(Vector3.right, GridMetrics.DirToWorld(Dir.Right));
            Assert.AreEqual(Vector3.back, GridMetrics.DirToWorld(Dir.Down));
            Assert.AreEqual(Vector3.left, GridMetrics.DirToWorld(Dir.Left));
            Assert.AreEqual(new Vector3(4f, 0f, 3.5f), GridMetrics.GridCenter(9, 8));
        }
    }
}
