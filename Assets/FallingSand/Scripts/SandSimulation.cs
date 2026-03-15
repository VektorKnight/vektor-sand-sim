using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;
using FallingSand.Scripts;

namespace FallingSand {
    public enum SimResolution { Res960x540, Res1920x1080, Res2560x1440 }

    /// <summary>
    /// Drives the falling-sand compute simulation and handles user input.
    ///
    /// Controls:
    ///   Left click        – paint selected material
    ///   Right click       – erase
    ///   Scroll wheel      – change brush radius
    ///   Shift + scroll    – cycle material
    ///   Insert            – toggle replacement paint mode
    /// </summary>
    public class SandSimulation : MonoBehaviour {
        [Header("Painting")]
        [SerializeField] private int _paintRadius = 5;
        [SerializeField] private int _paintRadiusMin = 1;
        [SerializeField] private int _paintRadiusMax = 50;

        [Header("Materials")]
        [SerializeField] private MaterialDefinition[] _materials = {
            new() { Name = "Stone", Fluidity = 0,  Density = 255, Weight = 0,    Drag = 0,   Color = new Color(0.5f, 0.5f, 0.5f) },
            new() { Name = "Sand",  Fluidity = 0,  Density = 200, Weight = 256,  Drag = 16,  Color = new Color(0.9f, 0.8f, 0.5f) },
            new() { Name = "Water", Fluidity = 64, Density = 100, Weight = 256,  Drag = 32,  Color = new Color(0.2f, 0.4f, 0.9f) },
        };

        [Header("Simulation")]
        [SerializeField] private int _gravity = 16;
        [SerializeField] private SimResolution _simResolution = SimResolution.Res1920x1080;
        [SerializeField] [Range(1, 3)] private int _windowScale = 1;
        [SerializeField] private int _simSteps = 256;
        [SerializeField] private ComputeShader _compute;

        [Header("Visualization")]
        [SerializeField] private RawImage _tempVis;
        [SerializeField] [Range(0f, 360f)] private float _lightAngle = 60f;
        [SerializeField] private Color _lightColor = Color.white;
        [SerializeField] [Range(0f, 5f)] private float _lightIntensity = 2f;
        [SerializeField] private Color _ambientColor = new(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] [Range(16, 512)] private int _lightMaxSteps = 64;
        [SerializeField] [Range(1, 4)] private int _lightDownscale = 2;

        // The empty material is always index 0 in the GPU buffer.
        // Inspector materials follow starting at index 1.
        private static readonly MaterialProperties EmptyMaterial = new(0, 0, 0, 0, 0f, 0f, default);

        // Paint state.
        private int _selectedIndex;
        private bool _paintReplace;
        private Vector2Int _lastPaintPos;
        private bool _wasPainting;

        // FPS and sim timing.
        private float _fpsTimer;
        private int _fpsCounter;
        private int _fpsDisplay;

        // Resources.
        private int _simFrame;
        private ComputeBuffer _simData;
        private ComputeBuffer _materialBuffer;
        private RenderTexture _visTexture;
        private RenderTexture _lightTexture;

        // Deferred recreation flag.
        private bool _pendingRecreate;

        // Kernel handles.
        private int KERNEL_PAINT;
        private int KERNEL_PREPARE_FRAME;
        private int KERNEL_SIM;
        private int KERNEL_LIGHT;
        private int KERNEL_VIS;

        // Shader property IDs.
        private static readonly int ID_PAINT_X       = Shader.PropertyToID("_PaintX");
        private static readonly int ID_PAINT_Y       = Shader.PropertyToID("_PaintY");
        private static readonly int ID_PAINT_R       = Shader.PropertyToID("_PaintR");
        private static readonly int ID_PAINT_TYPE    = Shader.PropertyToID("_PaintType");
        private static readonly int ID_PAINT_REPLACE = Shader.PropertyToID("_PaintReplace");

        private static readonly int ID_CURSOR_X      = Shader.PropertyToID("_CursorX");
        private static readonly int ID_CURSOR_Y      = Shader.PropertyToID("_CursorY");
        private static readonly int ID_CURSOR_R      = Shader.PropertyToID("_CursorR");
        private static readonly int ID_CURSOR_MAT    = Shader.PropertyToID("_CursorMat");

