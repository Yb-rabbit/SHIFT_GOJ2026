using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 开场「分阶段生成」的不变式：<see cref="LevelBuilder.Prepare"/> 只登记数据不建视觉，
    /// 之后每一段只补自己那一类元件，最后必须和一次全建（<see cref="LevelBuilder.Build(LevelSO, LevelDefinition)"/>）
    /// 得到完全一样的场景。
    ///
    /// 这些断言是演出能不能放心拆帧的前提：拆错了（漏建 / 重建成两份 / 坐标错位）在这里就该红，
    /// 而不是等进了 Play、被黑幕盖着才发现「少了两个接收器」。
    /// </summary>
    public class LevelRevealStageTests
    {
        readonly List<GameObject> roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in roots)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            roots.Clear();
        }

        LevelBuilder CreateBuilder()
        {
            GameObject root = new GameObject("reveal-test-root");
            roots.Add(root);
            return root.AddComponent<LevelBuilder>();
        }

        static int Count(LevelBuilder builder)
        {
            return new List<ElementView>(builder.ElementViews).Count;
        }

        [Test]
        public void Prepare_RegistersDataWithoutBuildingAnything()
        {
            LevelBuilder builder = CreateBuilder();
            LevelDefinition level = DemoLevels.CreateLevel01();

            builder.Prepare(level);

            Assert.AreSame(level, builder.Definition, "Prepare 之后数据应已就位（模拟器不依赖视图）");
            Assert.AreEqual(0, Count(builder), "Prepare 不应建出任何元件视图");
            Assert.AreEqual(1, builder.StageChildCount, "只应留下空的 Bits 容器");
            Assert.IsNotNull(builder.BitsRoot, "位流的父节点要在 Prepare 里就备好，位流才不依赖演出进度");
        }

        [Test]
        public void BuildTerrain_AddsOnlyFloorAndGridLines()
        {
            LevelBuilder builder = CreateBuilder();
            LevelDefinition level = DemoLevels.CreateLevel01();

            builder.Prepare(level);
            builder.BuildTerrain();

            Assert.AreEqual(0, Count(builder), "第 1 段只长网格，不该有元件");
            Assert.AreEqual(3, builder.StageChildCount, "Bits + Floor + GridLines");
        }

        [Test]
        public void BuildElementsOfKind_OnlyBuildsThatKind()
        {
            LevelBuilder builder = CreateBuilder();
            LevelDefinition level = DemoLevels.CreateLevel01();

            int walls = new List<ElementDefinition>(level.ElementsOfKind(CellKind.Wall)).Count;
            Assert.Greater(walls, 0, "演示关里应该有墙，否则这条断言没有意义");

            builder.Prepare(level);
            Assert.AreEqual(walls, builder.BuildElementsOfKind(CellKind.Wall));
            Assert.AreEqual(walls, Count(builder));

            foreach (ElementDefinition element in level.Elements)
            {
                if (element.Kind == CellKind.Wall) continue;
                Assert.IsNull(builder.GetElement(element.Id), "第 2 段不该顺手把非墙元件也建出来：" + element);
            }
        }

        [Test]
        public void BuildElement_IsIdempotent()
        {
            LevelBuilder builder = CreateBuilder();
            LevelDefinition level = DemoLevels.CreateLevel01();

            builder.Prepare(level);
            ElementDefinition element = level.Elements[0];

            ElementView first = builder.BuildElement(element);
            ElementView second = builder.BuildElement(element);

            Assert.IsNotNull(first);
            Assert.AreSame(first, second, "同一个元件重复建应返回已有视图，不能建出两份");
            Assert.AreEqual(1, Count(builder));
            Assert.AreEqual(1 + 1, builder.StageChildCount, "Bits + 那一个元件");
        }

        [Test]
        public void StagedBuild_EndsUpIdenticalToFullBuild()
        {
            LevelDefinition level = DemoLevels.CreateLevel01();

            // 一次全建（老路径，也是测试里一直在用的路径）
            LevelBuilder full = CreateBuilder();
            full.Build(null, level);

            // 按演出的顺序分段建
            LevelBuilder staged = CreateBuilder();
            staged.Prepare(level);
            staged.BuildTerrain();
            staged.BuildElementsOfKind(CellKind.Wall);
            staged.BuildElementsOfKind(CellKind.Receiver);
            staged.BuildElementsOfKind(CellKind.Emitter);
            staged.BuildElementsOfKind(CellKind.Splitter);
            staged.BuildElementsOfKind(CellKind.NotGate);
            staged.BuildElementsOfKind(CellKind.Filter);

            Assert.AreEqual(Count(full), Count(staged), "分段建完之后元件数与一次全建必须一致");
            Assert.AreEqual(level.Elements.Count, Count(staged), "演示关的每个元件都应被某一段建到");
            Assert.AreEqual(full.StageChildCount, staged.StageChildCount, "舞台下的生成物数量也必须一致");

            foreach (ElementDefinition element in level.Elements)
            {
                ElementView expected = full.GetElement(element.Id);
                ElementView actual = staged.GetElement(element.Id);

                Assert.IsNotNull(actual, "分段建漏了元件：" + element);
                Assert.AreSame(actual, staged.GetElementAt(element.Coord), "坐标索引也要跟着建好：" + element);
                Assert.AreEqual(expected.transform.position.x, actual.transform.position.x, 0.001f);
                Assert.AreEqual(expected.transform.position.z, actual.transform.position.z, 0.001f);
            }
        }

        [Test]
        public void BuildAllElements_AfterFullBuildDoesNotDuplicate()
        {
            LevelBuilder builder = CreateBuilder();
            LevelDefinition level = DemoLevels.CreateLevel01();

            builder.Build(null, level);
            int stageChildren = builder.StageChildCount;

            builder.BuildAllElements();

            Assert.AreEqual(level.Elements.Count, Count(builder), "重复全建不应产生第二份视图");
            Assert.AreEqual(stageChildren, builder.StageChildCount);
        }
    }
}
