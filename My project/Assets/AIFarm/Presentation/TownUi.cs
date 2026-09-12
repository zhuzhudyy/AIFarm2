using UnityEngine;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    public static class TownUi
    {
        private static Font font;
        public static Font Font => font != null ? font : font = UnityEngine.Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "Noto Sans CJK SC", "SimHei", "Arial Unicode MS", "Arial" }, 24);

        public static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static Image Panel(string name, Transform parent, float x, float y, float w, float h)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = new Color(.055f, .105f, .09f, .96f);
            return image;
        }

        public static Text Label(string name, Transform parent, string value, float x, float y, float w, float h, int size = 24)
        {
            var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.resizeTextForBestFit = false;
            text.text = value;
            text.color = new Color(.94f, .98f, .92f);
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        public static Button Button(string name, Transform parent, string value, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
        {
            var bg = Panel(name, parent, x, y, w, h);
            bg.color = new Color(.19f, .37f, .27f, 1);
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var label = Label("Label", bg.transform, value, 5, 2, w - 10, h - 4, 22);
            label.alignment = TextAnchor.MiddleCenter;
            if (action != null)
                button.onClick.AddListener(action);
            return button;
        }

        public static InputField Input(string name, Transform parent, string hint, float x, float y, float w, float h)
        {
            var bg = Panel(name, parent, x, y, w, h);
            bg.color = new Color(.12f, .19f, .15f, 1);
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = Label("Text", bg.transform, "", 14, 8, w - 28, h - 16, 24);
            input.textComponent.alignment = TextAnchor.MiddleLeft;
            input.placeholder = Label("Placeholder", bg.transform, hint, 14, 8, w - 28, h - 16, 22);
            input.placeholder.color = new Color(.63f, .7f, .65f);
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }
    }
}
