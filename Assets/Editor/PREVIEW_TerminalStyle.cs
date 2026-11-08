// ---------------------------------------------------------------------------
// PREVIEW ONLY —— 终端开场「视觉原型」。
//
// 目的：先看静态长什么样，确认风格之后才决定要不要写正式系统。
// 这里的数据是假的、静态的（没有打字机、没有推进），只用来回答：
//   面板放哪 / 说话人与正文怎么分栏 / 三位说话人的颜色能不能一眼分开 /
//   扫描线要不要 / 两台模型在背景里够不够存在感。
//
// 正式的 TerminalView 落地后，本文件整个删除。
// ---------------------------------------------------------------------------

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.View;

namespace ShiftGo.Preview
{
    static class TerminalStylePreview
    {
        // ---------------------------------------------------------------- 常量

        const string FontPath = "Assets/Arts/Resources/Text/BOUTIQUEBITMAP9X9_1.9.TTF";

        // 和 IntroCurtain 同一套画布参数，保证原型与正式 UI 在同一个坐标系里
        const float RefW = 1920f;
        const float RefH = 1080f;

        // BoutiqueBitmap9x9：16 号时每字符 10px，所以 32 号正好 20px/字符。
        // SpeakerCols = 6 字符 → 栏宽 120px，是 20 的整数倍，边缘不会糊。
        const int BodySize = 32;
        const int SmallSize = 20;
        const int LineHeight = 44;
        const int SpeakerCols = 6;

        static readonly Color PanelBg = new Color(0.055f, 0.070f, 0.100f, 0.90f);
        static readonly Color PanelEdge = new Color(0.118f, 0.227f, 0.290f, 1.00f);
        static readonly Color BodyColor = new Color(0.851f, 0.922f, 0.969f, 1.00f);
        static readonly Color MutedColor = new Color(0.353f, 0.420f, 0.478f, 1.00f);
        static readonly Color SysColor = new Color(0.486f, 0.576f, 0.651f, 1.00f);
        static readonly Color EmitColor = new Color(0.357f, 0.851f, 1.000f, 1.00f);
        static readonly Color RecvColor = new Color(0.298f, 0.902f, 0.549f, 1.00f);
        static readonly Color NullColor = new Color(1.000f, 0.769f, 0.420f, 1.00f);

        // ---------------------------------------------------------------- 假数据

        struct Line
        {
            public string Speaker;
            public Color Color;
            public string Text;

            public Line(string speaker, Color color, string text)
            {
                Speaker = speaker;
                Color = color;
                Text = text;
            }
        }

        /// <summary>
        /// 假剧本。注意全英文：这个位图字体没有中文字形（校验过），
        /// 中文会 fallback 到系统字体 —— 编辑器里能看，打包后会变方块。
        /// </summary>
        static readonly Line[] Script =
        {
            new Line("SYS", SysColor, "LINK ESTABLISHED."),
            new Line("SYS", SysColor, "CHANNEL 00 HANDSHAKE OK."),
            new Line("SYS", SysColor, "UNKNOWN SOURCE DETECTED. AWAITING REPLY ......"),
            new Line("EMIT", EmitColor, "YOU SHOULD NOT BE HERE."),
            new Line("EMIT", EmitColor, "THERE IS NO EXIT. ONLY THE LOOP."),
            new Line("SYS", SysColor, "UPLINK ATTEMPT 1 ......... FAIL"),
            new Line("SYS", SysColor, "UPLINK ATTEMPT 2 ......... FAIL"),
            new Line("RECV", RecvColor, "... I HEARD THAT."),
            new Line("RECV", RecvColor, "BUT WHOSE VOICE DID YOU SEND ME?"),
            new Line("SYS", SysColor, "WARNING: RECEIVER OFFLINE."),
            new Line("", NullColor, "DON'T STOP.")
        };

        // ---------------------------------------------------------------- 菜单

        [MenuItem("Tools/PREVIEW/Terminal Style/A - Centered Panel", false, 10)]
        static void BuildVariantA()
        {
            Build(0, false);
        }

        [MenuItem("Tools/PREVIEW/Terminal Style/B - Fullscreen", false, 11)]
        static void BuildVariantB()
        {
            Build(1, false);
        }

        [MenuItem("Tools/PREVIEW/Terminal Style/Capture A PNG", false, 20)]
        static void CaptureVariantA()
        {
            Build(0, true);
        }

        [MenuItem("Tools/PREVIEW/Terminal Style/Capture B PNG", false, 21)]
        static void CaptureVariantB()
        {
            Build(1, true);
        }

        // ---------------------------------------------------------------- 主流程

