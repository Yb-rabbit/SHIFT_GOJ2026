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
        /// <summary>位值配色与 BitActorView 完全一致：青=1、橙=0。分流器臂、序列点阵、过滤器闸门都复用它。</summary>
        static readonly Color ColorOne = BitActorView.ColorOne;
        static readonly Color ColorZero = BitActorView.ColorZero;

        /// <summary>非门「翻转」标记的颜色。粉只出现在非门上，是它专有的信号色。</summary>
        static readonly Color InvertColor = new Color(1f, 0.45f, 0.75f);

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

        // ---- 摆放动效（纯表现）----
        /// <summary>落地时长。0.26s 是"看得见但不用等"的折中：再长会挡住下一步操作。</summary>
        const float DropDuration = 0.26f;
        /// <summary>起始悬空高度。太高像从天上掉，太低看不出是"放下去的"。</summary>
        const float DropHeight = 0.42f;
        /// <summary>下坠占总时长的比例，剩下的时间留给落地挤压回弹。</summary>
        const float DropLandAt = 0.72f;
        const float DropSquash = 0.88f;
        const float RotateDuration = 0.16f;

        float dropTimer = -1f;
        float rotateTimer = -1f;
        float rotateFromAngle;

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
        /// 形状语言（不看文字也能分辨元件，凡是带方向的地方都配箭头头，方向不会看反）：
        /// - 墙=高立方；分流器=小块+两条臂。
        /// - 发射器=矮宽实心块+朝外箭头。
        /// - 接收器=四角柱围出的托盘，既没有箭头也没有缺口（终点只收不吐）；全向时四个口全开，严格模式堵掉三个口只留进料面。
        /// - 非门 / 过滤器=三面矮栏+一面开口，开口就是输出方向。它们刻意不出箭头：缺口已经把方向说完了，
        ///   再从缺口插一根杆出去，在 3/4 视角下只是一根浮在矮栏上方的把手，重复且挡视线。
        ///   这是它们和接收器（四向对称的角柱）最省事的区分方式。
        /// 一句话读法：**实心本体+朝外箭头＝单面进单面吐；矮栏三缺一＝多面进、缺口吐；四角柱＝只收不吐。**
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
                    // 非门是「三面进、一面出」的处理器：逻辑上除出口面之外的三个面都能进（逆行才销毁）。
                    // 所以本体不能是过去那个 45° 菱形方块 —— 菱形只说明"我和别人不一样"，
                    // 完全没交代"从哪都能进来"，还容易看成分流器的小块。
                    // 现在用和接收器同一套语言：矮栏围出缺口＝多个面能进，缺口开在输出方向。
                    //
                    // 这里刻意不再画出口箭头：缺口本身就是出口，再插一根从缺口伸出去的杆，
                    // 在 3/4 视角下就是一根浮在矮栏上方的"把手"，既重复又挡视线（箭头 y=0.30，
                    // 矮栏顶 y=0.22，正好错开一点、看起来像悬空的零件）。方向交给缺口。
                    AddBlock(new Vector3(0.56f, 0.10f, 0.56f), 0.05f); // 底盘：保证元件落地有存在感
                    AddRails(notGate.OutputDir);
                    AddInvertMark();
                    break;

                case FilterDefinition filter:
                    // 过滤器和非门一样是三面进：它的个性在"只放行某个值"，
                    // 所以开口处立一道闸门，闸门的颜色就是它放行的值 —— 整格只有这一样东西带颜色，读起来最直接。
                    // 同样不画出口箭头：闸门已经站在开口上，它就是"这里出去"的告示牌。
                    AddBlock(new Vector3(0.56f, 0.10f, 0.56f), 0.05f);
                    AddRails(filter.OutputDir);
                    AddGateBarrier(filter.OutputDir, filter.PassValue);
                    break;

                case DelayGateDefinition delayGate:
                    // 延迟器 = 方底座 + 钢蓝齿轮柱：方形底盘说明"这一格是机器"，
                    // 齿轮柱说明"位在这里被卷进去等一会儿"。齿数和顶部刻度点都跟着 delay 数走，
                    // 所以不悬停也能一眼看出它是 1 拍还是 4 拍的延迟器。
                    AddBlock(new Vector3(0.94f, 0.09f, 0.94f), 0.045f);
                    AddDelayGear(delayGate.SafeDelayTicks);
                    break;
            }
        }

        /// <summary>
        /// 延迟器本体：一根钢蓝圆柱 + 一圈齿轮齿 + 顶面刻度点。
        ///
        /// 用圆柱而不是方块，是为了和"实心矮块 = 发射器 / 分流器"分开：
        /// 旋转体天生读作"机构里的一个滚轮"，没有任何正面，也就不会让玩家去找方向。
        /// </summary>
        void AddDelayGear(int ticks)
        {
            Color steel = new Color(0.52f, 0.68f, 0.82f);

            GameObject drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            drum.name = "gear_drum";
            DestroyImmediateSafe(drum.GetComponent<Collider>());
            drum.transform.SetParent(transform, false);
            drum.transform.localPosition = new Vector3(0f, 0.20f, 0f);
            // Unity 的圆柱原始高度是 2，所以 y 缩放取"目标高度的一半"
            drum.transform.localScale = new Vector3(0.58f, 0.16f, 0.58f);
            ApplyRenderer(drum, bodyMaterial);

            // 齿：比柱子更亮一点，斜视角下轮廓才不会被柱面吃掉
            Material toothMaterial = MaterialFactory.Emissive(steel, 1.6f);

            const int teeth = 8;
            const float radius = 0.34f;
            for (int i = 0; i < teeth; i++)
            {
                float angle = i * Mathf.PI * 2f / teeth;
                GameObject tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = "gear_tooth_" + i;
                DestroyImmediateSafe(tooth.GetComponent<Collider>());
                tooth.transform.SetParent(transform, false);
                tooth.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.20f,
                    Mathf.Sin(angle) * radius);
                tooth.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                tooth.transform.localScale = new Vector3(0.13f, 0.10f, 0.16f);
                ApplyRenderer(tooth, toothMaterial);
            }

            // 顶面刻度：几拍就几颗点，排成一排压在齿轮上方
            const float step = 0.10f;
            const float size = 0.07f;
            float startX = -(ticks - 1) * step * 0.5f;
            Material pipMaterial = MaterialFactory.Emissive(new Color(0.78f, 0.90f, 1f), 2.2f);

            for (int i = 0; i < ticks; i++)
            {
                GameObject pip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pip.name = "delay_pip_" + i;
                DestroyImmediateSafe(pip.GetComponent<Collider>());
                pip.transform.SetParent(transform, false);
                pip.transform.localPosition = new Vector3(startX + i * step, 0.40f, 0f);
                pip.transform.localScale = Vector3.one * size;
                ApplyRenderer(pip, pipMaterial);
            }
        }

        void AddBlock(Vector3 size, float y, float yRotation = 0f)
        {
            AddBlockAt(size, new Vector3(0f, y, 0f), yRotation);
        }

        /// <param name="blockName">
        /// 子物体名。本体块一律叫 "block"（老测试靠这个名字找本体），
        /// 只有「同属本体但不是那块主方块」的零件（矮栏、栅栏）才另起名字，
        /// 否则测试和调试时数不清一格里到底谁是谁。
        /// </param>
        void AddBlockAt(Vector3 size, Vector3 localPosition, float yRotation = 0f, string blockName = "block")
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = blockName;
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

        /// <summary>
        /// 三面矮栏：留出 <paramref name="openDir"/> 那一面不封，其余三面各立一道矮栏。
        ///
        /// 这是「我从多个面收」的统一符号 —— 接收器的四角柱是它的四向版本。
        /// 之所以用连续矮栏而不是角柱，是为了和接收器区分开：角柱＝四面对称的终点，
        /// 矮栏＋一面开口＝三面收、一面吐的处理器（非门 / 过滤器）。
        /// 零件名用 "rail" 而不是 "block"：本体块只能有一块，不然测试数不清谁是谁。
        /// </summary>
        void AddRails(Dir openDir)
        {
            const float offset = 0.32f;
            const float thickness = 0.10f;
            const float length = 0.62f;
            const float height = 0.22f;

            Vector3 open = GridMetrics.DirToWorld(openDir);
            Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

            foreach (Vector3 side in sides)
            {
                if (side == open) continue;

                // 立在左右两面时栏沿 z 铺开，立在前后面时沿 x 铺开
                bool alongZ = Mathf.Abs(side.x) > 0.5f;
                Vector3 size = alongZ
                    ? new Vector3(thickness, height, length)
                    : new Vector3(length, height, thickness);

                AddBlockAt(size, new Vector3(side.x * offset, height * 0.5f, side.z * offset), 0f, "rail");
            }
        }

        /// <summary>
        /// 非门的「翻转」标记：底盘上一根 45° 斜条。
        /// 斜杠在书写里就是「划掉 / 否定」，比画一个感叹号省几何，
        /// 也不会和序列点阵、进度条这些横平竖直的零件撞脸。
        /// </summary>
        void AddInvertMark()
        {
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.name = "invert_mark";
            DestroyImmediateSafe(mark.GetComponent<Collider>());
            mark.transform.SetParent(transform, false);
            mark.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            mark.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            mark.transform.localScale = new Vector3(0.46f, 0.07f, 0.11f);

            ApplyRenderer(mark, MaterialFactory.Emissive(InvertColor, 2.2f));
        }

        /// <summary>
        /// 过滤器的闸门：开口处并排三根细条、中间留缝 —— 「筛子」的图形语言。
        /// 整道闸门用放行值的颜色（青=只放 1、橙=只放 0）。
        /// 它是这个元件唯一带颜色的东西，所以闸门本身就是"只放这一种"的告示牌。
        /// </summary>
        void AddGateBarrier(Dir dir, byte passValue)
        {
            Material material = MaterialFactory.Emissive(passValue == 0 ? ColorZero : ColorOne, 2.2f);

            Vector3 outDir = GridMetrics.DirToWorld(dir);
            // 比矮栏（0.32）稍稍退进格内，闸门就立在缺口里侧 —— 看起来是"卡在门口的一道栅栏"，
            // 而不是贴在格子边缘。两者差 0.10，斜视角下能看出前后关系。
            const float distance = 0.22f;
            bool alongZ = Mathf.Abs(outDir.x) > 0.5f;

            const int bars = 3;
            const float spacing = 0.18f;
            const float start = -(bars - 1) * spacing * 0.5f;

            for (int i = 0; i < bars; i++)
            {
                float shift = start + i * spacing;

                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "gate_" + i;
                DestroyImmediateSafe(bar.GetComponent<Collider>());
                bar.transform.SetParent(transform, false);
                bar.transform.localPosition = new Vector3(
                    outDir.x * distance + (alongZ ? 0f : shift),
                    0.13f,
                    outDir.z * distance + (alongZ ? shift : 0f));
                bar.transform.localScale = alongZ
                    ? new Vector3(0.07f, 0.26f, 0.11f)
                    : new Vector3(0.11f, 0.26f, 0.07f);
                ApplyRenderer(bar, material);
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

        /// <summary>
        /// 落地动效：从上方 <see cref="DropHeight"/> 处掉下来，落地瞬间纵向压扁再弹回，同时闪一下白。
        ///
        /// 为什么值得做：卡牌一消失、网格里凭空多一个元件，玩家会怀疑"我到底点中了没有"。
        /// 有了这 0.26 秒，操作和结果之间才有因果感。全程只改 transform 和自发光，
        /// 模拟状态里一个字节都不动 —— 想关掉随时可以不管它。
        /// </summary>
        public void PlayDropIn()
        {
            dropTimer = 0f;
            ApplyDropPose(0f);

            // 白色脉冲而不是绿色：绿色是"这个接收器成了"，别把两件事混成同一个信号
            flashTimer = 0.26f;
            flashColor = Color.white;
        }

        /// <summary>
        /// 旋转动效：绕 Y 轴转 90° 到新朝向。
        /// R 键如果只是"啪"一下换个形状，玩家会以为是自己看错了；
        /// 160ms 的转动能让视线跟得上，也顺带说明"转的是这个元件，不是旁边那个"。
        /// </summary>
        /// <param name="clockwise">往哪边转。调用方按"离新朝向最近的方向"给，别绕远路。</param>
        public void PlayRotateIn(bool clockwise)
        {
            rotateTimer = 0f;
            rotateFromAngle = clockwise ? -90f : 90f;
            transform.localRotation = Quaternion.Euler(0f, rotateFromAngle, 0f);
        }

        /// <summary>
        /// 下坠曲线：返回"还剩多少离地高度"，1=刚松手，0=已经踩实。
        /// 用 t² 而不是线性 —— 线性看着像电梯，加速下坠才像物体在重力下掉。
        /// 落地点之后的 28% 时间归压扁回弹用，所以这里直接返回 0。
        /// </summary>
        public static float DropOffset01(float t)
        {
            t = Mathf.Clamp01(t);
            if (t >= DropLandAt) return 0f;

            float fall = t / DropLandAt;
            return 1f - fall * fall;
        }

        /// <summary>
        /// 落地挤压：落地瞬间 y 压到 0.88 再弹回 1（x/z 不动）。
        /// 一点形变就能让人读出"这东西有重量"；完全没有形变会像穿模落进去。
        /// </summary>
        public static float DropScaleY(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= DropLandAt) return 1f;

            float back = (t - DropLandAt) / (1f - DropLandAt);
            return Mathf.Lerp(DropSquash, 1f, Mathf.SmoothStep(0f, 1f, back));
        }

        /// <summary>把下坠进度写成 transform（位置抬高 + 纵向压扁）。抽出单独一份，测试也好直接调。</summary>
        void ApplyDropPose(float t)
        {
            Vector3 basePosition = GridMetrics.CellToWorld(Definition.Coord);
            transform.position = basePosition + Vector3.up * (DropHeight * DropOffset01(t));
            transform.localScale = new Vector3(1f, DropScaleY(t), 1f);
        }

        /// <summary>
        /// 推进落地 / 旋转这两段纯表现动画，结束后把 transform 复位成标准姿态。
        /// 复位是必须的：动画中途被打断（换关、重放、元件被删）时，
        /// 元件不能停在半空或者歪着 —— 那会被当成关卡bug。
        /// </summary>
        void TickPlacementMotion()
        {
            if (dropTimer >= 0f)
            {
                dropTimer += Time.deltaTime;
                float t = dropTimer / DropDuration;
                ApplyDropPose(t);

                if (t >= 1f)
                {
                    dropTimer = -1f;
                    transform.localScale = Vector3.one;
                }
            }

            if (rotateTimer >= 0f)
            {
                rotateTimer += Time.deltaTime;
                float t = Mathf.Clamp01(rotateTimer / RotateDuration);
                // EaseOutCubic：起手快、收手稳，到位那一下才不显得拖
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                transform.localRotation = Quaternion.Euler(0f, Mathf.Lerp(rotateFromAngle, 0f, eased), 0f);

                if (t >= 1f)
                {
                    rotateTimer = -1f;
                    transform.localRotation = Quaternion.identity;
                }
            }
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

            TickPlacementMotion();

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
