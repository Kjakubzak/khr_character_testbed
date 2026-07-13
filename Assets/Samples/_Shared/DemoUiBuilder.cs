using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Samples.Shared
{
    /// <summary>
    /// Builds a Screen-Space-Overlay uGUI control panel entirely in code (no prefabs needed). Controls are created
    /// with <see cref="DefaultControls"/> so their internal hierarchies (slider fill/handle, dropdown template,
    /// scroll view viewport) are always correctly wired, then appended as rows to a scrollable column. The overlay
    /// canvas renders identically on Built-in, URP, and HDRP.
    /// </summary>
    public class DemoUiBuilder : MonoBehaviour
    {
        /// <summary>Parent for appended rows (the scroll view's content).</summary>
        public RectTransform Content { get; private set; }

        /// <summary>The root panel RectTransform (top-left by default); expose it so callers can re-dock the panel.</summary>
        public RectTransform Panel { get; private set; }

        private DefaultControls.Resources _res;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color TextColor = Color.white;
        private const int FontSize = 14;
        private const float RowHeight = 26f;
        private const float LabelWidth = 120f;
        private const float ControlWidth = 150f;

        private static Font _font;

        /// <summary>Create a panel on a fresh GameObject with a title and a scrollable body.</summary>
        public static DemoUiBuilder Create(string title, Vector2 panelSize = default)
        {
            var go = new GameObject("DemoUI");
            var builder = go.AddComponent<DemoUiBuilder>();
            builder.Build(title, panelSize == default ? new Vector2(340f, 480f) : panelSize);
            return builder;
        }

        /// <summary>Build the canvas, panel, title, and scroll body. Called once by <see cref="Create"/>.</summary>
        public void Build(string title, Vector2 panelSize)
        {
            EnsureEventSystem();

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            gameObject.AddComponent<GraphicRaycaster>();

            // Root panel, pinned to the top-left.
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panelRt = (RectTransform)panel.transform;
            panelRt.SetParent(transform, false);
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(10f, -10f);
            panelRt.sizeDelta = panelSize;
            panel.GetComponent<Image>().color = PanelColor;
            Panel = panelRt;

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(8, 8, 8, 8);
            panelLayout.spacing = 6f;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            // Title row.
            var titleText = MakeText(panelRt, string.IsNullOrEmpty(title) ? "Panel" : title);
            titleText.fontStyle = FontStyle.Bold;
            titleText.fontSize = FontSize + 3;
            var titleLe = titleText.gameObject.AddComponent<LayoutElement>();
            titleLe.minHeight = 22f;
            titleLe.flexibleWidth = 1f;

            // Scroll body fills the remaining height.
            var scrollGo = DefaultControls.CreateScrollView(_res);
            scrollGo.name = "ScrollView";
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(panelRt, false);
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleWidth = 1f;
            scrollLe.flexibleHeight = 1f;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            Content = scrollRect.content;
            Content.anchorMin = new Vector2(0f, 1f);
            Content.anchorMax = new Vector2(1f, 1f);
            Content.pivot = new Vector2(0.5f, 1f);

            var contentLayout = Content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(6, 6, 6, 6);
            contentLayout.spacing = 4f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var fitter = Content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ApplyFont(gameObject);
        }

        /// <summary>Destroy all appended rows (used when rebuilding for a swapped character).</summary>
        public void ClearRows()
        {
            if (Content == null) return;
            for (int i = Content.childCount - 1; i >= 0; i--)
                Destroy(Content.GetChild(i).gameObject);
        }

        /// <summary>Append a read-only text row.</summary>
        public Text AddLabel(string text)
        {
            var row = NewRow("LabelRow");
            var t = MakeText(row, text);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = RowHeight - 6f;
            ApplyFont(t.gameObject);
            return t;
        }

        /// <summary>Append a full-width button.</summary>
        public Button AddButton(string label, UnityAction onClick)
        {
            var row = NewRow("ButtonRow");
            var go = DefaultControls.CreateButton(_res);
            go.name = "Button";
            ((RectTransform)go.transform).SetParent(row, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = RowHeight;

            var caption = go.GetComponentInChildren<Text>(true);
            if (caption != null) { caption.text = label; caption.color = Color.black; }

            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            ApplyFont(go);
            return button;
        }

        /// <summary>Append a labelled slider.</summary>
        public Slider AddSlider(string label, UnityAction<float> onChanged, float min = 0f, float max = 1f, float value = 0f)
        {
            var row = NewRow("SliderRow");
            MakeRowLabel(row, label);

            var go = DefaultControls.CreateSlider(_res);
            go.name = "Slider";
            ((RectTransform)go.transform).SetParent(row, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ControlWidth;
            le.minHeight = 18f;

            var slider = go.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);

            ApplyFont(go);
            return slider;
        }

        /// <summary>Append a labelled toggle (used for binary expressions).</summary>
        public Toggle AddToggle(string label, UnityAction<bool> onChanged, bool value = false)
        {
            var row = NewRow("ToggleRow");
            MakeRowLabel(row, label);

            var go = DefaultControls.CreateToggle(_res);
            go.name = "Toggle";
            ((RectTransform)go.transform).SetParent(row, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 28f;
            le.minHeight = 20f;

            // The toggle ships its own label; clear it since the row already has one on the left.
            var inner = go.GetComponentInChildren<Text>(true);
            if (inner != null) inner.text = string.Empty;

            var toggle = go.GetComponent<Toggle>();
            toggle.SetIsOnWithoutNotify(value);
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);

            ApplyFont(go);
            return toggle;
        }

        /// <summary>Append a labelled dropdown.</summary>
        public Dropdown AddDropdown(string label, System.Collections.Generic.List<string> options, UnityAction<int> onChanged, int value = 0)
        {
            var row = NewRow("DropdownRow");
            MakeRowLabel(row, label);

            var go = DefaultControls.CreateDropdown(_res);
            go.name = "Dropdown";
            ((RectTransform)go.transform).SetParent(row, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ControlWidth;
            le.minHeight = 22f;

            var dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            if (options != null && options.Count > 0)
            {
                dropdown.AddOptions(options);
                dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, options.Count - 1));
            }
            if (onChanged != null) dropdown.onValueChanged.AddListener(onChanged);

            ApplyFont(go);

            // Guarantee the caption + item labels render dark against the default (light)
            // dropdown-template background. Without this, custom uGUI setups can end up with
            // white-on-white invisible captions on some Unity versions.
            if (dropdown.captionText != null)
            {
                dropdown.captionText.color = Color.black;
                if (options != null && options.Count > 0)
                    dropdown.captionText.text = options[Mathf.Clamp(value, 0, options.Count - 1)];
            }
            if (dropdown.itemText != null) dropdown.itemText.color = Color.black;

            // Force a caption refresh AFTER font + color setup — Unity's Dropdown.Set only
            // fires RefreshShownValue when value CHANGES; if value=0 and default was 0,
            // caption may not have been populated on setup.
            dropdown.RefreshShownValue();

            return dropdown;
        }

        /// <summary>Append a labelled single-line text input.</summary>
        public InputField AddInputField(string label, string value, UnityAction<string> onEndEdit)
        {
            var row = NewRow("InputRow");
            MakeRowLabel(row, label);

            var go = DefaultControls.CreateInputField(_res);
            go.name = "InputField";
            ((RectTransform)go.transform).SetParent(row, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ControlWidth;
            le.minHeight = 22f;

            var field = go.GetComponent<InputField>();
            field.text = value ?? string.Empty;
            if (onEndEdit != null) field.onEndEdit.AddListener(onEndEdit);

            ApplyFont(go);
            return field;
        }

        private RectTransform NewRow(string name)
        {
            var row = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)row.transform;
            rt.SetParent(Content, false);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = RowHeight;
            le.flexibleWidth = 1f;
            return rt;
        }

        private void MakeRowLabel(RectTransform row, string label)
        {
            var t = MakeText(row, label);
            t.alignment = TextAnchor.MiddleLeft;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = LabelWidth;
            le.flexibleWidth = 1f;
            le.minHeight = RowHeight - 6f;
        }

        private Text MakeText(Transform parent, string text)
        {
            var go = DefaultControls.CreateText(_res);
            go.name = "Text";
            ((RectTransform)go.transform).SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text ?? string.Empty;
            t.color = TextColor;
            t.fontSize = FontSize;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Font UiFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", FontSize);
            return _font;
        }

        private static void ApplyFont(GameObject root)
        {
            var f = UiFont();
            if (f == null) return;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.font == null) t.font = f;
        }
    }
}
