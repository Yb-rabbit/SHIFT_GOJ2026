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

                definition.Elements.Add(element);
                nextId++;
            }

            return definition;
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
