using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 一次性工程设置工具（可在 batchmode 下调用，也可在编辑器菜单里点）。
    ///
    /// 命令行用法：
    ///   Unity.exe -batchmode -quit -projectPath "&lt;项目根目录&gt;"
    ///             -executeMethod ShiftGo.EditorTools.ProjectSetup.RunAll -logFile "&lt;日志路径&gt;"
    ///
    /// 做四件事：
    /// 1. 建立 Assets/Settings 目录；
    /// 2. 把旧的推箱子场景改名为 Test_1_PushBox 并登记进 Build Settings；
    /// 3. 生成 URP 管线资产（URP_Asset + URP_Renderer），绑到 Graphics / 全部质量档；
    /// 4. 把项目里仍是 Built-in Standard 的材质升级到 URP/Lit，并生成全局 Volume Profile（Bloom 等）。
    /// </summary>
    public static class ProjectSetup
    {
        const string SettingsFolder = "Assets/Settings";
        const string UrpAssetPath = SettingsFolder + "/URP_Asset.asset";
        const string RendererDataPath = SettingsFolder + "/URP_Renderer.asset";
        const string VolumeProfilePath = SettingsFolder + "/URP_GlobalVolumeProfile.asset";

        const string PostProcessDataPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        const string LegacyScenePath = "Assets/Scenes/PushBoxTest.unity";
        const string TestOneScenePath = "Assets/Scenes/Test_1_PushBox.unity";

        [MenuItem("SHIFT GO/Setup/全部执行（URP 切换 + 场景改名 + 材质升级）", priority = 0)]
        public static void RunAll()
        {
            EnsureSettingsFolder();
            RenameLegacyScene();
            UniversalRenderPipelineAsset pipeline = EnsureRenderPipelineAsset();
            ApplyPipelineToQualityLevels(pipeline);
            EnsurePostProcessData();
            EnsureGlobalVolumeProfile();
            EnsureAlwaysIncludedShaders();
            UpgradeStandardMaterials();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectSetup] 完成：URP 已启用（" + UrpAssetPath + "）");
        }

        [MenuItem("SHIFT GO/Setup/仅重新绑定 URP 管线", priority = 20)]
        public static void RebindPipelineOnly()
        {
            UniversalRenderPipelineAsset pipeline = EnsureRenderPipelineAsset();
            ApplyPipelineToQualityLevels(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 管线已重新绑定");
        }

        [MenuItem("SHIFT GO/Setup/仅升级旧材质到 URP", priority = 21)]
        public static void UpgradeMaterialsOnly()
        {
            UpgradeStandardMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 材质升级完成");
        }

        /// <summary>新建 Assets/Settings（URP 资产的常规存放位置）。</summary>
        static void EnsureSettingsFolder()
        {
            if (AssetDatabase.IsValidFolder(SettingsFolder)) return;
            AssetDatabase.CreateFolder("Assets", "Settings");
            Debug.Log("[ProjectSetup] 已创建 " + SettingsFolder);
        }

        /// <summary>把旧的 PushBoxTest 场景改名并登记进 Build Settings（MoveAsset 保留 GUID，引用不丢）。</summary>
        static void RenameLegacyScene()
        {
            bool legacyExists = AssetDatabase.LoadAssetAtPath<Object>(LegacyScenePath) != null;
            bool renamedExists = AssetDatabase.LoadAssetAtPath<Object>(TestOneScenePath) != null;

            if (legacyExists && !renamedExists)
            {
                string error = AssetDatabase.MoveAsset(LegacyScenePath, TestOneScenePath);
                if (string.IsNullOrEmpty(error))
                    Debug.Log("[ProjectSetup] 场景改名：" + LegacyScenePath + " → " + TestOneScenePath);
                else
                    Debug.LogError("[ProjectSetup] 场景改名失败：" + error);
            }
            else if (renamedExists)
            {
                Debug.Log("[ProjectSetup] 场景已是 " + TestOneScenePath + "，跳过改名");
            }

            // 登记进 Build Settings，方便直接打包测试
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (AssetDatabase.LoadAssetAtPath<Object>(TestOneScenePath) != null &&
                !scenes.Any(s => s.path == TestOneScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(TestOneScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[ProjectSetup] 已登记进 Build Settings：" + TestOneScenePath);
            }
        }

        /// <summary>
        /// 生成（或复用）URP 管线资产。
        /// 注意：URP 内部真正建资产的 CreateRendererAsset 是 internal，外部调用不到；
        /// 这里用公开的 Create（菜单里走的同一条路径）+ ResourceReloader 手动补齐包内资源引用，效果等价。
        /// </summary>
        static UniversalRenderPipelineAsset EnsureRenderPipelineAsset()
        {
            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);

            if (pipeline == null)
            {
                // RendererData 里 postProcessData / shader 等字段默认是空的，必须由 ResourceReloader 从包内补齐，
                // 否则 Bloom 之类的后处理会静默失效。
                UniversalRendererData rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);

                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, UrpAssetPath);
                Debug.Log("[ProjectSetup] 已生成 URP 管线资产：" + UrpAssetPath);
            }

            // 小场景固定机位：HDR + 4x MSAA 能让发光块边缘干净，Bloom 也有 HDR 亮度可用
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 30f;
            EditorUtility.SetDirty(pipeline);

            // 默认管线（渲染用的就是它）+ 全部质量档都指向它，避免切档位时掉回 Built-in
            GraphicsSettings.defaultRenderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            return pipeline;
        }

        /// <summary>
        /// 补齐 RendererData 的 PostProcessData 引用。
        /// URP 的自动 Reload 只处理带 [Reload] 的字段，postProcessData 不在其中，
        /// 漏了它 Bloom 不会报错、但整条后处理链会静默失效。
        /// </summary>
        static void EnsurePostProcessData()
        {
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                Debug.LogWarning("[ProjectSetup] 找不到 " + RendererDataPath);
                return;
            }
            if (rendererData.postProcessData != null) return;

            PostProcessData postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            if (postProcessData == null)
            {
                Debug.LogWarning("[ProjectSetup] 找不到 " + PostProcessDataPath);
                return;
            }

            rendererData.postProcessData = postProcessData;
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 已补齐 RendererData.postProcessData");
        }

        /// <summary>逐个质量档绑定管线，最后恢复用户原来的档位。</summary>
        static void ApplyPipelineToQualityLevels(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return;

            int originalLevel = QualitySettings.GetQualityLevel();
            string[] levelNames = QualitySettings.names;
            for (int i = 0; i < levelNames.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalLevel, false);
            Debug.Log("[ProjectSetup] 已绑定到 " + levelNames.Length + " 个质量档");
        }

        /// <summary>生成全局 Volume Profile：Bloom 让霓虹位发光，ACES 压高光，暗角收边。</summary>
        static void EnsureGlobalVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
                Debug.Log("[ProjectSetup] 已生成 Volume Profile：" + VolumeProfilePath);
            }

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = AddOverride<Bloom>(profile);
            }
            bloom.active = true;
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.4f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(0.85f, 0.95f, 1f));

            if (!profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping = AddOverride<Tonemapping>(profile);
            }
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);

            if (!profile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments = AddOverride<ColorAdjustments>(profile);
            }
            colorAdjustments.active = true;
            colorAdjustments.contrast.Override(12f);
            colorAdjustments.saturation.Override(8f);
            colorAdjustments.postExposure.Override(0.15f);

            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = AddOverride<Vignette>(profile);
            }
            vignette.active = true;
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.5f);

            EditorUtility.SetDirty(profile);
        }

        /// <summary>往 VolumeProfile 里加一个后处理覆盖，并作为子资产持久化（不加子资产重启后会丢）。</summary>
        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>
        /// 把 URP 的 Lit / Unlit 加进 Always Included Shaders：
        /// 运行时材质是 Shader.Find 动态建的，不加这一条打包后会被 strip 掉，画面全黑。
        /// </summary>
        static void EnsureAlwaysIncludedShaders()
        {
            UnityEngine.Object graphicsSettings =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null)
            {
                Debug.LogWarning("[ProjectSetup] 读不到 GraphicsSettings.asset，跳过 Always Included Shaders");
                return;
            }

            SerializedObject serialized = new SerializedObject(graphicsSettings);
            SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            AddShaderIfMissing(list, "Universal Render Pipeline/Lit");
            AddShaderIfMissing(list, "Universal Render Pipeline/Unlit");

            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 已确保 URP/Lit、URP/Unlit 在 Always Included Shaders 中");
        }

        static void AddShaderIfMissing(SerializedProperty list, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        }

        /// <summary>把 Assets 下仍是 Built-in Standard 的材质升到 URP/Lit（颜色/金属度/光滑度/自发光都会搬过去）。</summary>
        static void UpgradeStandardMaterials()
        {
            StandardUpgrader upgrader = new StandardUpgrader("Standard");
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            int upgraded = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null) continue;
                if (material.shader.name != "Standard") continue;

                upgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                EditorUtility.SetDirty(material);
                upgraded++;
                Debug.Log("[ProjectSetup] 材质升级：" + path);
            }

            Debug.Log("[ProjectSetup] 共升级 " + upgraded + " 个 Standard 材质");
        }
    }
}
