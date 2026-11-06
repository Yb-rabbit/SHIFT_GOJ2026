using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 单个元件的视觉表现：用基础几何体拼出形状 + 方向箭头 + 序列点阵，
    /// 并负责三类反馈：接收器进度/锁定变绿、收到错误位时的红色脉冲、悬停时的说明文本。
    /// </summary>
    public sealed class ElementView : MonoBehaviour
    {
        /// <summary>位值配色与 BitActorView 完全一致：青=1、橙=0。分流器臂、序列点阵、过滤器出口都复用它。</summary>
        static readonly Color ColorOne = BitActorView.ColorOne;
        static readonly Color ColorZero = BitActorView.ColorZero;

        public int ElementId { get; private set; }
        public ElementDefinition Definition { get; private set; }
        public string Description { get; private set; } = string.Empty;

        Material bodyMaterial;
        Color bodyBaseColor;
        float bodyBaseEmission;

        Transform progressBar;
        Material progressMaterial;
        Color progressBaseColor;

        /// <summary>序列点阵（发射器=发什么、接收器=要什么）：一颗点对应一位，青=1、橙=0。</summary>
        readonly List<Transform> sequenceDots = new List<Transform>();
        readonly List<Material> sequenceDotMaterials = new List<Material>();
        readonly List<Color> sequenceDotColors = new List<Color>();

        float flashTimer;
        Color flashColor = Color.red;
        bool locked;
        bool highlighted;

        /// <summary>
        /// 本体最高点（本地 Y）。有自制模型时它就是模型的真实顶面，
        /// 方向箭头 / 序列点阵 / 进度条 / 悬停碰撞盒都按它往上摆，模型换个高度也不会被穿模。
        /// </summary>
        float bodyTopY = 0.3f;

        /// <summary>本体用的是自制模型（模型保留自己的材质，反馈改走地面反馈环）。</summary>
        bool usesModel;

        /// <summary>
        /// 自制模型时的反馈载体：元件脚下的一圈发光片。
        /// 红闪 / 达标变绿 / 悬停提亮都画在它身上，这样模型自己的贴图和颜色一点都不会被改。
        /// 平时隐藏，只在有反馈时出现，免得场上每格都在发光。
        /// </summary>
        Transform feedbackRing;

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
            if (usesModel) AddFeedbackRing();
            EnsureHoverCollider();
        }

        static Color ReadBaseColor(Material material)
        {
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            return Color.white;
        }

        /// <summary>
        /// 形状语言（不看文字也能分辨元件，箭头一律带箭头头，方向不会看反）：
        /// 墙=高立方、发射器=自制模型（缺模型时退回矮宽块）、接收器=自制模型（缺模型时退回中块+顶部进度条）、
        /// 分流器=小块+两条臂、非门=菱形、过滤器=薄板且出口箭头颜色=它放行的值。
        ///
        /// 发射器 / 接收器优先用 <see cref="ElementModelLibrary"/> 里的自制模型；
        /// 模型缺失时自动退回原来的几何体拼装，所以删了 fbx 游戏也能跑。
        /// 模型**不缩放、不染色**（尺寸和材质是美术的），代码只负责落地/居中/转向；
        /// 箭头、点阵、进度条都摆在 <see cref="bodyTopY"/> 之上：既不会被模型挡住，
        /// 也不会因为模型比原来的方块高而穿模。
        /// </summary>
        void BuildShape(ElementDefinition definition)
        {
            switch (definition)
            {
                case WallDefinition _:
                    AddBlock(new Vector3(0.96f, 0.55f, 0.96f), 0.275f);
                    bodyTopY = 0.55f;
                    break;

                case EmitterDefinition emitter:
                {
                    if (!TryAddModel(ElementModelLibrary.EmitterModel, ElementModelLibrary.EmitterYawOffset,
                            emitter.Facing, ElementModelLibrary.EmitterModelScale))
                    {
                        AddBlock(new Vector3(0.82f, 0.32f, 0.82f), 0.16f);
                        bodyTopY = 0.32f;
                    }

                    AddArrow(emitter.Facing, 0.32f, new Color(0.55f, 0.85f, 1f), 2.4f, bodyTopY + ArrowLift);
                    // 点阵从左到右＝真实发射顺序（序列是从末字符开始弹的，直接照抄字符串会让玩家看反）
                    BuildSequenceDots(EmitOrderText(emitter), bodyTopY + DotLift, -0.30f);
                    break;
                }

                case ReceiverDefinition receiver:
                {
                    if (!TryAddModel(ElementModelLibrary.ReceiverModel, ElementModelLibrary.ReceiverYawOffset,
                            receiver.FeedFace, ElementModelLibrary.ReceiverModelScale))
                    {
                        AddBlock(new Vector3(0.92f, 0.34f, 0.92f), 0.17f);
                        bodyTopY = 0.34f;
                    }

                    AddArrow(receiver.FeedFace, 0.26f, new Color(0.95f, 0.95f, 1f), 1.6f, bodyTopY + ArrowLift);
                    BuildProgressBar(bodyTopY + DotLift);
                    BuildSequenceDots(NormalizeSequence(receiver.TargetSequence), bodyTopY + DotLift, -0.34f);
                    break;
                }

                case SplitterDefinition splitter:
                    AddBlock(new Vector3(0.62f, 0.3f, 0.62f), 0.15f);
                    bodyTopY = 0.30f;
                    // 臂的颜色就是这条臂接收的值：青=走 1，橙=走 0
                    AddArrow(splitter.OneArm, 0.34f, ColorOne, 2.2f, 0.26f);
                    AddArrow(splitter.ZeroArm, 0.34f, ColorZero, 2.2f, 0.26f);
                    break;

                case NotGateDefinition notGate:
                    AddBlock(new Vector3(0.54f, 0.34f, 0.54f), 0.17f, 45f);
                    bodyTopY = 0.34f;
                    AddArrow(notGate.OutputDir, 0.30f, new Color(1f, 0.45f, 0.75f), 2.2f, 0.30f);
                    break;

                case FilterDefinition filter:
                    AddBlock(new Vector3(0.84f, 0.16f, 0.84f), 0.08f);
                    bodyTopY = 0.16f;
                    // 出口箭头颜色 = 它放行的值（青=只放 1、橙=只放 0）
                    AddArrow(filter.OutputDir, 0.34f, filter.PassValue == 0 ? ColorZero : ColorOne, 2.2f, 0.24f);
                    break;
            }
        }

        /// <summary>方向箭头比本体顶面再高一点：既不和进度条/点阵打架，也不会被模型挡住。</summary>
        const float ArrowLift = 0.16f;

        /// <summary>序列点阵 / 进度条贴着本体顶面放，看起来像设备自带的读数。</summary>
        const float DotLift = 0.02f;

        /// <summary>
        /// 直接装自制模型（底面落地、水平居中、按 facing 转向）。
        /// 成功时把 <see cref="bodyTopY"/> 更新成模型真实顶面高度、并标记 <see cref="usesModel"/>；
        /// 失败返回 false 让调用方退回方块。模型自己的材质不动，反馈交给 <see cref="feedbackRing"/>。
        /// </summary>
        bool TryAddModel(string modelName, float yawOffset, Dir facing, float modelScale)
        {
            if (!ElementModelLibrary.TryBuild(modelName, yawOffset, facing, transform, modelScale,
                    out ElementModelLibrary.Fit fit))
            {
                return false;
            }

            usesModel = true;
            bodyTopY = fit.TopY;
            return true;
        }

        /// <summary>
        /// 脚下的反馈环：模型自己的颜色/贴图是美术的，反馈不能染在模型上，
        /// 于是把红闪 / 达标变绿 / 悬停提亮集中到脚下一圈发光片上（本体材质实例只给它用）。
        /// </summary>
        void AddFeedbackRing()
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "feedback_ring";
            DestroyImmediateSafe(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            ring.transform.localScale = new Vector3(0.92f, 0.006f, 0.92f);

            Renderer renderer = ring.GetComponent<Renderer>();
            renderer.sharedMaterial = bodyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ring.SetActive(false);
            feedbackRing = ring.transform;
        }

        /// <summary>只有正在红闪 / 已锁定 / 悬停时才显示反馈环。</summary>
        void UpdateFeedbackRing()
        {
            if (feedbackRing == null) return;

            bool show = flashTimer > 0f || locked || highlighted;
            if (feedbackRing.gameObject.activeSelf != show) feedbackRing.gameObject.SetActive(show);
        }

        void AddBlock(Vector3 size, float y, float yRotation = 0f)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "block";
            DestroyImmediateSafe(block.GetComponent<Collider>());
            block.transform.SetParent(transform, false);
            block.transform.localPosition = new Vector3(0f, y, 0f);
            block.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            block.transform.localScale = size;

            ApplyRenderer(block, bodyMaterial);
        }

        /// <summary>方向箭头 = 细杆 + 明显加宽的头。原来只有一根等粗光条，玩家经常把方向看反。</summary>
        void AddArrow(Dir dir, float length, Color color, float emission, float y)
        {
            Vector3 direction = GridMetrics.DirToWorld(dir);
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            Material material = MaterialFactory.Emissive(color, emission);

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "arrow_" + dir;
            DestroyImmediateSafe(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(transform, false);
            shaft.transform.localRotation = rotation;
            shaft.transform.localPosition = direction * (0.18f + length * 0.5f) + Vector3.up * y;
            shaft.transform.localScale = new Vector3(0.14f, 0.12f, length);
            ApplyRenderer(shaft, material);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "arrowhead_" + dir;
            DestroyImmediateSafe(head.GetComponent<Collider>());
            head.transform.SetParent(transform, false);
            head.transform.localRotation = rotation;
            head.transform.localPosition = direction * (0.18f + length + 0.11f) + Vector3.up * y;
            head.transform.localScale = new Vector3(0.34f, 0.2f, 0.22f);
            ApplyRenderer(head, material);
        }

        /// <summary>把 "1011" 这样的序列摆成一排小方块：青=1、橙=0，不用悬停就能读出内容。</summary>
        void BuildSequenceDots(string sequence, float y, float z)
        {
            int count = Mathf.Min(sequence.Length, 8);
            if (count <= 0) return;

            const float step = 0.112f;
            const float size = 0.085f;
            float startX = -(count - 1) * step * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Color color = sequence[i] == '0' ? ColorZero : ColorOne;

                Material material = new Material(MaterialFactory.LitShader) { name = "M_SeqDot_" + i };
                MaterialFactory.SetBaseColor(material, color);
                MaterialFactory.SetEmission(material, color, 2.4f);
                sequenceDotMaterials.Add(material);
                sequenceDotColors.Add(color);

                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dot.name = "seqdot_" + i;
                DestroyImmediateSafe(dot.GetComponent<Collider>());
                dot.transform.SetParent(transform, false);
                dot.transform.localPosition = new Vector3(startX + i * step, y, z);
                dot.transform.localScale = Vector3.one * size;
                ApplyRenderer(dot, material);
                sequenceDots.Add(dot.transform);
            }
        }

        /// <summary>只保留 '0'/'1'，其它字符（空白、逗号）忽略；空序列按 "1" 处理。</summary>
        static string NormalizeSequence(string source)
        {
            if (string.IsNullOrEmpty(source)) return "1";

            StringBuilder builder = new StringBuilder(source.Length);
            foreach (char c in source)
            {
                if (c == '0' || c == '1') builder.Append(c);
            }
            return builder.Length > 0 ? builder.ToString() : "1";
        }

        /// <summary>一轮真实发射顺序（最长为序列长度上限 8 位），用于点阵显示。</summary>
        static string EmitOrderText(EmitterDefinition emitter)
        {
            List<byte> order = emitter.BuildEmitOrder();
            if (order.Count == 0) return NormalizeSequence(emitter.Sequence);

            int count = Mathf.Min(Mathf.Min(order.Count, emitter.Sequence.Length), 8);
            StringBuilder builder = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                builder.Append(order[i] == 0 ? '0' : '1');
            }
            return builder.ToString();
        }

        static void ApplyRenderer(GameObject go, Material material)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void BuildProgressBar(float y)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "progress";
            DestroyImmediateSafe(bar.GetComponent<Collider>());
            bar.transform.SetParent(transform, false);
            bar.transform.localPosition = new Vector3(0f, y, 0f);
            bar.transform.localScale = new Vector3(0.02f, 0.05f, 0.24f);

            progressBaseColor = new Color(0.35f, 0.95f, 1f);
            progressMaterial = new Material(MaterialFactory.LitShader) { name = "M_Progress" };
            MaterialFactory.SetBaseColor(progressMaterial, progressBaseColor);
            MaterialFactory.SetEmission(progressMaterial, progressBaseColor, 2.2f);

            bar.GetComponent<Renderer>().sharedMaterial = progressMaterial;
            progressBar = bar.transform;
        }

        /// <summary>
        /// 悬停 / 拾取用的碰撞盒：高度跟着本体走，模型比原来的方块高时也还能点得到。
        /// 先建的元件不会被后建的视觉撑大，所以只要一格的投影面积（1×1）即可。
        /// </summary>
        void EnsureHoverCollider()
        {
            if (GetComponent<Collider>() != null) return;
            float top = Mathf.Max(0.7f, bodyTopY + 0.16f);
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, top * 0.5f, 0f);
            collider.size = new Vector3(1f, top, 1f);
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

        /// <summary>发射器已吐出的位数：点阵里对应的小方块变暗，一眼看出"发到第几个了"。</summary>
        public void SetEmitterProgress(int emitted)
        {
            if (sequenceDots.Count == 0) return;

            int sent = Mathf.Clamp(emitted, 0, sequenceDots.Count);
            for (int i = 0; i < sequenceDots.Count; i++)
            {
                bool done = i < sent;
                Color baseColor = sequenceDotColors[i];
                Color color = done ? Color.Lerp(baseColor, Color.black, 0.62f) : baseColor;
                MaterialFactory.SetBaseColor(sequenceDotMaterials[i], color);
                MaterialFactory.SetEmission(sequenceDotMaterials[i], color, done ? 0.5f : 2.4f);
            }
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

        /// <summary>游戏内编辑器的悬停/选中高亮（只抬自发光，不改数据）。</summary>
        public void SetHighlighted(bool value)
        {
            if (highlighted == value) return;
            highlighted = value;

            // 取消高亮时立刻恢复本色；正在红闪或已锁定就交给 Update 自己收尾
            if (!highlighted && flashTimer <= 0f && !locked) ApplyBodyColors();
        }

        void ApplyBodyColors()
        {
            MaterialFactory.SetBaseColor(bodyMaterial, bodyBaseColor);
            MaterialFactory.SetEmission(bodyMaterial, bodyBaseColor, bodyBaseEmission);
        }

        void Update()
        {
            if (bodyMaterial == null) return;

            UpdateFeedbackRing();

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
            else if (highlighted)
            {
                // 游戏内编辑器：悬停/选中的格子提亮，不改任何逻辑数据
                MaterialFactory.SetBaseColor(bodyMaterial, bodyBaseColor);
                MaterialFactory.SetEmission(bodyMaterial, bodyBaseColor, bodyBaseEmission + 2.2f);
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
            for (int i = 0; i < sequenceDotMaterials.Count; i++)
            {
                DestroyMaterial(sequenceDotMaterials[i]);
            }
            sequenceDotMaterials.Clear();
            sequenceDotColors.Clear();
            sequenceDots.Clear();
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
