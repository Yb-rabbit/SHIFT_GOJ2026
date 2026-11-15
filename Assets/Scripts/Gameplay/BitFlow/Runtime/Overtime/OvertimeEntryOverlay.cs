using UnityEngine;
using UnityEngine.UI;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 选关面板上的 OVERTIME 入口。它自己有一张独立 Canvas（sortingOrder 41），
    /// 只在 LevelSelectCanvas 处于激活状态时显示；点开后先关面板，再进入终端。
    /// </summary>
    public sealed class OvertimeEntryOverlay : MonoBehaviour
    {
        GameObject canvasObject;
        LevelSelectPanel panel;
        float nextLookup;

        void Awake()
        {
            BuildUi();
        }

        void BuildUi()
        {
            canvasObject = new GameObject("OvertimeEntryCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 41;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject buttonObject = new GameObject("OvertimeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(280f, 60f);
            rect.anchoredPosition = new Vector2(-32f, 32f);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.08f, 0.24f, 0.30f, 0.96f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(OpenTerminal);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(rect, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.GetComponent<Text>();
            label.font = PixelFont.Get();
            label.fontSize = PixelFont.Body;
            label.color = TerminalPalette.BodyColor;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = "OVERTIME";

            canvasObject.SetActive(false);
        }

        void Update()
        {
            OvertimeTerminal terminal = FindFirstObjectByType<OvertimeTerminal>();
            if (terminal != null && terminal.IsOpen)
            {
                SetVisible(false);
                return;
            }

            if (panel == null)
            {
                if (Time.unscaledTime < nextLookup) return;
                nextLookup = Time.unscaledTime + 0.5f;
                panel = FindFirstObjectByType<LevelSelectPanel>();
            }

            SetVisible(IsLevelSelectVisible());
        }

        void OpenTerminal()
        {
            if (panel != null) panel.Close();
            OvertimeTerminal.Ensure().Open();
        }

        bool IsLevelSelectVisible()
        {
            if (panel == null) return false;

            Transform canvas = panel.transform.Find("LevelSelectCanvas");
            return canvas != null && canvas.gameObject.activeInHierarchy;
        }

        void SetVisible(bool visible)
        {
            if (canvasObject == null) return;
            if (canvasObject.activeSelf != visible) canvasObject.SetActive(visible);
        }
    }
}
