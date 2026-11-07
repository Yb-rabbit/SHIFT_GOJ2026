using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 关卡资产：网格尺寸 + 元件摆放表。运行时只读，转换成纯逻辑的 <see cref="LevelDefinition"/>
    /// 交给 BitFlowSimulation，任何运行时进度都不会写回这个资产（避免 Editor 里资产被改脏）。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Level", fileName = "Level_01")]
    public sealed class LevelSO : ScriptableObject
    {
        [Header("关卡信息")]
        public string displayName = "LEVEL 01 - BIT FLOW";

        [Tooltip("网格列数（x 方向）")]
        [Min(1)] public int width = 9;

        [Tooltip("网格行数（y 方向）")]
        [Min(1)] public int height = 8;

        [Header("节奏")]
        [Tooltip("1 倍速时一个 tick 的时长（秒）")]
        [Min(0.05f)] public float tickInterval = 0.5f;

        [Header("元件摆放")]
        public List<ElementPlacement> placements = new List<ElementPlacement>();

        [Header("玩家手牌")]
        [Tooltip("留空 = 不限量（作者拿来摆草图的演示关）。填了才是有限元件的解谜关：" +
                 "placements 给的是场景，supplies 给的是玩家能做的事")]
        public List<ElementSupply> supplies = new List<ElementSupply>();

        /// <summary>有没有给玩家手牌。false 时这个关卡只是演示/作者的沙盒。</summary>
        public bool HasSupplies => supplies != null && supplies.Count > 0;

        /// <summary>
        /// 转成纯逻辑关卡。Id 按摆放顺序从 1 递增分配，
        /// 保证同一份资产每次转换得到的 Id 完全一致（UI 进度条与视图映射都靠它）。
        /// </summary>
        public LevelDefinition ToDefinition()
        {
            LevelDefinition definition = new LevelDefinition
            {
                DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName,
                Width = Mathf.Max(1, width),
                Height = Mathf.Max(1, height),
                TickInterval = Mathf.Max(0.05f, tickInterval),
            };

            int nextId = 1;
            foreach (ElementPlacement placement in placements)
            {
                if (placement == null || placement.type == null) continue;

                ElementDefinition element = placement.type.CreateDefinition(
                    new GridPoint(placement.coord.x, placement.coord.y),
                    placement.rotationSteps,
                    nextId);

                if (element == null) continue;

                element.Label = placement.type.label;
                element.Hint = placement.type.Describe();
                element.TypeKey = placement.type.Key;

                // 关卡设计权：这一格在编辑器里填的序列 / 次数，覆盖类型资产的默认值。
                // 没填过的格子这里是个空操作，所以老关卡行为完全不变。
                placement.ApplyOverrides(element);

                definition.Elements.Add(element);
                nextId++;
            }

            return definition;
        }

        /// <summary>
        /// 生成玩家局内草稿：placements 里 locked 的成为预设件（搬不动），其余成为可自由改动的件；
        /// supplies 变成有限手牌。这是「有限元件解谜」的唯一入口 —— 玩家所有操作都发生在这份草稿上，
        /// 想验证解法就 ToDefinition() 丢给模拟器，跟作者模式共用同一套渲染/模拟管线。
        /// </summary>
        public LevelDraft CreatePlayerDraft()
        {
            LevelDraft draft = new LevelDraft(width, height, tickInterval)
            {
                DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName,
            };

            foreach (ElementPlacement placement in placements)
            {
                if (placement == null || placement.type == null) continue;

                EditableElement element = placement.type.CreateEditable(
                    new GridPoint(placement.coord.x, placement.coord.y), placement.rotationSteps);
                if (element == null) continue;

                // 和 ToDefinition() 用同一份覆盖逻辑，保证作者视角与玩家视角参数一致
                placement.ApplyOverrides(element);

                draft.Place(element, !placement.playerEditable);
            }

            foreach (ElementSupply supply in supplies)
            {
                if (supply == null || supply.type == null) continue;

                // 累加而不是覆盖：库存按类型 Key 记账，同一个类型写两条就是「再多给几张」。
                // 覆盖会让作者手滑加两次时玩家少一张牌（Level_05 就踩过这个坑）。
                draft.AddStock(supply.type.Key, supply.count);
            }

            // 关卡初始化不算玩家的一步：第一次 Ctrl+Z 不该把预设件撤掉
            draft.ClearHistory();
            return draft;
        }

        /// <summary>按坐标找摆放项（LevelValidator / 编辑器辅助用）。</summary>
        public ElementPlacement FindPlacement(Vector2Int coord)
        {
            foreach (ElementPlacement placement in placements)
            {
                if (placement != null && placement.coord == coord) return placement;
            }
            return null;
        }
    }
}
