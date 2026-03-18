using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FallingSand.Scripts {
    /// <summary>
    /// Drives the falling-sand compute simulation and handles user input.
    /// Lighting is delegated to SandLightingController.
    /// All GPU work is recorded into a single CommandBuffer per frame.
    ///
    /// Controls:
    ///   Left click        – paint selected material
    ///   Right click       – erase
    ///   Scroll wheel      – change brush radius
    ///   Shift + scroll    – cycle material
    ///   Insert            – toggle replacement paint mode
    ///   F12               – save screenshot
    /// </summary>
    public class SandSimulationController : MonoBehaviour {
        /// <summary>
        /// Various canvas resolutions for the sim.
        /// TODO: Consider standardizing on one like other sims to make saving/loading easier in the future.
        /// </summary>
        public enum SimResolution { Res960x600, Res1280x800, Res1600x1000, Res1920x1200, Res2560x1600 }
        
        /// <summary>
        /// Simulation is designed around 128 steps at 60Hz.
        /// We can scale the steps and other interval-based kernels to try and keep consistent behavior at other rates.
        /// Allows those with beefy GPUs and high refresh rates to get nice smooth motion and better frame-pacing.
        /// </summary>
        public enum FrameRateCap { VSync, Cap60 }

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
        [SerializeField] [Range(1, 16)] private int _heatStepInterval = 8;
        [SerializeField] [Range(1, 8)] private int _heatBurstCount = 4;
        [SerializeField] private FrameRateCap _frameRateCap = FrameRateCap.VSync;
        [SerializeField] private ComputeShader _simCompute;

        [Header("Visualization")]
        [SerializeField] private RawImage _tempVis;
        [SerializeField] private Shader _premultShader;
        [SerializeField] private SandLightingController _lighting;
        [SerializeField] private ComputeShader _visCompute;
        [SerializeField] private Gradient _heatGradient;
        [SerializeField] private Gradient _blackbodyGradient;

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

        // Kernel handles.
        private int KERNEL_PAINT;
        private int KERNEL_PREP;
        private int KERNEL_SIM;
        private int KERNEL_HEAT;
        private int KERNEL_VIS;
        private int KERNEL_VIS_HEAT;

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

        // Resources
        private int _simFrame;
        private CommandBuffer _cmd;

        // Data buffers.
        private ComputeBuffer _simData;
        private ComputeBuffer _simHeat;

        // LUT buffers.
        private ComputeBuffer _materialBuffer;
        private ComputeBuffer _reactionBuffer;
        private ComputeBuffer _decayBuffer;

        // Constant buffers.
        private ComputeBuffer _simConfigBuffer;
        private ComputeBuffer _simPaintBuffer;
        private ComputeBuffer _visConfigBuffer;

        // Textures
        private RenderTexture _visTexture;
        private RenderTexture _lightTexture;
        private RenderTexture _lightTextureTemp;

        private Material _premultMaterial;

        // Deferred recreation flag.
        private bool _pendingRecreate;

        // Pause state.
        private bool _paused;

        // Heat view state.
        private bool _heatView;
        private Texture2D _heatGradTexture;

        // --- Public API ---

        public bool Paused {
            get => _paused;
            set => _paused = value;
        }

        public bool HeatView {
            get => _heatView;
            set => _heatView = value;
        }

        public bool DebugReadback { get; set; }
        public float CursorTemp { get; private set; }
        public uint CursorCell { get; private set; }

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
            if (_lightTexture) {
                Destroy(_lightTexture);
            }

            if (_lightTextureTemp) {
                Destroy(_lightTextureTemp);
            }

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

            if (_simData != null && _simData.IsValid()) _simData.Release();
            if (_simHeat != null && _simHeat.IsValid()) _simHeat.Release();
            
            if (_visTexture)        Destroy(_visTexture);
            if (_lightTexture)      Destroy(_lightTexture);
            if (_lightTextureTemp)  Destroy(_lightTextureTemp);

            var dims = GetSimDimensions();
            var simWidth = dims.x;
            var simHeight = dims.y;

            _simData = new ComputeBuffer(simWidth * simHeight, sizeof(int));
            _simHeat = new ComputeBuffer(simWidth * simHeight, sizeof(float));

            // DX12/VK do not zero new buffers.
            var zeroed = new NativeArray<int>(simWidth * simHeight, Allocator.Temp, NativeArrayOptions.ClearMemory);
            _simData.SetData(zeroed);
            _simHeat.SetData(zeroed);
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

        // --- Table Uploads ---

        private void CreateTableBuffers() {
            var mats = MaterialTable.Materials;
            var count = mats.Count;
            var data = new GpuMaterialDefinition[count];
            
            for (var i = 0; i < count; i++) {
                data[i] = GpuMaterialDefinition.FromDefinition(mats[i]);
            }

            _materialBuffer = new ComputeBuffer(
                count,
                Marshal.SizeOf<GpuMaterialDefinition>(),
                ComputeBufferType.Structured
            );

            _materialBuffer.SetData(data);

            // Upload reaction LUT indexed by [source * MAX_MATERIALS + trigger].
            var reactionsLut = new GpuReactionEntry[SandBindings.MAX_MATERIALS * SandBindings.MAX_MATERIALS];
            foreach (var r in MaterialTable.Reactions) {
                reactionsLut[(int)r.Source * SandBindings.MAX_MATERIALS + (int)r.Trigger] = new GpuReactionEntry {
                    Result = (uint)r.Result,
                    Probability = r.Probability,
                };
            }

            _reactionBuffer = new ComputeBuffer(
                SandBindings.MAX_MATERIALS * SandBindings.MAX_MATERIALS,
                Marshal.SizeOf<GpuReactionEntry>(),
                ComputeBufferType.Structured
            );

            _reactionBuffer.SetData(reactionsLut);

            // Upload decay LUT indexed by material ID.
            var decayLut = new GpuDecayEntry[SandBindings.MAX_MATERIALS];
            foreach (var d in MaterialTable.Decay) {
                decayLut[(int)d.Source] = new GpuDecayEntry {
                    Result = (uint)d.Result,
                    Probability = d.Probability,
                };
            }

            _decayBuffer = new ComputeBuffer(
                SandBindings.MAX_MATERIALS,
                Marshal.SizeOf<GpuDecayEntry>(),
                ComputeBufferType.Structured
            );

            _decayBuffer.SetData(decayLut);
        }

        private void CreateConstantBuffers() {
            _simConfigBuffer = new ComputeBuffer(
                1,
                Marshal.SizeOf<SandBindings.SimConfigBuffer>(),
                ComputeBufferType.Constant
            );

            _simPaintBuffer = new ComputeBuffer(
                1,
                Marshal.SizeOf<SandBindings.SimPaintBuffer>(),
                ComputeBufferType.Constant
            );

            _visConfigBuffer = new ComputeBuffer(
                1,
                Marshal.SizeOf<SandBindings.VisConfigBuffer>(),
                ComputeBufferType.Constant
            );

            _simConfigBuffer.SetData(new[] { default(SandBindings.SimConfigBuffer) });
            _simPaintBuffer.SetData(new[] { default(SandBindings.SimPaintBuffer) });
            _visConfigBuffer.SetData(new[] { default(SandBindings.VisConfigBuffer) });
        }

        private void ReleaseTableBuffers() {
            if (_materialBuffer != null && _materialBuffer.IsValid()) {
                _materialBuffer.Release();
                _materialBuffer = null;
            }

            if (_reactionBuffer != null && _reactionBuffer.IsValid()) {
                _reactionBuffer.Release();
                _reactionBuffer = null;
            }

            if (_decayBuffer != null && _decayBuffer.IsValid()) {
                _decayBuffer.Release();
                _decayBuffer = null;
            }
        }

        private void ReleaseConstantBuffers() {
            if (_simConfigBuffer != null && _simConfigBuffer.IsValid()) {
                _simConfigBuffer.Release();
                _simConfigBuffer = null;
            }

            if (_simPaintBuffer != null && _simPaintBuffer.IsValid()) {
                _simPaintBuffer.Release();
                _simPaintBuffer = null;
            }

            if (_visConfigBuffer != null && _visConfigBuffer.IsValid()) {
                _visConfigBuffer.Release();
                _visConfigBuffer = null;
            }
        }

        private void Reset() {
            // Nice gradient for the heat view mode.
            _heatGradient = new Gradient();
            _heatGradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.05f, 0.05f, 0.60f), 0.00f),
                    new GradientColorKey(new Color(0.00f, 0.40f, 0.80f), 0.08f),
                    new GradientColorKey(new Color(0.00f, 0.75f, 0.65f), 0.14f),
                    new GradientColorKey(new Color(0.10f, 0.80f, 0.15f), 0.18f),
                    new GradientColorKey(new Color(0.95f, 0.90f, 0.10f), 0.25f),
                    new GradientColorKey(new Color(1.00f, 0.50f, 0.05f), 0.32f),
                    new GradientColorKey(new Color(0.90f, 0.10f, 0.05f), 0.43f),
                    new GradientColorKey(new Color(1.00f, 1.00f, 1.00f), 0.72f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );

            // Stylized blackbody emission curve.
            _blackbodyGradient = new Gradient();
            _blackbodyGradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.00f, 0.00f, 0.00f), 0.00f),
                    new GradientColorKey(new Color(0.00f, 0.00f, 0.00f), 0.26f),
                    new GradientColorKey(new Color(0.25f, 0.02f, 0.00f), 0.28f),
                    new GradientColorKey(new Color(0.70f, 0.08f, 0.02f), 0.32f),
                    new GradientColorKey(new Color(1.00f, 0.45f, 0.05f), 0.38f),
                    new GradientColorKey(new Color(1.00f, 0.90f, 0.70f), 0.50f),
                    new GradientColorKey(new Color(0.80f, 0.85f, 1.00f), 0.72f),
                    new GradientColorKey(new Color(0.65f, 0.75f, 1.00f), 1.00f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );
        }

        private void BakeHeatGradient() {
            const int width = 256;
            _heatGradTexture = new Texture2D(width, 2, TextureFormat.RGBA32, false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // Row 0: heat view gradient. Row 1: blackbody emission.
            var heatRow = new Color[width];
            var bbRow = new Color[width];
            for (var i = 0; i < width; i++) {
                var t = (float)i / (width - 1);
                heatRow[i] = _heatGradient.Evaluate(t);
                bbRow[i] = _blackbodyGradient.Evaluate(t);
            }

            _heatGradTexture.SetPixels(0, 0, width, 1, heatRow);
            _heatGradTexture.SetPixels(0, 1, width, 1, bbRow);
            _heatGradTexture.Apply();
        }

        // --- Lifecycle ---

        private void Start() {
            KERNEL_PAINT    = _simCompute.FindKernel(SandBindings.KERNEL_PAINT);
            KERNEL_PREP     = _simCompute.FindKernel(SandBindings.KERNEL_PREPARE);
            KERNEL_SIM      = _simCompute.FindKernel(SandBindings.KERNEL_SIMULATE);
            KERNEL_HEAT     = _simCompute.FindKernel(SandBindings.KERNEL_HEAT);
            KERNEL_VIS      = _visCompute.FindKernel(SandBindings.KERNEL_VISUALIZE);
            KERNEL_VIS_HEAT = _visCompute.FindKernel(SandBindings.KERNEL_VISUALIZE_HEAT);

            _cmd = new CommandBuffer { name = "Sand Simulation" };

            _lighting.Initialize();
            BakeHeatGradient();

            CreateTableBuffers();
            CreateConstantBuffers();
            RecreateSimulation();
            ApplyFrameRateCap();

            _tempVis.raycastTarget = false;
            if (_premultShader) {
                _premultMaterial = new Material(_premultShader);
                _tempVis.material = _premultMaterial;
            }
        }

        private void OnDisable() {
            // TODO: Move these into a dedicated function if we add even more data layers.
            if (_simData != null && _simData.IsValid()) _simData.Release();
            if (_simHeat != null && _simHeat.IsValid()) _simHeat.Release();

            ReleaseTableBuffers();
            ReleaseConstantBuffers();

            _lighting.Cleanup();

            if (_visTexture)        Destroy(_visTexture);
            if (_lightTexture)      Destroy(_lightTexture);
            if (_lightTextureTemp)  Destroy(_lightTextureTemp);
            if (_premultMaterial)   Destroy(_premultMaterial);
            if (_heatGradTexture)   Destroy(_heatGradTexture);

            _cmd?.Release();

            _simData = null;
            _materialBuffer = null;
            _reactionBuffer = null;
            _decayBuffer = null;
            _visTexture = null;
            _lightTexture = null;
            _lightTextureTemp = null;
            _premultMaterial = null;
            _cmd = null;
        }

        private void Update() {
            if (!_simCompute || !_lighting || !_lighting.IsReady || !_simData.IsValid()) {
                return;
            }

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

            if (Input.GetKeyDown(KeyCode.Space)) {
                _paused = !_paused;
            }

            if (Input.GetKeyDown(KeyCode.H)) {
                _heatView = !_heatView;
            }

            if (Input.GetKeyDown(KeyCode.F3)) {
                DebugReadback = !DebugReadback;
            }

            // Handle deferred recreation before anything that touches sim resources.
            if (_pendingRecreate) {
                _pendingRecreate = false;
                RecreateSimulation();
            }

            // Compute effective steps to maintain consistent behavior at frame-rates other than 60.
            _effectiveSteps = Mathf.Clamp(
                Mathf.RoundToInt(_baseSimSteps * 60f * Time.unscaledDeltaTime),
                1, _baseSimSteps
            );

            // Build and stage SimConfig cbuffer.
            var configStaging = new NativeArray<SandBindings.SimConfigBuffer>(1, Allocator.Temp);
            configStaging[0] = new SandBindings.SimConfigBuffer {
                Dimensions = new uint2((uint)_visTexture.width, (uint)_visTexture.height),
                StepsPerFrame = (uint)_baseSimSteps,
                Gravity = (uint)_gravity,
                CurrentFrame = (uint)_simFrame,
            };

            
            _simConfigBuffer.SetData(configStaging);

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

            // Begin recording commands.
            _cmd.Clear();

            // Bind SimConfig cbuffer to all compute shaders.
            var simConfigSize = Marshal.SizeOf<SandBindings.SimConfigBuffer>();

            _cmd.SetComputeConstantBufferParam(_simCompute, SandBindings.ID_CB_SIM_CONFIG, _simConfigBuffer, 0, simConfigSize);
            _cmd.SetComputeConstantBufferParam(_lighting.Compute, SandBindings.ID_CB_SIM_CONFIG, _simConfigBuffer, 0, simConfigSize);
            _cmd.SetComputeConstantBufferParam(_visCompute, SandBindings.ID_CB_SIM_CONFIG, _simConfigBuffer, 0, simConfigSize);

            if (!_paused) {
                // Bind structured buffers to sim kernels.
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_PREP, SandBindings.ID_SIM_DATA, _simData);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_PREP, SandBindings.ID_SIM_HEAT, _simHeat);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_PREP, SandBindings.ID_MATERIALS, _materialBuffer);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_PREP, SandBindings.ID_REACTIONS, _reactionBuffer);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_PREP, SandBindings.ID_DECAYS, _decayBuffer);
                
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_SIM, SandBindings.ID_SIM_DATA, _simData);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_SIM, SandBindings.ID_SIM_HEAT, _simHeat);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_SIM, SandBindings.ID_MATERIALS, _materialBuffer);
                
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_HEAT, SandBindings.ID_SIM_DATA, _simData);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_HEAT, SandBindings.ID_SIM_HEAT, _simHeat);
                _cmd.SetComputeBufferParam(_simCompute, KERNEL_HEAT, SandBindings.ID_MATERIALS, _materialBuffer);

                // PrepareFrame is conditional on accumulator for consistent behavior.
                _prepareFrameAccum += Time.unscaledDeltaTime;
                if (_prepareFrameAccum >= 1f / 60f) {
                    _prepareFrameAccum -= 1f / 60f;
                    
                    if (_prepareFrameAccum >= 1f / 60f) {
                        _prepareFrameAccum = 0f;
                    }
                    
                    RecordPrepareCommands(_cmd);
                }

                // Simulate loop.
                RecordSimulateCommands(_cmd);
            }

            var cursorPos = Input.mousePosition;
            var cursorX = Mathf.FloorToInt(cursorPos.x * _visTexture.width / Screen.width);
            var cursorY = Mathf.FloorToInt(cursorPos.y * _visTexture.height / Screen.height);

            // Lighting.
            if (!_heatView) {
                _lighting.Record(
                    _cmd, _simData, _simHeat,
                    _materialBuffer, _heatGradTexture,
                    _lightTexture, _lightTextureTemp,
                    _simFrame
                );
            }

            // Visualization.
            RecordVisualizeCommands(_cmd, cursorX, cursorY);

            // Execute.
            Graphics.ExecuteCommandBuffer(_cmd);

            // Debug readback of cell data under cursor.
            // Unfortunately requires a hard sync but debug mode is debug mode.
            // We don't get the luxury of easy particle data access like we would on the CPU.
            // TODO: Async readback latency may be fine to try and reduce sync overhead.
            if (DebugReadback &&
                cursorX >= 0 && cursorX < _visTexture.width &&
                cursorY >= 0 && cursorY < _visTexture.height) {
                var idx = cursorX + cursorY * _visTexture.width;
                var tmpHeat = new float[1];
                var tmpCell = new uint[1];
                
                _simHeat.GetData(tmpHeat, 0, idx, 1);
                _simData.GetData(tmpCell, 0, idx, 1);
                
                CursorTemp = tmpHeat[0];
                CursorCell = tmpCell[0];
            }

            if (!_paused) {
                _simFrame++;
                _stepOffset = (_stepOffset + _effectiveSteps) % _baseSimSteps;
            }
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
            // Paint uses immediate-mode dispatch since each stroke point needs its own cbuffer state.
            var paintData = new SandBindings.SimPaintBuffer {
                PaintOrigin = new int2(x, y),
                PaintRadius = (uint)r,
                PaintMaterial = (uint)mat,
                PaintReplace = _paintReplace ? 1u : 0u,
            };

            var staging = new NativeArray<SandBindings.SimPaintBuffer>(1, Allocator.Temp);
            staging[0] = paintData;
            _simPaintBuffer.SetData(staging);

            _simCompute.SetConstantBuffer(SandBindings.ID_CB_SIM_CONFIG, _simConfigBuffer, 0, Marshal.SizeOf<SandBindings.SimConfigBuffer>());
            _simCompute.SetConstantBuffer(SandBindings.ID_CB_SIM_PAINT, _simPaintBuffer, 0, Marshal.SizeOf<SandBindings.SimPaintBuffer>());
            _simCompute.SetBuffer(KERNEL_PAINT, SandBindings.ID_SIM_DATA, _simData);
            _simCompute.SetBuffer(KERNEL_PAINT, SandBindings.ID_SIM_HEAT, _simHeat);
            _simCompute.SetBuffer(KERNEL_PAINT, SandBindings.ID_MATERIALS, _materialBuffer);

            var diameter = 2 * r + 1;
            var groupsX = Mathf.CeilToInt((float)diameter / 8);
            var groupsY = Mathf.CeilToInt((float)diameter / 8);

            _simCompute.Dispatch(KERNEL_PAINT, groupsX, groupsY, 1);
        }

        // --- Command Recording ---

        private void RecordPrepareCommands(CommandBuffer cmd) {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            cmd.DispatchCompute(_simCompute, KERNEL_PREP, groupsX, groupsY, 1);
        }

        private void RecordVisualizeCommands(CommandBuffer cmd, int cursorX, int cursorY) {
            var visConfig = new SandBindings.VisConfigBuffer {
                CursorPos = new int2(cursorX, cursorY),
                CursorRadius = (uint)_paintRadius,
                CursorMaterial = (uint)_selectedIndex,
                LightEnabled = !_heatView && _lighting.LightEnabled ? 1u : 0u,
                BloomIntensity = _heatView ? 0f : _lighting.EffectiveBloomIntensity,
            };

            var staging = new NativeArray<SandBindings.VisConfigBuffer>(1, Allocator.Temp);
            staging[0] = visConfig;
            _visConfigBuffer.SetData(staging);

            cmd.SetComputeConstantBufferParam(_visCompute, SandBindings.ID_CB_VIS_CONFIG, _visConfigBuffer, 0, Marshal.SizeOf<SandBindings.VisConfigBuffer>());

            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 8);

            if (_heatView) {
                cmd.SetComputeBufferParam(_visCompute, KERNEL_VIS_HEAT, SandBindings.ID_SIM_DATA, _simData);
                cmd.SetComputeBufferParam(_visCompute, KERNEL_VIS_HEAT, SandBindings.ID_SIM_HEAT, _simHeat);
                cmd.SetComputeBufferParam(_visCompute, KERNEL_VIS_HEAT, SandBindings.ID_MATERIALS, _materialBuffer);
                cmd.SetComputeTextureParam(_visCompute, KERNEL_VIS_HEAT, SandBindings.ID_VIS_TEXTURE, _visTexture);
                cmd.SetComputeTextureParam(_visCompute, KERNEL_VIS_HEAT, SandBindings.ID_HEAT_GRAD, _heatGradTexture);
                cmd.DispatchCompute(_visCompute, KERNEL_VIS_HEAT, groupsX, groupsY, 1);
            } else {
                cmd.SetComputeBufferParam(_visCompute, KERNEL_VIS, SandBindings.ID_SIM_DATA, _simData);
                cmd.SetComputeBufferParam(_visCompute, KERNEL_VIS, SandBindings.ID_SIM_HEAT, _simHeat);
                cmd.SetComputeBufferParam(_visCompute, KERNEL_VIS, SandBindings.ID_MATERIALS, _materialBuffer);
                cmd.SetComputeTextureParam(_visCompute, KERNEL_VIS, SandBindings.ID_VIS_TEXTURE, _visTexture);
                cmd.SetComputeTextureParam(_visCompute, KERNEL_VIS, SandBindings.ID_HEAT_GRAD, _heatGradTexture);
                cmd.SetComputeTextureParam(_visCompute, KERNEL_VIS, SandBindings.ID_LIGHT_TEXTURE_READ, _lightTexture);

                var bloomTexture = _lighting.BloomTexture;
                if (bloomTexture)
                    cmd.SetComputeTextureParam(_visCompute, KERNEL_VIS, SandBindings.ID_BLOOM_TEXTURE_READ, bloomTexture);

                cmd.DispatchCompute(_visCompute, KERNEL_VIS, groupsX, groupsY, 1);
            }
        }

        private void RecordSimulateCommands(CommandBuffer cmd) {
            var groupsX = Mathf.CeilToInt((float)_visTexture.width / 2f / 8);
            var groupsY = Mathf.CeilToInt((float)_visTexture.height / 2f / 8);

            for (var i = 0; i < _effectiveSteps; i++) {
                var offset = (_simFrame * _effectiveSteps + i) & 3;
                for (var p = 0; p < 4; p++) {
                    cmd.SetComputeIntParam(_simCompute, SandBindings.ID_SIM_STEP, _stepOffset + i);
                    cmd.SetComputeIntParam(_simCompute, SandBindings.ID_SIM_PASS, (p + offset) & 3);
                    cmd.DispatchCompute(_simCompute, KERNEL_SIM, groupsX, groupsY, 1);
                }
                
                // Run heat diffusion in bursts with red/black checkerboard.
                // Dispatched at half width — each thread remaps to its checkerboard cell.
                if ((i % _heatStepInterval) == 0) {
                    var hGroupsX = Mathf.CeilToInt((float)_visTexture.width / 2f / 8);
                    var hGroupsY = Mathf.CeilToInt((float)_visTexture.height / 8);
                    for (var b = 0; b < _heatBurstCount; b++) {
                        cmd.SetComputeIntParam(_simCompute, SandBindings.ID_HEAT_PARITY, 0);
                        cmd.DispatchCompute(_simCompute, KERNEL_HEAT, hGroupsX, hGroupsY, 1);
                        cmd.SetComputeIntParam(_simCompute, SandBindings.ID_HEAT_PARITY, 1);
                        cmd.DispatchCompute(_simCompute, KERNEL_HEAT, hGroupsX, hGroupsY, 1);
                    }
                }
            }
        }
    }
}
