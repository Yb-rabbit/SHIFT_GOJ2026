using System;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>一次性的销毁爆闪：放大 + 淡出后自毁，避免堆积对象。</summary>
    public sealed class FlashBurst : MonoBehaviour
    {
        Material material;
        Color color;
        float duration = 0.4f;
        float elapsed;
        Vector3 targetScale = Vector3.one * 1.1f;

        public void Init(Color burstColor, float lifeTime)
        {
            color = burstColor;
            duration = Mathf.Max(0.05f, lifeTime);
            material = GetComponent<Renderer>()?.sharedMaterial;
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);

            transform.localScale = Vector3.Lerp(Vector3.one * 0.22f, targetScale, k);

            if (material != null)
            {
                // 没有透明通道，用"亮度衰减"表现消散
                MaterialFactory.SetEmission(material, color, Mathf.Lerp(5f, 0f, k));
                MaterialFactory.SetBaseColor(material, Color.Lerp(color, Color.black, k));
            }

            if (k >= 1f)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>爆闪材质由 Runner 为本实例单独 new 出来的，销毁时必须一起释放。</summary>
        void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }
    }
}
