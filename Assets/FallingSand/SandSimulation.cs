using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace FallingSand {
    /// <summary>
    /// Drives the falling-sand compute simulation and handles user input.
    ///
    /// Controls:
    ///   Left click        – paint selected material
    ///   Right click       – erase
    ///   Scroll wheel      – cycle material (Stone → Sand → Water)
    ///   Shift + scroll    – change brush radius
    ///   Insert            – toggle replacement paint mode
    /// </summary>
    public class SandSimulation : MonoBehaviour {
        [Header("Painting")]
        [SerializeField] private int _paintRadius = 5;
        [SerializeField] private int _paintRadiusMin = 1;
        [SerializeField] private int _paintRadiusMax = 50;

        [Header("Simulation")]
        [SerializeField] private int _simScale = 4;
        [SerializeField] private int _simSteps = 256;
        [SerializeField] private ComputeShader _compute;

        [Header("Visualization")]
        [SerializeField] private RawImage _tempVis;
        [SerializeField] private Text _hudText;

        // Available paint materials and their shader IDs.
        private static readonly string[] MaterialNames = { "Stone", "Sand", "Water" };
        private static readonly int[] MaterialIDs = { 1, 2, 3 };

        // Paint state.
        private int _selectedIndex;
        private bool _paintReplace;
        private Vector2Int _lastPaintPos;
        private bool _wasPainting;

        // FPS tracking.
        private float _fpsTimer;
        private int _fpsCounter;
        private int _fpsDisplay;

        // Resources.
        private int _simFrame;
        private ComputeBuffer _simData;
        private RenderTexture _visTexture;

        // Kernel handles.
        private int KERNEL_PAINT;
        private int KERNEL_GRAVITY;
        private int KERNEL_SIM;
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

        private static readonly int ID_SIM_DATA      = Shader.PropertyToID("_SimData");
        private static readonly int ID_VIS_TEXTURE   = Shader.PropertyToID("_VisTexture");

        private void Start() {
            KERNEL_PAINT   = _compute.FindKernel("Paint");
            KERNEL_GRAVITY = _compute.FindKernel("Gravity");
            KERNEL_SIM     = _compute.FindKernel("Simulate");
            KERNEL_VIS     = _compute.FindKernel("Visualize");

            var simWidth  = Screen.width / _simScale;
            var simHeight = Screen.height / _simScale;

            _simData = new ComputeBuffer(simWidth * simHeight, sizeof(int));

            _visTexture = new RenderTexture(simWidth, simHeight, 0, RenderTextureFormat.ARGB32) {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                useMipMap = false
            };

            _visTexture.Create();
            _tempVis.texture = _visTexture;
        }

        private void OnDestroy() {
            if (_simData.IsValid()) _simData.Release();
            if (_visTexture) _visTexture.Release();
        }

        /// <summary>
        /// All input handling runs in Update for per-render-frame mouse sampling.
        /// Paint strokes interpolate between frames to avoid gaps at high speed.
        /// </summary>
        private void Update() {
            if (!_compute || !_simData.IsValid()) return;

            // macOS translates shift+scroll from vertical to horizontal at the OS level.
            var delta = Input.mouseScrollDelta;
            var scroll = delta.y != 0 ? delta.y : delta.x;

            if (scroll != 0) {
                var shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (shiftHeld) {
                    _paintRadius += scroll > 0 ? 1 : -1;
                    _paintRadius = Mathf.Clamp(_paintRadius, _paintRadiusMin, _paintRadiusMax);
                } else {
                    var count = MaterialIDs.Length;
                    _selectedIndex = ((_selectedIndex + (scroll > 0 ? 1 : -1)) % count + count) % count;
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
                    Mathf.FloorToInt(mousePos.x) / _simScale,
                    Mathf.FloorToInt(mousePos.y) / _simScale
                );
                var mat = erasing ? 0 : MaterialIDs[_selectedIndex];

                if (_wasPainting) {
                    PaintLine(_lastPaintPos, pos, _paintRadius, mat);
                } else {
                    Paint(pos.x, pos.y, _paintRadius, mat);
                }

                _lastPaintPos = pos;
            }

            _wasPainting = painting || erasing;

            // Update FPS counter and HUD text.
            _fpsCounter++;
            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1f) {
                _fpsDisplay = _fpsCounter;
                _fpsCounter = 0;
                _fpsTimer -= 1f;
            }

            if (_hudText) {
                var matName = MaterialNames[_selectedIndex];
                var mode = _paintReplace ? "Replace" : "Fill";
                _hudText.text = $"{_fpsDisplay} FPS\n{matName} ({mode})";
            }
        }

        /// <summary>
        /// Run simulation at fixed timestep.
        /// </summary>
        private void FixedUpdate() {
            if (!_compute || !_simData.IsValid()) {
                return;
            }

            Profiler.BeginSample("Falling Sand Simulation");

            // Set common uniforms.
            _compute.SetInt(ID_SIM_WIDTH, _visTexture.width);
            _compute.SetInt(ID_SIM_HEIGHT, _visTexture.height);
            _compute.SetInt(ID_SIM_STEPS, _simSteps);
            _compute.SetInt(ID_SIM_FRAME, _simFrame);

            // Simulate.
            ApplyGravity();

            for (var i = 0; i < _simSteps; i++) {
                // Alternate which checkerboard half gets first-mover advantage.
                var first = (_simFrame + i) & 1;
                Simulate(first, i);
                Simulate(1 - first, i);
            }

            // Visualize with cursor overlay.
            var cursorPos = Input.mousePosition;
            var cursorX = Mathf.FloorToInt(cursorPos.x) / _simScale;
            var cursorY = Mathf.FloorToInt(cursorPos.y) / _simScale;
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

        private void ApplyGravity() {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            _compute.SetBuffer(KERNEL_GRAVITY, ID_SIM_DATA, _simData);
            _compute.Dispatch(KERNEL_GRAVITY, groupsX, groupsY, 1);
        }

        private void Simulate(int pass, int step) {
            // Half-width dispatch for the red/black checkerboard pattern.
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 2 / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            _compute.SetInt(ID_SIM_STEP, step);
            _compute.SetInt(ID_SIM_PASS, pass);
            _compute.SetBuffer(KERNEL_SIM, ID_SIM_DATA, _simData);

            _compute.Dispatch(KERNEL_SIM, groupsX, groupsY, 1);
        }

        private void Visualize(int cursorX, int cursorY) {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            _compute.SetInt(ID_CURSOR_X, cursorX);
            _compute.SetInt(ID_CURSOR_Y, cursorY);
            _compute.SetInt(ID_CURSOR_R, _paintRadius);
            _compute.SetInt(ID_CURSOR_MAT, MaterialIDs[_selectedIndex]);

            _compute.SetBuffer(KERNEL_VIS, ID_SIM_DATA, _simData);
            _compute.SetTexture(KERNEL_VIS, ID_VIS_TEXTURE, _visTexture);

            _compute.Dispatch(KERNEL_VIS, groupsX, groupsY, 1);
        }
    }
}
