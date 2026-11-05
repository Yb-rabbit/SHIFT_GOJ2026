using System.Collections.Generic;
using UnityEngine;

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

        public static Shader LitShader
        {
            get
            {
                if (litShader != null) return litShader;

                litShader = Shader.Find("Universal Render Pipeline/Lit");
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
    }
}
