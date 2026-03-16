using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FallingSand.Scripts {
    /// <summary>
    /// Programmatic uGUI for the falling-sand simulation.
    /// Attach to the Canvas and assign the SandSimulation reference.
    ///
    /// Claude largely handled this. I hate UI. Thanks, Claude!
    /// </summary>
    public class SandSimUI : MonoBehaviour {
        [SerializeField] private SandSimulation _sim;

        private Text _hudText;
        private Text _tooltipText;
        private readonly List<Outline> _matOutlines = new();
        private GameObject _settingsPanel;
        private bool _settingsOpen;

        // Color slider refs for live update.
        private Slider _lightRSlider, _lightGSlider, _lightBSlider;
        private Slider _ambientRSlider, _ambientGSlider, _ambientBSlider;

        private void Start() {
            // Disable raycastTarget on all pre-existing RawImages (background, sim view)
            // so they don't block IsPointerOverGameObject for the sim's click guard.
            foreach (var rawImg in GetComponentsInChildren<RawImage>())
                rawImg.raycastTarget = false;

            LoadSettings();
            BuildMaterialBar();
            BuildSettingsPanel();
            BuildHUD();
        }

        private void OnApplicationQuit() => SaveSettings();

        private void Update() {
            // Refresh material highlight.
            for (var i = 0; i < _matOutlines.Count; i++) {
                _matOutlines[i].enabled = (i + 1) == _sim.SelectedMaterialIndex;
            }

            // Refresh HUD.
            if (_hudText) {
                var matName = _sim.Materials[_sim.SelectedMaterialIndex].Name;
                var mode = _sim.PaintReplace ? "Replace" : "Fill";
                _hudText.text = $"{_sim.FPS} FPS | Steps {_sim.EffectiveSteps}/{_sim.BaseSimSteps}\nSim {_sim.SimWidth}x{_sim.SimHeight} | Light {_sim.LightWidth}x{_sim.LightHeight}\n{matName} ({mode})";
            }
        }

        // ---- Material bar ----

        private const int MaterialsPerRow = 10;

        private void BuildMaterialBar() {
            var panel = CreatePanel(transform, "MaterialBar", new Color(0, 0, 0, 0.5f));
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 8);

            var grid = panel.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(56, 24);
            grid.spacing = new Vector2(2, 2);
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = MaterialsPerRow;
            grid.childAlignment = TextAnchor.MiddleCenter;

            var csf = panel.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var mats = _sim.Materials;
            for (var i = 1; i < mats.Count; i++) {
                var idx = i;
                var desc = mats[i].Description;
                var btn = CreateButton(panel.transform, mats[i].Color, new Vector2(56, 24), () => {
                    _sim.SelectedMaterialIndex = idx;
                    SaveSettings();
                });

                // Hover tooltip.
                if (!string.IsNullOrEmpty(desc)) {
                    var trigger = btn.gameObject.AddComponent<EventTrigger>();
                    var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    enterEntry.callback.AddListener(_ => { if (_tooltipText) _tooltipText.text = desc; });
                    trigger.triggers.Add(enterEntry);
                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener(_ => { if (_tooltipText) _tooltipText.text = ""; });
                    trigger.triggers.Add(exitEntry);
                }

                var label = CreateText(btn.transform, mats[i].Name, 10, TextAnchor.MiddleCenter);
                label.raycastTarget = false;
                label.color = GetLabelColor(mats[i].Color);
                var labelRT = label.GetComponent<RectTransform>();
                labelRT.anchorMin = Vector2.zero;
                labelRT.anchorMax = Vector2.one;
                labelRT.offsetMin = Vector2.zero;
                labelRT.offsetMax = Vector2.zero;

                var outline = btn.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(1, -1);
                outline.enabled = false;
                _matOutlines.Add(outline);
            }

            // Tooltip text sits just above the material bar.
            _tooltipText = CreateText(transform, "", 11, TextAnchor.MiddleCenter);
            _tooltipText.raycastTarget = false;
            var tooltipRT = _tooltipText.GetComponent<RectTransform>();
            tooltipRT.anchorMin = new Vector2(0.5f, 0f);
            tooltipRT.anchorMax = new Vector2(0.5f, 0f);
            tooltipRT.pivot = new Vector2(0.5f, 0f);
            // Two rows + padding + gap.
            var barHeight = Mathf.CeilToInt((float)(mats.Count - 1) / MaterialsPerRow) * 26 + 8;
            tooltipRT.anchoredPosition = new Vector2(0, 8 + barHeight + 4);
            tooltipRT.sizeDelta = new Vector2(400, 20);
        }

        // ---- Settings panel ----

        private void BuildSettingsPanel() {
            // Toggle button.
            var toggleBtn = CreateButton(transform, new Color(0.2f, 0.2f, 0.2f, 0.8f), new Vector2(90, 30), () => {
                _settingsOpen = !_settingsOpen;
                _settingsPanel.SetActive(_settingsOpen);
            });
            var toggleRT = toggleBtn.GetComponent<RectTransform>();
            toggleRT.anchorMin = new Vector2(1f, 1f);
            toggleRT.anchorMax = new Vector2(1f, 1f);
            toggleRT.pivot = new Vector2(1f, 1f);
            toggleRT.anchoredPosition = new Vector2(-8, -8);
            var toggleText = CreateText(toggleBtn.transform, "Settings", 12, TextAnchor.MiddleCenter);
            var toggleTextRT = toggleText.GetComponent<RectTransform>();
            toggleTextRT.anchorMin = Vector2.zero;
            toggleTextRT.anchorMax = Vector2.one;
            toggleTextRT.offsetMin = Vector2.zero;
            toggleTextRT.offsetMax = Vector2.zero;

            // Panel.
            _settingsPanel = CreatePanel(transform, "SettingsPanel", new Color(0, 0, 0, 0.75f));
            var panelRT = _settingsPanel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(1f, 1f);
            panelRT.anchorMax = new Vector2(1f, 1f);
            panelRT.pivot = new Vector2(1f, 1f);
            panelRT.anchoredPosition = new Vector2(-8, -44);
            panelRT.sizeDelta = new Vector2(260, 0);

            var vlg = _settingsPanel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = _settingsPanel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Lighting toggle.
            CreateToggle(_settingsPanel.transform, "Lighting", _sim.Lighting.LightEnabled, v => { _sim.Lighting.LightEnabled = v; SaveSettings(); });

            // Light Angle.
            CreateSlider(_settingsPanel.transform, "Light Angle", 0f, 360f, _sim.Lighting.LightAngle, v => { _sim.Lighting.LightAngle = v; SaveSettings(); });

            // Light Intensity.
            CreateSlider(_settingsPanel.transform, "Light Intensity", 0f, 5f, _sim.Lighting.LightIntensity, v => { _sim.Lighting.LightIntensity = v; SaveSettings(); });

            // Light Color (RGB).
            CreateText(_settingsPanel.transform, "Light Color", 11, TextAnchor.MiddleLeft);
            var lc = _sim.Lighting.LightColor;
            _lightRSlider = CreateSlider(_settingsPanel.transform, "  R", 0f, 1f, lc.r, _ => { ApplyLightColor(); SaveSettings(); });
            _lightGSlider = CreateSlider(_settingsPanel.transform, "  G", 0f, 1f, lc.g, _ => { ApplyLightColor(); SaveSettings(); });
            _lightBSlider = CreateSlider(_settingsPanel.transform, "  B", 0f, 1f, lc.b, _ => { ApplyLightColor(); SaveSettings(); });

            // Ambient Color (RGB).
            CreateText(_settingsPanel.transform, "Ambient Color", 11, TextAnchor.MiddleLeft);
            var ac = _sim.Lighting.AmbientColor;
            _ambientRSlider = CreateSlider(_settingsPanel.transform, "  R", 0f, 1f, ac.r, _ => { ApplyAmbientColor(); SaveSettings(); });
            _ambientGSlider = CreateSlider(_settingsPanel.transform, "  G", 0f, 1f, ac.g, _ => { ApplyAmbientColor(); SaveSettings(); });
            _ambientBSlider = CreateSlider(_settingsPanel.transform, "  B", 0f, 1f, ac.b, _ => { ApplyAmbientColor(); SaveSettings(); });

            // Resolution dropdown.
            CreateDropdown(_settingsPanel.transform, "Sim Resolution",
                new[] { "960x540", "1280x720", "1600x900", "1920x1080", "2560x1440" },
                (int)_sim.Resolution,
                idx => { _sim.Resolution = (SimResolution)idx; SaveSettings(); });

            // Window Scale dropdown.
            CreateDropdown(_settingsPanel.transform, "Window Scale",
                new[] { "1x", "2x", "3x" },
                _sim.WindowScale - 1,
                idx => { _sim.WindowScale = idx + 1; SaveSettings(); });

            // Frame Rate Cap dropdown.
            CreateDropdown(_settingsPanel.transform, "Frame Rate",
                new[] { "VSync", "60 FPS" },
                (int)_sim.FrameCap,
                idx => { _sim.FrameCap = (FrameRateCap)idx; SaveSettings(); });

            _settingsPanel.SetActive(false);
        }

        private void ApplyLightColor() {
            _sim.Lighting.LightColor = new Color(_lightRSlider.value, _lightGSlider.value, _lightBSlider.value);
        }

        private void ApplyAmbientColor() {
            _sim.Lighting.AmbientColor = new Color(_ambientRSlider.value, _ambientGSlider.value, _ambientBSlider.value);
        }

        // ---- HUD ----

        private void BuildHUD() {
            _hudText = CreateText(transform, "", 10, TextAnchor.UpperLeft);
            var rt = _hudText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8, -8);
            rt.sizeDelta = new Vector2(300, 60);
            _hudText.raycastTarget = false;
        }

        // ---- PlayerPrefs persistence ----

        private const string PK = "sand_";

        private void LoadSettings() {
            if (!PlayerPrefs.HasKey(PK + "saved")) return;

            _sim.Lighting.LightEnabled = PlayerPrefs.GetInt(PK + "lightEnabled", 1) != 0;
            _sim.Lighting.LightAngle = PlayerPrefs.GetFloat(PK + "lightAngle", 60f);
            _sim.Lighting.LightIntensity = PlayerPrefs.GetFloat(PK + "lightIntensity", 2f);
            _sim.Lighting.LightColor = new Color(
                PlayerPrefs.GetFloat(PK + "lightR", 1f),
                PlayerPrefs.GetFloat(PK + "lightG", 1f),
                PlayerPrefs.GetFloat(PK + "lightB", 1f));
            _sim.Lighting.AmbientColor = new Color(
                PlayerPrefs.GetFloat(PK + "ambientR", 0.1f),
                PlayerPrefs.GetFloat(PK + "ambientG", 0.1f),
                PlayerPrefs.GetFloat(PK + "ambientB", 0.1f));
            _sim.Resolution = (SimResolution)PlayerPrefs.GetInt(PK + "resolution", (int)SimResolution.Res1920x1080);
            _sim.WindowScale = PlayerPrefs.GetInt(PK + "windowScale", 1);
            _sim.FrameCap = (FrameRateCap)PlayerPrefs.GetInt(PK + "frameCap", 0);
            _sim.PaintRadius = PlayerPrefs.GetInt(PK + "paintRadius", 5);
            _sim.SelectedMaterialIndex = PlayerPrefs.GetInt(PK + "selectedMat", 0);
        }

        private void SaveSettings() {
            PlayerPrefs.SetInt(PK + "saved", 1);
            PlayerPrefs.SetInt(PK + "lightEnabled", _sim.Lighting.LightEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(PK + "lightAngle", _sim.Lighting.LightAngle);
            PlayerPrefs.SetFloat(PK + "lightIntensity", _sim.Lighting.LightIntensity);
            var lc = _sim.Lighting.LightColor;
            PlayerPrefs.SetFloat(PK + "lightR", lc.r);
            PlayerPrefs.SetFloat(PK + "lightG", lc.g);
            PlayerPrefs.SetFloat(PK + "lightB", lc.b);
            var ac = _sim.Lighting.AmbientColor;
            PlayerPrefs.SetFloat(PK + "ambientR", ac.r);
            PlayerPrefs.SetFloat(PK + "ambientG", ac.g);
            PlayerPrefs.SetFloat(PK + "ambientB", ac.b);
            PlayerPrefs.SetInt(PK + "resolution", (int)_sim.Resolution);
            PlayerPrefs.SetInt(PK + "windowScale", _sim.WindowScale);
            PlayerPrefs.SetInt(PK + "frameCap", (int)_sim.FrameCap);
            PlayerPrefs.SetInt(PK + "paintRadius", _sim.PaintRadius);
            PlayerPrefs.SetInt(PK + "selectedMat", _sim.SelectedMaterialIndex);
            PlayerPrefs.Save();
        }

        // ---- Utilities ----

        private static Color GetLabelColor(Color bg) {
            var lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.5f ? Color.black : Color.white;
        }

        // ---- Helper methods ----

        private static void CreateToggle(Transform parent, string label, bool value, Action<bool> onChange) {
            var row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 22;
            le.flexibleWidth = 1;

            // Label.
            var lblText = CreateText(row.transform, label, 11, TextAnchor.MiddleLeft);
            lblText.raycastTarget = false;
            var lblLE = lblText.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth = 100;
            lblLE.minWidth = 100;

            // Checkbox.
            var toggleGO = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            toggleGO.transform.SetParent(row.transform, false);
            var toggleLE = toggleGO.AddComponent<LayoutElement>();
            toggleLE.preferredWidth = 18;
            toggleLE.preferredHeight = 18;

            // Background box.
            var bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(toggleGO.transform, false);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);

            // Checkmark.
            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            check.transform.SetParent(bg.transform, false);
            var checkRT = check.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;
            check.GetComponent<Image>().color = Color.white;

            var toggle = toggleGO.GetComponent<Toggle>();
            toggle.targetGraphic = bg.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => onChange(v));
        }


        private static GameObject CreatePanel(Transform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return go;
        }

        private static Button CreateButton(Transform parent, Color color, Vector2 size, Action onClick) {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = color;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static Slider CreateSlider(Transform parent, string label, float min, float max, float value, Action<float> onChange) {
            var row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 22;
            le.flexibleWidth = 1;

            // Label.
            var lblText = CreateText(row.transform, label, 11, TextAnchor.MiddleLeft);
            lblText.raycastTarget = false;
            var lblLE = lblText.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth = 100;
            lblLE.minWidth = 100;

            // Slider.
            var sliderGO = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGO.transform.SetParent(row.transform, false);
            var sliderLE = sliderGO.AddComponent<LayoutElement>();
            sliderLE.flexibleWidth = 1;
            sliderLE.preferredHeight = 18;

            var slider = sliderGO.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            // Background.
            var bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(sliderGO.transform, false);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.25f);
            bgRT.anchorMax = new Vector2(1, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);

            // Fill area.
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGO.transform, false);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = Vector2.zero;
            faRT.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(0.6f, 0.6f, 0.6f);
            slider.fillRect = fillRT;

            // Handle slide area.
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGO.transform, false);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(5, 0);
            haRT.offsetMax = new Vector2(-5, 0);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(10, 0);
            handle.GetComponent<Image>().color = Color.white;
            slider.handleRect = handleRT;
            slider.targetGraphic = handle.GetComponent<Image>();

            slider.onValueChanged.AddListener(v => onChange(v));

            return slider;
        }

        private static void CreateDropdown(Transform parent, string label, string[] options, int value, Action<int> onChange) {
            var row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 28;
            le.flexibleWidth = 1;

            // Label.
            var lblText = CreateText(row.transform, label, 11, TextAnchor.MiddleLeft);
            lblText.raycastTarget = false;
            var lblLE = lblText.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth = 100;
            lblLE.minWidth = 100;

            // Dropdown.
            var ddGO = new GameObject("Dropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            ddGO.transform.SetParent(row.transform, false);
            var ddLE = ddGO.AddComponent<LayoutElement>();
            ddLE.flexibleWidth = 1;
            ddLE.preferredHeight = 24;

            var ddImg = ddGO.GetComponent<Image>();
            ddImg.color = new Color(0.25f, 0.25f, 0.25f);

            var dd = ddGO.GetComponent<Dropdown>();

            // Caption text.
            var captionText = CreateText(ddGO.transform, "", 11, TextAnchor.MiddleLeft);
            var captionRT = captionText.GetComponent<RectTransform>();
            captionRT.anchorMin = Vector2.zero;
            captionRT.anchorMax = Vector2.one;
            captionRT.offsetMin = new Vector2(6, 0);
            captionRT.offsetMax = new Vector2(-20, 0);
            dd.captionText = captionText;

            // Template.
            var template = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(ddGO.transform, false);
            var templateRT = template.GetComponent<RectTransform>();
            templateRT.anchorMin = new Vector2(0, 0);
            templateRT.anchorMax = new Vector2(1, 0);
            templateRT.pivot = new Vector2(0.5f, 1f);
            templateRT.anchoredPosition = Vector2.zero;
            templateRT.sizeDelta = new Vector2(0, 150);
            template.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = Vector2.one;
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 24);

            var scroll = template.GetComponent<ScrollRect>();
            scroll.content = contentRT;
            scroll.viewport = vpRT;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Item template.
            var item = new GameObject("Item", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
            item.transform.SetParent(content.transform, false);
            var itemRT = item.GetComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0, 0.5f);
            itemRT.anchorMax = new Vector2(1, 0.5f);
            itemRT.sizeDelta = new Vector2(0, 24);

            var itemBg = item.GetComponent<Image>();
            itemBg.color = new Color(0.25f, 0.25f, 0.25f);

            var itemText = CreateText(item.transform, "", 11, TextAnchor.MiddleLeft);
            var itemTextRT = itemText.GetComponent<RectTransform>();
            itemTextRT.anchorMin = Vector2.zero;
            itemTextRT.anchorMax = Vector2.one;
            itemTextRT.offsetMin = new Vector2(6, 0);
            itemTextRT.offsetMax = new Vector2(-6, 0);

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBg;
            toggle.isOn = false;

            dd.template = templateRT;
            dd.itemText = itemText;

            template.SetActive(false);

            // Populate options.
            dd.ClearOptions();
            var optList = new List<Dropdown.OptionData>();
            foreach (var opt in options)
                optList.Add(new Dropdown.OptionData(opt));
            dd.AddOptions(optList);
            dd.value = value;
            dd.RefreshShownValue();

            dd.onValueChanged.AddListener(idx => onChange(idx));
        }

        private static Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment) {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
