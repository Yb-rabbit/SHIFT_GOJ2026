using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 生成 / 校验运行时引用的 URP 材质。
    ///
    /// 为什么需要这几个材质：工程不再把 URP/Lit、URP/Unlit 塞进 Always Included Shaders
    /// （那会把两个 Shader 的全部变体打进包，实测让 Data 多出两百多 MB）。
    /// 但 Shader 又不能被剥离 —— 让 Resources 下的材质引用它，Unity 就只保留
    /// 这些材质真正用到的变体，同时保证运行时 <see cref="ShiftGo.BitFlow.View.MaterialFactory"/>
    /// 能拿到 Shader。
    ///
    /// 菜单：SHIFT GO/Setup/4. 生成/校验 URP 引用材质
    /// </summary>
    public static class BitFlowMaterialRefs
    {
        const string ResourcesFolder = "Assets/Resources";
        const string BitFlowFolder = ResourcesFolder + "/BitFlow";
        const string MaterialFolder = BitFlowFolder + "/Materials";

        const string LitPath = MaterialFolder + "/URP_Lit_Ref.mat";
        const string UnlitOpaquePath = MaterialFolder + "/URP_Unlit_Opaque_Ref.mat";
        const string UnlitTransparentPath = MaterialFolder + "/URP_Unlit_Transparent_Ref.mat";

        [MenuItem("SHIFT GO/Setup/4. 生成/校验 URP 引用材质", priority = 3)]
        public static void EnsureAll()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");

            if (litShader == null || unlitShader == null)
            {
                Debug.LogError("[BitFlowMaterialRefs] 找不到 URP/Lit 或 URP/Unlit，确认 URP 已启用");
                return;
            }

            EnsureFolder(ResourcesFolder);
            EnsureFolder(BitFlowFolder);
            EnsureFolder(MaterialFolder);

            Material lit = EnsureMaterial(LitPath, litShader);
            ConfigureLit(lit);

            Material unlitOpaque = EnsureMaterial(UnlitOpaquePath, unlitShader);
            ConfigureUnlit(unlitOpaque, false);

            Material unlitTransparent = EnsureMaterial(UnlitTransparentPath, unlitShader);
            ConfigureUnlit(unlitTransparent, true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BitFlowMaterialRefs] 引用材质就绪：" + LitPath + " / " + UnlitOpaquePath + " / " + UnlitTransparentPath);
        }

        static Material EnsureMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.shader != shader) material.shader = shader;

            material.name = System.IO.Path.GetFileNameWithoutExtension(path);
            return material;
        }

        static void ConfigureLit(Material material)
        {
            SetColor(material, "_BaseColor", Color.white);
            SetFloat(material, "_Smoothness", 0.5f);

            // 自带一个 _EMISSION 变体：MaterialFactory.Emissive 会在运行时打开这个关键字。
            material.EnableKeyword("_EMISSION");
            SetColor(material, "_EmissionColor", Color.white);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(material);
        }

        static void ConfigureUnlit(Material material, bool transparent)
        {
            SetColor(material, "_BaseColor", Color.white);

            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", transparent ? 1f : 0f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", transparent ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", transparent ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", transparent ? 0 : 1);

            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;

            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            EditorUtility.SetDirty(material);
        }

        static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property)) material.SetColor(property, value);
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
