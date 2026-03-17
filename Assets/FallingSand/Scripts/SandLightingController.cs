using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FallingSand.Scripts {
    /// <summary>
    /// Records lighting compute passes into a CommandBuffer.
    /// Owns the LightConfig cbuffer, bloom textures, and lighting settings.
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

        // Defaults captured from inspector before overrides.
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

        // LightConfig cbuffer.
        private ComputeBuffer _lightConfigBuffer;

        // Kernel handles.
        private int KERNEL_LIGHT;
        private int KERNEL_EMISSION;
        private int KERNEL_BLUR_H;
        private int KERNEL_BLUR_V;
        private int KERNEL_BLOOM_GATHER;

        // --- Public API ---

        public ComputeShader Compute => _compute;
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

        public float EffectiveBloomIntensity => _lightEnabled && _bloomEnabled ? _bloomIntensity : 0f;
        public RenderTexture BloomTexture => _bloomTexture;

        // --- Initialization ---

        private void Awake() {
            _defaultEnabled = _lightEnabled;
            _defaultAngle = _lightAngle;
            _defaultIntensity = _lightIntensity;
            _defaultLightColor = _lightColor;
            _defaultAmbientColor = _ambientColor;
            _defaultBloomEnabled = _bloomEnabled;
            _defaultDownscale = _lightDownscale;
        }

        public void Initialize() {
            KERNEL_LIGHT        = _compute.FindKernel(SandBindings.KERNEL_LIGHT);
            KERNEL_EMISSION     = _compute.FindKernel(SandBindings.KERNEL_EMISSION);
            KERNEL_BLUR_H       = _compute.FindKernel(SandBindings.KERNEL_BLUR_H);
            KERNEL_BLUR_V       = _compute.FindKernel(SandBindings.KERNEL_BLUR_V);
            KERNEL_BLOOM_GATHER = _compute.FindKernel(SandBindings.KERNEL_BLOOM_GATHER);

            _lightConfigBuffer = new ComputeBuffer(
                1,
                Marshal.SizeOf<SandBindings.LightConfigBuffer>(),
                ComputeBufferType.Constant
            );

            _lightConfigBuffer.SetData(new[] { default(SandBindings.LightConfigBuffer) });
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

        public void Cleanup() {
            ReleaseBloomTextures();

            if (_lightConfigBuffer != null && _lightConfigBuffer.IsValid()) {
                _lightConfigBuffer.Release();
                _lightConfigBuffer = null;
            }
        }

        private void OnDisable() {
            Cleanup();
        }

        // --- Record ---

        public void Record(
            CommandBuffer cmd,
            ComputeBuffer simData,
            ComputeBuffer materialBuffer,
            RenderTexture lightTexture,
            RenderTexture lightTextureTemp,
            int simFrame
        ) {
            if (!_lightEnabled) {
                return;
            }

            EnsureBloomTextures(lightTexture.width, lightTexture.height);

            var lightCadence = Mathf.Max(1, Mathf.RoundToInt(1f / (60f * Time.unscaledDeltaTime)));
            if (simFrame % lightCadence != 0) {
                return;
            }

            // Build and stage LightConfig cbuffer.
            var rad = _lightAngle * Mathf.Deg2Rad;
            var lc = _lightColor.linear;
            var ac = _ambientColor.linear;

            var lightConfig = new SandBindings.LightConfigBuffer {
                LightColor = new float4(lc.r * _lightIntensity, lc.g * _lightIntensity, lc.b * _lightIntensity, 1f),
                AmbientColor = new float4(ac.r, ac.g, ac.b, 1f),
                
                LightDir = new int2(
                    Mathf.RoundToInt(Mathf.Cos(rad) * 16),
                    Mathf.RoundToInt(Mathf.Sin(rad) * 16)
                ),
                
                LightMaxSteps = (uint)_lightMaxSteps,
                LightDownscale = (uint)_lightDownscale,
                
                EmissionMaxSteps = (uint)_emissionMaxSteps,
                BloomSteps = (uint)_bloomSteps,
                LightDimensions = new uint2((uint)lightTexture.width, (uint)lightTexture.height),
            };

            var staging = new NativeArray<SandBindings.LightConfigBuffer>(1, Allocator.Temp);
            
            staging[0] = lightConfig;
            
            _lightConfigBuffer.SetData(staging);

            // Bind LightConfig cbuffer to lighting compute shader.
            cmd.SetComputeConstantBufferParam(_compute, SandBindings.ID_CB_LIGHT_CONFIG, _lightConfigBuffer, 0, Marshal.SizeOf<SandBindings.LightConfigBuffer>());

            var lightGroupsX = Mathf.CeilToInt((float)lightTexture.width / 8);
            var lightGroupsY = Mathf.CeilToInt((float)lightTexture.height / 8);

            // Bind buffers/textures.
            cmd.SetComputeBufferParam(_compute, KERNEL_LIGHT, SandBindings.ID_SIM_DATA, simData);
            cmd.SetComputeBufferParam(_compute, KERNEL_LIGHT, SandBindings.ID_MATERIAL_TABLE, materialBuffer);
            cmd.SetComputeTextureParam(_compute, KERNEL_LIGHT, SandBindings.ID_LIGHT_TEXTURE, lightTexture);

            // Light dispatch.
            cmd.DispatchCompute(_compute, KERNEL_LIGHT, lightGroupsX, lightGroupsY, 1);

            // Emission dispatch.
            cmd.SetComputeBufferParam(_compute, KERNEL_EMISSION, SandBindings.ID_SIM_DATA, simData);
            cmd.SetComputeBufferParam(_compute, KERNEL_EMISSION, SandBindings.ID_MATERIAL_TABLE, materialBuffer);
            cmd.SetComputeTextureParam(_compute, KERNEL_EMISSION, SandBindings.ID_LIGHT_TEXTURE, lightTexture);
            cmd.DispatchCompute(_compute, KERNEL_EMISSION, lightGroupsX, lightGroupsY, 1);

            // Blur passes on light texture.
            for (var b = 0; b < _lightBlurPasses; b++) {
                RecordBlur(cmd, lightTexture, lightTextureTemp, lightGroupsX, lightGroupsY);
            }

            // Bloom.
            if (_bloomEnabled) {
                cmd.SetComputeBufferParam(_compute, KERNEL_BLOOM_GATHER, SandBindings.ID_SIM_DATA, simData);
                cmd.SetComputeBufferParam(_compute, KERNEL_BLOOM_GATHER, SandBindings.ID_MATERIAL_TABLE, materialBuffer);
                cmd.SetComputeTextureParam(_compute, KERNEL_BLOOM_GATHER, SandBindings.ID_BLOOM_TEXTURE, _bloomTexture);
                cmd.DispatchCompute(_compute, KERNEL_BLOOM_GATHER, lightGroupsX, lightGroupsY, 1);

                var bloomGroupsX = Mathf.CeilToInt((float)_bloomTexture.width / 8);
                var bloomGroupsY = Mathf.CeilToInt((float)_bloomTexture.height / 8);
                for (var b = 0; b < _bloomBlurPasses; b++) {
                    RecordBlur(cmd, _bloomTexture, _bloomTextureTemp, bloomGroupsX, bloomGroupsY);
                }
            }
        }

        private void RecordBlur(CommandBuffer cmd, RenderTexture texture, RenderTexture textureTemp, int groupsX, int groupsY) {
            cmd.SetComputeTextureParam(_compute, KERNEL_BLUR_H, SandBindings.ID_LIGHT_TEXTURE, texture);
            cmd.SetComputeTextureParam(_compute, KERNEL_BLUR_H, SandBindings.ID_LIGHT_TEXTURE_TEMP, textureTemp);
            cmd.DispatchCompute(_compute, KERNEL_BLUR_H, groupsX, groupsY, 1);

            cmd.SetComputeTextureParam(_compute, KERNEL_BLUR_V, SandBindings.ID_LIGHT_TEXTURE_TEMP, textureTemp);
            cmd.SetComputeTextureParam(_compute, KERNEL_BLUR_V, SandBindings.ID_LIGHT_TEXTURE, texture);
            cmd.DispatchCompute(_compute, KERNEL_BLUR_V, groupsX, groupsY, 1);
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
    }
}
