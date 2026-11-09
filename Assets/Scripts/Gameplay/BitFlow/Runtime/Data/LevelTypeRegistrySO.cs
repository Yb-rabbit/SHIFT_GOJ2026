using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 元件类型注册表：<b>打包后</b>关卡 JSON 唯一能查到的类型来源。
    ///
    /// 为什么需要它：编辑器里找类型资产靠 <c>AssetDatabase.FindAssets("t:ElementTypeSO")</c>，
    /// 那是 Editor 专属 API，Player 里不存在。JSON 要能在「编辑器 Play / EditMode 测试 / 打包后运行」
    /// 三个地方走<b>同一条</b>解析路径，就必须把这张清单变成一个进包的资源
    /// （放在 Resources 下，运行时 <see cref="Load"/> 直接取）。
    ///
    /// 清单由菜单 <c>SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表</c> 生成/刷新，
    /// 新增或改名类型资产后要再刷一次，否则 JSON 里的 <c>type</c> 会变成断链。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/BitFlow/Level Type Registry", fileName = "LevelTypeRegistry")]
    public sealed class LevelTypeRegistrySO : ScriptableObject
    {
        /// <summary>Resources 下的固定路径（不含扩展名）。改动会让所有 JSON 关卡失效，别动。</summary>
        public const string ResourcePath = "BitFlow/LevelTypeRegistry";

        [Tooltip("JSON 里 type / kind 能解析到的全部元件类型资产（含墙）")]
        [SerializeField] List<ElementTypeSO> types = new List<ElementTypeSO>();

        static LevelTypeRegistrySO cached;

        public IReadOnlyList<ElementTypeSO> Types => types;

        public int Count => types != null ? types.Count : 0;

        /// <summary>取注册表（带缓存）。找不到时只警告一次，不抛异常 —— 缺注册表不该让游戏起不来。</summary>
        public static LevelTypeRegistrySO Load()
        {
            if (cached != null) return cached;

            cached = Resources.Load<LevelTypeRegistrySO>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning("[BitFlow] 找不到类型注册表 Resources/" + ResourcePath + ".asset："
                    + "JSON 关卡里的 type / kind 都解析不了。菜单 SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表 可以生成一份。");
            }
            return cached;
        }

        /// <summary>编辑器改完注册表后清缓存，免得还在用旧清单。</summary>
        public static void ClearCache()
        {
            cached = null;
        }

        /// <summary>按 Key（= 资产名）找类型；大小写不敏感，手写 JSON 不会因为大小写翻车。</summary>
        public ElementTypeSO Find(string key)
        {
            if (string.IsNullOrEmpty(key) || types == null) return null;

            string wanted = key.Trim();
            foreach (ElementTypeSO type in types)
            {
                if (type == null) continue;
                if (string.Equals(type.Key, wanted, System.StringComparison.OrdinalIgnoreCase)) return type;
                if (string.Equals(type.name, wanted, System.StringComparison.OrdinalIgnoreCase)) return type;
            }
            return null;
        }

        /// <summary>找墙类型：给了 key 就按 key 找，没给就取清单里第一份墙。</summary>
        public WallTypeSO FindWall(string key)
        {
            if (!string.IsNullOrEmpty(key) && Find(key) is WallTypeSO named) return named;
            if (types == null) return null;

            foreach (ElementTypeSO type in types)
            {
                if (type is WallTypeSO wall) return wall;
            }
            return null;
        }

        /// <summary>某一种类的全部候选（kind 模式的匹配池，顺序 = 清单顺序，保证结果稳定）。</summary>
        public IEnumerable<ElementTypeSO> OfKind(CellKind kind)
        {
            if (types == null) yield break;

            foreach (ElementTypeSO type in types)
            {
                if (type == null) continue;
                if (KindOf(type) == kind) yield return type;
            }
        }

        /// <summary>类型资产 → 元件种类（建一份 0 号定义再读 Kind，不用在资产上多存一个会和代码走偏的字段）。</summary>
        public static CellKind KindOf(ElementTypeSO type)
        {
            if (type == null) return CellKind.Wall;

            ElementDefinition definition = type.CreateDefinition(new GridPoint(0, 0), 0, 0);
            return definition != null ? definition.Kind : CellKind.Wall;
        }
    }
}
