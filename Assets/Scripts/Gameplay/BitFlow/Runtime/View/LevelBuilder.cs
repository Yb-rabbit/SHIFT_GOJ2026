using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 按关卡数据搭出整张场景：暗色地板 + 发光网格线 + 每个元件的 ElementView。
    /// 完全不写入任何资产，纯运行时生成，方便直接在这里调美术参数。
    /// </summary>
    public sealed class LevelBuilder : MonoBehaviour
    {
        readonly Dictionary<int, ElementView> elements = new Dictionary<int, ElementView>();
        readonly Dictionary<GridPoint, ElementView> elementsByCoord = new Dictionary<GridPoint, ElementView>();

        static readonly Color FloorColor = new Color(0.055f, 0.065f, 0.085f);
        static readonly Color GridLineColor = new Color(0.16f, 0.55f, 0.72f);

        Transform stageRoot;

        public Transform BitsRoot { get; private set; }
        public LevelDefinition Definition { get; private set; }

        public IEnumerable<ElementView> ElementViews => elements.Values;

        /// <summary>舞台下的直接子物体数量；Clear 之后应为 0（测试用）。</summary>
        public int StageChildCount => stageRoot != null ? stageRoot.childCount : 0;

        /// <summary>搭场景。levelAsset 可以为空（此时用逻辑定义里的 Hint）。</summary>
        public void Build(LevelSO levelAsset, LevelDefinition definition)
        {
            Clear();
            Definition = definition;

            Transform stage = EnsureStageRoot();

            BuildFloor(stage, definition);
            BuildGridLines(stage, definition);

            GameObject bitsRoot = new GameObject("Bits");
            bitsRoot.transform.SetParent(stage, false);
            BitsRoot = bitsRoot.transform;

            foreach (ElementDefinition element in definition.Elements)
            {
                GameObject go = new GameObject("Elem_" + element.Id);
                go.transform.SetParent(stage, false);

                ElementView view = go.AddComponent<ElementView>();
                view.Build(element, BuildDescription(element), MaterialFactory.Emissive(
                    KindColor(element.Kind), KindEmission(element.Kind)));

                elements[element.Id] = view;
                elementsByCoord[element.Coord] = view;
            }
        }

        /// <summary>
        /// 只清舞台自己生成的东西。
        /// 关键：本组件可能和 HUD / Runner 共用一个 GameObject，所以绝不能清 transform 的所有子物体，
        /// 否则会连 HUD_Canvas 一起删掉（表现为 MissingReferenceException）。
        /// 这里刻意保留 Stage 容器本身（Destroy 是延迟的，销毁后再做 null 判断容易误判成"还在"）。
        /// </summary>
        public void Clear()
        {
            if (stageRoot != null)
            {
                for (int i = stageRoot.childCount - 1; i >= 0; i--)
                {
                    DestroySafe(stageRoot.GetChild(i).gameObject);
                }
            }

            elements.Clear();
            elementsByCoord.Clear();
            Definition = null;
            BitsRoot = null;
        }

        Transform EnsureStageRoot()
        {
            if (stageRoot != null) return stageRoot;

            GameObject stage = new GameObject("Stage");
            stage.transform.SetParent(transform, false);
            stageRoot = stage.transform;
            return stageRoot;
        }

        public ElementView GetElement(int elementId)
        {
            return elements.TryGetValue(elementId, out ElementView view) ? view : null;
        }

        /// <summary>按坐标取元件视图（销毁特效需要知道位撞在了哪个元件上）。</summary>
        public ElementView GetElementAt(GridPoint coord)
        {
            return elementsByCoord.TryGetValue(coord, out ElementView view) ? view : null;
        }

        static string BuildDescription(ElementDefinition element)
        {
            if (!string.IsNullOrEmpty(element.Hint)) return element.Hint;

            switch (element)
            {
                case EmitterDefinition emitter:
                    return $"发射器 #{emitter.Id}\n序列 “{emitter.Sequence}” ×{emitter.RepeatCount}，朝 {emitter.Facing}";
                case ReceiverDefinition receiver:
                    return $"接收器 #{receiver.Id}\n需要 “{receiver.TargetSequence}” ×{receiver.TargetCount}，进料面朝 {receiver.FeedFace}";
                case SplitterDefinition splitter:
                    return "分流器 #" + splitter.Id + "\n1 → " + splitter.OneArm + "，0 → " + splitter.ZeroArm;
                case NotGateDefinition notGate:
                    return "非门 #" + notGate.Id + "\n取反后向 " + notGate.OutputDir + " 输出";
                case FilterDefinition filter:
                    return "过滤器 #" + filter.Id + "\n只放行 " + filter.PassValue;
                default:
                    return "墙 #" + element.Id;
            }
        }

        static Color KindColor(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Emitter: return new Color(0.20f, 0.52f, 1f);
                case CellKind.Receiver: return new Color(0.62f, 0.36f, 1f);
                case CellKind.Splitter: return new Color(0.95f, 0.78f, 0.22f);
                case CellKind.NotGate: return new Color(1f, 0.38f, 0.66f);
                case CellKind.Filter: return new Color(0.30f, 0.95f, 0.55f);
                default: return new Color(0.30f, 0.33f, 0.40f);
            }
        }

        static float KindEmission(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Emitter: return 1.5f;
                case CellKind.Receiver: return 1.3f;
                case CellKind.Splitter: return 1.2f;
                case CellKind.NotGate: return 1.3f;
                case CellKind.Filter: return 1.2f;
                default: return 0.15f;
            }
        }

        // ---------------------------------------------------------------- 舞台

        void BuildFloor(Transform stage, LevelDefinition level)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Floor";
            DestroySafe(floor.GetComponent<Collider>());
            floor.transform.SetParent(stage, false);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.position = new Vector3(
                (level.Width - 1) * 0.5f,
                -0.02f,
                (level.Height - 1) * 0.5f);
            floor.transform.localScale = new Vector3(level.Width + 6f, level.Height + 6f, 1f);

            Renderer renderer = floor.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialFactory.Emissive(FloorColor, 0f, 0.15f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>用细长 Quad 画网格线：比程序化贴图更清晰，也不需要额外资源。</summary>
        void BuildGridLines(Transform stage, LevelDefinition level)
        {
            GameObject root = new GameObject("GridLines");
            root.transform.SetParent(stage, false);

            Material lineMaterial = MaterialFactory.Emissive(GridLineColor, 0.85f);
            const float thickness = 0.035f;
            float centerX = (level.Width - 1) * 0.5f;
            float centerZ = (level.Height - 1) * 0.5f;

            for (int x = 0; x <= level.Width; x++)
            {
                AddLine(root.transform, lineMaterial, thickness, level.Height + 1f,
                    new Vector3(x - 0.5f, 0f, centerZ), 0f);
            }
            for (int y = 0; y <= level.Height; y++)
            {
                AddLine(root.transform, lineMaterial, thickness, level.Width + 1f,
                    new Vector3(centerX, 0f, y - 0.5f), 90f);
            }
        }

        void AddLine(Transform parent, Material material, float thickness, float length, Vector3 position, float yRotation)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Quad);
            line.name = "gridline";
            DestroySafe(line.GetComponent<Collider>());
            line.transform.SetParent(parent, false);
            line.transform.rotation = Quaternion.Euler(90f, yRotation, 0f);
            line.transform.position = position;
            line.transform.localScale = new Vector3(thickness, length, 1f);

            Renderer renderer = line.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static void DestroySafe(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
