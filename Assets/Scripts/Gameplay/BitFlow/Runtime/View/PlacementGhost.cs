using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 放置预览（俗称"幽灵"）：手里拿着牌、鼠标悬在网格上时，在那一格显示一个半透明的同款元件。
    ///
    /// 三条硬约束（决定了这里的写法）：
    /// 1) 外观必须和真正放下去的完全一致 —— 几何直接借 <see cref="ElementView.Build"/>，
    ///    不做第二套简化模型。否则就会出现"预览看着能塞进去、放下去是另一个样"。
    /// 2) 换格不能重建 —— 鼠标扫过整片网格时一秒要换几十格，每格重建一堆 Cube 会直接掉帧。
    ///    所以按「类型 + 朝向」缓存，换格只改 transform，换颜色只换共享材质。
    /// 3) 不能挡拾取 —— <see cref="ElementView.Build"/> 会给自己挂 BoxCollider，
    ///    幽灵必须把碰撞体全部拆掉，否则以后任何射线/物理查询都会打到幽灵身上。
    /// </summary>
    public sealed class PlacementGhost : MonoBehaviour
    {
        /// <summary>半透明度。太淡看不清形状，太实会让人分不清"这是预览还是已经放下了"。</summary>
        const float Alpha = 0.48f;

        /// <summary>不能放时的颜色（红）：和可行时的类型色形成明确对比，不用额外文字提示。</summary>
        static readonly Color InvalidColor = new Color(1f, 0.30f, 0.32f);

        /// <summary>同一份几何 + 一套共享材质。缓存粒度是「类型 + 朝向」，鼠标来回扫也只建这几个。</summary>
        sealed class Variant
        {
            public GameObject Root;
            public Renderer[] Renderers;
            public CellKind Kind;
        }

        readonly Dictionary<string, Variant> variants = new Dictionary<string, Variant>();
        readonly Dictionary<CellKind, Material> validMaterials = new Dictionary<CellKind, Material>();
        Material invalidMaterial;

        Variant current;
        bool currentValid = true;

        /// <summary>
        /// 在 <paramref name="element"/> 自己的坐标上显示幽灵（坐标系和真正放下时同一套），
        /// <paramref name="result"/> 只用来决定颜色，不改变任何数据。
        /// </summary>
        public void Show(EditableElement element, PlaceResult result)
        {
            if (element == null)
            {
                Hide();
                return;
            }

            Variant variant = GetVariant(element);
            if (variant == null)
            {
                Hide();
                return;
            }

            bool valid = result == PlaceResult.Ok;

            if (current != null && current != variant) current.Root.SetActive(false);

            // 只在"换了形状"或"能放/不能放变了"的时候才重设材质，避免每帧刷一遍 Renderer
            bool needsTint = current != variant || currentValid != valid || !variant.Root.activeSelf;

            current = variant;
            currentValid = valid;

            variant.Root.SetActive(true);
            variant.Root.transform.position = GridMetrics.CellToWorld(element.Coord);
            if (needsTint) ApplyTint(variant, valid);
        }

        /// <summary>收起预览（鼠标离开网格、放下牌、或者选了别的卡时）。不销毁，下次还要用。</summary>
        public void Hide()
        {
            if (current != null && current.Root != null) current.Root.SetActive(false);
            current = null;
        }

        /// <summary>换关 / 手牌变了：连缓存一起扔掉，下次按新元件重建。</summary>
        public void Clear()
        {
            Hide();

            foreach (Variant variant in variants.Values)
            {
                if (variant.Root != null) DestroySafe(variant.Root);
            }
            variants.Clear();

            // 这些材质是本组件自己 new 的（不是 MaterialFactory 的缓存），必须自己释放
            if (invalidMaterial != null)
            {
                DestroySafe(invalidMaterial);
                invalidMaterial = null;
            }

            foreach (Material material in validMaterials.Values)
            {
                if (material != null) DestroySafe(material);
            }
            validMaterials.Clear();
        }

        void OnDestroy()
        {
            Clear();
        }

        Variant GetVariant(EditableElement element)
        {
            // 形状由"是什么元件 + 主副朝向 + 放行值"唯一决定，所以缓存键就用这几项
            string key = element.TypeKey + "|" + element.Kind + "|" + element.PrimaryDir + "|" +
                         element.SecondaryDir + "|" + element.PassValue;

            if (variants.TryGetValue(key, out Variant cached) && cached.Root != null) return cached;

            GameObject root = new GameObject("Ghost_" + element.Kind);
            root.transform.SetParent(transform, false);

            // 借真正的元件视图生成几何：形状代码只有一份，"预览"和"真件"不可能跑偏
            ElementView view = root.AddComponent<ElementView>();
            view.Build(element.ToDefinition(0), string.Empty, ValidMaterial(element.Kind));
            view.enabled = false; // 预览不要红闪 / 呼吸 / 悬停提亮

            // 幽灵不参与任何拾取：Build 顺手挂的 BoxCollider 必须拆干净
            foreach (Collider box in root.GetComponentsInChildren<Collider>(true)) DestroySafe(box);

            Variant variant = new Variant
            {
                Root = root,
                Renderers = root.GetComponentsInChildren<Renderer>(true),
                Kind = element.Kind,
            };
            ApplyTint(variant, true);
            root.SetActive(false);

            variants[key] = variant;
            return variant;
        }

        void ApplyTint(Variant variant, bool valid)
        {
            Material material = valid ? ValidMaterial(variant.Kind) : InvalidMaterial();
            for (int i = 0; i < variant.Renderers.Length; i++)
            {
                if (variant.Renderers[i] == null) continue;
                variant.Renderers[i].sharedMaterial = material;
            }
        }

        Material ValidMaterial(CellKind kind)
        {
            if (validMaterials.TryGetValue(kind, out Material cached) && cached != null) return cached;

            Material material = MaterialFactory.Ghost(LevelBuilder.KindColor(kind), Alpha, "M_Ghost_" + kind);
            validMaterials[kind] = material;
            return material;
        }

        Material InvalidMaterial()
        {
            if (invalidMaterial != null) return invalidMaterial;
            invalidMaterial = MaterialFactory.Ghost(InvalidColor, Alpha, "M_Ghost_Invalid");
            return invalidMaterial;
        }

        /// <summary>EditMode（测试/编辑器）下必须用 DestroyImmediate，否则 Unity 会报 "Destroy may not be called from edit mode"。</summary>
        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
