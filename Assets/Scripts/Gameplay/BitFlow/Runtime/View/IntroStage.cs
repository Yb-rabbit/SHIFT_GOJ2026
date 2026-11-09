using UnityEngine;
using UnityEngine.Rendering;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 开场终端背后的那台"舞台"：一块 9×8 的空网格 + 左右两台机器（Receiver 左 / Emitter 右），
    /// 外加开场专用的灯光、雾和机位。
    ///
    /// <para>为什么需要它：终端面板（<see cref="TerminalView"/>）是半透明的，
    /// 背后什么都没有的时候就只是一块纯黑 —— 台词里在跟"发射器 / 接收器"说话，
    /// 画面上却看不见任何机器。这块背景就是让那两台机器出现在面板上方，
    /// 玩家一眼能把 EMIT / RECV 和左边的方块对上号。</para>
    ///
    /// <para><b>只服务开场演出</b>：关卡里的元件外观仍然是几何体拼装（见 <see cref="ElementView"/>），
    /// 这里走的是 <see cref="ElementModelLibrary"/> 里那条"不归一化、只修材质"的旁路，
    /// 尺寸由本文件自己定（最长边 3.4），跟格子里的元件尺寸互不影响。</para>
    ///
    /// <para>所有被改过的全局状态（相机、主光、环境光、天空盒、雾）都在 <see cref="Dispose"/> 里
    /// 原样还回去 —— 演出结束后关卡拿到的是它本来那套灯光。</para>
    /// </summary>
    public sealed class IntroStage
    {
        /// <summary>背景网格的尺寸（和开场原型一致：9×8 正好一屏）。</summary>
        const int StageWidth = 9;
        const int StageHeight = 8;

        /// <summary>模型缩放后的最长边（米）。比一格大得多 —— 这里要的是"背景里的视觉分量"，不是格子大小。</summary>
        const float ModelLongestEdge = 3.4f;

        /// <summary>两台模型相对舞台中心的水平偏移比例（0.30 → 站在中线两侧，中间留出空隙）。</summary>
        const float ModelSpreadX = 0.30f;

        /// <summary>两台模型在 Z 上的偏移比例：往网格远端挪一点，退到终端面板上沿之外。</summary>
        const float ModelSpreadZ = 0.22f;

        static readonly Color BackgroundColor = new Color(0.020f, 0.030f, 0.050f, 1f);

        GameObject root;
        bool disposed;

        // ---------------------------------------------------------------- 现场快照

        Camera camera;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        float cameraFieldOfView;
        float cameraNear;
        float cameraFar;
        CameraClearFlags cameraClearFlags;
        Color cameraBackground;

        Light[] lights = new Light[0];
        Vector3[] lightRotations = new Vector3[0];
        float[] lightIntensities = new float[0];
        Color[] lightColors = new Color[0];

        GameObject rimLight;

        AmbientMode ambientMode;
        Color ambientLight;
        Material skybox;
        bool fog;
        FogMode fogMode;
        Color fogColor;
        float fogStartDistance;
        float fogEndDistance;

        /// <summary>舞台根节点（Dispose 之后就没了，测试里只用来量包围盒）。</summary>
        public GameObject Root => root;

        /// <summary>
        /// 搭一块开场舞台。<paramref name="parent"/> 为空时铺在场景根下。
        /// 模型 / 相机缺任何一个都不会抛异常：模型没加载到就只留网格，没有相机就只搭场景。
        /// </summary>
        public static IntroStage Build(Transform parent = null)
        {
            IntroStage stage = new IntroStage();
            stage.Construct(parent);
            return stage;
        }

        void Construct(Transform parent)
        {
            root = new GameObject("IntroStage");
            if (parent != null) root.transform.SetParent(parent, false);

            LevelBuilder builder = root.AddComponent<LevelBuilder>();
            LevelDefinition definition = new LevelDefinition { Width = StageWidth, Height = StageHeight };
            builder.Prepare(definition);
            builder.BuildTerrain();

            // 机位按"只有地板"的舞台算：机器是在算完之后才摆上去的。
            // 反过来（先摆机器再算）包围盒会高出一大截，镜头会被抬到俯视机器头顶的角度上。
            Bounds bounds = ComputeBounds(root);

            Snapshot();
            ApplyCamera(bounds);
            ApplyLighting();
            PlaceModels(bounds);
        }

        /// <summary>
        /// 拆掉舞台并把所有全局状态还回去。重复调用安全 ——
        /// 演出被提前掐掉时 <see cref="IntroTerminal.OnDestroy"/> 也会走一次。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            Restore();

            DestroyObject(root);
            root = null;
        }

        // ---------------------------------------------------------------- 场景

        static Bounds ComputeBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, new Vector3(StageWidth, 0.1f, StageHeight));

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>
        /// 两台模型摆在网格远端，相机在近端看过去 —— 正好被终端面板裁住下半截，
        /// 既看得见体积，又不会和台词抢注意力。
        /// </summary>
        void PlaceModels(Bounds stageBounds)
        {
            float x = stageBounds.extents.x * ModelSpreadX;
            float z = stageBounds.extents.z * ModelSpreadZ;
            float y = stageBounds.max.y;

            SpawnModel(ElementModelLibrary.ReceiverModel,
                new Vector3(stageBounds.center.x - x, y, stageBounds.center.z + z), 180f);

            SpawnModel(ElementModelLibrary.EmitterModel,
                new Vector3(stageBounds.center.x + x, y, stageBounds.center.z + z), 0f);
        }

        void SpawnModel(string modelName, Vector3 position, float yaw)
        {
            GameObject holder = new GameObject("Intro_" + modelName);
            holder.transform.SetParent(root.transform, false);

            GameObject mesh = ElementModelLibrary.SpawnDisplay(modelName, holder.transform);
            if (mesh == null)
            {
                // 模型没导入时不留一个空节点：舞台就是纯网格，观众只少看两台机器，不该看到报错
                DestroyObject(holder);
                return;
            }

            // 等比归一化到最长边 3.4，然后底面贴地、水平居中到 holder 原点。
            // 模型 pivot 五花八门，不对齐就会浮在半空或者陷进地板里。
            Bounds bounds = ComputeBounds(mesh);
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest > 0.0001f)
            {
                mesh.transform.localScale = Vector3.one * (ModelLongestEdge / largest);
                bounds = ComputeBounds(mesh);
            }

            mesh.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            holder.transform.position = position;
        }

        // ---------------------------------------------------------------- 相机

        /// <summary>
        /// 开场机位：从近端高处俯看（34°），视线落在网格中心上方一点。
        /// 抬高视线是有意的 —— 地板被推到画面下方，两台机器整体落到终端面板上沿之外，
        /// 不会被面板拦腰切断。
        /// </summary>
        void ApplyCamera(Bounds stageBounds)
        {
            camera = Camera.main;
            if (camera == null) return;

            Vector3 pivot = stageBounds.center;
            float distance = Mathf.Max(stageBounds.size.x, stageBounds.size.z);
            Vector3 offset = Quaternion.Euler(34f, 0f, 0f) * Vector3.back * distance;

            camera.transform.position = pivot + offset + Vector3.up * 0.2f;
            camera.transform.LookAt(pivot + Vector3.up * 1.0f);

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;
        }

        // ---------------------------------------------------------------- 灯光

        void ApplyLighting()
        {
            // 主光：把场景里那盏平行光摆成开场角度，演出结束再还回去
            foreach (Light light in lights)
            {
                if (light == null) continue;

                light.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
                light.intensity = 1.15f;
                light.color = new Color(0.82f, 0.90f, 1.00f);
            }

            // 背面补一盏冷色轮廓光：灰色模型在黑场里只靠主光会糊成一块方砖，
            // 有了轮廓才看得出「机器」的边角，也和网格的青线同调。
            rimLight = new GameObject("Intro_RimLight");
            rimLight.transform.SetParent(root.transform, false);

            Light rim = rimLight.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.35f;
            rim.color = new Color(0.32f, 0.72f, 0.95f);
            rimLight.transform.rotation = Quaternion.Euler(24f, 158f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.09f, 0.12f, 1f);

            // 有天空盒时地板会反出蓝天，整块地面变成亮蓝色；开场要的是黑场
            RenderSettings.skybox = null;

            // 远端渐隐进黑：比指望"看到地板边"干净，也不会露出无意义的天空
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = BackgroundColor;
            RenderSettings.fogStartDistance = 10f;
            RenderSettings.fogEndDistance = 38f;
        }

        // ---------------------------------------------------------------- 快照 / 还原

        void Snapshot()
        {
            camera = Camera.main;
            if (camera != null)
            {
                cameraPosition = camera.transform.position;
                cameraRotation = camera.transform.rotation;
                cameraFieldOfView = camera.fieldOfView;
                cameraNear = camera.nearClipPlane;
                cameraFar = camera.farClipPlane;
                cameraClearFlags = camera.clearFlags;
                cameraBackground = camera.backgroundColor;
            }

            Light[] all = Object.FindObjectsOfType<Light>();
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].type == LightType.Directional) count++;
            }

            lights = new Light[count];
            lightRotations = new Vector3[count];
            lightIntensities = new float[count];
            lightColors = new Color[count];

            int index = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Light light = all[i];
                if (light == null || light.type != LightType.Directional) continue;

                lights[index] = light;
                lightRotations[index] = light.transform.rotation.eulerAngles;
                lightIntensities[index] = light.intensity;
                lightColors[index] = light.color;
                index++;
            }

            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            skybox = RenderSettings.skybox;
            fog = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogColor = RenderSettings.fogColor;
            fogStartDistance = RenderSettings.fogStartDistance;
            fogEndDistance = RenderSettings.fogEndDistance;
        }

        void Restore()
        {
            if (camera != null)
            {
                camera.transform.position = cameraPosition;
                camera.transform.rotation = cameraRotation;
                camera.fieldOfView = cameraFieldOfView;
                camera.nearClipPlane = cameraNear;
                camera.farClipPlane = cameraFar;
                camera.clearFlags = cameraClearFlags;
                camera.backgroundColor = cameraBackground;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;

                lights[i].transform.rotation = Quaternion.Euler(lightRotations[i]);
                lights[i].intensity = lightIntensities[i];
                lights[i].color = lightColors[i];
            }

            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.skybox = skybox;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStartDistance;
            RenderSettings.fogEndDistance = fogEndDistance;
        }

        static void DestroyObject(Object target)
        {
            if (target == null) return;

            // EditMode 测试里 Object.Destroy 会报错（并残留对象），所以两种模式分开处理
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}