        private static readonly int ID_SIM_WIDTH     = Shader.PropertyToID("_SimWidth");
        private static readonly int ID_SIM_HEIGHT    = Shader.PropertyToID("_SimHeight");
        private static readonly int ID_SIM_STEPS     = Shader.PropertyToID("_SimSteps");
        private static readonly int ID_SIM_STEP      = Shader.PropertyToID("_SimStep");
        private static readonly int ID_SIM_FRAME     = Shader.PropertyToID("_SimFrame");
        private static readonly int ID_SIM_PASS      = Shader.PropertyToID("_SimPass");
        private static readonly int ID_GRAVITY       = Shader.PropertyToID("_Gravity");
        private static readonly int ID_MATERIALS      = Shader.PropertyToID("_Materials");
        private static readonly int ID_SIM_DATA      = Shader.PropertyToID("_SimData");
        private static readonly int ID_VIS_TEXTURE      = Shader.PropertyToID("_VisTexture");
        private static readonly int ID_LIGHT_TEXTURE     = Shader.PropertyToID("_LightTexture");
        private static readonly int ID_LIGHT_TEXTURE_READ = Shader.PropertyToID("_LightTextureRead");

        private static readonly int ID_LIGHT_DIR_X   = Shader.PropertyToID("_LightDirX");
        private static readonly int ID_LIGHT_DIR_Y   = Shader.PropertyToID("_LightDirY");
        private static readonly int ID_LIGHT_COLOR   = Shader.PropertyToID("_LightColor");
        private static readonly int ID_AMBIENT_COLOR = Shader.PropertyToID("_AmbientColor");
        private static readonly int ID_LIGHT_MAX_STEPS = Shader.PropertyToID("_LightMaxSteps");
        private static readonly int ID_LIGHT_DOWNSCALE = Shader.PropertyToID("_LightDownscale");

        // --- Public API ---

        public MaterialDefinition[] Materials => _materials;

        public int SelectedMaterialIndex {
            get => _selectedIndex;
            set => _selectedIndex = Mathf.Clamp(value, 0, _materials.Length - 1);
        }

        public float LightAngle {
            get => _lightAngle;
            set => _lightAngle = value;
        }

        public float LightIntensity {
            get => _lightIntensity;
            set => _lightIntensity = value;
        }

        public Color LightColor {
            get => _lightColor;
            set => _lightColor = value;
        }

        public Color AmbientColor {
            get => _ambientColor;
            set => _ambientColor = value;
        }

        public int LightMaxSteps {
            get => _lightMaxSteps;
            set => _lightMaxSteps = value;
        }

        public int PaintRadius {
            get => _paintRadius;
            set => _paintRadius = Mathf.Clamp(value, _paintRadiusMin, _paintRadiusMax);
        }

        public bool PaintReplace {
            get => _paintReplace;
            set => _paintReplace = value;
        }

        public int SimWidth => _visTexture ? _visTexture.width : 0;
        public int SimHeight => _visTexture ? _visTexture.height : 0;
        public int LightWidth => _lightTexture ? _lightTexture.width : 0;
        public int LightHeight => _lightTexture ? _lightTexture.height : 0;
        public int FPS => _fpsDisplay;

        public SimResolution Resolution {
            get => _simResolution;
            set { _simResolution = value; _pendingRecreate = true; }
        }

        public int WindowScale {
            get => _windowScale;
            set { _windowScale = Mathf.Clamp(value, 1, 3); _pendingRecreate = true; }
        }

        // --- Resolution helpers ---

        private Vector2Int GetSimDimensions() => _simResolution switch {
            SimResolution.Res960x540   => new(960, 540),
            SimResolution.Res1920x1080 => new(1920, 1080),
            SimResolution.Res2560x1440 => new(2560, 1440),
            _ => new(1920, 1080)
        };

        public void RecreateSimulation() {
            if (_simData != null && _simData.IsValid()) _simData.Release();
            if (_visTexture) _visTexture.Release();
            if (_lightTexture) _lightTexture.Release();

            var dims = GetSimDimensions();
            var simWidth = dims.x;
            var simHeight = dims.y;

            _simData = new ComputeBuffer(simWidth * simHeight, sizeof(int));

            _visTexture = new RenderTexture(simWidth, simHeight, 0, RenderTextureFormat.ARGB32) {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                useMipMap = false
            };
            _visTexture.Create();
            _tempVis.texture = _visTexture;

            _lightTexture = new RenderTexture(simWidth / _lightDownscale, simHeight / _lightDownscale, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _lightTexture.Create();

            Screen.SetResolution(simWidth * _windowScale, simHeight * _windowScale, FullScreenMode.Windowed);
            _simFrame = 0;
        }

        // --- Lifecycle ---

        private void Start() {
            KERNEL_PAINT   = _compute.FindKernel("Paint");
            KERNEL_PREPARE_FRAME = _compute.FindKernel("PrepareFrame");
            KERNEL_SIM     = _compute.FindKernel("Simulate");
            KERNEL_LIGHT   = _compute.FindKernel("Light");
            KERNEL_VIS     = _compute.FindKernel("Visualize");

            UploadMaterials();
            RecreateSimulation();

            _tempVis.raycastTarget = false;
        }

        private void OnDestroy() {
            if (_simData.IsValid()) _simData.Release();
            if (_materialBuffer != null && _materialBuffer.IsValid()) _materialBuffer.Release();
            if (_visTexture) _visTexture.Release();
            if (_lightTexture) _lightTexture.Release();
        }

        /// <summary>
        /// All input handling runs in Update for per-render-frame mouse sampling.
        /// Paint strokes interpolate between frames to avoid gaps at high speed.
        /// </summary>
        private void Update() {
            if (!_compute || !_simData.IsValid()) return;

            // FPS counter (always runs regardless of UI focus).
            _fpsCounter++;
            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1f) {
                _fpsDisplay = _fpsCounter;
                _fpsCounter = 0;
                _fpsTimer -= 1f;
            }

            // Skip all sim input when pointer is over UI.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) {
                _wasPainting = false;
                return;
            }

