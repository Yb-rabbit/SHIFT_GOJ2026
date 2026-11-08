// ---------------------------------------------------------------------------
// PREVIEW ONLY —— 终端视觉原型的截屏器。
//
// 为什么放在运行时程序集：Editor 程序集里的 MonoBehaviour 进 Play 会变成
// 「missing script」，截屏逻辑根本不会执行。所以这里必须是运行时脚本，
// 只在编辑器里由 PREVIEW_TerminalStyle 菜单临时挂上去。
//
// 正式的 TerminalView 落地后，本文件整个删除。
// ---------------------------------------------------------------------------

using System.IO;
using UnityEngine;

namespace ShiftGo.Preview
{
    /// <summary>
    /// 把画面渲到 1920×1080 的 RenderTexture 存成 PNG，存完自动退出 Play。
    /// </summary>
    public class TerminalPreviewCapture : MonoBehaviour
    {
        public int Variant;

        void Start()
        {
            StartCoroutine(Run());
        }

        System.Collections.IEnumerator Run()
        {
            const int width = 1920;
            const int height = 1080;
            const int uiLayer = 5;   // Unity 内置 UI 层

            Canvas canvas = FindObjectOfType<Canvas>();
            Camera camera = Camera.main;

            if (canvas == null || camera == null)
            {
                Debug.LogError("[PREVIEW] 场景里缺 Canvas 或主相机，截图中止。");
                Quit();
                yield break;
            }

            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;

            // 等两帧：一帧让 URP 把 3D 渲进 RT，一帧兜底
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return new WaitForEndOfFrame();

            // ScreenSpaceOverlay 不会出现在相机渲染结果里，所以截图时改成「只吃 UI 层的第二台相机」，
            // 叠在同一张 RT 上 —— 观感和 Overlay 一致，但能截下来。
            GameObject uiCameraGo = new GameObject("PREVIEW_UICamera");
            Camera uiCamera = uiCameraGo.AddComponent<Camera>();
            // URP 下第二台相机渲到同一张 RT 时，Depth 清屏会把 3D 结果整块清成天空盒（实测背景变成蓝天）。
            // 必须是 Nothing：颜色和深度都保留，只往上叠 UI。
            uiCamera.clearFlags = CameraClearFlags.Nothing;
            uiCamera.cullingMask = 1 << uiLayer;
            uiCamera.depth = camera.depth + 1;
            uiCamera.nearClipPlane = 0.01f;
            uiCamera.farClipPlane = 10f;

            camera.cullingMask = ~(1 << uiLayer);

            SetLayer(canvas.gameObject, uiLayer);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = 1f;
            uiCamera.targetTexture = target;

            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return new WaitForEndOfFrame();

            camera.targetTexture = null;
            uiCamera.targetTexture = null;

            SaveShot(target, width, height, "Assets/Screenshots/terminal_variant_" + (Variant == 0 ? "a" : "b"));

            target.Release();
            Destroy(target);
            Quit();
        }

        static void SaveShot(RenderTexture target, int width, int height, string path)
        {
            RenderTexture.active = target;
            Texture2D shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            shot.Apply();
            RenderTexture.active = null;

            Directory.CreateDirectory("Assets/Screenshots");
            File.WriteAllBytes(path + ".png", shot.EncodeToPNG());
            Debug.Log("[PREVIEW] 截图已保存：" + path + ".png");

            Destroy(shot);
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayer(go.transform.GetChild(i).gameObject, layer);
            }
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
