using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 关卡资产测试：确保 Assets/Data/Levels/Level_01.asset 与代码内置演示关一致、能通关，
    /// 且摆放项都引用了可复用的元件类型资产（坐标不写死在类型资产里）。
    /// </summary>
    public class LevelDataTests
    {
        const string LevelAssetPath = "Assets/Data/Levels/Level_01.asset";

        [Test]
        public void Level01Asset_IsWinnableWithinExpectedTicks()
        {
            LevelSO levelAsset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(levelAsset, "缺少关卡资产：" + LevelAssetPath);

            LevelDefinition fromAsset = levelAsset.ToDefinition();
            LevelDefinition fromCode = DemoLevels.CreateLevel01();
            Assert.AreEqual(fromCode.Elements.Count, fromAsset.Elements.Count, "资产版元件数量应与代码演示关一致");
            Assert.AreEqual(fromCode.Width, fromAsset.Width);
            Assert.AreEqual(fromCode.Height, fromAsset.Height);

            BitFlowSimulation simulation = new BitFlowSimulation(fromAsset);
            while (!simulation.IsFinished) simulation.Step();

            LevelResult result = simulation.BuildResult();
            Assert.IsTrue(result.IsSuccess, "资产版关卡应能通关：" + result.Summary());
            Assert.LessOrEqual(simulation.TickIndex, DemoLevels.ExpectedMaxTicks);
        }

        [Test]
        public void Level01Asset_EveryPlacementReferencesTypeAsset()
        {
            LevelSO levelAsset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(levelAsset);
            Assert.Greater(levelAsset.placements.Count, 0);

            foreach (ElementPlacement placement in levelAsset.placements)
            {
                Assert.IsNotNull(placement, "摆放表里不应有空项");
                Assert.IsNotNull(placement.type, "摆放项必须引用元件类型资产");
                Assert.IsFalse(string.IsNullOrEmpty(placement.type.Describe()), "类型资产应能给出提示文本");
            }
        }

        [Test]
        public void Level01Asset_WallsLiveInWallMapNotPlacements()
        {
            LevelSO levelAsset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(levelAsset);

            Assert.IsNotNull(levelAsset.wallType,
                "迁移后关卡必须指定墙类型资产，否则 wallMap 里的 # 会被静默忽略");
            Assert.IsFalse(string.IsNullOrEmpty(levelAsset.wallMap), "Level_01 的墙应该写在墙层里");

            // placements 里不该再留墙：这正是这次改造的目的（18 条摆放 → 8 条 + 8 行字符图）
            foreach (ElementPlacement placement in levelAsset.placements)
            {
                Assert.IsNotNull(placement);
                Assert.IsFalse(placement.type is WallTypeSO,
                    "墙应该写在 wallMap 里，不该再占一条 placements（坐标 " + placement.coord + "）");
            }

            int wallCount = 0;
            foreach (Vector2Int coord in levelAsset.ParseWalls()) wallCount++;

            Assert.AreEqual(10, wallCount, "迁移前后墙的数量必须一致（Level_01 是 10 面）");
        }

        [Test]
        public void WallMap_ToleratesShortLinesAndOutOfBounds()
        {
            // 手写的字符图难免少打几个点、或者网格改小了却没改图。
            // 规则是"短行=右侧留空、超出的字符忽略"，绝不能因此错位或抛异常。
            LevelSO scratch = ScriptableObject.CreateInstance<LevelSO>();
            WallTypeSO wallType = ScriptableObject.CreateInstance<WallTypeSO>();

            try
            {
                scratch.width = 4;
                scratch.height = 3;
                scratch.wallType = wallType;
                scratch.wallMap = "#\n..#####\n####\n####\n####"; // 第 1 行是短行；第 2 行横向超宽（x=4,5 要忽略）；后两行超出 height

                List<Vector2Int> walls = new List<Vector2Int>(scratch.ParseWalls());

                CollectionAssert.Contains(walls, new Vector2Int(0, 0));
                CollectionAssert.Contains(walls, new Vector2Int(2, 1));
                CollectionAssert.Contains(walls, new Vector2Int(3, 1));

                Assert.AreEqual(1 + 2 + 4, walls.Count, "超出 height 的行必须被忽略，短行的缺失部分按空处理");
            }
            finally
            {
                Object.DestroyImmediate(scratch);
                Object.DestroyImmediate(wallType);
            }
        }

        [Test]
        public void WallMap_SetWallRoundTrips()
        {
            // SetWall 是网格点击的唯一入口，写错一格就会让整行错位
            LevelSO scratch = ScriptableObject.CreateInstance<LevelSO>();

            try
            {
                scratch.width = 4;
                scratch.height = 3;

                Assert.IsTrue(scratch.SetWall(new Vector2Int(1, 2), true), "第一次画墙应该算作修改");
                Assert.IsTrue(scratch.IsWallInMap(new Vector2Int(1, 2)), "刚画的格必须能读回来");
                Assert.IsFalse(scratch.IsWallInMap(new Vector2Int(1, 1)), "别的行不该被一起画上");
                Assert.IsFalse(scratch.SetWall(new Vector2Int(1, 2), true), "重复画同一格不算修改，避免堆一堆 undo");

                Assert.IsTrue(scratch.SetWall(new Vector2Int(1, 2), false), "擦墙应该算作修改");
                Assert.IsFalse(scratch.IsWallInMap(new Vector2Int(1, 2)));

                Assert.IsFalse(scratch.SetWall(new Vector2Int(9, 9), true), "越界的格必须被拒绝");
            }
            finally
            {
                Object.DestroyImmediate(scratch);
            }
        }

        [Test]
        public void TypeAssets_DoNotCarryCoordinates()
        {
            // 类型资产按设计只描述"是什么"，坐标与旋转必须放在关卡摆放表里。
            // 这里用一个反向断言守住约定：同一种类型资产被两关复用时，创建出的定义坐标由调用方决定。
            LevelSO levelAsset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            Assert.IsNotNull(levelAsset);

            ElementTypeSO emitterType = null;
            foreach (ElementPlacement placement in levelAsset.placements)
            {
                if (placement.type is EmitterTypeSO emitter)
                {
                    emitterType = emitter;
                    break;
                }
            }
            Assert.IsNotNull(emitterType, "关卡里应有发射器类型资产");

            ElementDefinition atOrigin = emitterType.CreateDefinition(new GridPoint(0, 0), 0, 1);
            ElementDefinition elsewhere = emitterType.CreateDefinition(new GridPoint(7, 3), 0, 2);

            Assert.AreEqual(new GridPoint(0, 0), atOrigin.Coord);
            Assert.AreEqual(new GridPoint(7, 3), elsewhere.Coord);
        }
    }
}
