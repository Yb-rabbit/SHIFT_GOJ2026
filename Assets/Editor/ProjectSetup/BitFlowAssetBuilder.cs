using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;
using ShiftGo.BitFlow.View;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 演示关资产与场景的生成工具（菜单或 batchmode 均可调用）。
    ///
    /// 命令行：
    ///   Unity.exe -batchmode -quit -projectPath "&lt;项目根&gt;"
    ///             -executeMethod ShiftGo.EditorTools.BitFlowAssetBuilder.BuildAll -logFile "&lt;日志&gt;"
    ///
    /// 产出：
    ///   Assets/Data/Elements/&lt;种类&gt;/*.asset —— 元件类型配置（按元件种类分目录，只含类型信息，不含坐标）
    ///   Assets/Data/Levels/Level_01.asset —— 演示关摆放表（坐标 + 旋转态）
    ///   Assets/Scenes/Test_2_BitFlow.unity—— 可直接 Play 的演示场景
    /// </summary>
    public static class BitFlowAssetBuilder
    {
        /// <summary>元件类型资产根目录。</summary>
        const string ElementFolder = "Assets/Data/Elements";

        // 类型资产按「元件种类」分子目录存放：CreateAssetMenu 的 SHIFT GO/Elements/xxx
        // 与这里的目录一一对应，新建/查找/统计缺口都在同一条线上。
        const string EmitterFolder = ElementFolder + "/Emitter";
        const string ReceiverFolder = ElementFolder + "/Receiver";
        const string SplitterFolder = ElementFolder + "/Splitter";
        const string NotGateFolder = ElementFolder + "/NotGate";
        const string FilterFolder = ElementFolder + "/Filter";
        const string WallFolder = ElementFolder + "/Wall";

        const string LevelFolder = "Assets/Data/Levels";
        const string LevelAssetPath = LevelFolder + "/Level_01.asset";
        const string ScenePath = "Assets/Scenes/Test_2_BitFlow.unity";
        const string VolumeProfilePath = "Assets/Settings/URP_GlobalVolumeProfile.asset";

        [MenuItem("SHIFT GO/Setup/2. 生成关卡资产 + 演示场景", priority = 1)]
        public static void BuildAll()
        {
            EnsureFolder("Assets/Data");
            EnsureFolder(ElementFolder);
            EnsureFolder(EmitterFolder);
            EnsureFolder(ReceiverFolder);
            EnsureFolder(SplitterFolder);
            EnsureFolder(NotGateFolder);
            EnsureFolder(FilterFolder);
            EnsureFolder(WallFolder);
            EnsureFolder(LevelFolder);
            EnsureFolder("Assets/Scenes");

            Dictionary<string, ElementTypeSO> types = CreateElementTypes();
            CreateLevelAsset(types);

            // 只有真的生成了场景才去动 Build Settings：场景已存在时 RegisterScene 会把
            // Test_2 顶到第一位，等于悄悄改掉打包入口场景（玩家场景本该排第一）。
            if (CreateScene()) RegisterScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BitFlowAssetBuilder] 完成：Level_01.asset + Test_2_BitFlow.unity");
        }

        /// <summary>
        /// 逃生门：清掉 Level_01 的摆放再重新种一遍。会丢掉所有手改，只在
        /// 「想把关卡恢复成设计文档原样」或「新克隆项目种子里数据坏了」时用。
        /// </summary>
        [MenuItem("SHIFT GO/Setup/⚠ 强制重建 Level_01（覆盖手改）", priority = 90)]
        public static void ForceRebuildLevel01()
        {
            LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            if (level == null)
            {
                Debug.LogError("[BitFlowAssetBuilder] 没有 " + LevelAssetPath + "，先跑「2. 生成关卡资产 + 演示场景」");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "强制重建 Level_01",
                "这会清空 Level_01.asset 里现有的 "
                    + (level.placements != null ? level.placements.Count : 0)
                    + " 个摆放，并写回代码里的原始演示关。\n\n手改内容不可撤销，确定吗？",
                "清空并重建", "取消");

            if (!confirmed) return;

            Undo.RecordObject(level, "Force Rebuild Level_01");
            level.placements = new List<ElementPlacement>();
            level.supplies = new List<ElementSupply>();
            EditorUtility.SetDirty(level);

            Dictionary<string, ElementTypeSO> types = CreateElementTypes();
            CreateLevelAsset(types);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BitFlowAssetBuilder] Level_01 已强制重建");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        /// <summary>建 9 个元件类型资产（按演示关实际需要的配置）。</summary>
        static Dictionary<string, ElementTypeSO> CreateElementTypes()
        {
            Dictionary<string, ElementTypeSO> types = new Dictionary<string, ElementTypeSO>();

            // 守卫：类型资产只在「还没初始化」（label 为空）时写默认配置。
            // 已经配置过的一律不碰 —— 作者可能改过序列、朝向、目标个数，重跑菜单不该打回去。
            EmitterTypeSO emitter1 = EnsureAsset<EmitterTypeSO>(EmitterFolder + "/Type_Emitter_E1.asset");
            if (string.IsNullOrEmpty(emitter1.label))
            {
                emitter1.label = "E1";
                emitter1.facing = Dir.Right;
                emitter1.sequence = "1011";
                emitter1.repeatCount = 2;
                EditorUtility.SetDirty(emitter1);
            }
            types["E1"] = emitter1;

            EmitterTypeSO emitter2 = EnsureAsset<EmitterTypeSO>(EmitterFolder + "/Type_Emitter_E2.asset");
            if (string.IsNullOrEmpty(emitter2.label))
            {
                emitter2.label = "E2";
                emitter2.facing = Dir.Right;
                emitter2.sequence = "100";
                emitter2.repeatCount = 1;
                EditorUtility.SetDirty(emitter2);
            }
            types["E2"] = emitter2;

            SplitterTypeSO splitter = EnsureAsset<SplitterTypeSO>(SplitterFolder + "/Type_Splitter_L1.asset");
            if (string.IsNullOrEmpty(splitter.label))
            {
                splitter.label = "L1";
                splitter.oneArm = Dir.Up;
                splitter.zeroArm = Dir.Right;
                EditorUtility.SetDirty(splitter);
            }
            types["L1"] = splitter;

            NotGateTypeSO notGate = EnsureAsset<NotGateTypeSO>(NotGateFolder + "/Type_NotGate_N1.asset");
            if (string.IsNullOrEmpty(notGate.label))
            {
                notGate.label = "N1";
                notGate.outputDir = Dir.Right;
                EditorUtility.SetDirty(notGate);
            }
            types["N1"] = notGate;

            FilterTypeSO filter = EnsureAsset<FilterTypeSO>(FilterFolder + "/Type_Filter_F1.asset");
            if (string.IsNullOrEmpty(filter.label))
            {
                filter.label = "F1";
                filter.outputDir = Dir.Right;
                filter.passValue = 1;
                EditorUtility.SetDirty(filter);
            }
            types["F1"] = filter;

            ReceiverTypeSO receiver1 = EnsureAsset<ReceiverTypeSO>(ReceiverFolder + "/Type_Receiver_R1.asset");
            if (string.IsNullOrEmpty(receiver1.label))
            {
                receiver1.label = "R1";
                receiver1.feedFace = Dir.Down;
                receiver1.targetSequence = "1";
                receiver1.targetCount = 6;
                EditorUtility.SetDirty(receiver1);
            }
            types["R1"] = receiver1;

            ReceiverTypeSO receiver2 = EnsureAsset<ReceiverTypeSO>(ReceiverFolder + "/Type_Receiver_R2.asset");
            if (string.IsNullOrEmpty(receiver2.label))
            {
                receiver2.label = "R2";
                receiver2.feedFace = Dir.Left;
                receiver2.targetSequence = "0";
                receiver2.targetCount = 2;
                EditorUtility.SetDirty(receiver2);
            }
            types["R2"] = receiver2;

            ReceiverTypeSO receiver3 = EnsureAsset<ReceiverTypeSO>(ReceiverFolder + "/Type_Receiver_R3.asset");
            if (string.IsNullOrEmpty(receiver3.label))
            {
                receiver3.label = "R3";
                receiver3.feedFace = Dir.Left;
                receiver3.targetSequence = "1";
                receiver3.targetCount = 2;
                EditorUtility.SetDirty(receiver3);
            }
            types["R3"] = receiver3;

            WallTypeSO wall = EnsureAsset<WallTypeSO>(WallFolder + "/Type_Wall.asset");
            if (string.IsNullOrEmpty(wall.label))
            {
                wall.label = "WALL";
                EditorUtility.SetDirty(wall);
            }
            types["WALL"] = wall;

            return types;
        }

        /// <summary>
        /// 生成 Level_01.asset：坐标与旋转态写在这里，类型资产保持可复用。
        ///
        /// 守卫：资产里已经有摆放就**什么都不改**。关卡内容是作者手改出来的，
        /// 这段代码只是「新克隆项目时的一次性种子」；旧版每次跑菜单都会重写，
        /// 手改等于白改。要重置请走 <see cref="ForceRebuildLevel01"/>。
        /// </summary>
        static void CreateLevelAsset(Dictionary<string, ElementTypeSO> types)
        {
            LevelSO level = EnsureAsset<LevelSO>(LevelAssetPath);

            if (level.placements != null && level.placements.Count > 0)
            {
                Debug.Log("[BitFlowAssetBuilder] Level_01.asset 已有 " + level.placements.Count
                    + " 个摆放，跳过生成、不覆盖手改"
                    + "（要重置请用菜单 SHIFT GO/Setup/⚠ 强制重建 Level_01）");
                return;
            }

            level.displayName = "LEVEL 01 - BIT FLOW";
            level.width = 9;
            level.height = 8;
            level.tickInterval = 0.35f;
            level.placements = new List<ElementPlacement>();

            Add(level, types["E1"], 0, 5);
            Add(level, types["E2"], 0, 1);
            Add(level, types["L1"], 3, 5);
            Add(level, types["N1"], 2, 1);
            Add(level, types["F1"], 5, 1);
            Add(level, types["R1"], 3, 7);
            Add(level, types["R2"], 8, 5);
            Add(level, types["R3"], 8, 1);

            // 墙：与 DemoLevels 中的装饰墙完全一致
            Vector2Int[] walls =
            {
                new Vector2Int(0, 0), new Vector2Int(8, 0), new Vector2Int(0, 7), new Vector2Int(8, 7),
                new Vector2Int(5, 7), new Vector2Int(6, 7), new Vector2Int(7, 7),
                new Vector2Int(4, 3), new Vector2Int(5, 3), new Vector2Int(6, 3),
            };
            foreach (Vector2Int coord in walls)
            {
                Add(level, types["WALL"], coord.x, coord.y);
            }

            EditorUtility.SetDirty(level);
            ValidateAgainstDemo(level);
        }

        static void Add(LevelSO level, ElementTypeSO type, int x, int y, int rotation = 0)
        {
            level.placements.Add(new ElementPlacement
            {
                type = type,
                coord = new Vector2Int(x, y),
                rotationSteps = rotation,
            });
        }

        /// <summary>资产版关卡与代码内置演示关做一致性检查，防止两边漂移。</summary>
        static void ValidateAgainstDemo(LevelSO levelAsset)
        {
            LevelDefinition fromAsset = levelAsset.ToDefinition();
            LevelDefinition fromCode = DemoLevels.CreateLevel01();

            if (fromAsset.Elements.Count != fromCode.Elements.Count)
            {
                Debug.LogWarning("[BitFlowAssetBuilder] 关卡元件数量与 DemoLevels 不一致：资产 "
                    + fromAsset.Elements.Count + " vs 代码 " + fromCode.Elements.Count);
                return;
            }

            Dictionary<GridPoint, ElementDefinition> lookup = new Dictionary<GridPoint, ElementDefinition>();
            foreach (ElementDefinition element in fromCode.Elements) lookup[element.Coord] = element;

            foreach (ElementDefinition element in fromAsset.Elements)
            {
                if (!lookup.TryGetValue(element.Coord, out ElementDefinition expected))
                {
                    Debug.LogWarning("[BitFlowAssetBuilder] 资产里多了一个元件：" + element);
                    continue;
                }
                if (expected.Kind != element.Kind)
                {
                    Debug.LogWarning("[BitFlowAssetBuilder] " + element.Coord + " 类型不一致：资产 "
                        + element.Kind + " vs 代码 " + expected.Kind);
                }
            }

            // 用同一套逻辑跑一遍资产版关卡，确保它同样能通关
            BitFlowSimulation simulation = new BitFlowSimulation(levelAsset.ToDefinition());
            while (!simulation.IsFinished) simulation.Step();
            LevelResult result = simulation.BuildResult();
            Debug.Log("[BitFlowAssetBuilder] 资产版关卡自检：" + (result.IsSuccess ? "通过" : "未通过")
                + "，用时 " + result.TickCount + " ticks");
        }

        /// <summary>生成可直接 Play 的演示场景。返回 true = 这次真的生成了（调用方才去改 Build Settings）。</summary>
        static bool CreateScene()
        {
            // 守卫：场景文件已经存在就绝不动它。CreateScene 走的是 NewScene(EmptyScene, Single)，
            // 会先把当前场景整个关掉再重建成 4 个基础对象 —— 手调的相机、模型朝向/缩放、
            // VFX 位置全都会没。只在场景不存在时生成一次。
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)))
            {
                Debug.Log("[BitFlowAssetBuilder] " + ScenePath
                    + " 已存在，跳过场景重建（要重置请手动删除该场景文件后再跑）");
                return false;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 环境光：整体压暗，靠自发光与 Bloom 出效果
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.12f, 0.16f);
            RenderSettings.skybox = null;

            CreateCamera();
            CreateLight();
            CreateGlobalVolume();

            GameObject root = new GameObject("BitFlowDemo");
            root.AddComponent<LevelBuilder>();
            BitFlowRunner runner = root.AddComponent<BitFlowRunner>();
            root.AddComponent<BitFlowHud>();

            LevelSO levelAsset = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelAssetPath);
            SerializedObject runnerSerialized = new SerializedObject(runner);
            SerializedProperty levelProperty = runnerSerialized.FindProperty("levelAsset");
            if (levelProperty != null)
            {
                levelProperty.objectReferenceValue = levelAsset;
                runnerSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[BitFlowAssetBuilder] 场景保存" + (saved ? "成功：" : "失败：") + ScenePath);
            return saved;
        }

        static void CreateCamera()
        {
            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";

            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.02f, 0.035f);
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            // URP 需要显式挂后处理数据，否则 Volume 里的 Bloom 不生效
            UniversalAdditionalCameraData cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.None; // 已开 4x MSAA，不必再叠 SMAA

            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<CameraRig>();
        }

        static void CreateLight()
        {
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.78f, 0.88f, 1f);
            light.intensity = 0.85f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;
            lightGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
        }

        static void CreateGlobalVolume()
        {
            GameObject volumeGo = new GameObject("Global Volume");
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                Debug.LogWarning("[BitFlowAssetBuilder] 没找到 Volume Profile（先跑一次 ProjectSetup），Bloom 暂不生效");
                return;
            }
            volume.sharedProfile = profile;
        }

        /// <summary>把演示场景排到 Build Settings 第一位，打包后直接进演示。</summary>
        static void RegisterScene()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == ScenePath) continue;
                scenes.Add(existing);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[BitFlowAssetBuilder] 已登记进 Build Settings（index 0）：" + ScenePath);
        }
    }
}
