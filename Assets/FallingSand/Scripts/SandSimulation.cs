using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace FallingSand.Scripts {
    public enum SimResolution { Res960x600, Res1280x800, Res1600x1000, Res1920x1200, Res2560x1600 }
    public enum FrameRateCap { VSync, Cap60 }

    /// <summary>
    /// Drives the falling-sand compute simulation and handles user input.
    /// Lighting is delegated to SandLightingController.
    ///
    /// Controls:
    ///   Left click        – paint selected material
    ///   Right click       – erase
    ///   Scroll wheel      – change brush radius
    ///   Shift + scroll    – cycle material
    ///   Insert            – toggle replacement paint mode
    ///   F12               – save screenshot
    /// </summary>
    public class SandSimulation : MonoBehaviour {
        // Must match MAT_ID_BITS in SandUtil.hlsl.
        private const int MaxMaterials = 1 << 5;

        // Fix for windows to maintain proper resolution on high DPI.
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnableHighDpi() => SetProcessDPIAware();
#endif

        [Header("Painting")]
        [SerializeField] private int _paintRadius = 5;
        [SerializeField] private int _paintRadiusMin = 1;
        [SerializeField] private int _paintRadiusMax = 50;

        [Header("Simulation")]
        [SerializeField] private int _gravity = 16;
        [SerializeField] private SimResolution _simResolution = SimResolution.Res1600x1000;
        [SerializeField] [Range(1, 3)] private int _windowScale = 1;
        [SerializeField] private int _baseSimSteps = 256;
        [SerializeField] private FrameRateCap _frameRateCap = FrameRateCap.VSync;
        [SerializeField] private ComputeShader _compute;

        [Header("Visualization")]
        [SerializeField] private RawImage _tempVis;
        [SerializeField] private Shader _premultShader;
        [SerializeField] private SandLightingController _lighting;

        // GPU-side interaction structs. Must match SandUtil.hlsl layout.
        [StructLayout(LayoutKind.Sequential)]
        private struct GpuReactionEntry {
            public uint Result;
            public float Probability;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuDecayEntry {
            public uint Result;
            public float Probability;
        }

        // Paint state.
        private int _selectedIndex = 1;
        private bool _paintReplace;
        private Vector2Int _lastPaintPos;
        private Vector2Int _lineOrigin;
        private int _lineAxis; // -1 = undecided, 0 = horizontal, 1 = vertical, 2 = diagonal
        private bool _wasPainting;

        // FPS and sim timing.
        private float _fpsTimer;
        private int _fpsCounter;
        private int _fpsDisplay;
        private int _effectiveSteps;
        private int _stepOffset;
        private float _prepareFrameAccum = 1f / 60f;

        // Resources.
        private int _simFrame;
        private ComputeBuffer _simData;
        private ComputeBuffer _materialBuffer;
        private ComputeBuffer _reactionBuffer;
        private ComputeBuffer _decayBuffer;
        private RenderTexture _visTexture;
        private RenderTexture _lightTexture;
        private RenderTexture _lightTextureTemp;
        private Material _premultMaterial;

        // Deferred recreation flag.
        private bool _pendingRecreate;


        // Kernel handles.
        private int KERNEL_PAINT;
        private int KERNEL_PREPARE_FRAME;
        private int KERNEL_SIM;

        // Shader property IDs.
        private static readonly int ID_PAINT_X          = Shader.PropertyToID("_PaintX");
        private static readonly int ID_PAINT_Y          = Shader.PropertyToID("_PaintY");
        private static readonly int ID_PAINT_R          = Shader.PropertyToID("_PaintR");
        private static readonly int ID_PAINT_TYPE       = Shader.PropertyToID("_PaintType");
        private static readonly int ID_PAINT_REPLACE    = Shader.PropertyToID("_PaintReplace");

        private static readonly int ID_SIM_WIDTH        = Shader.PropertyToID("_SimWidth");
        private static readonly int ID_SIM_HEIGHT       = Shader.PropertyToID("_SimHeight");
        private static readonly int ID_SIM_STEPS        = Shader.PropertyToID("_SimSteps");
        private static readonly int ID_SIM_STEP         = Shader.PropertyToID("_SimStep");
        private static readonly int ID_SIM_FRAME        = Shader.PropertyToID("_SimFrame");
        private static readonly int ID_SIM_PASS         = Shader.PropertyToID("_SimPass");
        private static readonly int ID_GRAVITY          = Shader.PropertyToID("_Gravity");
        private static readonly int ID_MATERIALS        = Shader.PropertyToID("_Materials");
        private static readonly int ID_SIM_DATA         = Shader.PropertyToID("_SimData");
        private static readonly int ID_REACTIONS        = Shader.PropertyToID("_Reactions");
        private static readonly int ID_DECAY            = Shader.PropertyToID("_Decay");

        // --- Public API ---

        public IReadOnlyList<MaterialDefinition> Materials => MaterialTable.Materials;
        public SandLightingController Lighting => _lighting;

        public int SelectedMaterialIndex {
            get => _selectedIndex;
            set => _selectedIndex = Mathf.Clamp(value, 1, Materials.Count - 1);
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
        public int EffectiveSteps => _effectiveSteps;
        public int BaseSimSteps => _baseSimSteps;

        public FrameRateCap FrameCap {
            get => _frameRateCap;
            set { _frameRateCap = value; ApplyFrameRateCap(); }
        }

        public SimResolution Resolution {
            get => _simResolution;
            set { _simResolution = value; _pendingRecreate = true; }
        }

        public int WindowScale {
            get => _windowScale;
            set { _windowScale = Mathf.Clamp(value, 1, 3); _pendingRecreate = true; }
        }

        public void RecreateLightTextures() {
            if (_lightTexture) Destroy(_lightTexture);
            if (_lightTextureTemp) Destroy(_lightTextureTemp);

            var dims = GetSimDimensions();
            var downscale = _lighting ? _lighting.LightDownscale : 2;
            var lightW = dims.x / downscale;
            var lightH = dims.y / downscale;

            _lightTexture = new RenderTexture(lightW, lightH, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            
            _lightTexture.Create();

            _lightTextureTemp = new RenderTexture(lightW, lightH, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            
            _lightTextureTemp.Create();
        }

        // --- Resolution ---

        private Vector2Int GetSimDimensions() => _simResolution switch {
            SimResolution.Res960x600   => new(960, 600),
            SimResolution.Res1280x800  => new(1280, 800),
            SimResolution.Res1600x1000 => new(1600, 1000),
            SimResolution.Res1920x1200 => new(1920, 1200),
            SimResolution.Res2560x1600 => new(2560, 1600),
            _ => new(1600, 1000)
        };

        public void RecreateSimulation() {
            GL.Flush();
            
            _tempVis.texture = null;

            if (_simData != null && _simData.IsValid()) {_simData.Release();}
            if (_visTexture) Destroy(_visTexture);
            if (_lightTexture) Destroy(_lightTexture);
            if (_lightTextureTemp) Destroy(_lightTextureTemp);

            var dims = GetSimDimensions();
            var simWidth = dims.x;
            var simHeight = dims.y;

            _simData = new ComputeBuffer(simWidth * simHeight, sizeof(int));

            // DX12/VK do not zero new buffers.
            var zeroed = new NativeArray<int>(simWidth * simHeight, Allocator.Temp, NativeArrayOptions.ClearMemory);
            _simData.SetData(zeroed);
            zeroed.Dispose();

            _visTexture = new RenderTexture(simWidth, simHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                useMipMap = false
            };
            
            _visTexture.Create();
            
            _tempVis.texture = _visTexture;

            var downscale = _lighting ? _lighting.LightDownscale : 2;
            var lightW = simWidth / downscale;
            var lightH = simHeight / downscale;

            _lightTexture = new RenderTexture(lightW, lightH, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            
            _lightTexture.Create();

            _lightTextureTemp = new RenderTexture(lightW, lightH, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            
            _lightTextureTemp.Create();

            Screen.SetResolution(simWidth * _windowScale, simHeight * _windowScale, FullScreenMode.Windowed);
            
            _simFrame = 0;
            _stepOffset = 0;
            _prepareFrameAccum = 1f / 60f;
        }

        private void ApplyFrameRateCap() {
            switch (_frameRateCap) {
                case FrameRateCap.VSync:
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = -1;
                    break;
                case FrameRateCap.Cap60:
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = 60;
                    break;
            }
        }

        // --- Lifecycle ---

        private void Start() {
            KERNEL_PAINT            = _compute.FindKernel("Paint");
            KERNEL_PREPARE_FRAME    = _compute.FindKernel("PrepareFrame");
            KERNEL_SIM              = _compute.FindKernel("Simulate");

            _lighting.Initialize();

            UploadMaterials();
            RecreateSimulation();
            ApplyFrameRateCap();

            _tempVis.raycastTarget = false;
            if (_premultShader) {
                _premultMaterial = new Material(_premultShader);
                _tempVis.material = _premultMaterial;
            }
        }

        private void OnDisable() {
            if (_simData != null && _simData.IsValid())                 _simData.Release();
            if (_materialBuffer != null && _materialBuffer.IsValid())   _materialBuffer.Release();
            if (_reactionBuffer != null && _reactionBuffer.IsValid())   _reactionBuffer.Release();
            if (_decayBuffer != null && _decayBuffer.IsValid())         _decayBuffer.Release();

            if (_visTexture)        Destroy(_visTexture);
            if (_lightTexture)      Destroy(_lightTexture);
            if (_lightTextureTemp)  Destroy(_lightTextureTemp);
            if (_premultMaterial)   Destroy(_premultMaterial);

            _simData = null;
            _materialBuffer = null;
            _reactionBuffer = null;
            _decayBuffer = null;
            _visTexture = null;
            _lightTexture = null;
            _lightTextureTemp = null;
            _premultMaterial = null;
        }

        private void Update() {
            if (!_compute || !_lighting || !_lighting.IsReady || !_simData.IsValid()) return;

            // FPS counter.
            _fpsCounter++;
            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1f) {
                _fpsDisplay = _fpsCounter;
                _fpsCounter = 0;
                _fpsTimer -= 1f;
            }

            // Screenshot.
            if (Input.GetKeyDown(KeyCode.F12)) {
                var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Screenshots");
                
                System.IO.Directory.CreateDirectory(dir);
                
                var path = System.IO.Path.Combine(dir, $"sand_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
                
                ScreenCapture.CaptureScreenshot(path);
                
                Debug.Log($"Screenshot saved: {path}");
            }

            // Guard UI input.
            var overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (overUI) {
                _wasPainting = false;
            } else {
                var delta = Input.mouseScrollDelta;
                var scroll = delta.y != 0 ? delta.y : delta.x;

                if (scroll != 0) {
                    var shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                    if (shiftHeld) {
                        var count = Materials.Count - 1;
                        _selectedIndex = (_selectedIndex - 1 + (scroll > 0 ? 1 : -1) + count) % count + 1;
                    } else {
                        _paintRadius += scroll > 0 ? 1 : -1;
                        _paintRadius = Mathf.Clamp(_paintRadius, _paintRadiusMin, _paintRadiusMax);
                    }
                }

                if (Input.GetKeyDown(KeyCode.Insert)) {
                    _paintReplace = !_paintReplace;
                }

                var painting = Input.GetKey(KeyCode.Mouse0);
                var erasing  = Input.GetKey(KeyCode.Mouse1);

                if (painting || erasing) {
                    var mousePos = Input.mousePosition;
                    var pos = new Vector2Int(
                        Mathf.FloorToInt(mousePos.x * _visTexture.width / Screen.width),
                        Mathf.FloorToInt(mousePos.y * _visTexture.height / Screen.height)
                    );
                    var mat = erasing ? 0 : _selectedIndex;

                    if (!_wasPainting) {
                        _lineOrigin = pos;
                        _lineAxis = -1;
                    }

                    var shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (shiftHeld) pos = SnapToLine(_lineOrigin, pos, ref _lineAxis);

                    if (_wasPainting) {
                        PaintLine(_lastPaintPos, pos, _paintRadius, mat);
                    } else {
                        Paint(pos.x, pos.y, _paintRadius, mat);
                    }

                    _lastPaintPos = pos;
                }

                _wasPainting = painting || erasing;
            }

            // --- Simulation ---

            if (_pendingRecreate) {
                _pendingRecreate = false;
                RecreateSimulation();
            }

            // Compute effective steps to maintain consistent behavior at frame-rates other than 60.
            _effectiveSteps = Mathf.Clamp(
                Mathf.RoundToInt(_baseSimSteps * 60f * Time.unscaledDeltaTime),
                1, _baseSimSteps
            );

            Profiler.BeginSample("Falling Sand Simulation");

            _compute.SetInt(ID_SIM_WIDTH, _visTexture.width);
            _compute.SetInt(ID_SIM_HEIGHT, _visTexture.height);
            _compute.SetInt(ID_SIM_STEPS, _baseSimSteps);
            _compute.SetInt(ID_SIM_FRAME, _simFrame);
            _compute.SetInt(ID_GRAVITY, _gravity);

            // We need to scale when PrepareFrame is run based on the FPS as well.
            _prepareFrameAccum += Time.unscaledDeltaTime;
            if (_prepareFrameAccum >= 1f / 60f) {
                _prepareFrameAccum -= 1f / 60f;
                if (_prepareFrameAccum >= 1f / 60f) _prepareFrameAccum = 0f;
                PrepareFrame();
            }

            // Step the simulation with the 4-color tiling pattern.
            for (var i = 0; i < _effectiveSteps; i++) {
                var offset = (_simFrame * _effectiveSteps + i) & 3;
                for (var p = 0; p < 4; p++)
                    Simulate((p + offset) & 3, _stepOffset + i);
            }

            _stepOffset = (_stepOffset + _effectiveSteps) % _baseSimSteps;

            // --- Lighting ---

            var cursorPos = Input.mousePosition;
            var cursorX = Mathf.FloorToInt(cursorPos.x * _visTexture.width / Screen.width);
            var cursorY = Mathf.FloorToInt(cursorPos.y * _visTexture.height / Screen.height);

            _lighting.Render(
                _simData, _visTexture, _lightTexture, _lightTextureTemp,
                _simFrame, cursorX, cursorY, _paintRadius, _selectedIndex
            );

            _simFrame++;

            Profiler.EndSample();
        }

        // --- Paint ---

        private static Vector2Int SnapToLine(Vector2Int origin, Vector2Int pos, ref int axis) {
            var dx = pos.x - origin.x;
            var dy = pos.y - origin.y;
            var ax = Mathf.Abs(dx);
            var ay = Mathf.Abs(dy);

            // Lock axis once cursor moves far enough from origin.
            if (axis < 0 && ax + ay > 5) {
                if (ax > 2 * ay) axis = 0;
                else if (ay > 2 * ax) axis = 1;
                else axis = 2;
            }

            return axis switch {
                0 => new Vector2Int(pos.x, origin.y),
                1 => new Vector2Int(origin.x, pos.y),
                2 => new Vector2Int(
                    origin.x + Mathf.Max(ax, ay) * (int)Mathf.Sign(dx),
                    origin.y + Mathf.Max(ax, ay) * (int)Mathf.Sign(dy)),
                _ => pos
            };
        }

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

        // --- Upload ---

        private void UploadMaterials() {
            if (_materialBuffer != null && _materialBuffer.IsValid()) {
                _materialBuffer.Release();
            }

            var mats = MaterialTable.Materials;
            var stride = Marshal.SizeOf<GpuMaterialDefinition>();
            var count = mats.Count;
            var data = new GpuMaterialDefinition[count];

            for (var i = 0; i < count; i++) {
                data[i] = GpuMaterialDefinition.FromDefinition(mats[i]);
            }

            _materialBuffer = new ComputeBuffer(count, stride);
            _materialBuffer.SetData(data);

            _compute.SetBuffer(KERNEL_PREPARE_FRAME, ID_MATERIALS, _materialBuffer);
            _compute.SetBuffer(KERNEL_SIM, ID_MATERIALS, _materialBuffer);
            _lighting.BindMaterials(_materialBuffer);

            // Upload reaction LUT indexed by [source * MaxMaterials + trigger].
            if (_reactionBuffer != null && _reactionBuffer.IsValid()) {
                _reactionBuffer.Release();
            }

            var lut = new GpuReactionEntry[MaxMaterials * MaxMaterials];
            foreach (var r in MaterialTable.Reactions) {
                lut[(int)r.Source * MaxMaterials + (int)r.Trigger] = new GpuReactionEntry {
                    Result = (uint)r.Result,
                    Probability = r.Probability,
                };
            }

            _reactionBuffer = new ComputeBuffer(MaxMaterials * MaxMaterials, Marshal.SizeOf<GpuReactionEntry>());
            _reactionBuffer.SetData(lut);
            _compute.SetBuffer(KERNEL_PREPARE_FRAME, ID_REACTIONS, _reactionBuffer);

            // Upload decay LUT indexed by material ID.
            if (_decayBuffer != null && _decayBuffer.IsValid()) {
                _decayBuffer.Release();
            }

            var decayLut = new GpuDecayEntry[MaxMaterials];
            foreach (var d in MaterialTable.Decay) {
                decayLut[(int)d.Source] = new GpuDecayEntry {
                    Result = (uint)d.Result,
                    Probability = d.Probability,
                };
            }

            _decayBuffer = new ComputeBuffer(MaxMaterials, Marshal.SizeOf<GpuDecayEntry>());
            _decayBuffer.SetData(decayLut);
            _compute.SetBuffer(KERNEL_PREPARE_FRAME, ID_DECAY, _decayBuffer);
        }

        // --- Sim ---

        private void PrepareFrame() {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            _compute.SetBuffer(KERNEL_PREPARE_FRAME, ID_SIM_DATA, _simData);
            _compute.Dispatch(KERNEL_PREPARE_FRAME, groupsX, groupsY, 1);
        }

        private void Simulate(int pass, int step) {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 2f / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 2f / 8);

            _compute.SetInt(ID_SIM_STEP, step);
            _compute.SetInt(ID_SIM_PASS, pass);
            _compute.SetBuffer(KERNEL_SIM, ID_SIM_DATA, _simData);

            _compute.Dispatch(KERNEL_SIM, groupsX, groupsY, 1);
        }
    }
}
