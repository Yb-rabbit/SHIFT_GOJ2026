using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 单个位的视觉表现：0.2 单位的发光方块（半格），1=青、0=橙。位置由 Runner 每 tick 插值驱动。
    /// </summary>
    public sealed class BitActorView : MonoBehaviour
    {
        public static readonly Color ColorOne = new Color(0.20f, 0.88f, 1f);
        public static readonly Color ColorZero = new Color(1f, 0.60f, 0.24f);

        /// <summary>
        /// 方块边长（世界单位）。0.2 = 半格：位只占格子中间一小块，不挡元件本体、也不盖住网格线。
        /// </summary>
        public const float Size = 0.2f;

        /// <summary>悬浮高度：底面离格子面留一点，避免和地板/网格线 z-fighting。</summary>
        public const float HoverHeight = 0.06f;

        /// <summary>位中心离格子面的高度。Runner 摆爆闪也读它，保证爆闪永远套在位上而不是飘在半空。</summary>
        public static float CenterY => Size * 0.5f + HoverHeight;

        /// <summary>体积缩到 1/4 后发光面积也只剩 1/4，提一档亮度免得看起来发灰。</summary>
        const float EmissionBoost = 3.2f;

        Transform body;
        Material ownMaterial;
        byte value;
        float punch;

        public int BitId { get; private set; }

        public void Init(int bitId, byte initialValue, Vector3 worldPosition)
        {
            BitId = bitId;
            transform.position = worldPosition + Vector3.up * CenterY;

            if (body == null)
            {
                CreateBody();
            }

            ApplyValue(initialValue, false);
        }

        void CreateBody()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "body";
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * Size;

            // 每个位一份材质实例：非门取反时只改自己这一颗的颜色
            ownMaterial = new Material(MaterialFactory.LitShader) { name = "M_Bit" };
            MaterialFactory.SetBaseColor(ownMaterial, ColorOne);
            MaterialFactory.SetEmission(ownMaterial, ColorOne, EmissionBoost);

            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = ownMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            body = cube.transform;
        }

        /// <summary>值变化（非门取反）时换色并做一次轻微缩放脉冲，让观众看见"被改了"。</summary>
        public void SetValue(byte newValue)
        {
            if (newValue == value) return;
            ApplyValue(newValue, true);
        }

        void ApplyValue(byte newValue, bool animate)
        {
            value = newValue;
            Color color = newValue == 1 ? ColorOne : ColorZero;

            if (ownMaterial != null)
            {
                MaterialFactory.SetBaseColor(ownMaterial, color);
                MaterialFactory.SetEmission(ownMaterial, color, EmissionBoost);
            }

            if (animate) punch = 1f;
        }

        void OnDestroy()
        {
            if (ownMaterial == null) return;
            if (Application.isPlaying) Destroy(ownMaterial);
            else DestroyImmediate(ownMaterial);
            ownMaterial = null;
        }

        void Update()
        {
            if (body == null) return;

            // 缓慢自转，让"这是活的位"一眼可辨
            body.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);

            if (punch > 0f)
            {
                punch = Mathf.Max(0f, punch - Time.deltaTime * 4f);
                float scale = Size * (1f + 0.35f * punch);
                body.localScale = Vector3.one * scale;
            }
            else
            {
                body.localScale = Vector3.one * Size;
            }
        }
    }
}