            // macOS translates shift+scroll from vertical to horizontal at the OS level.
            var delta = Input.mouseScrollDelta;
            var scroll = delta.y != 0 ? delta.y : delta.x;

            if (scroll != 0) {
                var shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (shiftHeld) {
                    var count = _materials.Length;
                    _selectedIndex = ((_selectedIndex + (scroll > 0 ? 1 : -1)) % count + count) % count;
                } else {
                    _paintRadius += scroll > 0 ? 1 : -1;
                    _paintRadius = Mathf.Clamp(_paintRadius, _paintRadiusMin, _paintRadiusMax);
                }
            }

            if (Input.GetKeyDown(KeyCode.Insert)) {
                _paintReplace = !_paintReplace;
            }

            // Paint or erase with line interpolation for fast mouse movement.
            var painting = Input.GetKey(KeyCode.Mouse0);
            var erasing  = Input.GetKey(KeyCode.Mouse1);

            if (painting || erasing) {
                var mousePos = Input.mousePosition;
                var pos = new Vector2Int(
                    Mathf.FloorToInt(mousePos.x * _visTexture.width / Screen.width),
                    Mathf.FloorToInt(mousePos.y * _visTexture.height / Screen.height)
                );
                var mat = erasing ? 0 : _selectedIndex + 1;

                if (_wasPainting) {
                    PaintLine(_lastPaintPos, pos, _paintRadius, mat);
                } else {
                    Paint(pos.x, pos.y, _paintRadius, mat);
                }

                _lastPaintPos = pos;
            }

            _wasPainting = painting || erasing;
        }

        /// <summary>
        /// Run simulation at fixed timestep.
        /// </summary>
        private void FixedUpdate() {
            if (!_compute || !_simData.IsValid()) {
                return;
            }

            if (_pendingRecreate) {
                _pendingRecreate = false;
                RecreateSimulation();
            }

            Profiler.BeginSample("Falling Sand Simulation");

            // Set common uniforms.
            _compute.SetInt(ID_SIM_WIDTH, _visTexture.width);
            _compute.SetInt(ID_SIM_HEIGHT, _visTexture.height);
            _compute.SetInt(ID_SIM_STEPS, _simSteps);
            _compute.SetInt(ID_SIM_FRAME, _simFrame);
            _compute.SetInt(ID_GRAVITY, _gravity);

            PrepareFrame();

            for (var i = 0; i < _simSteps; i++) {
                // Four-color tiling: rotate pass order each step to prevent
                // systematic priority bias between the four quadrants.
                var offset = (_simFrame * _simSteps + i) & 3;
                for (var p = 0; p < 4; p++)
                    Simulate((p + offset) & 3, i);
            }

            // Light pass at half resolution, then composite in Visualize.
            ComputeLight();

            var cursorPos = Input.mousePosition;
            var cursorX = Mathf.FloorToInt(cursorPos.x * _visTexture.width / Screen.width);
            var cursorY = Mathf.FloorToInt(cursorPos.y * _visTexture.height / Screen.height);
            Visualize(cursorX, cursorY);

            _simFrame++;

            Profiler.EndSample();
        }