        static void Build(int variant, bool capture)
        {
            // 有未保存的改动时先问一句，别把正在编辑的场景顶掉
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject stageGo = new GameObject("Stage");
            LevelBuilder builder = stageGo.AddComponent<LevelBuilder>();

            LevelDefinition definition = new LevelDefinition();
            definition.Width = 9;
            definition.Height = 8;

            builder.Prepare(definition);
            builder.BuildTerrain();

            Bounds stage = ComputeBounds(stageGo);

            Camera camera = SetupCamera(stage);
            SetupLighting();
            PlaceModels(stage);
            BuildUi(variant);

            if (!capture)
            {
                Debug.Log("[PREVIEW] 变体 " + (variant == 0 ? "A（居中面板）" : "B（全屏）") + " 已生成，直接 Play 看效果。");
                return;
            }

            // 截图必须走 Play 模式：项目是 URP，编辑器里手动渲染（camera.Render / SubmitRenderRequest）
            // 都画不出正确画面（实测只剩底色，模型和网格全丢）。
            // 所以这里只把场景搭好并挂上截屏器，进 Play 后由它渲到 1920×1080 的 RT、存 PNG、自己退出。
            GameObject driver = new GameObject("PREVIEW_Capture");
            TerminalPreviewCapture captureDriver = driver.AddComponent<TerminalPreviewCapture>();
            captureDriver.Variant = variant;
            EditorApplication.isPlaying = true;
        }

        // ---------------------------------------------------------------- 3D

        static Bounds ComputeBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, new Vector3(9f, 0.1f, 8f));

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Camera SetupCamera(Bounds stage)
        {
            Camera camera = Object.FindObjectOfType<Camera>();
            if (camera == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                camera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.020f, 0.030f, 0.050f, 1f);
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;

            Vector3 pivot = stage.center;
            float distance = Mathf.Max(stage.size.x, stage.size.z) * 1.0f;
            Vector3 offset = Quaternion.Euler(34f, 0f, 0f) * Vector3.back * distance;

            camera.transform.position = pivot + offset + Vector3.up * 0.2f;
            // 看向网格上方一点：地面被推低，两台机器整体落到终端面板上方，不会被面板拦腰切断
            camera.transform.LookAt(pivot + Vector3.up * 1.0f);
            return camera;
        }

