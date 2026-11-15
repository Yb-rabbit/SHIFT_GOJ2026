using UnityEngine;
using UnityEngine.UI;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// OVERTIME 的终端画面。和开场 TerminalView 同一套像素字体 / 配色，
    /// 但它的生命周期由 OvertimeTerminal 状态机控制，不自动播放台词。
    /// </summary>
    public sealed class OvertimeIdleView : MonoBehaviour
    {
        CanvasGroup group;
        Text titleText;
        Text bodyText;
        Text hintText;

        public static OvertimeIdleView Create(Transform parent)
        {
            GameObject go = new GameObject("OvertimeIdleView", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            OvertimeIdleView view = go.AddComponent<OvertimeIdleView>();
            view.BuildUi();
            return view;
        }

        void BuildUi()
        {
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            group = GetComponent<CanvasGroup>();

            GameObject scrimObject = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            scrimObject.transform.SetParent(transform, false);
            Image scrim = scrimObject.GetComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.78f);
            Stretch(scrim.rectTransform, 0f);

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(TerminalPalette.PanelWidth, TerminalPalette.PanelHeight), new Vector2(0f, -40f));

            Image panel = panelObject.GetComponent<Image>();
            panel.color = new Color(0.045f, 0.060f, 0.085f, 0.96f);

            GameObject edgeObject = new GameObject("Edge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            edgeObject.transform.SetParent(panelRect, false);
            Image edge = edgeObject.GetComponent<Image>();
            edge.color = TerminalPalette.PanelEdge;
            SetRect(edge.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 2f), Vector2.zero);

            titleText = CreateText("Title", panelRect, PixelFont.Large, TerminalPalette.BodyColor, TextAnchor.MiddleLeft);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-96f, 56f), new Vector2(0f, -46f));

            bodyText = CreateText("Body", panelRect, PixelFont.Body, TerminalPalette.BodyColor, TextAnchor.UpperLeft);
            SetRect(bodyText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-96f, 390f), new Vector2(0f, -118f));

            GameObject lineObject = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineObject.transform.SetParent(panelRect, false);
            Image line = lineObject.GetComponent<Image>();
            line.color = new Color(0.16f, 0.28f, 0.34f, 1f);
            SetRect(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-96f, 1f), new Vector2(0f, 76f));

            hintText = CreateText("Hint", panelRect, PixelFont.Body, TerminalPalette.MutedColor, TextAnchor.MiddleLeft);
            SetRect(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-96f, 48f), new Vector2(0f, 40f));
        }
        public void SetContent(string title, string body, string hint, Color titleColor, Color hintColor)
        {
            if (titleText != null)
            {
                titleText.text = title ?? string.Empty;
                titleText.color = titleColor;
            }

            if (bodyText != null) bodyText.text = body ?? string.Empty;
            if (hintText != null)
            {
                hintText.text = hint ?? string.Empty;
                hintText.color = hintColor;
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }
        }

        public void Hide()
        {
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }

        static Text CreateText(string name, Transform parent, int size, Color color, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = PixelFont.Get();
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.1f;
            return text;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-inset, -inset);
            rect.offsetMax = new Vector2(inset, inset);
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}