        /// <summary>
        /// Stamp brush circles along a line from <paramref name="from"/> to
        /// <paramref name="to"/>, spaced at half-radius intervals so they overlap.
        /// </summary>
        private void PaintLine(Vector2Int from, Vector2Int to, int r, int mat) {
            var dx = to.x - from.x;
            var dy = to.y - from.y;
            var dist = Mathf.Sqrt(dx * dx + dy * dy);
            var spacing = Mathf.Max(1, r / 2);
            var steps = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));

            for (var i = 0; i <= steps; i++) {
                var t = (float)i / steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
                var y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
                Paint(x, y, r, mat);
            }
        }

        private void Paint(int x, int y, int r, int mat) {
            var diameter = 2 * r + 1;
            var groupsX = Mathf.CeilToInt((float)diameter / 8);
            var groupsY = Mathf.CeilToInt((float)diameter / 8);

            // Sim dimensions are needed for the in-bounds check in the kernel.
            // Paint can run from Update() before the first FixedUpdate() sets them.
            _compute.SetInt(ID_SIM_WIDTH, _visTexture.width);
            _compute.SetInt(ID_SIM_HEIGHT, _visTexture.height);
            _compute.SetInt(ID_PAINT_X, x);
            _compute.SetInt(ID_PAINT_Y, y);
            _compute.SetInt(ID_PAINT_R, r);
            _compute.SetInt(ID_PAINT_TYPE, mat);
            _compute.SetInt(ID_PAINT_REPLACE, _paintReplace ? 1 : 0);
            _compute.SetBuffer(KERNEL_PAINT, ID_SIM_DATA, _simData);

            _compute.Dispatch(KERNEL_PAINT, groupsX, groupsY, 1);
        }

        private void UploadMaterials() {
            if (_materialBuffer != null && _materialBuffer.IsValid()) _materialBuffer.Release();

            var stride = Marshal.SizeOf<MaterialProperties>();
            var count = _materials.Length + 1; // +1 for empty at index 0.
            var data = new MaterialProperties[count];

            data[0] = EmptyMaterial;
            for (var i = 0; i < _materials.Length; i++) {
                data[i + 1] = MaterialProperties.FromDefinition(_materials[i]);
            }

            _materialBuffer = new ComputeBuffer(count, stride);
            _materialBuffer.SetData(data);

            // Bind to all kernels that read _Materials.
            _compute.SetBuffer(KERNEL_PREPARE_FRAME, ID_MATERIALS, _materialBuffer);
            _compute.SetBuffer(KERNEL_SIM, ID_MATERIALS, _materialBuffer);
            _compute.SetBuffer(KERNEL_LIGHT, ID_MATERIALS, _materialBuffer);
            _compute.SetBuffer(KERNEL_VIS, ID_MATERIALS, _materialBuffer);
        }

        private void PrepareFrame() {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            _compute.SetBuffer(KERNEL_PREPARE_FRAME, ID_SIM_DATA, _simData);
            _compute.Dispatch(KERNEL_PREPARE_FRAME, groupsX, groupsY, 1);
        }

        private void Simulate(int pass, int step) {
            // Half-width dispatch for the red/black checkerboard pattern.
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 2f / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 2f / 8);

            _compute.SetInt(ID_SIM_STEP, step);
            _compute.SetInt(ID_SIM_PASS, pass);
            _compute.SetBuffer(KERNEL_SIM, ID_SIM_DATA, _simData);

            _compute.Dispatch(KERNEL_SIM, groupsX, groupsY, 1);
        }

        private void ComputeLight() {
            var groupsX = Mathf.CeilToInt((float)_lightTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_lightTexture.height / 8);

            // Convert angle to integer DDA direction.
            // 0° = right, 90° = up. Scale by 16 for slope precision, flip Y (sim Y=0 is bottom).
            var rad = _lightAngle * Mathf.Deg2Rad;
            _compute.SetInt(ID_LIGHT_DIR_X, Mathf.RoundToInt(Mathf.Cos(rad) * 16));
            _compute.SetInt(ID_LIGHT_DIR_Y, Mathf.RoundToInt(Mathf.Sin(rad) * 16));

            var lc = _lightColor.linear;
            _compute.SetVector(ID_LIGHT_COLOR, new Vector4(lc.r * _lightIntensity, lc.g * _lightIntensity, lc.b * _lightIntensity, 1f));
            var ac = _ambientColor.linear;
            _compute.SetVector(ID_AMBIENT_COLOR, new Vector4(ac.r, ac.g, ac.b, 1f));
            _compute.SetInt(ID_LIGHT_MAX_STEPS, _lightMaxSteps);
            _compute.SetInt(ID_LIGHT_DOWNSCALE, _lightDownscale);

            _compute.SetBuffer(KERNEL_LIGHT, ID_SIM_DATA, _simData);
            _compute.SetTexture(KERNEL_LIGHT, ID_LIGHT_TEXTURE, _lightTexture);

            _compute.Dispatch(KERNEL_LIGHT, groupsX, groupsY, 1);
        }

        private void Visualize(int cursorX, int cursorY) {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            _compute.SetInt(ID_CURSOR_X, cursorX);
            _compute.SetInt(ID_CURSOR_Y, cursorY);
            _compute.SetInt(ID_CURSOR_R, _paintRadius);
            _compute.SetInt(ID_CURSOR_MAT, _selectedIndex + 1);

            _compute.SetBuffer(KERNEL_VIS, ID_SIM_DATA, _simData);
            _compute.SetTexture(KERNEL_VIS, ID_VIS_TEXTURE, _visTexture);
            _compute.SetTexture(KERNEL_VIS, ID_LIGHT_TEXTURE_READ, _lightTexture);

            _compute.Dispatch(KERNEL_VIS, groupsX, groupsY, 1);
        }
    }
}
