using System;
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
    /// 玩家模式（有限手牌）的关卡 + 场景生成工具，配合 <see cref="BitFlowAssetBuilder"/> 使用。
    ///
    /// 命令行：
    ///   Unity.exe -batchmode -quit -projectPath "&lt;项目根&gt;"
    ///             -executeMethod ShiftGo.EditorTools.BitFlowPlayerSetup.BuildAll -logFile "&lt;日志&gt;"
    ///
    /// 产出：
    ///   Assets/Data/Elements/Receiver/Type_Receiver_R4.asset —— 左侧进料的「六个 1」接收器（教学关专用）
    ///   Assets/Data/Elements/Filter/Type_Filter_F0.asset     —— 只放行 0 的过滤器（教学关专用）
    ///   Assets/Data/Levels/Level_02~04.asset        —— 三关教学（过滤器 / 分流器 / 分流+旋转）
    ///   Assets/Data/Levels/LevelCatalog.asset       —— 关卡顺序表（02 → 03 → 04）
    ///   Assets/Scenes/Test_3_BitFlowPlayer.unity   —— 玩家场景（BUILD MODE 关掉，手牌盘接管）
    ///
    /// 三关都会自检：不放任何元件必输、按参考解摆上去必须通关。任何一关自检不过就报错，
    /// 免得手改关卡数据之后悄悄变成不可通关。
    /// </summary>
    public static class BitFlowPlayerSetup
    {
        const string ElementFolder = "Assets/Data/Elements";

        // 与 BitFlowAssetBuilder 保持同一套「按元件种类分目录」的存放规则。
        const string EmitterFolder = ElementFolder + "/Emitter";
        const string ReceiverFolder = ElementFolder + "/Receiver";
        const string SplitterFolder = ElementFolder + "/Splitter";
        const string NotGateFolder = ElementFolder + "/NotGate";
        const string FilterFolder = ElementFolder + "/Filter";
        const string WallFolder = ElementFolder + "/Wall";

        const string LevelFolder = "Assets/Data/Levels";
        const string CatalogPath = LevelFolder + "/LevelCatalog.asset";
        const string PlayerScenePath = "Assets/Scenes/Test_3_BitFlowPlayer.unity";
        const string VolumeProfilePath = "Assets/Settings/URP_GlobalVolumeProfile.asset";

        /// <summary>自检时最多推进多少 tick；教学关都在 40 tick 内跑完，留足余量。</summary>
        const int ValidationTickCap = 256;

        [MenuItem("SHIFT GO/Setup/3. 生成教学关 + 玩家场景", priority = 2)]
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

            Dictionary<string, ElementTypeSO> types = LoadElementTypes();

            LevelSO level02 = CreateLevel02(types);
            LevelSO level03 = CreateLevel03(types);
            LevelSO level04 = CreateLevel04(types);

            LevelCatalogSO catalog = CreateCatalog(level02, level03, level04);

            // 只有真的生成了玩家场景才动 Build Settings（否则会把别人排好的入口场景顶掉）
            if (CreatePlayerScene(level02, catalog)) RegisterScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BitFlowPlayerSetup] 完成：Level_02~04 + LevelCatalog + Test_3_BitFlowPlayer.unity");
        }

        /// <summary>
        /// 逃生门：清空三关的摆放与手牌，再按代码里的教学关重新种一遍。
        /// 会丢掉所有手改，只在「想恢复原始教学关」时用。
        /// </summary>
        [MenuItem("SHIFT GO/Setup/⚠ 强制重建教学关（覆盖手改）", priority = 91)]
        public static void ForceRebuildTeachingLevels()
        {
            string[] names = { "Level_02", "Level_03", "Level_04" };
            List<LevelSO> found = new List<LevelSO>();
            int totalLoads = 0;

            foreach (string name in names)
            {
                LevelSO level = AssetDatabase.LoadAssetAtPath<LevelSO>(LevelFolder + "/" + name + ".asset");
                if (level == null)
                {
                    Debug.LogWarning("[BitFlowPlayerSetup] 没有 " + name + ".asset，跳过");
                    continue;
                }
                found.Add(level);
                totalLoads += level.placements != null ? level.placements.Count : 0;
            }

            if (found.Count == 0)
            {
                Debug.LogError("[BitFlowPlayerSetup] 三个教学关资产都没找到，先跑「3. 生成教学关 + 玩家场景」");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "强制重建教学关",
                "这会清空 Level_02~04 现有的 " + totalLoads + " 个摆放与全部手牌，"
                    + "并按代码里的原始教学关重写。\n\n手改内容不可撤销，确定吗？",
                "清空并重建", "取消");

            if (!confirmed) return;

            foreach (LevelSO level in found)
            {
                Undo.RecordObject(level, "Force Rebuild Teaching Levels");
                level.placements = new List<ElementPlacement>();
                level.supplies = new List<ElementSupply>();
                EditorUtility.SetDirty(level);
            }

            BuildAll();
            Debug.Log("[BitFlowPlayerSetup] 三个教学关已强制重建");
        }

        // ---------------------------------------------------------------- 资产小工具

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

        /// <summary>
        /// 读齐生成教学关要用的类型资产；缺哪个就现场补一个，不依赖第 2 步的执行顺序。
        /// 守卫：只在资产「还没初始化」（label 为空）时写默认配置，已配置过的一律不碰。
        /// </summary>
        static Dictionary<string, ElementTypeSO> LoadElementTypes()
        {
            Dictionary<string, ElementTypeSO> types = new Dictionary<string, ElementTypeSO>();

            EmitterTypeSO emitter1 = EnsureAsset<EmitterTypeSO>(EmitterFolder + "/Type_Emitter_E1.asset");
            if (string.IsNullOrEmpty(emitter1.label)) ConfigureEmitter(emitter1, "E1", Dir.Right, "1011", 2);
            types["E1"] = emitter1;

            SplitterTypeSO splitter1 = EnsureAsset<SplitterTypeSO>(SplitterFolder + "/Type_Splitter_L1.asset");
            if (string.IsNullOrEmpty(splitter1.label)) ConfigureSplitter(splitter1, "L1", Dir.Up, Dir.Right);
            types["L1"] = splitter1;

            FilterTypeSO filter1 = EnsureAsset<FilterTypeSO>(FilterFolder + "/Type_Filter_F1.asset");
            if (string.IsNullOrEmpty(filter1.label)) ConfigureFilter(filter1, "F1", Dir.Right, 1);
            types["F1"] = filter1;

            ReceiverTypeSO receiver1 = EnsureAsset<ReceiverTypeSO>(ReceiverFolder + "/Type_Receiver_R1.asset");
            if (string.IsNullOrEmpty(receiver1.label)) ConfigureReceiver(receiver1, "R1", Dir.Down, "1", 6);
            types["R1"] = receiver1;

            ReceiverTypeSO receiver2 = EnsureAsset<ReceiverTypeSO>(ReceiverFolder + "/Type_Receiver_R2.asset");
            if (string.IsNullOrEmpty(receiver2.label)) ConfigureReceiver(receiver2, "R2", Dir.Left, "0", 2);
            types["R2"] = receiver2;

            // 教学关需要的两个新类型：左侧进料的六位 1 接收器、只放行 0 的过滤器
            ReceiverTypeSO receiver4 = EnsureAsset<ReceiverTypeSO>(ReceiverFolder + "/Type_Receiver_R4.asset");
            if (string.IsNullOrEmpty(receiver4.label)) ConfigureReceiver(receiver4, "R4", Dir.Left, "1", 6);
            types["R4"] = receiver4;

            FilterTypeSO filter0 = EnsureAsset<FilterTypeSO>(FilterFolder + "/Type_Filter_F0.asset");
            if (string.IsNullOrEmpty(filter0.label)) ConfigureFilter(filter0, "F0", Dir.Right, 0);
            types["F0"] = filter0;

            WallTypeSO wall = EnsureAsset<WallTypeSO>(WallFolder + "/Type_Wall.asset");
            if (string.IsNullOrEmpty(wall.label)) wall.label = "WALL";
            types["WALL"] = wall;

            return types;
        }

        static void ConfigureEmitter(EmitterTypeSO type, string label, Dir facing, string sequence, int repeatCount)
        {
            type.label = label;
            type.facing = facing;
            type.sequence = sequence;
            type.repeatCount = repeatCount;
            EditorUtility.SetDirty(type);
        }

        static void ConfigureSplitter(SplitterTypeSO type, string label, Dir oneArm, Dir zeroArm)
        {
            type.label = label;
            type.oneArm = oneArm;
            type.zeroArm = zeroArm;
            EditorUtility.SetDirty(type);
        }

        static void ConfigureFilter(FilterTypeSO type, string label, Dir outputDir, byte passValue)
        {
            type.label = label;
            type.outputDir = outputDir;
            type.passValue = passValue;
            EditorUtility.SetDirty(type);
        }

        static void ConfigureReceiver(ReceiverTypeSO type, string label, Dir feedFace, string sequence, int count)
        {
            type.label = label;
            type.feedFace = feedFace;
            type.targetSequence = sequence;
            type.targetCount = count;
            EditorUtility.SetDirty(type);
        }

        // ---------------------------------------------------------------- 摆放表小工具

        /// <summary>关卡预设件：默认 playerEditable = false，玩家搬不动这些（发射器 / 接收器 / 墙）。</summary>
        static ElementPlacement Placement(ElementTypeSO type, int x, int y, int rotationSteps = 0)
        {
            return new ElementPlacement
            {
                type = type,
                coord = new Vector2Int(x, y),
                rotationSteps = rotationSteps,
                playerEditable = false,
            };
        }

        static ElementSupply Supply(ElementTypeSO type, int count)
        {
            return new ElementSupply { type = type, count = count };
        }

        static LevelSO EnsureLevel(string fileName, string displayName, int width, int height, float tickInterval)
        {
            LevelSO level = EnsureAsset<LevelSO>(LevelFolder + "/" + fileName + ".asset");

            // 守卫：已有内容的关卡连尺寸都不动。作者可能调过网格，重新生成时不该打回去。
            if (level.placements != null && level.placements.Count > 0) return level;

            level.displayName = displayName;
            level.width = width;
            level.height = height;
            level.tickInterval = tickInterval;
            return level;
        }

        /// <summary>
        /// 守卫：这一关已经有摆放了就完全不动它（连摆放、手牌、自检一起跳过）。
        /// 生成器只是「新克隆项目时的一次性种子」，之后资产就是唯一真相；
        /// 旧版每次跑菜单都重写摆放 + 手牌，作者手改等于白改。
        /// 要重置请走菜单 SHIFT GO/Setup/⚠ 强制重建教学关。
        /// </summary>
        static bool SkipIfAuthored(LevelSO level, string tag)
        {
            if (level.placements == null || level.placements.Count == 0) return false;

            Debug.Log("[BitFlowPlayerSetup] " + tag + " 已有 " + level.placements.Count
                + " 个摆放 + " + (level.supplies != null ? level.supplies.Count : 0)
                + " 张手牌，跳过生成、不覆盖手改"
                + "（要重置请用菜单 SHIFT GO/Setup/⚠ 强制重建教学关）");
            return true;
        }

        // ---------------------------------------------------------------- 三关教学

        /// <summary>
        /// 第 2 关 · 过滤器：走廊是通的，但发射器会混着吐 0。玩家放一张「只放行 1」的过滤器把 0 挡掉，
        /// 接收器只认六个 1 —— 不放牌必输，放哪一格都行，教的是「元件是通关条件，不是装饰」。
        /// </summary>
        static LevelSO CreateLevel02(Dictionary<string, ElementTypeSO> types)
        {
            LevelSO level = EnsureLevel("Level_02", "LEVEL 02 - FILTER", 7, 5, 0.35f);
            if (SkipIfAuthored(level, "Level_02")) return level;

            level.placements = new List<ElementPlacement>
            {
                Placement(types["E1"], 0, 2), // 发射器：六个 1 + 两个 0
                Placement(types["R4"], 6, 2), // 接收器：左侧进料，只收六个 1
            };

            level.supplies = new List<ElementSupply>
            {
                Supply(types["F1"], 1),
            };

            EditorUtility.SetDirty(level);
            ValidateLevel(level, "Level_02", draft =>
            {
                draft.TryPlace(types["F1"].CreateEditable(new GridPoint(3, 2)));
            });
            return level;
        }

        /// <summary>
        /// 第 3 关 · 分流器：一路走成两路。1 往上进 R1，0 往右进 R2，
        /// 两块接收器各自只认自己的值 —— 教的是「分流器按值分岔」。
        /// </summary>
        static LevelSO CreateLevel03(Dictionary<string, ElementTypeSO> types)
        {
            LevelSO level = EnsureLevel("Level_03", "LEVEL 03 - SPLITTER", 9, 7, 0.35f);
            if (SkipIfAuthored(level, "Level_03")) return level;

            level.placements = new List<ElementPlacement>
            {
                Placement(types["E1"], 0, 2), // 发射器：六个 1 + 两个 0
                Placement(types["R1"], 3, 5), // 上方：进料面朝下，只收六个 1
                Placement(types["R2"], 6, 2), // 右侧：进料面朝左，只收两个 0
            };

            level.supplies = new List<ElementSupply>
            {
                Supply(types["L1"], 1),
            };

            EditorUtility.SetDirty(level);
            ValidateLevel(level, "Level_03", draft =>
            {
                // L1 基准是 1→上 / 0→右，正好就是这一关要的分法
                draft.TryPlace(types["L1"].CreateEditable(new GridPoint(3, 2)));
            });
            return level;
        }

        /// <summary>
        /// 第 4 关 · 分流 + 旋转：1 要往右进 R4，0 要往下拐进 R2。
        /// 分流器基准是「1→上 / 0→右」，所以必须放下之后按一次 R 转 90°，
        /// 0 支路再从下一格的过滤器（只放行 0）接出去 —— 两件牌、一次旋转，第一次要求「组合」。
        /// </summary>
        static LevelSO CreateLevel04(Dictionary<string, ElementTypeSO> types)
        {
            LevelSO level = EnsureLevel("Level_04", "LEVEL 04 - TURN & FILTER", 9, 6, 0.35f);
            if (SkipIfAuthored(level, "Level_04")) return level;

            level.placements = new List<ElementPlacement>
            {
                Placement(types["E1"], 0, 2), // 发射器：六个 1 + 两个 0
                Placement(types["R4"], 7, 2), // 上面那条：只收六个 1
                Placement(types["R2"], 7, 0), // 下面那条：只收两个 0
            };

            level.supplies = new List<ElementSupply>
            {
                Supply(types["L1"], 1),
                Supply(types["F0"], 1),
            };

            EditorUtility.SetDirty(level);
            ValidateLevel(level, "Level_04", draft =>
            {
                // 分流器转一次：1 → 右（继续走向 R4），0 → 下（拐去 R2 那条）
                draft.TryPlace(types["L1"].CreateEditable(new GridPoint(4, 2)));
                draft.Rotate(new GridPoint(4, 2));

                // 0 支路的出口过滤器：只放行 0，朝右送到 R2
                draft.TryPlace(types["F0"].CreateEditable(new GridPoint(4, 0)));
            });
            return level;
        }

        static LevelCatalogSO CreateCatalog(params LevelSO[] levels)
        {
            LevelCatalogSO catalog = EnsureAsset<LevelCatalogSO>(CatalogPath);
            catalog.levels = new List<LevelSO>(levels);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        // ---------------------------------------------------------------- 自检

        /// <summary>
        /// 关卡自检：① 一个元件都不放必须失败（否则这一关没有难度）；② 参考解必须通关。
        /// 参考解只能通过 <see cref="LevelDraft"/> 摆，走的正是玩家那条路径（含手牌扣减与锁定判定）。
        /// </summary>
        static void ValidateLevel(LevelSO level, string tag, Action<LevelDraft> solve)
        {
            LevelDraft unsolved = level.CreatePlayerDraft();
            if (RunToEnd(unsolved.ToDefinition()))
            {
                Debug.LogError("[BitFlowPlayerSetup] " + tag + " 不放任何元件就能通关，这一关没有难度");
            }

            if (solve == null) return;

            LevelDraft solved = level.CreatePlayerDraft();
            solve(solved);
            if (solved.Count == unsolved.Count)
            {
                Debug.LogError("[BitFlowPlayerSetup] " + tag + " 参考解一件都没放上去，检查 hand 里的类型 asset 引用");
                return;
            }

            if (!RunToEnd(solved.ToDefinition()))
            {
                Debug.LogError("[BitFlowPlayerSetup] " + tag + " 参考解跑不通，检查元件配置与坐标");
                return;
            }

            Debug.Log("[BitFlowPlayerSetup] " + tag + " 自检通过（不放必输 / 参考解通关）");
        }

        static bool RunToEnd(LevelDefinition definition)
        {
            BitFlowSimulation simulation = new BitFlowSimulation(definition);
            int guard = 0;
            while (!simulation.IsFinished && guard++ < ValidationTickCap)
            {
                simulation.Step();
            }
            return simulation.BuildResult().IsSuccess;
        }

        // ---------------------------------------------------------------- 玩家场景

        /// <summary>
        /// 生成玩家场景：BUILD MODE 默认关掉（allowBuildMode = false），放置交给手牌盘；
        /// HUD 的 RETRY / NEXT LEVEL 走 <see cref="LevelFlow"/>，于是整个关卡循环就是 catalog 的顺序。
        /// </summary>
        static bool CreatePlayerScene(LevelSO firstLevel, LevelCatalogSO catalog)
        {
            // 守卫：玩家场景已存在就绝不重建。NewScene(EmptyScene, Single) 会先关掉当前场景
            // 再重建成空白场景 + 6 个基础对象，手调的模型朝向/缩放、相机、VFX 全都会没。
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(PlayerScenePath)))
            {
                Debug.Log("[BitFlowPlayerSetup] " + PlayerScenePath
                    + " 已存在，跳过场景重建（要重置请手动删除该场景文件后再跑）；"
                    + "若 LevelCatalog 顺序有变，请手动确认 BitFlowRunner.levelAsset 指向第一关");
                return false;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.12f, 0.16f);
            RenderSettings.skybox = null;

            CreateCamera();
            CreateLight();
            CreateGlobalVolume();

            GameObject root = new GameObject("BitFlowPlayer");
            root.AddComponent<LevelBuilder>();
            BitFlowRunner runner = root.AddComponent<BitFlowRunner>();

            // 开场演出：读数据之后按 网格 → 墙 → 收发器 → 其他元件 一段段把画面建出来。
            // 没有它也能玩（BitFlowRunner 会在同一帧全建），但开场会掉那一下帧。
            root.AddComponent<LevelReveal>();

            root.AddComponent<BitFlowHud>();
            root.AddComponent<BitFlowCardTray>();
            LevelFlow flow = root.AddComponent<LevelFlow>();

            // 换关黑幕 + 选关面板：两个都靠 [DefaultExecutionOrder] 抢在 LevelFlow(-10) 之前
            // Awake，所以就算这里是最后加的，第一帧也不会露出空网格。
            root.AddComponent<IntroCurtain>();
            root.AddComponent<LevelSelectPanel>();

            SerializedObject runnerSerialized = new SerializedObject(runner);
            SetObjectReference(runnerSerialized, "levelAsset", firstLevel);
            SetBool(runnerSerialized, "allowBuildMode", false);
            runnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject flowSerialized = new SerializedObject(flow);
            SetObjectReference(flowSerialized, "catalog", catalog);
            SetBool(flowSerialized, "disableBuildMode", true);
            flowSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, PlayerScenePath);
            Debug.Log("[BitFlowPlayerSetup] 玩家场景保存" + (saved ? "成功：" : "失败：") + PlayerScenePath);
            return saved;
        }

        static void SetObjectReference(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning("[BitFlowPlayerSetup] 序列化字段没找到：" + propertyName);
                return;
            }
            property.objectReferenceValue = value;
        }

        static void SetBool(SerializedObject target, string propertyName, bool value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null) return;
            property.boolValue = value;
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

            UniversalAdditionalCameraData cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.None;

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
                Debug.LogWarning("[BitFlowPlayerSetup] 没找到 Volume Profile（先跑一次 ProjectSetup），Bloom 暂不生效");
                return;
            }
            volume.sharedProfile = profile;
        }

        /// <summary>把玩家场景排到 Build Settings 第一位（打包后直接进有限手牌模式）。</summary>
        static void RegisterScene()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(PlayerScenePath, true),
            };

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == PlayerScenePath) continue;
                scenes.Add(existing);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[BitFlowPlayerSetup] 已登记进 Build Settings（index 0）：" + PlayerScenePath);
        }
    }
}
