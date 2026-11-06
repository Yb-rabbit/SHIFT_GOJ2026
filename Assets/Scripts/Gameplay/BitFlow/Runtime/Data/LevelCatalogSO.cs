using System.Collections.Generic;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 关卡顺序表。关卡切换只在这里决定顺序，运行时进度不写回资产
    /// （PlayerPrefs 存进度是后面的事，现在重开就从头开始）。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Level Catalog", fileName = "LevelCatalog")]
    public sealed class LevelCatalogSO : ScriptableObject
    {
        [Tooltip("按顺序排好的关卡；索引 0 就是开局第一关")]
        public List<LevelSO> levels = new List<LevelSO>();

        public int Count => levels != null ? levels.Count : 0;

        public LevelSO Get(int index)
        {
            if (levels == null || index < 0 || index >= levels.Count) return null;
            return levels[index];
        }
    }
}
