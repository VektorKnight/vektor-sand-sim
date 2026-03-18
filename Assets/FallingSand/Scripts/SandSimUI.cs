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
        [SerializeField] private SandSimulationController _sim;

        [Header("Layout")]
        [SerializeField] private int _fontSize = 11;
        [SerializeField] private int _hudFontSize = 10;
        [SerializeField] private Vector2 _matCellSize = new(56, 24);
        [SerializeField] private int _matColumnsPerRow = 10;
        [SerializeField] private float _settingsPanelWidth = 260f;
        [SerializeField] private float _settingsLabelWidth = 100f;
        [SerializeField] private float _settingsRowHeight = 22f;
        [SerializeField] private int _settingsPadding = 8;
        [SerializeField] private int _settingsSpacing = 4;

        [Header("Icons")]
        [SerializeField] private Sprite _settingsIcon;
        [SerializeField] private Sprite _pauseIcon;
        [SerializeField] private Sprite _playIcon;

        [Header("Category Icons")]
        [SerializeField] private Sprite _iconSolids;
        [SerializeField] private Sprite _iconPowders;
        [SerializeField] private Sprite _iconLiquids;
        [SerializeField] private Sprite _iconGases;
        [SerializeField] private Sprite _iconEnergy;
        [SerializeField] private Sprite _iconLife;

        private Text _hudText;
        private Image _pauseButtonImage;
        private MaterialCategory _activeCategory = MaterialCategory.Solids;
        private readonly Dictionary<MaterialCategory, List<GameObject>> _matButtonsByCategory = new();
        private readonly Dictionary<MaterialCategory, Outline> _catOutlines = new();
        private Text _tooltipText;
        private readonly List<Outline> _matOutlines = new();
        private readonly List<int> _matOutlineIndices = new();
        private GameObject _settingsPanel;
        private bool _settingsOpen;

        // Color slider refs for live update.
        private Toggle _lightToggle, _bloomToggle;
        private Slider _lightAngleSlider, _lightIntensitySlider;
        private Slider _lightRSlider, _lightGSlider, _lightBSlider;
        private Slider _ambientRSlider, _ambientGSlider, _ambientBSlider;
        private Dropdown _lightQualityDropdown;

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
                _matOutlines[i].enabled = _matOutlineIndices[i] == _sim.SelectedMaterialIndex;
            }

            // Refresh HUD.
            if (_hudText) {
                var matName = _sim.Materials[_sim.SelectedMaterialIndex].Name;
                var mode = _sim.PaintReplace ? "Replace" : "Fill";
                var paused = _sim.Paused ? " | Paused" : "";
                _hudText.text = $"{_sim.FPS} FPS | Steps {_sim.EffectiveSteps}/{_sim.BaseSimSteps}{paused}\nSim {_sim.SimWidth}x{_sim.SimHeight} | Light {_sim.LightWidth}x{_sim.LightHeight}\n{matName} ({mode})";
            }
        }

        // ---- Material bar ----

        private void BuildMaterialBar() {
            var mats = _sim.Materials;
            var categories = (MaterialCategory[])Enum.GetValues(typeof(MaterialCategory));

            // Material row (bottom center) — single horizontal row, filtered by active category.
            var matPanel = CreatePanel(transform, "MaterialBar", new Color(0, 0, 0, 0.5f));
            var matPanelRT = matPanel.GetComponent<RectTransform>();
            matPanelRT.anchorMin = new Vector2(0f, 0f);
            matPanelRT.anchorMax = new Vector2(0f, 0f);
            matPanelRT.pivot = new Vector2(0f, 0f);
            matPanelRT.anchoredPosition = new Vector2(46, 8);

            var matGrid = matPanel.AddComponent<HorizontalLayoutGroup>();
            matGrid.spacing = 2;
            matGrid.padding = new RectOffset(4, 4, 4, 4);
            matGrid.childAlignment = TextAnchor.MiddleCenter;
            matGrid.childForceExpandWidth = false;
            matGrid.childForceExpandHeight = false;

            var matCSF = matPanel.AddComponent<ContentSizeFitter>();
            matCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            matCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Build material buttons for all categories (toggled visible per category).
            foreach (var cat in categories)
                _matButtonsByCategory[cat] = new List<GameObject>();

            for (var i = 1; i < mats.Count; i++) {
                var idx = i;
                var mat = mats[i];
                var btn = CreateButton(matPanel.transform, mat.Color, _matCellSize, () => {
                    _sim.SelectedMaterialIndex = idx;
                    SaveSettings();
                });

                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = _matCellSize.x;
                le.preferredHeight = _matCellSize.y;

                // Hover tooltip.
                if (!string.IsNullOrEmpty(mat.Description)) {
                    var trigger = btn.gameObject.AddComponent<EventTrigger>();
                    var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    enterEntry.callback.AddListener(_ => { if (_tooltipText) _tooltipText.text = mat.Description; });
                    trigger.triggers.Add(enterEntry);
                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener(_ => { if (_tooltipText) _tooltipText.text = ""; });
                    trigger.triggers.Add(exitEntry);
                }

                var displayName = string.IsNullOrEmpty(mat.Label) ? mat.Name : mat.Label;
                var label = CreateText(btn.transform, displayName, _fontSize - 1, TextAnchor.MiddleCenter);
                label.raycastTarget = false;
                label.color = GetLabelColor(mat.Color);
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
                _matOutlineIndices.Add(i);

                _matButtonsByCategory[mat.Category].Add(btn.gameObject);
            }

            // Category sidebar (left edge, vertically centered).
            var catPanel = CreatePanel(transform, "CategoryBar", new Color(0, 0, 0, 0.5f));
            var catPanelRT = catPanel.GetComponent<RectTransform>();
            catPanelRT.anchorMin = new Vector2(0f, 0f);
            catPanelRT.anchorMax = new Vector2(0f, 0f);
            catPanelRT.pivot = new Vector2(0f, 0f);
            catPanelRT.anchoredPosition = new Vector2(8, 8);

            var catLayout = catPanel.AddComponent<VerticalLayoutGroup>();
            catLayout.spacing = 2;
            catLayout.padding = new RectOffset(4, 4, 4, 4);
            catLayout.childAlignment = TextAnchor.MiddleCenter;
            catLayout.childForceExpandWidth = false;
            catLayout.childForceExpandHeight = false;

            var catCSF = catPanel.AddComponent<ContentSizeFitter>();
            catCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            catCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var catBtnSize = new Vector2(30, 30);
            foreach (var cat in categories) {
                var capturedCat = cat;
                var icon = GetCategoryIcon(cat);
                var catBtn = CreateIconButton(catPanel.transform, new Color(0.25f, 0.25f, 0.25f, 0.9f), catBtnSize, icon, cat.ToString(), () => {
                    SetActiveCategory(capturedCat);
                });

                var catLE = catBtn.gameObject.AddComponent<LayoutElement>();
                catLE.preferredWidth = catBtnSize.x;
                catLE.preferredHeight = catBtnSize.y;

                // Hover to preview category.
                var hoverTrigger = catBtn.gameObject.AddComponent<EventTrigger>();
                var hoverEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                hoverEntry.callback.AddListener(_ => SetActiveCategory(capturedCat));
                hoverTrigger.triggers.Add(hoverEntry);

                var outline = catBtn.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(1, -1);
                outline.enabled = false;
                _catOutlines[cat] = outline;
            }

            // Tooltip text sits just above the material bar.
            _tooltipText = CreateText(transform, "", _fontSize, TextAnchor.MiddleCenter);
            _tooltipText.raycastTarget = false;
            var tooltipRT = _tooltipText.GetComponent<RectTransform>();
            tooltipRT.anchorMin = new Vector2(0.5f, 0f);
            tooltipRT.anchorMax = new Vector2(0.5f, 0f);
            tooltipRT.pivot = new Vector2(0.5f, 0f);
            var barHeight = (int)_matCellSize.y + 8;
            tooltipRT.anchoredPosition = new Vector2(0, 8 + barHeight + 4);
            tooltipRT.sizeDelta = new Vector2(400, 20);

            // Show initial category.
            SetActiveCategory(_activeCategory);
        }

        private void SetActiveCategory(MaterialCategory cat) {
            _activeCategory = cat;
            foreach (var kvp in _matButtonsByCategory) {
                var visible = kvp.Key == cat;
                foreach (var go in kvp.Value)
                    go.SetActive(visible);
            }
            foreach (var kvp in _catOutlines)
                kvp.Value.enabled = kvp.Key == cat;
        }

        // ---- Settings panel ----

        private void BuildSettingsPanel() {
            var btnSize = new Vector2(30, 30);
            var btnColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Settings button.
            var settingsBtn = CreateIconButton(transform, btnColor, btnSize, _settingsIcon, "Settings", () => {
                _settingsOpen = !_settingsOpen;
                _settingsPanel.SetActive(_settingsOpen);
            });
            var settingsRT = settingsBtn.GetComponent<RectTransform>();
            settingsRT.anchorMin = new Vector2(1f, 1f);
            settingsRT.anchorMax = new Vector2(1f, 1f);
            settingsRT.pivot = new Vector2(1f, 1f);
            settingsRT.anchoredPosition = new Vector2(-8, -8);

            // Pause button (below settings).
            var pauseBtn = CreateIconButton(transform, btnColor, btnSize, _pauseIcon, "Pause", () => {
                _sim.Paused = !_sim.Paused;
                _pauseButtonImage.sprite = _sim.Paused ? _playIcon : _pauseIcon;
            });
            var pauseRT = pauseBtn.GetComponent<RectTransform>();
            pauseRT.anchorMin = new Vector2(1f, 1f);
            pauseRT.anchorMax = new Vector2(1f, 1f);
            pauseRT.pivot = new Vector2(1f, 1f);
            pauseRT.anchoredPosition = new Vector2(-8, -42);
            _pauseButtonImage = pauseBtn.transform.Find("Icon").GetComponent<Image>();

            // Panel.
            _settingsPanel = CreatePanel(transform, "SettingsPanel", new Color(0, 0, 0, 0.75f));
            var panelRT = _settingsPanel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(1f, 1f);
            panelRT.anchorMax = new Vector2(1f, 1f);
            panelRT.pivot = new Vector2(1f, 1f);
            panelRT.anchoredPosition = new Vector2(-8, -76);
            panelRT.sizeDelta = new Vector2(_settingsPanelWidth, 0);

            var vlg = _settingsPanel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = _settingsSpacing;
            vlg.padding = new RectOffset(_settingsPadding, _settingsPadding, _settingsPadding, _settingsPadding);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = _settingsPanel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Lighting toggle.
            _lightToggle = CreateToggle(_settingsPanel.transform, "Lighting", _sim.Lighting.LightEnabled, v => { _sim.Lighting.LightEnabled = v; SaveSettings(); });
            _bloomToggle = CreateToggle(_settingsPanel.transform, "Bloom", _sim.Lighting.BloomEnabled, v => { _sim.Lighting.BloomEnabled = v; SaveSettings(); });

            // Light Quality: Low=4, Medium=3, High=2.
            var qualityOptions = new[] { "Low", "Medium", "High" };
            var currentQuality = _sim.Lighting.LightDownscale switch { 4 => 0, 3 => 1, _ => 2 };
            _lightQualityDropdown = CreateDropdown(_settingsPanel.transform, "Light Quality", qualityOptions, currentQuality, idx => {
                _sim.Lighting.LightDownscale = idx switch { 0 => 4, 1 => 3, _ => 2 };
                _sim.RecreateLightTextures();
                SaveSettings();
            });

            // Light Angle.
            _lightAngleSlider = CreateSlider(_settingsPanel.transform, "Light Angle", 0f, 360f, _sim.Lighting.LightAngle, v => { _sim.Lighting.LightAngle = v; SaveSettings(); });

            // Light Intensity.
            _lightIntensitySlider = CreateSlider(_settingsPanel.transform, "Light Intensity", 0f, 5f, _sim.Lighting.LightIntensity, v => { _sim.Lighting.LightIntensity = v; SaveSettings(); });

            // Light Color (RGB).
            CreateText(_settingsPanel.transform, "Light Color", _fontSize, TextAnchor.MiddleLeft);
            var lc = _sim.Lighting.LightColor;
            _lightRSlider = CreateSlider(_settingsPanel.transform, "  R", 0f, 1f, lc.r, _ => { ApplyLightColor(); SaveSettings(); });
            _lightGSlider = CreateSlider(_settingsPanel.transform, "  G", 0f, 1f, lc.g, _ => { ApplyLightColor(); SaveSettings(); });
            _lightBSlider = CreateSlider(_settingsPanel.transform, "  B", 0f, 1f, lc.b, _ => { ApplyLightColor(); SaveSettings(); });

            // Ambient Color (RGB).
            CreateText(_settingsPanel.transform, "Ambient Color", _fontSize, TextAnchor.MiddleLeft);
            var ac = _sim.Lighting.AmbientColor;
            _ambientRSlider = CreateSlider(_settingsPanel.transform, "  R", 0f, 1f, ac.r, _ => { ApplyAmbientColor(); SaveSettings(); });
            _ambientGSlider = CreateSlider(_settingsPanel.transform, "  G", 0f, 1f, ac.g, _ => { ApplyAmbientColor(); SaveSettings(); });
            _ambientBSlider = CreateSlider(_settingsPanel.transform, "  B", 0f, 1f, ac.b, _ => { ApplyAmbientColor(); SaveSettings(); });

            // Reset lighting to defaults.
            var resetBtn = CreateButton(_settingsPanel.transform, new Color(0.3f, 0.15f, 0.15f), new Vector2(0, _settingsRowHeight), ResetLighting);
            var resetLE = resetBtn.gameObject.AddComponent<LayoutElement>();
            resetLE.preferredHeight = _settingsRowHeight;
            resetLE.flexibleWidth = 1;
            var resetLabel = CreateText(resetBtn.transform, "Reset Lighting", _fontSize, TextAnchor.MiddleCenter);
            resetLabel.raycastTarget = false;
            var resetLabelRT = resetLabel.GetComponent<RectTransform>();
            resetLabelRT.anchorMin = Vector2.zero;
            resetLabelRT.anchorMax = Vector2.one;
            resetLabelRT.offsetMin = Vector2.zero;
            resetLabelRT.offsetMax = Vector2.zero;

            // Resolution dropdown.
            CreateDropdown(_settingsPanel.transform, "Sim Resolution",
                new[] { "960x600", "1280x800", "1600x1000", "1920x1200", "2560x1600" },
                (int)_sim.Resolution,
                idx => { _sim.Resolution = (SandSimulationController.SimResolution)idx; SaveSettings(); });

            // Window Scale dropdown.
            CreateDropdown(_settingsPanel.transform, "Window Scale",
                new[] { "1x", "2x", "3x" },
                _sim.WindowScale - 1,
                idx => { _sim.WindowScale = idx + 1; SaveSettings(); });

            // Frame Rate Cap dropdown.
            CreateDropdown(_settingsPanel.transform, "Frame Rate",
                new[] { "VSync", "60 FPS" },
                (int)_sim.FrameCap,
                idx => { _sim.FrameCap = (SandSimulationController.FrameRateCap)idx; SaveSettings(); });

            // Clear simulation.
            var clearBtn = CreateButton(_settingsPanel.transform, new Color(0.3f, 0.15f, 0.15f), new Vector2(0, _settingsRowHeight), () => _sim.RecreateSimulation());
            var clearLE = clearBtn.gameObject.AddComponent<LayoutElement>();
            clearLE.preferredHeight = _settingsRowHeight;
            clearLE.flexibleWidth = 1;
            var clearLabel = CreateText(clearBtn.transform, "Clear Simulation", _fontSize, TextAnchor.MiddleCenter);
            clearLabel.raycastTarget = false;
            var clearLabelRT = clearLabel.GetComponent<RectTransform>();
            clearLabelRT.anchorMin = Vector2.zero;
            clearLabelRT.anchorMax = Vector2.one;
            clearLabelRT.offsetMin = Vector2.zero;
            clearLabelRT.offsetMax = Vector2.zero;

            _settingsPanel.SetActive(false);
        }

        private void ApplyLightColor() {
            _sim.Lighting.LightColor = new Color(_lightRSlider.value, _lightGSlider.value, _lightBSlider.value);
        }

        private void ApplyAmbientColor() {
            _sim.Lighting.AmbientColor = new Color(_ambientRSlider.value, _ambientGSlider.value, _ambientBSlider.value);
        }

        private void ResetLighting() {
            _sim.Lighting.ResetToDefaults();

            _lightToggle.isOn = _sim.Lighting.LightEnabled;
            _bloomToggle.isOn = _sim.Lighting.BloomEnabled;
            _lightAngleSlider.value = _sim.Lighting.LightAngle;
            _lightIntensitySlider.value = _sim.Lighting.LightIntensity;

            var lc = _sim.Lighting.LightColor;
            _lightRSlider.value = lc.r;
            _lightGSlider.value = lc.g;
            _lightBSlider.value = lc.b;

            var ac = _sim.Lighting.AmbientColor;
            _ambientRSlider.value = ac.r;
            _ambientGSlider.value = ac.g;
            _ambientBSlider.value = ac.b;

            _lightQualityDropdown.value = _sim.Lighting.LightDownscale switch { 4 => 0, 3 => 1, _ => 2 };
            _sim.RecreateLightTextures();

            SaveSettings();
        }

        // ---- HUD ----

        private void BuildHUD() {
            _hudText = CreateText(transform, "", _hudFontSize, TextAnchor.UpperLeft);
            var rt = _hudText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8, -8);
            rt.sizeDelta = new Vector2(300, 60);
            _hudText.raycastTarget = false;
        }

        // ---- JSON file persistence ----

        private const string SettingsFile = "settings.json";

        [Serializable]
        private class Settings {
            public bool lightEnabled = true;
            public bool bloomEnabled = true;
            public int lightDownscale = 3;
            public float lightAngle = 60f;
            public float lightIntensity = 2f;
            public float lightR = 1f, lightG = 1f, lightB = 1f;
            public float ambientR = 0.1f, ambientG = 0.1f, ambientB = 0.1f;
            public int resolution = (int)SandSimulationController.SimResolution.Res1600x1000;
            public int windowScale = 1;
            public int frameCap;
            public int paintRadius = 5;
            public int selectedMat = 1;
        }

        private static string GetSettingsPath() {
            var dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            return System.IO.Path.Combine(dir, SettingsFile);
        }

        private static bool ShouldPersist() {
#if UNITY_EDITOR
            return false;
#else
            return true;
#endif
        }

        private void LoadSettings() {
            if (!ShouldPersist()) return;
            var path = GetSettingsPath();
            if (!System.IO.File.Exists(path)) return;

            try {
                var json = System.IO.File.ReadAllText(path);
                var s = JsonUtility.FromJson<Settings>(json);

                _sim.Lighting.LightEnabled = s.lightEnabled;
                _sim.Lighting.BloomEnabled = s.bloomEnabled;
                _sim.Lighting.LightDownscale = Mathf.Clamp(s.lightDownscale, 2, 4);
                _sim.Lighting.LightAngle = Mathf.Clamp(s.lightAngle, 0f, 360f);
                _sim.Lighting.LightIntensity = Mathf.Clamp(s.lightIntensity, 0f, 5f);
                _sim.Lighting.LightColor = new Color(
                    Mathf.Clamp01(s.lightR), Mathf.Clamp01(s.lightG), Mathf.Clamp01(s.lightB));
                _sim.Lighting.AmbientColor = new Color(
                    Mathf.Clamp01(s.ambientR), Mathf.Clamp01(s.ambientG), Mathf.Clamp01(s.ambientB));
                _sim.Resolution = (SandSimulationController.SimResolution)Mathf.Clamp(s.resolution, 0, 4);
                _sim.WindowScale = s.windowScale; // Setter already clamps 1-3.
                _sim.FrameCap = (SandSimulationController.FrameRateCap)Mathf.Clamp(s.frameCap, 0, 1);
                _sim.PaintRadius = s.paintRadius; // Setter already clamps to min/max.
                _sim.SelectedMaterialIndex = s.selectedMat; // Setter already clamps to valid range.
            } catch (System.Exception e) {
                Debug.LogWarning($"Failed to load settings: {e.Message}");
            }
        }

        private void SaveSettings() {
            if (!ShouldPersist()) return;
            var lc = _sim.Lighting.LightColor;
            var ac = _sim.Lighting.AmbientColor;

            var s = new Settings {
                lightEnabled = _sim.Lighting.LightEnabled,
                bloomEnabled = _sim.Lighting.BloomEnabled,
                lightDownscale = _sim.Lighting.LightDownscale,
                lightAngle = _sim.Lighting.LightAngle,
                lightIntensity = _sim.Lighting.LightIntensity,
                lightR = lc.r, lightG = lc.g, lightB = lc.b,
                ambientR = ac.r, ambientG = ac.g, ambientB = ac.b,
                resolution = (int)_sim.Resolution,
                windowScale = _sim.WindowScale,
                frameCap = (int)_sim.FrameCap,
                paintRadius = _sim.PaintRadius,
                selectedMat = _sim.SelectedMaterialIndex,
            };

            try {
                System.IO.File.WriteAllText(GetSettingsPath(), JsonUtility.ToJson(s, true));
            } catch (System.Exception e) {
                Debug.LogWarning($"Failed to save settings: {e.Message}");
            }
        }

        // ---- Utilities ----

        private Sprite GetCategoryIcon(MaterialCategory cat) => cat switch {
            MaterialCategory.Solids  => _iconSolids,
            MaterialCategory.Powders => _iconPowders,
            MaterialCategory.Liquids => _iconLiquids,
            MaterialCategory.Gases   => _iconGases,
            MaterialCategory.Energy  => _iconEnergy,
            MaterialCategory.Life    => _iconLife,
            _ => null,
        };

        private static Color GetLabelColor(Color bg) {
            var lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.5f ? Color.black : Color.white;
        }

        // ---- Helper methods ----

        private Toggle CreateToggle(Transform parent, string label, bool value, Action<bool> onChange) {
            var row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = _settingsSpacing;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = _settingsRowHeight;
            le.flexibleWidth = 1;

            // Label.
            var lblText = CreateText(row.transform, label, _fontSize, TextAnchor.MiddleLeft);
            lblText.raycastTarget = false;
            var lblLE = lblText.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth = _settingsLabelWidth;
            lblLE.minWidth = _settingsLabelWidth;

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
            return toggle;
        }

        private static GameObject CreatePanel(Transform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return go;
        }

        private static Button CreateIconButton(Transform parent, Color bgColor, Vector2 size, Sprite icon, string name, Action onClick) {
            var btn = CreateButton(parent, bgColor, size, onClick);
            btn.gameObject.name = name;

            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGO.transform.SetParent(btn.transform, false);
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.15f, 0.15f);
            iconRT.anchorMax = new Vector2(0.85f, 0.85f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            var iconImg = iconGO.GetComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            return btn;
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

        private Slider CreateSlider(Transform parent, string label, float min, float max, float value, Action<float> onChange) {
            var row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = _settingsSpacing;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = _settingsRowHeight;
            le.flexibleWidth = 1;

            // Label.
            var lblText = CreateText(row.transform, label, _fontSize, TextAnchor.MiddleLeft);
            lblText.raycastTarget = false;
            var lblLE = lblText.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth = _settingsLabelWidth;
            lblLE.minWidth = _settingsLabelWidth;

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

        private Dropdown CreateDropdown(Transform parent, string label, string[] options, int value, Action<int> onChange) {
            var row = new GameObject(label + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = _settingsSpacing;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = _settingsRowHeight + 6;
            le.flexibleWidth = 1;

            // Label.
            var lblText = CreateText(row.transform, label, _fontSize, TextAnchor.MiddleLeft);
            lblText.raycastTarget = false;
            var lblLE = lblText.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth = _settingsLabelWidth;
            lblLE.minWidth = _settingsLabelWidth;

            // Dropdown.
            var ddGO = new GameObject("Dropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            ddGO.transform.SetParent(row.transform, false);
            var ddLE = ddGO.AddComponent<LayoutElement>();
            ddLE.flexibleWidth = 1;
            ddLE.preferredHeight = _settingsRowHeight;

            var ddImg = ddGO.GetComponent<Image>();
            ddImg.color = new Color(0.25f, 0.25f, 0.25f);

            var dd = ddGO.GetComponent<Dropdown>();

            // Caption text.
            var captionText = CreateText(ddGO.transform, "", _fontSize, TextAnchor.MiddleLeft);
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
            contentRT.sizeDelta = new Vector2(0, _settingsRowHeight);

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
            itemRT.sizeDelta = new Vector2(0, _settingsRowHeight);

            var itemBg = item.GetComponent<Image>();
            itemBg.color = new Color(0.25f, 0.25f, 0.25f);

            var itemText = CreateText(item.transform, "", _fontSize, TextAnchor.MiddleLeft);
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
            return dd;
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
