using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 自制低模（<c>Assets/Arts/Resources/Models/*.fbx</c>）的加载与"摆正"。
    ///
    /// 模型是美术资产，**颜色属于美术，比例也尽量保留**：
    /// 不换材质；尺寸只做一次等比归一化（水平方向占满 <see cref="CellFootprint"/> 格），
    /// 绝不单独压高度 —— 单独压高度会把模型压成矮扁的玩具（这就是上一版看起来"完全不对"的原因）。
    ///
    /// 三件必须由代码兜底的事：
    /// 1. 按名字从 Resources 取模型（取不到返回 false，调用方退回原来的几何体拼装）；
    /// 2. 底面落到格子上、水平居中（模型 pivot 五花八门，不对齐格子会浮空 / 偏心）；
    /// 3. 按元件的实际朝向绕 Y 轴转向（+ 作者补偿角 <see cref="EmitterYawOffset"/>）。
    ///
    /// 唯一的例外是 shader：老 FBX 常带 Built-in <c>Standard</c> 材质，在 URP 下会渲染成洋红，
    /// 这时才换成 URP Lit 并把原来的主贴图 / 主色搬过去 —— 这是"让它能看见"，不是"换颜色"。
    /// </summary>
    public static class ElementModelLibrary
    {
        /// <summary>发射器模型名（不带扩展名，放在 Resources/Models 下）。</summary>
        public const string EmitterModel = "Emitter001";

        /// <summary>接收器模型名（不带扩展名，放在 Resources/Models 下）。</summary>
        public const string ReceiverModel = "Receiver001";

        const string ModelFolder = "Models/";

        /// <summary>
        /// 模型建模时的正面朝向补偿角（度，绕 Y 轴）。
        /// 约定：模型正面朝 +Z（= 网格的"上"），也就是 LookRotation 之后不用再转。
        /// 如果换模型后发现它侧着 / 背对玩家，只改这两个值即可（±90 / 180）。
        /// </summary>
        public const float EmitterYawOffset = 0f;

        /// <inheritdoc cref="EmitterYawOffset"/>
        public const float ReceiverYawOffset = 0f;

        /// <summary>
        /// 模型在一格里应该占多大（1 = 正好一格）。默认 0.9：留一点边，避免和邻格贴死。
        ///
        /// 这是唯一一处代码"替美术决定尺寸"的地方，而且只做**等比**归一化：
        /// 自制模型是按自己舒服的比例建的（当前 Emitter001 ≈ 2.39 × 2.65 × 2.00 米，比一格大两倍多），
        /// 若原样放进 1 × 1 的格子会压到邻格；若按高度硬塞，又会被压成矮扁的玩具。
        /// 所以统一按"水平方向刚好占满 <see cref="CellFootprint"/> 格"缩放，高度随比例走 —— 造型和体积感不变。
        /// 想让模型更大 / 更小，只改这一个数（或下面两个 ModelScale）。
        /// </summary>
        public const float CellFootprint = 0.9f;

        /// <summary>归一化之后的额外倍率（1 = 按 <see cref="CellFootprint"/> 占满一格）。</summary>
        public const float EmitterModelScale = 1f;

        /// <summary>归一化之后的额外倍率（1 = 按 <see cref="CellFootprint"/> 占满一格）。</summary>
        public const float ReceiverModelScale = 1f;

        /// <summary>
        /// 归一化后仍然高过这个倍数（相对一格）就打一次警告：多半是模型比例太"瘦高"，
        /// 头顶的箭头 / 点阵会离得很远。这时调 <see cref="CellFootprint"/> 或回 Blender 改比例。
        /// </summary>
        const float HeightWarnFactor = 1.6f;

        /// <summary>模型底面离地的高度：略微抬起，避免和地板 / 网格线 z-fighting。</summary>
        public const float GroundY = 0.02f;

        /// <summary>一次装配的结果，单位全是元件本体的本地坐标。</summary>
        public struct Fit
        {
            /// <summary>装配好的模型根节点（名字固定为 "model"，方便测试和调试）。</summary>
            public GameObject Root;

            /// <summary>模型顶面高度：箭头、点阵、进度条都摆在它上面。</summary>
            public float TopY;

            /// <summary>模型装配后的 X 方向半径。</summary>
            public float HalfWidth;

            /// <summary>模型装配后的 Z 方向半径。</summary>
            public float HalfDepth;

            /// <summary>模型原始包围盒边长（未缩放的原始尺寸），排查比例问题时用。</summary>
            public Vector3 RawSize;

            /// <summary>实际用上的整体缩放（= CellFootprint 归一化 × 对应 ModelScale）。</summary>
            public float Scale;

            public bool Ok => Root != null;
        }

        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        static readonly HashSet<string> missing = new HashSet<string>();

        /// <summary>从 Resources/Models 取模型；取不到时只警告一次，避免每帧刷屏。</summary>
        public static GameObject Load(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return null;
            if (cache.TryGetValue(modelName, out GameObject cached)) return cached;
            if (missing.Contains(modelName)) return null;

            GameObject asset = Resources.Load<GameObject>(ModelFolder + modelName);
            if (asset == null)
            {
                missing.Add(modelName);
                Debug.LogWarning("[BitFlow] 没找到模型 Resources/" + ModelFolder + modelName
                    + "，该元件退回几何体拼装。检查文件是否在 Assets/Arts/Resources/Models 下且已导入。");
                return null;
            }

            cache[modelName] = asset;
            return asset;
        }

        /// <summary>
        /// 把模型挂到 <paramref name="parent"/> 下面：等比归一化到一格、底面落地、水平居中、按 <paramref name="facing"/> 转向。
        ///
        /// 缩放只有一次等比归一化（水平方向占满 <see cref="CellFootprint"/> 格，再乘 <paramref name="modelScale"/>），
        /// 三个轴共用一个系数，所以模型的造型比例不会被破坏。
        /// 材质原样保留；只有当前管线画不出来的材质（内置 Standard 在 URP 下变洋红）才换成等价 URP Lit。
        /// 失败（没模型 / 没有可渲染网格）时返回 false，且不会留下半成品节点。
        /// </summary>
        public static bool TryBuild(string modelName, float yawOffset, Dir facing, Transform parent,
            float modelScale, out Fit fit)
        {
            fit = default;
            if (parent == null) return false;

            GameObject asset = Load(modelName);
            if (asset == null) return false;

            GameObject wrapper = new GameObject("model");
            wrapper.transform.SetParent(parent, false);
            wrapper.transform.localPosition = Vector3.zero;
            wrapper.transform.localRotation = Quaternion.identity;
            wrapper.transform.localScale = Vector3.one;

            GameObject mesh = Object.Instantiate(asset, wrapper.transform, false);
            mesh.name = "mesh";
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = Vector3.one;

            // 模型正面朝 +Z：先对上元件的实际朝向，再叠作者补偿角
            Vector3 forward = GridMetrics.DirToWorld(facing);
            wrapper.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up)
                * Quaternion.Euler(0f, yawOffset, 0f);

            // 模型自带的碰撞体 / 灯光 / 相机一律丢掉：拾取统一用元件自己的 BoxCollider
            foreach (Collider collider in wrapper.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Light light in wrapper.GetComponentsInChildren<Light>(true)) Destroy(light);
            foreach (Camera childCamera in wrapper.GetComponentsInChildren<Camera>(true)) Destroy(childCamera);

            Renderer[] renderers = wrapper.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || !TryGetLocalBounds(renderers, parent, out Bounds bounds))
            {
                Destroy(wrapper);
                return false;
            }

            Vector3 size = bounds.size;
            if (size.x <= 0f && size.y <= 0f && size.z <= 0f)
            {
                Destroy(wrapper);
                return false;
            }

            // 归一化：水平方向刚好占满 CellFootprint 格，整体等比缩放（不单独压高度，造型和体积感都保留）
            float widest = Mathf.Max(size.x, size.z);
            float scale = widest > 0.0001f ? CellFootprint / widest : 1f;
            scale *= modelScale > 0.0001f ? modelScale : 1f;

            // 只做对齐：水平中心对到格心、底面压到地面。pivot 在中心还是在脚底都能摆正。
            Vector3 scaledCenter = bounds.center * scale;
            Vector3 desiredCenter = new Vector3(0f, GroundY + size.y * scale * 0.5f, 0f);
            wrapper.transform.localScale = Vector3.one * scale;
            wrapper.transform.localPosition = desiredCenter - scaledCenter;

            for (int i = 0; i < renderers.Length; i++)
            {
                EnsureRenderableMaterial(renderers[i]);
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }

            WarnIfTooTall(modelName, size.y * scale);

            fit = new Fit
            {
                Root = wrapper,
                TopY = GroundY + size.y * scale,
                HalfWidth = size.x * scale * 0.5f,
                HalfDepth = size.z * scale * 0.5f,
                RawSize = size,
                Scale = scale,
            };
            return true;
        }

        /// <summary>
        /// 归一化后模型还是"瘦高"时提醒一次（不强行压缩）：头顶的箭头 / 点阵会被顶得很远，
        /// 玩家可能看不出方向。要么调 <see cref="CellFootprint"/>，要么回 Blender 把比例压一压。
        /// </summary>
        static void WarnIfTooTall(string modelName, float height)
        {
            if (height <= HeightWarnFactor) return;
            if (!warnedSize.Add(modelName)) return;

            Debug.LogWarning("[BitFlow] 模型 " + modelName + " 归一化到一格后仍有 " + height.ToString("F2")
                + " 米高（一格是 1 米）。代码不会压扁模型，只能把箭头 / 点阵抬到它头顶；"
                + "如果觉得太高，调 ElementModelLibrary.CellFootprint 或回 Blender 改比例。");
        }

        static readonly HashSet<string> warnedSize = new HashSet<string>();
        static readonly HashSet<string> warnedMaterial = new HashSet<string>();
        static readonly Dictionary<Material, Material> materialSwap = new Dictionary<Material, Material>();

        /// <summary>
        /// 只在"当前渲染管线下这个材质根本画不出来"时替换（典型：内置 Standard 在 URP 里变洋红）。
        /// 替换时把主贴图和主色原样搬过去，模型看起来还是美术画的那个样子；
        /// 能正常渲染的材质一律原封不动（美术的颜色 / 贴图 / 金属度全部保留）。
        /// </summary>
        static void EnsureRenderableMaterial(Renderer renderer)
        {
            Material source = renderer.sharedMaterial;
            if (source == null || source.shader == null) return;
            if (IsRenderable(source.shader)) return;

            if (materialSwap.TryGetValue(source, out Material cached) && cached != null)
            {
                renderer.sharedMaterial = cached;
                return;
            }

            Material replacement = new Material(MaterialFactory.LitShader) { name = source.name + "_Renderable" };

            Texture main = null;
            if (source.HasProperty("_MainTex")) main = source.GetTexture("_MainTex");
            if (main == null && source.HasProperty("_BaseMap")) main = source.GetTexture("_BaseMap");
            if (main != null)
            {
                if (replacement.HasProperty("_BaseMap")) replacement.SetTexture("_BaseMap", main);
                if (replacement.HasProperty("_MainTex")) replacement.SetTexture("_MainTex", main);
            }

            Color baseColor = Color.white;
            if (source.HasProperty("_Color")) baseColor = source.GetColor("_Color");
            else if (source.HasProperty("_BaseColor")) baseColor = source.GetColor("_BaseColor");
            MaterialFactory.SetBaseColor(replacement, baseColor);

            if (warnedMaterial.Add(source.name))
            {
                Debug.LogWarning("[BitFlow] 模型材质 " + source.name + "（shader " + source.shader.name
                    + "）在当前渲染管线下会渲染成洋红，已临时换成 URP Lit 并保留原贴图 / 主色。"
                    + "彻底解决请在 Unity 里把该材质升级为 URP/Lit。");
            }

            materialSwap[source] = replacement;
            renderer.sharedMaterial = replacement;
        }

        /// <summary>
        /// 当前管线能不能画这个 shader。没有 SRP（纯 Built-in 工程）时一律放行；
        /// 走 URP / HDRP 时只认 SRP 自己的 shader，内置 Standard 会变洋红。
        /// </summary>
        static bool IsRenderable(Shader shader)
        {
            string name = shader.name;
            if (string.IsNullOrEmpty(name)) return false;

            if (GraphicsSettings.currentRenderPipeline == null) return true;

            if (name.StartsWith("Universal Render Pipeline/")) return true;
            if (name.StartsWith("HDRP/")) return true;
            if (name.StartsWith("Shader Graphs/")) return true;
            if (name.StartsWith("Sprites/")) return true;
            if (name.StartsWith("UI/")) return true;
            if (name.StartsWith("Skybox/")) return true;
            if (name.StartsWith("Hidden/")) return true;
            return false;
        }

        /// <summary>
        /// 把一组 Renderer 的世界包围盒换算成 <paramref name="parent"/> 的本地包围盒。
        /// 走世界空间再乘 worldToLocalMatrix，父节点的旋转 / 缩放都不会被漏掉。
        /// </summary>
        static bool TryGetLocalBounds(Renderer[] renderers, Transform parent, out Bounds bounds)
        {
            bounds = default;
            Matrix4x4 toLocal = parent.worldToLocalMatrix;
            bool has = false;

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null) continue;

                Bounds world = renderer.bounds;
                Vector3 center = world.center;
                Vector3 extents = world.extents;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = center + new Vector3(
                        (i & 1) == 0 ? -extents.x : extents.x,
                        (i & 2) == 0 ? -extents.y : extents.y,
                        (i & 4) == 0 ? -extents.z : extents.z);

                    Vector3 local = toLocal.MultiplyPoint3x4(corner);
                    if (!has)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return has;
        }

        static void Destroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
