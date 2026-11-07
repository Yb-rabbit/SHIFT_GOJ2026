using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.Tests
{
    /// <summary>
    /// 「本格参数」机制的护栏：发射总位数怎么展开、placement 的覆盖什么时候生效，
    /// 以及**没勾覆盖的老关卡必须保持原样**（零迁移承诺 —— 现有 4 关改代码前后行为逐位一致）。
    /// </summary>
    public class PlacementParamsTests
    {
        [Test]
        public void EmitOrder_FillsUpToEmitCount_NotWholeCycles()
        {
            // 旧语义只做得出 4/8/12 位，做不出 6 位；这是「发射数」这条需求的根本
            EmitterDefinition emitter = new EmitterDefinition { Sequence = "1011", EmitCount = 6 };
            Assert.AreEqual("110111", Bits(emitter.BuildEmitOrder()), "序列循环填充到 6 位就停");
        }

        [Test]
        public void EmitOrder_ShorterThanCycleStopsMidway()
        {
            EmitterDefinition emitter = new EmitterDefinition { Sequence = "1011", EmitCount = 2 };
            Assert.AreEqual("11", Bits(emitter.BuildEmitOrder()));
        }

        [Test]
        public void EmitOrder_MatchesOldRepeatSemantics()
        {
            // 老关卡 "1011" ×2 展开就是这 8 位：EmitCount=8 必须逐位一致
            EmitterDefinition emitter = new EmitterDefinition { Sequence = "1011", EmitCount = 8 };
            Assert.AreEqual("11011101", Bits(emitter.BuildEmitOrder()));

            EmitterDefinition shortOne = new EmitterDefinition { Sequence = "100", EmitCount = 3 };
            Assert.AreEqual("001", Bits(shortOne.BuildEmitOrder()), "\"100\" ×1 应发 0,0,1");
        }

        [Test]
        public void EmitterTypeSO_ConvertsRepeatCountToTotalBits()
        {
            EmitterTypeSO type = ScriptableObject.CreateInstance<EmitterTypeSO>();
            try
            {
                type.sequence = "1011";
                type.repeatCount = 2;

                EmitterDefinition emitter = (EmitterDefinition)type.CreateDefinition(new GridPoint(0, 0), 0, 1);
                Assert.AreEqual(8, emitter.EmitCount, "资产上的「轮数」要换算成模拟器的「总位数」");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(type);
            }
        }

        [Test]
        public void Placement_WithoutOverride_LeavesAssetParamsAlone()
        {
            EmitterTypeSO type = ScriptableObject.CreateInstance<EmitterTypeSO>();
            try
            {
                type.sequence = "1011";
                type.repeatCount = 2;

                ElementPlacement placement = new ElementPlacement { type = type, coord = Vector2Int.zero };
                EmitterDefinition emitter = (EmitterDefinition)type.CreateDefinition(new GridPoint(0, 0), 0, 1);

                placement.ApplyOverrides(emitter);

                Assert.AreEqual("1011", emitter.Sequence, "没勾覆盖就不该动序列");
                Assert.AreEqual(8, emitter.EmitCount, "没勾覆盖就不该动位数");
                Assert.IsFalse(placement.HasParamOverride);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(type);
            }
        }

        [Test]
        public void Placement_WithOverride_ReplacesSequenceAndCount()
        {
            EmitterTypeSO type = ScriptableObject.CreateInstance<EmitterTypeSO>();
            try
            {
                type.sequence = "1011";
                type.repeatCount = 2;

                ElementPlacement placement = new ElementPlacement
                {
                    type = type,
                    coord = Vector2Int.zero,
                    overrideSequence = true,
                    sequence = "1",
                    overrideCount = true,
                    count = 6,
                };

                EmitterDefinition emitter = (EmitterDefinition)type.CreateDefinition(new GridPoint(0, 0), 0, 1);
                placement.ApplyOverrides(emitter);

                Assert.AreEqual("1", emitter.Sequence);
                Assert.AreEqual(6, emitter.EmitCount);
                Assert.AreEqual("111111", Bits(emitter.BuildEmitOrder()), "6 位全 1 —— Level_02 就是这么修通的");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(type);
            }
        }

        [Test]
        public void LevelSO_ToDefinition_AppliesOverrideForPlayerPath()
        {
            EmitterTypeSO type = ScriptableObject.CreateInstance<EmitterTypeSO>();
            LevelSO level = ScriptableObject.CreateInstance<LevelSO>();
            try
            {
                type.sequence = "1011";
                type.repeatCount = 2;

                // 只覆盖位数，序列保持资产值 —— 作者只想改「发几位」时不该被序列拖累
                level.placements.Add(new ElementPlacement
                {
                    type = type,
                    coord = new Vector2Int(0, 0),
                    overrideCount = true,
                    count = 6,
                });

                EmitterDefinition emitter = level.ToDefinition().Elements[0] as EmitterDefinition;
                Assert.IsNotNull(emitter);
                Assert.AreEqual("1011", emitter.Sequence, "没覆盖序列时仍用资产里的 \"1011\"");
                Assert.AreEqual(6, emitter.EmitCount, "覆盖了位数就用 6");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(level);
                UnityEngine.Object.DestroyImmediate(type);
            }
        }

        [Test]
        public void ReceiverPlacement_OverrideFace_CanForceSingleFace()
        {
            ReceiverTypeSO type = ScriptableObject.CreateInstance<ReceiverTypeSO>();
            try
            {
                type.feedFace = Dir.Left;
                type.targetSequence = "1";
                type.targetCount = 6;

                // 默认跟随资产：四面都能收
                ElementDefinition plain = type.CreateDefinition(new GridPoint(0, 0), 0, 1);
                Assert.IsTrue(((ReceiverDefinition)plain).AcceptAnyFace, "接收器类型资产默认应四面可收");

                // 本格覆盖成严格单面
                ElementPlacement placement = new ElementPlacement { type = type, coord = Vector2Int.zero };
                Assert.IsFalse(placement.HasParamOverride, "没动参数时不该算「独立参数」");

                placement.overrideFace = true;
                placement.acceptAnyFace = false;

                ReceiverDefinition strict = (ReceiverDefinition)type.CreateDefinition(new GridPoint(0, 0), 0, 1);
                placement.ApplyOverrides(strict);

                Assert.IsFalse(strict.AcceptAnyFace, "勾了本格覆盖就该变成严格单面");
                Assert.IsTrue(strict.AcceptsEntry(Dir.Left), "进料面这一面必须收");
                Assert.IsFalse(strict.AcceptsEntry(Dir.Right), "严格模式下其余面不收");
                Assert.IsTrue(placement.HasParamOverride, "改过朝向细项就该算「独立参数」");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(type);
            }
        }

        static string Bits(List<byte> bits)
        {
            StringBuilder builder = new StringBuilder(bits.Count);
            foreach (byte bit in bits) builder.Append(bit == 0 ? '0' : '1');
            return builder.ToString();
        }
    }
}