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

        /// <summary>
        /// 形状语言（不看文字也能分辨元件，箭头一律带箭头头，方向不会看反）：
        /// 墙=高立方、发射器=矮宽实心块+朝外箭头、接收器=四角柱围出的托盘（全向时四个口全开，
        /// 严格模式堵掉三个口只留进料面）、分流器=小块+两条臂、非门=菱形、过滤器=薄板且出口箭头颜色=它放行的值。
        /// </summary>
        void BuildShape(ElementDefinition definition)
        {
            switch (definition)
            {
                case WallDefinition _:
                    AddBlock(new Vector3(0.96f, 0.55f, 0.96f), 0.275f);
                    break;

                case EmitterDefinition emitter:
                    AddBlock(new Vector3(0.82f, 0.32f, 0.82f), 0.16f);
                    AddArrow(emitter.Facing, 0.32f, new Color(0.55f, 0.85f, 1f), 2.4f, 0.30f);
                    // 点阵从左到右＝真实发射顺序（序列是从末字符开始弹的，直接照抄字符串会让玩家看反）
                    BuildSequenceDots(EmitOrderText(emitter), 0.36f, -0.30f);
                    break;

                case ReceiverDefinition receiver:
                    // 接收器 = 四角柱围出的四开口托盘：它没有正面（四面都能收），本体就必须四向对称。
                    // 和发射器的「实心矮块 + 朝外箭头」正好相反 —— 一个张着嘴接，一个扁扁地往外吐。
                    AddMouth(receiver);
                    BuildProgressBar();
                    BuildSequenceDots(NormalizeSequence(receiver.TargetSequence), 0.40f, -0.34f);
                    break;

                case SplitterDefinition splitter:
                    AddBlock(new Vector3(0.62f, 0.3f, 0.62f), 0.15f);
                    // 臂的颜色就是这条臂接收的值：青=走 1，橙=走 0
                    AddArrow(splitter.OneArm, 0.34f, ColorOne, 2.2f, 0.26f);
                    AddArrow(splitter.ZeroArm, 0.34f, ColorZero, 2.2f, 0.26f);
                    break;

                case NotGateDefinition notGate:
                    AddBlock(new Vector3(0.54f, 0.34f, 0.54f), 0.17f, 45f);
                    AddArrow(notGate.OutputDir, 0.30f, new Color(1f, 0.45f, 0.75f), 2.2f, 0.30f);
                    break;

                case FilterDefinition filter:
                    AddBlock(new Vector3(0.84f, 0.16f, 0.84f), 0.08f);
                    // 出口箭头颜色 = 它放行的值（青=只放 1、橙=只放 0）
                    AddArrow(filter.OutputDir, 0.34f, filter.PassValue == 0 ? ColorZero : ColorOne, 2.2f, 0.24f);
                    break;
            }
        }

        void AddBlock(Vector3 size, float y, float yRotation = 0f)
        {
            AddBlockAt(size, new Vector3(0f, y, 0f), yRotation);
        }

        void AddBlockAt(Vector3 size, Vector3 localPosition, float yRotation = 0f)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "block";
            DestroyImmediateSafe(block.GetComponent<Collider>());
            block.transform.SetParent(transform, false);
            block.transform.localPosition = localPosition;
            block.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            block.transform.localScale = size;

            ApplyRenderer(block, bodyMaterial);
        }

        /// <summary>
        /// 接收器的「四开口托盘」：四根角柱围出上/右/下/左四个缺口，中间垫一块矮底盘。
        /// 为什么不是实心块：接收器四面都能收，本体就不该有正面；四个开口本身就是「哪边都能进」的信号，
        /// 而实心矮块会和发射器撞脸（发射器也是扁块），玩家只能靠顶上的进度条去猜。
        /// 严格模式（只认进料面）不画箭头，而是把另外三个口用同色块堵死 —— 看形状就知道只有一边通，
        /// 比让玩家去数箭头指哪边更快，也不会像四支白箭头那样把本体糊成一朵白花。
        /// </summary>
        void AddMouth(ReceiverDefinition receiver)
        {
            const float ringOffset = 0.32f;
            const float ringSize = 0.30f;
            // 0.30 比发射器那个矮块（0.32）还矮一点：斜视角下"盘子"比"坑"好读。
            // 试过 0.36 的深口，结果进度条和序列点全被近端角块挡住 —— 凹得好看但看不见东西，亏。
            const float ringHeight = 0.30f;

            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -ringOffset : ringOffset;
                float z = (i & 2) == 0 ? -ringOffset : ringOffset;
                AddBlockAt(new Vector3(ringSize, ringHeight, ringSize), new Vector3(x, ringHeight * 0.5f, z));
            }

            // 底盘几乎和四角齐平（只低 0.14）—— 留一点"凹陷"的暗示，但不藏东西
            AddBlockAt(new Vector3(0.70f, 0.16f, 0.70f), new Vector3(0f, 0.08f, 0f));

            if (receiver.AcceptAnyFace) return;

            // 严格模式：三个不收货的口堵死，只留进料面那一个开口。
            // 堵块特意比角柱高 0.08 —— 俯视时三个口盖着"盖板"、剩下那个口是平的，一眼看出只有一边通；
            // 一样高的话它就和实心方块毫无区别了（试过，看不出来）。
            const float capHeight = ringHeight + 0.08f;
            Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            Vector3 feed = GridMetrics.DirToWorld(receiver.FeedFace);
            foreach (Vector3 side in sides)
            {
                if (side == feed) continue;
                float nx = Mathf.Abs(side.x) > 0.5f ? ringSize : 0.34f;
                float nz = Mathf.Abs(side.z) > 0.5f ? ringSize : 0.34f;
                AddBlockAt(
                    new Vector3(nx, capHeight, nz),
                    new Vector3(side.x * ringOffset, capHeight * 0.5f, side.z * ringOffset));
            }
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

        /// <summary>真实发射顺序的前 8 位（EmitCount 可能很长，点阵只画得下 8 个）。</summary>
        static string EmitOrderText(EmitterDefinition emitter)
        {
            List<byte> order = emitter.BuildEmitOrder();
            if (order.Count == 0) return NormalizeSequence(emitter.Sequence);

            int count = Mathf.Min(order.Count, 8);
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

        void BuildProgressBar()
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "progress";
            DestroyImmediateSafe(bar.GetComponent<Collider>());
            bar.transform.SetParent(transform, false);
            // 躺在托盘里（略高于底盘、略低于四角），像「往里填」而不是顶着一根横条
            bar.transform.localPosition = new Vector3(0f, 0.20f, 0f);
            bar.transform.localScale = new Vector3(0.02f, 0.06f, 0.22f);

            progressBaseColor = new Color(0.35f, 0.95f, 1f);
            progressMaterial = new Material(MaterialFactory.LitShader) { name = "M_Progress" };
            MaterialFactory.SetBaseColor(progressMaterial, progressBaseColor);
            // 1.6 而不是 2.2：满格会转绿，太亮的时候绿和青在过曝里分不出来
            MaterialFactory.SetEmission(progressMaterial, progressBaseColor, 1.6f);

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
            // 0.6 是方口内宽：填满时正好顶到两侧角块，读起来就是「这口装满了」
            progressBar.localScale = new Vector3(Mathf.Max(0.02f, 0.6f * ratio), 0.06f, 0.22f);

            Color color = ratio >= 1f ? new Color(0.3f, 1f, 0.45f) : progressBaseColor;
            MaterialFactory.SetBaseColor(progressMaterial, color);
            MaterialFactory.SetEmission(progressMaterial, color, 1.6f);
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
