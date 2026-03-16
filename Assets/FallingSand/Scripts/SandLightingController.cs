using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Dispatches lighting compute passes for direct lighting, emission, blur, and visualization.
    /// </summary>
    public class SandLightingController : MonoBehaviour {
        [SerializeField] private ComputeShader _compute;

        [Header("Lighting")]
        [SerializeField] [Range(0f, 360f)] private float _lightAngle = 60f;
        [SerializeField] private Color _lightColor = Color.white;
        [SerializeField] [Range(0f, 5f)] private float _lightIntensity = 2f;
        [SerializeField] private Color _ambientColor = new(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] [Range(16, 1024)] private int _lightMaxSteps = 64;
        [SerializeField] [Range(1, 4)] private int _lightDownscale = 3;
        [SerializeField] [Range(1, 8)] private int _lightBlurPasses = 3;
        [SerializeField] private bool _lightEnabled = true;
        [SerializeField] [Range(8, 256)] private int _emissionMaxSteps = 32;

        [Header("Bloom")]
        [SerializeField] private bool _bloomEnabled = true;
        [SerializeField] [Range(0f, 2f)] private float _bloomIntensity = 0.3f;
        [SerializeField] [Range(8, 128)] private int _bloomSteps = 48;
        [SerializeField] [Range(0, 8)] private int _bloomBlurPasses = 3;

        // Defaults captured from inspector before PlayerPrefs overrides.
        private float _defaultAngle;
        private float _defaultIntensity;
        private Color _defaultLightColor;
        private Color _defaultAmbientColor;
        private bool _defaultEnabled;
        private bool _defaultBloomEnabled;
        private int _defaultDownscale;

        // Bloom textures (owned by this controller).
        private RenderTexture _bloomTexture;
        private RenderTexture _bloomTextureTemp;

        // Kernel handles.
        private int KERNEL_LIGHT;
        private int KERNEL_EMISSION;
        private int KERNEL_BLUR_H;
        private int KERNEL_BLUR_V;
        private int KERNEL_BLOOM_GATHER;
        private int KERNEL_VIS;

        // Shader property IDs.
        // TODO: At this point we might want to just make a LightParams buffer for some of these.
        private static readonly int ID_SIM_WIDTH            = Shader.PropertyToID("_SimWidth");
        private static readonly int ID_SIM_HEIGHT           = Shader.PropertyToID("_SimHeight");
        private static readonly int ID_SIM_DATA             = Shader.PropertyToID("_SimData");
        private static readonly int ID_MATERIALS            = Shader.PropertyToID("_Materials");
        private static readonly int ID_VIS_TEXTURE          = Shader.PropertyToID("_VisTexture");
        private static readonly int ID_LIGHT_TEXTURE        = Shader.PropertyToID("_LightTexture");
        private static readonly int ID_LIGHT_TEXTURE_TEMP   = Shader.PropertyToID("_LightTextureTemp");
        private static readonly int ID_LIGHT_TEXTURE_READ   = Shader.PropertyToID("_LightTextureRead");
        private static readonly int ID_LIGHT_WIDTH          = Shader.PropertyToID("_LightWidth");
        private static readonly int ID_LIGHT_HEIGHT         = Shader.PropertyToID("_LightHeight");
        private static readonly int ID_LIGHT_DIR_X          = Shader.PropertyToID("_LightDirX");
        private static readonly int ID_LIGHT_DIR_Y          = Shader.PropertyToID("_LightDirY");
        private static readonly int ID_LIGHT_COLOR          = Shader.PropertyToID("_LightColor");
        private static readonly int ID_AMBIENT_COLOR        = Shader.PropertyToID("_AmbientColor");
        private static readonly int ID_LIGHT_MAX_STEPS      = Shader.PropertyToID("_LightMaxSteps");
        private static readonly int ID_LIGHT_DOWNSCALE      = Shader.PropertyToID("_LightDownscale");
        private static readonly int ID_EMISSION_MAX_STEPS   = Shader.PropertyToID("_EmissionMaxSteps");
        private static readonly int ID_LIGHT_ENABLED        = Shader.PropertyToID("_LightEnabled");
        private static readonly int ID_BLOOM_INTENSITY      = Shader.PropertyToID("_BloomIntensity");
        private static readonly int ID_BLOOM_STEPS          = Shader.PropertyToID("_BloomSteps");
        private static readonly int ID_BLOOM_TEXTURE        = Shader.PropertyToID("_BloomTexture");
        private static readonly int ID_BLOOM_TEXTURE_READ   = Shader.PropertyToID("_BloomTextureRead");
        private static readonly int ID_CURSOR_X             = Shader.PropertyToID("_CursorX");
        private static readonly int ID_CURSOR_Y             = Shader.PropertyToID("_CursorY");
        private static readonly int ID_CURSOR_R             = Shader.PropertyToID("_CursorR");
        private static readonly int ID_CURSOR_MAT           = Shader.PropertyToID("_CursorMat");

        // --- Public API ---

        public bool IsReady => _compute;

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

        public bool LightEnabled {
            get => _lightEnabled;
            set => _lightEnabled = value;
        }

        public bool BloomEnabled {
            get => _bloomEnabled;
            set => _bloomEnabled = value;
        }

        public int LightDownscale {
            get => _lightDownscale;
            set => _lightDownscale = value;
        }

        // --- Initialization ---

        private void Awake() {
            // Snapshot inspector defaults before any Start/LoadSettings can override them.
            _defaultEnabled = _lightEnabled;
            _defaultAngle = _lightAngle;
            _defaultIntensity = _lightIntensity;
            _defaultLightColor = _lightColor;
            _defaultAmbientColor = _ambientColor;
            _defaultBloomEnabled = _bloomEnabled;
            _defaultDownscale = _lightDownscale;
        }

        public void Initialize() {
            KERNEL_LIGHT        = _compute.FindKernel("Light");
            KERNEL_EMISSION     = _compute.FindKernel("Emission");
            KERNEL_BLUR_H       = _compute.FindKernel("BlurH");
            KERNEL_BLUR_V       = _compute.FindKernel("BlurV");
            KERNEL_BLOOM_GATHER = _compute.FindKernel("BloomGather");
            KERNEL_VIS          = _compute.FindKernel("Visualize");
        }

        public void ResetToDefaults() {
            _lightEnabled   = _defaultEnabled;
            _lightAngle     = _defaultAngle;
            _lightIntensity = _defaultIntensity;
            _lightColor     = _defaultLightColor;
            _ambientColor   = _defaultAmbientColor;
            _bloomEnabled   = _defaultBloomEnabled;
            _lightDownscale = _defaultDownscale;
        }

        public void BindMaterials(ComputeBuffer materialBuffer) {
            _compute.SetBuffer(KERNEL_LIGHT, ID_MATERIALS, materialBuffer);
            _compute.SetBuffer(KERNEL_EMISSION, ID_MATERIALS, materialBuffer);
            _compute.SetBuffer(KERNEL_BLOOM_GATHER, ID_MATERIALS, materialBuffer);
            _compute.SetBuffer(KERNEL_VIS, ID_MATERIALS, materialBuffer);
        }

        // --- Rendering ---

        public void Render(
            ComputeBuffer simData,
            RenderTexture visTexture,
            RenderTexture lightTexture,
            RenderTexture lightTextureTemp,
            int simFrame,
            int cursorX, int cursorY, int cursorR, int cursorMat
        ) {
            _compute.SetInt(ID_SIM_WIDTH, visTexture.width);
            _compute.SetInt(ID_SIM_HEIGHT, visTexture.height);

            if (_lightEnabled) {
                EnsureBloomTextures(lightTexture.width, lightTexture.height);
                var lightCadence = Mathf.Max(1, Mathf.RoundToInt(1f / (60f * Time.unscaledDeltaTime)));
                if (simFrame % lightCadence == 0) {
                    DispatchLight(simData, lightTexture);
                    DispatchEmission(simData, lightTexture);
                    for (var b = 0; b < _lightBlurPasses; b++)
                        DispatchBlur(lightTexture, lightTextureTemp);

                    if (_bloomEnabled) {
                        DispatchBloomGather(simData, lightTexture);
                        for (var b = 0; b < _bloomBlurPasses; b++)
                            DispatchBlur(_bloomTexture, _bloomTextureTemp);
                    }
                }
            }

            _compute.SetInt(ID_LIGHT_ENABLED, _lightEnabled ? 1 : 0);
            _compute.SetFloat(ID_BLOOM_INTENSITY, _lightEnabled && _bloomEnabled ? _bloomIntensity : 0f);

            DispatchVisualize(simData, visTexture, lightTexture, cursorX, cursorY, cursorR, cursorMat);
        }

        // --- Dispatch ---

        private void DispatchLight(ComputeBuffer simData, RenderTexture lightTexture) {
            var groupsX = Mathf.CeilToInt((float)lightTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)lightTexture.height / 8);

            var rad = _lightAngle * Mathf.Deg2Rad;
            _compute.SetInt(ID_LIGHT_DIR_X, Mathf.RoundToInt(Mathf.Cos(rad) * 16));
            _compute.SetInt(ID_LIGHT_DIR_Y, Mathf.RoundToInt(Mathf.Sin(rad) * 16));

            var lc = _lightColor.linear;
            _compute.SetVector(ID_LIGHT_COLOR, new Vector4(lc.r * _lightIntensity, lc.g * _lightIntensity, lc.b * _lightIntensity, 1f));
            
            var ac = _ambientColor.linear;
            _compute.SetVector(ID_AMBIENT_COLOR, new Vector4(ac.r, ac.g, ac.b, 1f));
            
            _compute.SetInt(ID_LIGHT_MAX_STEPS, _lightMaxSteps);
            _compute.SetInt(ID_LIGHT_DOWNSCALE, _lightDownscale);

            _compute.SetBuffer(KERNEL_LIGHT, ID_SIM_DATA, simData);
            _compute.SetTexture(KERNEL_LIGHT, ID_LIGHT_TEXTURE, lightTexture);

            _compute.Dispatch(KERNEL_LIGHT, groupsX, groupsY, 1);
        }

        private void DispatchEmission(ComputeBuffer simData, RenderTexture lightTexture) {
            var groupsX = Mathf.CeilToInt((float)lightTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)lightTexture.height / 8);

            _compute.SetInt(ID_EMISSION_MAX_STEPS, _emissionMaxSteps);
            _compute.SetInt(ID_LIGHT_DOWNSCALE, _lightDownscale);

            _compute.SetBuffer(KERNEL_EMISSION, ID_SIM_DATA, simData);
            _compute.SetTexture(KERNEL_EMISSION, ID_LIGHT_TEXTURE, lightTexture);

            _compute.Dispatch(KERNEL_EMISSION, groupsX, groupsY, 1);
        }

        private void DispatchBlur(RenderTexture lightTexture, RenderTexture lightTextureTemp) {
            var groupsX = Mathf.CeilToInt((float)lightTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)lightTexture.height / 8);

            _compute.SetInt(ID_LIGHT_WIDTH, lightTexture.width);
            _compute.SetInt(ID_LIGHT_HEIGHT, lightTexture.height);

            _compute.SetTexture(KERNEL_BLUR_H, ID_LIGHT_TEXTURE, lightTexture);
            _compute.SetTexture(KERNEL_BLUR_H, ID_LIGHT_TEXTURE_TEMP, lightTextureTemp);
            _compute.Dispatch(KERNEL_BLUR_H, groupsX, groupsY, 1);

            _compute.SetTexture(KERNEL_BLUR_V, ID_LIGHT_TEXTURE_TEMP, lightTextureTemp);
            _compute.SetTexture(KERNEL_BLUR_V, ID_LIGHT_TEXTURE, lightTexture);
            _compute.Dispatch(KERNEL_BLUR_V, groupsX, groupsY, 1);
        }

        private void DispatchBloomGather(ComputeBuffer simData, RenderTexture lightTexture) {
            var groupsX = Mathf.CeilToInt((float)lightTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)lightTexture.height / 8);

            _compute.SetInt(ID_BLOOM_STEPS, _bloomSteps);
            _compute.SetInt(ID_LIGHT_DOWNSCALE, _lightDownscale);

            _compute.SetBuffer(KERNEL_BLOOM_GATHER, ID_SIM_DATA, simData);
            _compute.SetTexture(KERNEL_BLOOM_GATHER, ID_BLOOM_TEXTURE, _bloomTexture);

            _compute.Dispatch(KERNEL_BLOOM_GATHER, groupsX, groupsY, 1);
        }

        private void DispatchVisualize(
            ComputeBuffer simData, RenderTexture visTexture, RenderTexture lightTexture,
            int cursorX, int cursorY, int cursorR, int cursorMat
        ) {
            var groupsX = Mathf.CeilToInt((float)visTexture.width / 8);
            var groupsY = Mathf.CeilToInt((float)visTexture.height / 8);

            _compute.SetInt(ID_CURSOR_X, cursorX);
            _compute.SetInt(ID_CURSOR_Y, cursorY);
            _compute.SetInt(ID_CURSOR_R, cursorR);
            _compute.SetInt(ID_CURSOR_MAT, cursorMat);

            _compute.SetBuffer(KERNEL_VIS, ID_SIM_DATA, simData);
            _compute.SetTexture(KERNEL_VIS, ID_VIS_TEXTURE, visTexture);
            _compute.SetTexture(KERNEL_VIS, ID_LIGHT_TEXTURE_READ, lightTexture);
            if (_bloomTexture)
                _compute.SetTexture(KERNEL_VIS, ID_BLOOM_TEXTURE_READ, _bloomTexture);

            _compute.Dispatch(KERNEL_VIS, groupsX, groupsY, 1);
        }

        // --- Bloom texture lifecycle ---

        private void EnsureBloomTextures(int width, int height) {
            if (_bloomTexture && _bloomTexture.width == width && _bloomTexture.height == height)
                return;

            ReleaseBloomTextures();

            _bloomTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _bloomTexture.Create();

            _bloomTextureTemp = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf) {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _bloomTextureTemp.Create();
        }

        private void ReleaseBloomTextures() {
            if (_bloomTexture) { Destroy(_bloomTexture); _bloomTexture = null; }
            if (_bloomTextureTemp) { Destroy(_bloomTextureTemp); _bloomTextureTemp = null; }
        }

        private void OnDisable() {
            ReleaseBloomTextures();
        }
    }
}