        // ---------------------------------------------------------------- 截屏器

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
            }
        }

        static void SetupLighting()
        {
            foreach (Light light in Object.FindObjectsOfType<Light>())
            {
                if (light.type != LightType.Directional) continue;

                light.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
                light.intensity = 1.15f;
                light.color = new Color(0.82f, 0.90f, 1.00f);
            }

            // 背面补一盏冷色轮廓光：灰色模型在黑场里只靠主光会糊成一块方砖，
            // 有了轮廓才看得出「机器」的边角，也和网格的青线同调。
            GameObject rimGo = new GameObject("PREVIEW_RimLight");
            Light rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.35f;
            rim.color = new Color(0.32f, 0.72f, 0.95f);
            rimGo.transform.rotation = Quaternion.Euler(24f, 158f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.09f, 0.12f, 1f);
            RenderSettings.skybox = null;   // 有天空盒时地板会反出蓝天，整块地面变成亮蓝色，开场要的是黑场

            // 远端渐隐进黑：比指望「看到地板边」干净，也不会露出无意义的天空
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.020f, 0.030f, 0.050f, 1f);
            RenderSettings.fogStartDistance = 10f;
            RenderSettings.fogEndDistance = 38f;
        }

        /// <summary>
        /// 两台模型摆在网格远端，相机在近端看过去 —— 正好被终端层裁住一部分，
        /// 既看得见体积，又不会和文字抢注意力。
        /// </summary>
        static void PlaceModels(Bounds stage)
        {
            float x = stage.extents.x * 0.30f;
            float z = stage.extents.z * 0.22f;
            float y = stage.max.y;

            SpawnModel(ElementModelLibrary.ReceiverModel, new Vector3(stage.center.x - x, y, stage.center.z + z), 180f);
            SpawnModel(ElementModelLibrary.EmitterModel, new Vector3(stage.center.x + x, y, stage.center.z + z), 0f);
        }

        static void SpawnModel(string modelName, Vector3 position, float yaw)
        {
            GameObject prefab = ElementModelLibrary.Load(modelName);
            if (prefab == null) return;

            GameObject holder = new GameObject("PREVIEW_" + modelName);
            holder.transform.position = Vector3.zero;
            holder.transform.rotation = Quaternion.identity;

            GameObject mesh = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = Vector3.one;

            // 等比归一化到 2.8 个单位，然后底面贴地、水平居中到 holder 原点。
            // 这里不走 ElementModelLibrary.TryBuild：那条路径按「一格」归一化，
            // 原型要的是「在背景里占多少视觉分量」，也不该把原型和正式装配逻辑绑在一起验。
            Bounds bounds = ComputeBounds(mesh);
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest > 0.0001f)
            {
                mesh.transform.localScale = Vector3.one * (3.4f / largest);
                bounds = ComputeBounds(mesh);
            }

            mesh.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            holder.transform.position = position;
        }

        // ---------------------------------------------------------------- UI

        static Canvas BuildUi(int variant)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogError("[PREVIEW] 字体没加载到：" + FontPath);
                return null;
            }

            GameObject canvasGo = new GameObject("TerminalPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;   // HUD(0) 之上、IntroCurtain(30) 之下：将来的幕能盖住它

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = canvasGo.GetComponent<RectTransform>();

            Image dim = CreateImage("Dim", root, new Color(0f, 0f, 0f, variant == 0 ? 0.25f : 0.62f));
            Stretch(dim.rectTransform);

            if (variant == 0) BuildCenteredPanel(root, font);
            else BuildFullscreen(root, font);

            AddScanlines(root);
            return canvas;
        }

        /// <summary>变体 A：居中终端窗 —— 偏「设备面板」，3D 退成背景。</summary>
        static void BuildCenteredPanel(RectTransform root, Font font)
        {
            const float panelW = 1220f;
            const float panelH = 620f;

            Image panel = CreateImage("Panel", root, PanelBg);
            SetRectCentered(panel.rectTransform, panelW, panelH, new Vector2(0f, -205f));

            // 1px 描边：外层铺满再外扩 1px，内层缩回 1px 盖住内部
            Image edge = CreateImage("Edge", panel.rectTransform, PanelEdge);
            Stretch(edge.rectTransform, 1f);

            Image inner = CreateImage("Inner", edge.rectTransform, PanelBg);
            Stretch(inner.rectTransform, -1f);

            Text title = CreateText("Title", inner.rectTransform, font, SmallSize, MutedColor, TextAnchor.MiddleLeft);
            title.text = "INTRO_00 // CHANNEL 00";
            SetRectTopLeft(title.rectTransform, 600f, 36f, new Vector2(28f, -8f));

            Text status = CreateText("Status", inner.rectTransform, font, SmallSize, MutedColor, TextAnchor.MiddleRight);
            status.text = "LINE 07/42   T 3.42s";
            SetRectTopRight(status.rectTransform, 400f, 36f, new Vector2(-28f, -8f));

            Image divider = CreateImage("Divider", inner.rectTransform, PanelEdge);
            SetRectTopStretch(divider.rectTransform, 0f, -48f, 1f);

            // 正文：一行一个 Text（将来逐行淡入 / 打字机都只替换单行，不必整块重建）
            for (int i = 0; i < Script.Length; i++)
            {
                bool last = i == Script.Length - 1;

                Text line = CreateText("Line_" + i.ToString("00"), inner.rectTransform, font, BodySize, BodyColor, TextAnchor.UpperLeft);
                line.text = Compose(Script[i], last);
                SetRectTopLeft(line.rectTransform, panelW - 96f, LineHeight, new Vector2(48f, -62f - i * LineHeight));
            }
        }

        /// <summary>变体 B：全屏无边框 —— 文字压左侧，右侧让给场景；旧行往上淡出。</summary>
        static void BuildFullscreen(RectTransform root, Font font)
        {
            const float columnW = 860f;
            const float columnLeft = 190f;

            // 左侧压一层横向渐变黑：文字区必须有底，否则会被机器的亮面吃掉可读性
            AddLeftScrim(root);

            int count = Script.Length;
            float totalH = count * LineHeight;

            for (int i = 0; i < count; i++)
            {
                // 越靠上越淡：做出「往上滚走」的感觉，最下面一行永远满亮
                float alpha = count <= 1 ? 1f : Mathf.Lerp(0.25f, 1f, (float)i / (count - 1));
                bool last = i == Script.Length - 1;

                Text line = CreateText("Line_" + i.ToString("00"), root, font, BodySize, BodyColor, TextAnchor.UpperLeft);
                line.text = Compose(Script[i], last);
                line.color = new Color(1f, 1f, 1f, alpha);
                SetRectLeftMiddle(line.rectTransform, columnW, LineHeight, columnLeft,
                    totalH * 0.5f - i * LineHeight - LineHeight * 0.5f);
            }

            AddGradientBand(root, true);
            AddGradientBand(root, false);
        }

        /// <summary>左侧渐变压暗（变体 B 用），把视觉重心让给右边的机器。</summary>
        static void AddLeftScrim(RectTransform root)
        {
            GameObject go = new GameObject("ScrimLeft", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(root, false);

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = MakeHorizontalFade();
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, 1f, 1f);

            RectTransform rt = raw.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(1300f, 0f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>上下各压一条竖向渐变，让文字进出画面时不硬切。</summary>
        static void AddGradientBand(RectTransform root, bool top)
        {
            const float bandH = 160f;

            GameObject go = new GameObject(top ? "FadeTop" : "FadeBottom", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(root, false);

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = MakeVerticalFade();
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, 1f, top ? -1f : 1f);

            RectTransform rt = raw.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.offsetMin = new Vector2(0f, top ? -bandH : 0f);
            rt.offsetMax = new Vector2(0f, top ? 0f : bandH);
        }

        /// <summary>CRT 扫描线：2×4 的贴图平铺，每 4px 一条 18% 黑。</summary>
        static void AddScanlines(RectTransform root)
        {
            GameObject go = new GameObject("Scanlines", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(root, false);

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = MakeScanTexture();
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, RefW / 2f, RefH / 4f);   // 1 像素 = 1 UI 单位

            Stretch(raw.rectTransform);
        }

        // ---------------------------------------------------------------- 文本拼装

        /// <summary>
        /// 一行日志：说话人栏（定宽 → 靠字符对齐，不靠 RectTransform）+ › + 正文。
        /// 等宽位图字体让「补空格对齐」这件事真的成立，这也是选它的主要理由。
        /// </summary>
        static string Compose(Line line, bool cursor)
        {
            string speaker = (line.Speaker ?? string.Empty).PadRight(SpeakerCols);
            string body = line.Text;

            if (cursor) body += "<color=#" + Hex(EmitColor) + ">\u258C</color>";

            return "<color=#" + Hex(line.Color) + ">" + speaker + "\u203A</color>"
                 + "<color=#" + Hex(BodyColor) + "> " + body + "</color>";
        }

        static string Hex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }

        // ---------------------------------------------------------------- uGUI 小工具

        static Text CreateText(string name, Transform parent, Font font, int size, Color color, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1f;
            return text;
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rt, float expand = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-expand, -expand);
            rt.offsetMax = new Vector2(expand, expand);
        }

        static void SetRectCentered(RectTransform rt, float width, float height, Vector2 position)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = position;
        }

        static void SetRectTopLeft(RectTransform rt, float width, float height, Vector2 position)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = position;
        }

        static void SetRectTopRight(RectTransform rt, float width, float height, Vector2 position)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = position;
        }

        /// <summary>贴左边的竖条（变体 B 的正文列用）：x 从屏幕左边算，y 从画面中线算。</summary>
        static void SetRectLeftMiddle(RectTransform rt, float width, float height, float left, float offsetY)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(left, offsetY);
        }

        /// <summary>贴顶的一条横线（分隔线用）。</summary>
        static void SetRectTopStretch(RectTransform rt, float left, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, top - height);
            rt.offsetMax = new Vector2(-left, top);
        }

        // ---------------------------------------------------------------- 生成的贴图

        static Texture2D MakeScanTexture()
        {
            Texture2D texture = new Texture2D(2, 4, TextureFormat.RGBA32, false);
            texture.name = "PREVIEW_Scanlines";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;

            for (int y = 0; y < 4; y++)
            {
                Color color = y == 0 ? new Color(0f, 0f, 0f, 0.22f) : new Color(0f, 0f, 0f, 0f);
                texture.SetPixel(0, y, color);
                texture.SetPixel(1, y, color);
            }

            texture.Apply();
            return texture;
        }

        static Texture2D MakeVerticalFade()
        {
            const int height = 64;

            Texture2D texture = new Texture2D(1, height, TextureFormat.RGBA32, false);
            texture.name = "PREVIEW_Fade";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < height; y++)
            {
                texture.SetPixel(0, y, new Color(0f, 0f, 0f, Mathf.Lerp(0f, 1f, (float)y / (height - 1))));
            }

            texture.Apply();
            return texture;
        }

        /// <summary>横向渐变：最左边 80% 黑，往右淡到透明。</summary>
        static Texture2D MakeHorizontalFade()
        {
            const int width = 64;

            Texture2D texture = new Texture2D(width, 1, TextureFormat.RGBA32, false);
            texture.name = "PREVIEW_FadeH";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int x = 0; x < width; x++)
            {
                float t = (float)x / (width - 1);
                texture.SetPixel(x, 0, new Color(0f, 0f, 0f, Mathf.Lerp(0.80f, 0f, t * t)));
            }

            texture.Apply();
            return texture;
        }
    }
}
