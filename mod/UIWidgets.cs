using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IGTAPMod
{
    /// <summary>
    /// Common interface for all widgets managed by a WidgetPanel.
    /// </summary>
    public interface IWidget
    {
        GameObject Root { get; }
        void Update();
    }

    /// <summary>
    /// A declarative container for UI widgets. Add widgets once via builder methods,
    /// then call UpdateAll() each frame to sync visuals with live game state.
    /// </summary>
    public class WidgetPanel
    {
        public RectTransform Root { get; }
        private readonly List<IWidget> _widgets = new List<IWidget>();
        private readonly Dictionary<string, WidgetDynamic> _dynamics = new Dictionary<string, WidgetDynamic>();

        public WidgetPanel(RectTransform root)
        {
            Root = root;
        }

        /// <summary>
        /// Sync all widget visuals with their backing data. Call each frame.
        /// </summary>
        public void UpdateAll()
        {
            for (int i = 0; i < _widgets.Count; i++)
                _widgets[i].Update();
        }

        private void Register(IWidget w) => _widgets.Add(w);

        // =====================================================================
        //  Widget builders
        // =====================================================================

        public WidgetToggle AddToggle(string label, Func<bool> getter, Action<bool> setter)
        {
            var w = new WidgetToggle(Root, label, getter, setter);
            Register(w);
            return w;
        }

        public WidgetButton AddButton(string label, Action onClick, Color? color = null)
        {
            var w = new WidgetButton(Root, label, onClick, color);
            Register(w);
            return w;
        }

        public WidgetButtonRow AddButtonRow(params (string label, Action onClick)[] buttons)
        {
            var w = new WidgetButtonRow(Root, buttons);
            Register(w);
            return w;
        }

        public WidgetIntField AddIntField(string label, Func<int> getter, Action<int> setter,
            int min = 0, ConfigEntry<bool> infToggle = null)
        {
            var w = new WidgetIntField(Root, label, getter, setter, min, infToggle);
            Register(w);
            return w;
        }

        public WidgetFloatField AddFloatField(string label, Func<float> getter, Action<float> setter)
        {
            var w = new WidgetFloatField(Root, label, getter, setter);
            Register(w);
            return w;
        }

        public WidgetLabel AddLabel(Func<string> getText, float fontSize = UIStyle.FontSizeBody,
            Color? color = null)
        {
            var w = new WidgetLabel(Root, getText, fontSize, color);
            Register(w);
            return w;
        }

        public WidgetSlider AddSlider(string label, float min, float max,
            Func<float> getter, Action<float> setter, bool wholeNumbers = false)
        {
            var w = new WidgetSlider(Root, label, min, max, getter, setter, wholeNumbers);
            Register(w);
            return w;
        }

        public WidgetTextField AddTextField(string label, Func<string> getter, Action<string> setter,
            float fieldWidth = 120f)
        {
            var w = new WidgetTextField(Root, label, getter, setter, fieldWidth);
            Register(w);
            return w;
        }

        public WidgetEnumCycle<T> AddEnumCycle<T>(string label, Func<T> getter, Action<T> setter)
            where T : struct, Enum
        {
            var w = new WidgetEnumCycle<T>(Root, label, getter, setter);
            Register(w);
            return w;
        }

        public WidgetDropdown<T> AddDropdown<T>(string label, Func<T> getter, Action<T> setter)
            where T : struct, Enum
        {
            var w = new WidgetDropdown<T>(Root, label, getter, setter);
            Register(w);
            return w;
        }

        public WidgetSpacer AddSpacer(float height = 8f)
        {
            var w = new WidgetSpacer(Root, height);
            Register(w);
            return w;
        }

        public WidgetSeparator AddSeparator()
        {
            var w = new WidgetSeparator(Root);
            Register(w);
            return w;
        }

        public WidgetDynamic AddDynamic(string id, Action<WidgetPanel> buildContent)
        {
            var w = new WidgetDynamic(Root, id, buildContent);
            Register(w);
            _dynamics[id] = w;
            return w;
        }

        public void RebuildDynamic(string id)
        {
            if (_dynamics.TryGetValue(id, out var dyn))
                dyn.Rebuild();
        }

        /// <summary>
        /// Destroy all widget GameObjects and clear the list.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _widgets.Count; i++)
            {
                if (_widgets[i].Root != null)
                    UnityEngine.Object.Destroy(_widgets[i].Root);
            }
            _widgets.Clear();
            _dynamics.Clear();
        }
    }

    // =====================================================================
    //  Widget implementations
    // =====================================================================

    public class WidgetToggle : IWidget
    {
        public GameObject Root { get; }
        private readonly Toggle _toggle;
        private readonly Func<bool> _getter;
        private readonly Action<bool> _setter;
        private bool _updating;

        public WidgetToggle(RectTransform parent, string label, Func<bool> getter, Action<bool> setter)
        {
            _getter = getter;
            _setter = setter;

            Plugin.Log.LogInfo($"[WidgetToggle] Creating '{label}' via theme.CloneToggle");
            var theme = GameUITheme.Instance;
            _toggle = theme.CloneToggle(parent.transform, label, getter(),
                v => { if (!_updating) setter(v); });
            GameUI.SetSize(_toggle.GetComponent<RectTransform>(), height: 36);
            Root = _toggle.gameObject;
            GameUITheme.DumpWidgetTreeDeep(Root, "Toggle_" + label);
        }

        public void Update()
        {
            _updating = true;
            _toggle.isOn = _getter();
            _updating = false;
        }
    }

    public class WidgetButton : IWidget
    {
        public GameObject Root { get; }

        public WidgetButton(RectTransform parent, string label, Action onClick, Color? color)
        {
            Plugin.Log.LogInfo($"[WidgetButton] Creating '{label}' via theme.CloneButton");
            var theme = GameUITheme.Instance;
            // Text-only style matches the game's convention (orange text = clickable).
            var btn = theme.CloneButton(parent.transform, label, onClick, textOnly: true);
            if (color.HasValue)
            {
                var c = btn.colors;
                c.normalColor = color.Value;
                btn.colors = c;
            }
            GameUI.SetSize(btn.GetComponent<RectTransform>(), height: 36);
            Root = btn.gameObject;
        }

        public void Update() { }
    }

    public class WidgetButtonRow : IWidget
    {
        public GameObject Root { get; }

        public WidgetButtonRow(RectTransform parent, (string label, Action onClick)[] buttons)
        {
            var row = GameUI.CreatePanel(parent.transform, "ButtonRow", Color.clear);
            var layout = GameUI.AddHorizontalLayout(row, spacing: 4, padding: 0);
            layout.childForceExpandWidth = true;
            GameUI.SetSize(row, height: 36);

            var theme = GameUITheme.Instance;
            foreach (var (label, onClick) in buttons)
            {
                var btn = theme.CloneButton(row.transform, label, onClick, textOnly: true);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1;
            }

            Root = row.gameObject;
        }

        public void Update() { }
    }

    public class WidgetIntField : IWidget
    {
        public GameObject Root { get; }
        private readonly Slider _slider;
        private readonly TMP_InputField _input;
        private readonly Func<int> _getter;
        private readonly Action<int> _setter;
        private readonly int _min;
        private readonly Toggle _infToggle;
        private readonly ConfigEntry<bool> _infConfig;
        private bool _updating;

        public WidgetIntField(RectTransform parent, string label, Func<int> getter, Action<int> setter,
            int min, ConfigEntry<bool> infConfig)
        {
            _getter = getter;
            _setter = setter;
            _min = min;
            _infConfig = infConfig;

            var row = GameUI.CreatePanel(parent.transform, "IntField_" + label, Color.clear);
            var layout = GameUI.AddHorizontalLayout(row, spacing: 6, padding: 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            GameUI.SetSize(row, height: 36);

            // Label - flexible, pushes controls right
            var labelTmp = GameUI.CreateText(row.transform, "Label", label, UIStyle.FontSizeBody);
            var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1;
            labelLe.minWidth = 60;

            // Controls group - fixed width so they align across rows
            var controls = GameUI.CreatePanel(row.transform, "Controls", Color.clear);
            var ctrlLayout = GameUI.AddHorizontalLayout(controls, spacing: 4, padding: 0);
            ctrlLayout.childAlignment = TextAnchor.MiddleCenter;
            ctrlLayout.childForceExpandWidth = false;
            ctrlLayout.childForceExpandHeight = false;
            ctrlLayout.childControlWidth = true;
            ctrlLayout.childControlHeight = true;
            var ctrlLe = controls.gameObject.AddComponent<LayoutElement>();
            ctrlLe.flexibleWidth = 2;

            var theme = GameUITheme.Instance;

            // Slider (min-20 range, integer snap)
            _slider = theme.CloneSlider(controls.transform, label, min, 20,
                getter(), v => { if (!_updating) setter(Mathf.RoundToInt(v)); });
            _slider.wholeNumbers = true;
            var sliderLe = _slider.gameObject.AddComponent<LayoutElement>();
            sliderLe.flexibleWidth = 1;
            sliderLe.preferredHeight = 28;

            // Editable text field for manual override
            _input = GameUI.CreateInputField(controls.transform, "Value", "", null, UIStyle.FontSizeBody);
            _input.contentType = TMP_InputField.ContentType.IntegerNumber;
            _input.text = getter().ToString();
            _input.onEndEdit.AddListener(text =>
            {
                if (int.TryParse(text, out int val) && val >= min)
                    setter(val);
            });
            var inputLe = _input.gameObject.AddComponent<LayoutElement>();
            inputLe.preferredWidth = 50;
            inputLe.preferredHeight = 28;

            // Optional Inf toggle
            if (infConfig != null)
            {
                _infToggle = theme.CloneToggle(controls.transform, "Inf", infConfig.Value, v =>
                {
                    if (!_updating) infConfig.Value = v;
                });
                var infLe = _infToggle.gameObject.AddComponent<LayoutElement>();
                infLe.preferredWidth = 70;
            }

            Root = row.gameObject;
        }

        public void Update()
        {
            _updating = true;
            int val = _getter();
            _slider.value = Mathf.Clamp(val, _slider.minValue, _slider.maxValue);
            if (!_input.isFocused)
                _input.text = val.ToString();
            if (_infToggle != null && _infConfig != null)
                _infToggle.isOn = _infConfig.Value;
            _updating = false;
        }
    }

    public class WidgetFloatField : IWidget
    {
        public GameObject Root { get; }
        private readonly TMP_InputField _input;
        private readonly Func<float> _getter;
        private readonly Action<float> _setter;
        private float _lastValue;

        public WidgetFloatField(RectTransform parent, string label, Func<float> getter, Action<float> setter)
        {
            _getter = getter;
            _setter = setter;
            _lastValue = getter();

            var row = GameUI.CreatePanel(parent.transform, "FloatField_" + label, Color.clear);
            var layout = GameUI.AddHorizontalLayout(row, spacing: 6, padding: 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            GameUI.SetSize(row, height: 36);

            // Label - flexible, pushes input right
            var labelTmp = GameUI.CreateText(row.transform, "Label", label, UIStyle.FontSizeBody);
            var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1;
            labelLe.minWidth = 60;

            // Input field - fixed width to align across rows
            _input = GameUI.CreateInputField(row.transform, "Input", "", null, UIStyle.FontSizeBody);
            var inputLe = _input.gameObject.AddComponent<LayoutElement>();
            inputLe.preferredWidth = 130;
            inputLe.preferredHeight = 30;
            _input.contentType = TMP_InputField.ContentType.DecimalNumber;
            _input.text = getter().ToString("F1");

            _input.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, out float val))
                {
                    _setter(val);
                    _lastValue = val;
                }
            });

            Root = row.gameObject;
        }

        public void Update()
        {
            float current = _getter();
            if (!_input.isFocused && Mathf.Abs(current - _lastValue) > 0.001f)
            {
                _input.text = current.ToString("F1");
                _lastValue = current;
            }
        }
    }

    public class WidgetLabel : IWidget
    {
        public GameObject Root { get; }
        private readonly TextMeshProUGUI _text;
        private readonly Func<string> _getText;

        public WidgetLabel(RectTransform parent, Func<string> getText, float fontSize, Color? color)
        {
            _getText = getText;
            string initial = getText() ?? "";
            _text = GameUI.CreateText(parent.transform, "Label", initial, fontSize,
                color: color, alignment: TextAlignmentOptions.MidlineLeft);
            _text.richText = true;
            Root = _text.gameObject;
        }

        public void Update()
        {
            string text = _getText();
            if (text == null)
            {
                Root.SetActive(false);
            }
            else
            {
                Root.SetActive(true);
                _text.text = text;
            }
        }
    }

    public class WidgetSlider : IWidget
    {
        public GameObject Root { get; }
        private readonly Slider _slider;
        private readonly Func<float> _getter;
        private readonly Action<float> _setter;
        private bool _updating;

        public WidgetSlider(RectTransform parent, string label, float min, float max,
            Func<float> getter, Action<float> setter, bool wholeNumbers)
        {
            _getter = getter;
            _setter = setter;

            Plugin.Log.LogInfo($"[WidgetSlider] Creating '{label}' via theme.CloneSlider");
            var theme = GameUITheme.Instance;
            _slider = theme.CloneSlider(parent.transform, label, min, max, getter(),
                v => { if (!_updating) setter(v); });
            _slider.wholeNumbers = wholeNumbers;
            Root = _slider.gameObject;
            GameUITheme.DumpWidgetTreeDeep(Root, "Slider_" + label);
        }

        public void Update()
        {
            _updating = true;
            _slider.value = _getter();
            _updating = false;
        }
    }

    public class WidgetTextField : IWidget
    {
        public GameObject Root { get; }
        private readonly TMP_InputField _input;
        private readonly Func<string> _getter;
        private readonly Action<string> _setter;

        public WidgetTextField(RectTransform parent, string label, Func<string> getter,
            Action<string> setter, float fieldWidth)
        {
            _getter = getter;
            _setter = setter;

            var row = GameUI.CreatePanel(parent.transform, "TextField_" + label, Color.clear);
            var layout = GameUI.AddHorizontalLayout(row, spacing: 6, padding: 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            GameUI.SetSize(row, height: 36);

            // Inline label — only created when a label string is provided. Pass an empty
            // string to get a full-width input field with no inline label (typically paired
            // with a separate AddLabel call above for long captions that don't fit).
            if (!string.IsNullOrEmpty(label))
            {
                var labelTmp = GameUI.CreateText(row.transform, "Label", label, UIStyle.FontSizeBody);
                var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
                labelLe.preferredWidth = 120;
                labelLe.minWidth = 80;
            }

            // Input field - fills remaining space
            _input = GameUI.CreateInputField(row.transform, "Input", "", null, UIStyle.FontSizeBody);
            var inputLe = _input.gameObject.AddComponent<LayoutElement>();
            inputLe.preferredWidth = fieldWidth;
            inputLe.preferredHeight = 30;
            inputLe.flexibleWidth = 1;
            _input.text = getter() ?? "";

            _input.onEndEdit.AddListener(text => setter(text));

            Root = row.gameObject;
        }

        public void Update()
        {
            if (!_input.isFocused)
            {
                string val = _getter();
                if (val != null && val != _input.text)
                    _input.text = val;
            }
        }
    }

    public class WidgetEnumCycle<T> : IWidget where T : struct, Enum
    {
        public GameObject Root { get; }
        private readonly TextMeshProUGUI _valueText;
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;
        private readonly T[] _values;

        public WidgetEnumCycle(RectTransform parent, string label, Func<T> getter, Action<T> setter)
        {
            _getter = getter;
            _setter = setter;
            _values = (T[])Enum.GetValues(typeof(T));

            var row = GameUI.CreatePanel(parent.transform, "EnumCycle_" + label, Color.clear);
            var layout = GameUI.AddHorizontalLayout(row, spacing: 6, padding: 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            GameUI.SetSize(row, height: 36);

            // Label - flexible, pushes controls right
            var labelTmp = GameUI.CreateText(row.transform, "Label", label, UIStyle.FontSizeBody);
            var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1;
            labelLe.minWidth = 60;

            // Controls group - fixed width so the < and > end up in the same column across all rows.
            // Width 180 leaves enough room for typical enum values without pushing the arrows to the
            // far edges of the row.
            var controls = GameUI.CreatePanel(row.transform, "Controls", Color.clear);
            var ctrlLayout = GameUI.AddHorizontalLayout(controls, spacing: 2, padding: 0);
            ctrlLayout.childAlignment = TextAnchor.MiddleCenter;
            ctrlLayout.childForceExpandWidth = false;
            ctrlLayout.childForceExpandHeight = false;
            ctrlLayout.childControlWidth = true;
            ctrlLayout.childControlHeight = true;
            var ctrlLe = controls.gameObject.AddComponent<LayoutElement>();
            ctrlLe.preferredWidth = 180;

            var theme = GameUITheme.Instance;

            // < button — text-only style with bumped font size for the arrow glyph.
            // Width 24 keeps the click region narrow so it visually hugs the value text.
            var prevBtn = theme.CloneButton(controls.transform, "<", () => Cycle(-1), textOnly: true, fontSize: 26f);
            var prevLe = prevBtn.gameObject.AddComponent<LayoutElement>();
            prevLe.preferredWidth = 24;
            prevLe.preferredHeight = 28;

            // Value display — flexible width so it fills the middle of the controls panel.
            // The < and > buttons are at fixed widths at the edges, so this is column-aligned.
            _valueText = GameUI.CreateText(controls.transform, "Value", getter().ToString(),
                UIStyle.FontSizeBody, alignment: TextAlignmentOptions.Center);
            var valLe = _valueText.gameObject.AddComponent<LayoutElement>();
            valLe.flexibleWidth = 1;

            // > button — text-only with same bumped font size
            var nextBtn = theme.CloneButton(controls.transform, ">", () => Cycle(1), textOnly: true, fontSize: 26f);
            var nextLe = nextBtn.gameObject.AddComponent<LayoutElement>();
            nextLe.preferredWidth = 24;
            nextLe.preferredHeight = 28;

            Root = row.gameObject;
        }

        private void Cycle(int dir)
        {
            int idx = Array.IndexOf(_values, _getter());
            idx = (idx + dir + _values.Length) % _values.Length;
            _setter(_values[idx]);
        }

        public void Update()
        {
            _valueText.text = _getter().ToString();
        }
    }

    public class WidgetDropdown<T> : IWidget where T : struct, Enum
    {
        public GameObject Root { get; }
        private readonly TMP_Dropdown _dropdown;
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;
        private readonly T[] _values;
        private bool _updating;

        public WidgetDropdown(RectTransform parent, string label, Func<T> getter, Action<T> setter)
        {
            _getter = getter;
            _setter = setter;
            _values = (T[])Enum.GetValues(typeof(T));

            Plugin.Log.LogInfo($"[WidgetDropdown] Creating '{label}' via theme.CloneDropdown");

            var row = GameUI.CreatePanel(parent.transform, "Dropdown_" + label, Color.clear);
            var layout = GameUI.AddHorizontalLayout(row, spacing: 6, padding: 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            GameUI.SetSize(row, height: 36);

            // Label - flexible, pushes the dropdown right
            var labelTmp = GameUI.CreateText(row.transform, "Label", label, UIStyle.FontSizeBody);
            var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1;
            labelLe.minWidth = 60;

            // Dropdown - fixed width to align across rows
            var theme = GameUITheme.Instance;
            _dropdown = theme.CloneDropdown(row.transform, label);
            if (_dropdown != null)
            {
                var ddLe = _dropdown.gameObject.AddComponent<LayoutElement>();
                ddLe.preferredWidth = 180;
                ddLe.preferredHeight = 28;

                // Populate options from enum names
                var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
                foreach (var v in _values)
                    options.Add(new TMP_Dropdown.OptionData(v.ToString()));
                _dropdown.AddOptions(options);

                // Set initial value
                int idx = Array.IndexOf(_values, getter());
                _dropdown.SetValueWithoutNotify(idx >= 0 ? idx : 0);
                _dropdown.RefreshShownValue();

                _dropdown.onValueChanged.AddListener(i =>
                {
                    if (_updating) return;
                    if (i >= 0 && i < _values.Length)
                        setter(_values[i]);
                });

                GameUITheme.DumpWidgetTreeDeep(_dropdown.gameObject, "Dropdown_" + label);
            }

            Root = row.gameObject;
        }

        public void Update()
        {
            if (_dropdown == null) return;
            _updating = true;
            int idx = Array.IndexOf(_values, _getter());
            if (idx >= 0 && _dropdown.value != idx)
            {
                _dropdown.SetValueWithoutNotify(idx);
                _dropdown.RefreshShownValue();
            }
            _updating = false;
        }
    }

    public class WidgetSpacer : IWidget
    {
        public GameObject Root { get; }

        public WidgetSpacer(RectTransform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            Root = go;
        }

        public void Update() { }
    }

    public class WidgetSeparator : IWidget
    {
        public GameObject Root { get; }

        public WidgetSeparator(RectTransform parent)
        {
            var go = new GameObject("Separator", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 9;

            var line = new GameObject("Line", typeof(RectTransform));
            line.transform.SetParent(go.transform, false);
            var lineRt = line.GetComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0, 0.5f);
            lineRt.anchorMax = new Vector2(1, 0.5f);
            lineRt.sizeDelta = new Vector2(0, 1);
            var img = line.AddComponent<Image>();
            img.color = UIStyle.Separator;

            Root = go;
        }

        public void Update() { }
    }

    public class WidgetDynamic : IWidget
    {
        public GameObject Root { get; }
        private readonly RectTransform _container;
        private readonly Action<WidgetPanel> _buildContent;
        private WidgetPanel _innerPanel;

        public WidgetDynamic(RectTransform parent, string id, Action<WidgetPanel> buildContent)
        {
            _buildContent = buildContent;

            var go = new GameObject("Dynamic_" + id, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            _container = go.GetComponent<RectTransform>();
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Root = go;
            Rebuild();
        }

        public void Rebuild()
        {
            _innerPanel?.Clear();
            _innerPanel = new WidgetPanel(_container);
            _buildContent(_innerPanel);
        }

        public void Update()
        {
            _innerPanel?.UpdateAll();
        }
    }
}
