using NUnit.Framework;
using UnityEditor;
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
