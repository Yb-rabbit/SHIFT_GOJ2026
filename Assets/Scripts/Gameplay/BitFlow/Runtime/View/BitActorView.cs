using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 单个位的视觉表现：0.4 单位的发光方块，1=青、0=橙。位置由 Runner 每 tick 插值驱动。
    /// </summary>
    public sealed class BitActorView : MonoBehaviour
    {
        public static readonly Color ColorOne = new Color(0.20f, 0.88f, 1f);
        public static readonly Color ColorZero = new Color(1f, 0.60f, 0.24f);

        const float Size = 0.4f;
        const float EmissionBoost = 2.6f;

        Transform body;
        Material ownMaterial;
        byte value;
        float punch;

        public int BitId { get; private set; }

        public void Init(int bitId, byte initialValue, Vector3 worldPosition)
        {
            BitId = bitId;
            transform.position = worldPosition + Vector3.up * (Size * 0.5f + 0.06f);

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
