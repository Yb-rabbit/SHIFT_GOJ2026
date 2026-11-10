using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 运行时材质工厂：本项目走 URP，需要 URP/Lit 才能有 _BaseColor + 自发光与 Bloom 配合。
    /// 若工程还没切到 URP（材质会变洋红），自动退回 Built-in Standard，保证任何时候都能看清画面。
    /// </summary>
    public static class MaterialFactory
    {
        static Shader litShader;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        /// <summary>
        /// 打包时不再把 URP/Lit、URP/Unlit 整个塞进 Always Included Shaders（那会把所有变体打进包，
        /// 光这两支就让 Data 多出两百多 MB）。改成 Resources 里放引用材质：
        /// 只保留这些材质真正用到的变体，同时保证 Shader 不会被剥离。
        /// </summary>
        const string LitReferencePath = "BitFlow/Materials/URP_Lit_Ref";
        const string UnlitReferencePath = "BitFlow/Materials/URP_Unlit_Opaque_Ref";

        public static Shader LitShader
        {
            get
            {
                if (litShader != null) return litShader;

                litShader = ReferencedShader(LitReferencePath);
                if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Standard");
                if (litShader == null) litShader = Shader.Find("Sprites/Default");
                if (litShader == null)
                {
                    Debug.LogError("[MaterialFactory] 找不到任何可用的光照 Shader，画面会缺失；请确认 URP 已启用");
                    litShader = Shader.Find("Hidden/InternalErrorShader");
                }
                return litShader;
            }
        }

        static Shader unlitShader;

        /// <summary>
        /// 贴图用无光照 Shader：待排区的卡面是美术设计稿，不能被场景灯光染色 / 提亮，
        /// 否则屏幕上看到的和设计稿对不上（而且卡平躺时法线朝上，光照角度会让每张卡亮度都不同）。
        /// URP 里是 URP/Unlit，Built-in 退回 Unlit/Texture。
        /// </summary>
        public static Shader UnlitShader
        {
            get
            {
                if (unlitShader != null) return unlitShader;

                unlitShader = ReferencedShader(UnlitReferencePath);
                if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Texture");
                if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
                if (unlitShader == null) unlitShader = LitShader;
                return unlitShader;
            }
        }

        static Shader ReferencedShader(string resourcePath)
        {
            Material reference = Resources.Load<Material>(resourcePath);
            return reference != null ? reference.shader : null;
        }

        /// <summary>
        /// 生成"整张贴图 × 染色"的无光照材质。每张卡都要独立染色（用完压暗、选中提亮），
        /// 所以这里故意不做缓存，调用方负责 Destroy。
        /// texture 为 null 时就是一块纯色板（没有卡面美术时退回这种卡）。
        /// </summary>
        public static Material Textured(Texture texture, Color tint, string name)
        {
            Material material = new Material(UnlitShader) { name = name };

            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            }

            SetBaseColor(material, tint);
            return material;
        }

        /// <summary>不透明 + 自发光材质（同一个颜色会复用，避免每帧新建材质）。</summary>
        public static Material Emissive(Color baseColor, float emission, float smoothness = 0.55f)
        {
            string key = "E" + ColorUtility.ToHtmlStringRGB(baseColor) + "_" + emission.ToString("F2") + "_" + smoothness.ToString("F2");
            if (cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material material = new Material(LitShader) { name = "M_" + key };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", baseColor * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            cache[key] = material;
            return material;
        }

        /// <summary>发送到自发光颜色（用于红闪/绿脉冲）。</summary>
        public static void SetEmission(Material material, Color color, float emission)
        {
            if (material == null || !material.HasProperty("_EmissionColor")) return;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }

        /// <summary>设置基础色（URP 与 Built-in 的属性名都覆盖）。</summary>
        public static void SetBaseColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        /// <summary>
        /// 半透明"幽灵"材质（放置预览用）。URP 的 Lit/Unlit 默认走不透明队列且 _ZWrite=1，
        /// 光把颜色 alpha 调低是不会透的 —— 必须把 _Surface 切成 Transparent 才会走混色那一支。
        /// 这里一次做完：切 Surface + Alpha 混合 + 关 ZWrite + 排进 Transparent 队列。
        /// 拿不到 _Surface（回退到了 Built-in 的 Unlit/Texture 或 Sprites/Default）就只设颜色，
        /// Sprites/Default 本来就是透明无光照的，照样能用。
        /// 故意不加自发光：场景里开着 Bloom，亮的半透明色会被放大成一团白，反而看不清底下的格子。
        /// 不做缓存：调用方（<see cref="PlacementGhost"/>）按需 new、自己 Destroy。
        /// </summary>
        public static Material Ghost(Color color, float alpha, string name)
        {
            Color tint = color;
            tint.a = Mathf.Clamp01(alpha);

            Material material = new Material(UnlitShader) { name = name };

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f); // 0 = Opaque，1 = Transparent
                material.SetFloat("_Blend", 0f);   // 0 = Alpha
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f); // Built-in Standard：3 = Transparent
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            SetBaseColor(material, tint);
            return material;
        }
    }
}
