using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 单个元件的视觉表现：用基础几何体拼出形状 + 方向条，
    /// 并负责三类反馈：接收器进度/锁定变绿、收到错误位时的红色脉冲、悬停时的说明文本。
    /// </summary>
    public sealed class ElementView : MonoBehaviour
    {
        public int ElementId { get; private set; }
        public ElementDefinition Definition { get; private set; }
        public string Description { get; private set; } = string.Empty;

        Material bodyMaterial;
        Color bodyBaseColor;
        float bodyBaseEmission;

        Transform progressBar;
        Material progressMaterial;
        Color progressBaseColor;

        float flashTimer;
        Color flashColor = Color.red;
        bool locked;

        public void Build(ElementDefinition definition, string description, Material bodySharedMaterial)
        {
            Definition = definition;
            ElementId = definition.Id;
            Description = description;
            name = "Elem_" + definition.Kind + "_" + definition.Id + "_" + definition.Coord;

            transform.position = GridMetrics.CellToWorld(definition.Coord);

            // 每个元件一份材质实例，红闪时只影响自己
            bodyMaterial = new Material(MaterialFactory.LitShader) { name = "M_Elem_" + definition.Id };
            bodyBaseColor = bodySharedMaterial != null ? ReadBaseColor(bodySharedMaterial) : Color.white;
            bodyBaseEmission = 1.1f;
            ApplyBodyColors();

            BuildShape(definition);
            EnsureHoverCollider();
        }

        static Color ReadBaseColor(Material material)
        {
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            return Color.white;
        }

        void BuildShape(ElementDefinition definition)
        {
            switch (definition)
            {
                case WallDefinition _:
                    AddBlock(new Vector3(0.96f, 0.55f, 0.96f), 0.275f);
                    break;

                case EmitterDefinition emitter:
                    AddBlock(new Vector3(0.82f, 0.32f, 0.82f), 0.16f);
                    AddBar(emitter.Facing, 0.55f, new Color(0.55f, 0.85f, 1f), 2.4f, 0.30f);
                    break;

                case ReceiverDefinition receiver:
                    AddBlock(new Vector3(0.92f, 0.34f, 0.92f), 0.17f);
                    AddBar(receiver.FeedFace, 0.42f, new Color(0.95f, 0.95f, 1f), 1.6f, 0.26f);
                    BuildProgressBar();
                    break;

                case SplitterDefinition splitter:
                    AddBlock(new Vector3(0.62f, 0.3f, 0.62f), 0.15f);
                    AddBar(splitter.OneArm, 0.5f, new Color(0.35f, 0.95f, 1f), 2.2f, 0.24f);
                    AddBar(splitter.ZeroArm, 0.5f, new Color(1f, 0.65f, 0.3f), 2.2f, 0.24f);
                    break;

                case NotGateDefinition notGate:
                    AddBlock(new Vector3(0.56f, 0.34f, 0.56f), 0.17f);
                    AddBar(notGate.OutputDir, 0.5f, new Color(1f, 0.45f, 0.75f), 2.2f, 0.26f);
                    break;

                case FilterDefinition filter:
                    AddBlock(new Vector3(0.62f, 0.32f, 0.62f), 0.16f);
                    AddBar(filter.OutputDir, 0.55f, new Color(0.45f, 1f, 0.6f), 2.2f, 0.28f);
                    break;
            }
        }

        void AddBlock(Vector3 size, float y)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "block";
            DestroyImmediateSafe(block.GetComponent<Collider>());
            block.transform.SetParent(transform, false);
            block.transform.localPosition = new Vector3(0f, y, 0f);
            block.transform.localScale = size;

            Renderer renderer = block.GetComponent<Renderer>();
            renderer.sharedMaterial = bodyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void AddBar(Dir dir, float length, Color color, float emission, float y)
        {
            Vector3 direction = GridMetrics.DirToWorld(dir);

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "bar_" + dir;
            DestroyImmediateSafe(bar.GetComponent<Collider>());
            bar.transform.SetParent(transform, false);
            bar.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            bar.transform.localPosition = direction * (length * 0.5f + 0.18f) + Vector3.up * y;
            bar.transform.localScale = new Vector3(0.16f, 0.14f, length);

            Renderer renderer = bar.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialFactory.Emissive(color, emission);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void BuildProgressBar()
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "progress";
            DestroyImmediateSafe(bar.GetComponent<Collider>());
            bar.transform.SetParent(transform, false);
            bar.transform.localPosition = new Vector3(0f, 0.46f, 0f);
            bar.transform.localScale = new Vector3(0.02f, 0.05f, 0.24f);

            progressBaseColor = new Color(0.35f, 0.95f, 1f);
            progressMaterial = new Material(MaterialFactory.LitShader) { name = "M_Progress" };
            MaterialFactory.SetBaseColor(progressMaterial, progressBaseColor);
            MaterialFactory.SetEmission(progressMaterial, progressBaseColor, 2.2f);

            bar.GetComponent<Renderer>().sharedMaterial = progressMaterial;
            progressBar = bar.transform;
        }

        void EnsureHoverCollider()
        {
            if (GetComponent<Collider>() != null) return;
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.3f, 0f);
            collider.size = new Vector3(1f, 0.7f, 1f);
        }

        /// <summary>接收器收料进度（按比例拉伸上面的进度条）。</summary>
        public void SetReceiverProgress(int received, int target)
        {
            if (progressBar == null) return;

            float ratio = target <= 0 ? 0f : Mathf.Clamp01((float)received / target);
            progressBar.localScale = new Vector3(Mathf.Max(0.02f, 0.9f * ratio), 0.05f, 0.24f);

            Color color = ratio >= 1f ? new Color(0.3f, 1f, 0.45f) : progressBaseColor;
            MaterialFactory.SetBaseColor(progressMaterial, color);
            MaterialFactory.SetEmission(progressMaterial, color, 2.2f);
        }

        /// <summary>接收器达标后整体变绿锁定。</summary>
        public void SetLocked()
        {
            locked = true;
            bodyBaseColor = new Color(0.25f, 0.85f, 0.45f);
            bodyBaseEmission = 1.6f;
            ApplyBodyColors();
        }

        /// <summary>红色脉冲：收到错误位 / 元件拒绝 / 逆行撞入。</summary>
        public void PlayError()
        {
            flashTimer = 0.5f;
            flashColor = new Color(1f, 0.12f, 0.16f);
        }

        /// <summary>绿色脉冲：收到正确的位。</summary>
        public void PlayAccepted()
        {
            flashTimer = 0.45f;
            flashColor = new Color(0.25f, 1f, 0.5f);
        }

        void ApplyBodyColors()
        {
            MaterialFactory.SetBaseColor(bodyMaterial, bodyBaseColor);
            MaterialFactory.SetEmission(bodyMaterial, bodyBaseColor, bodyBaseEmission);
        }

        void Update()
        {
            if (bodyMaterial == null) return;

            if (flashTimer > 0f)
            {
                flashTimer = Mathf.Max(0f, flashTimer - Time.deltaTime);
                float k = flashTimer / 0.5f;
                Color color = Color.Lerp(bodyBaseColor, flashColor, k);
                MaterialFactory.SetBaseColor(bodyMaterial, color);
                MaterialFactory.SetEmission(bodyMaterial, color, bodyBaseEmission + 3.5f * k);
            }
            else if (locked)
            {
                // 锁定后做呼吸式明暗，强调"这一路已经跑通"
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3f);
                MaterialFactory.SetEmission(bodyMaterial, bodyBaseColor, bodyBaseEmission * pulse);
            }
        }

        /// <summary>
        /// 本组件在 Build 里 new 了两份实例材质（本体 + 进度条），
        /// 重建关卡时 GameObject 会销毁但材质不会跟着走，必须自己释放，否则 REPLAY 会持续泄漏。
        /// 注意：只释放自己 new 的，MaterialFactory 缓存共享的材质不能碰。
        /// </summary>
        void OnDestroy()
        {
            DestroyMaterial(bodyMaterial);
            DestroyMaterial(progressMaterial);
            bodyMaterial = null;
            progressMaterial = null;
        }

        /// <summary>EditMode（测试/编辑器）下必须用 DestroyImmediate，否则 Unity 会报 "Destroy may not be called from edit mode"。</summary>
        static void DestroyMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Object.Destroy(material);
            else Object.DestroyImmediate(material);
        }

        static void DestroyImmediateSafe(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
