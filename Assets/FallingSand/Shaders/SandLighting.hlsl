#pragma once

#include "SandUtil.hlsl"

// ============================================================================
// Shared lighting functions and declarations.
// Used by SandLighting.compute for the light/emission/blur/vis kernels.
// ============================================================================

// Lighting params.
int _LightDirX;
int _LightDirY;
float4 _LightColor;
float4 _AmbientColor;
int _LightMaxSteps;
int _LightDownscale;
int _LightEnabled;
int _EmissionMaxSteps;

// Bloom params.
float _BloomIntensity;
int _BloomSteps;

// Cursor params for visualization.
int _CursorX;
int _CursorY;
int _CursorR;
uint _CursorMat;

// Blur params.
int _LightWidth;
int _LightHeight;

// Sim dimensions (needed for bounds checks and UV calculation).
int _SimWidth;
int _SimHeight;

// Buffers.
StructuredBuffer<MaterialProperties> _Materials;
RWStructuredBuffer<uint> _SimData;
RWTexture2D<float4> _LightTexture;
RWTexture2D<float4> _LightTextureTemp;
RWTexture2D<float4> _VisTexture;

// Sampled with bilinear filtering in Visualize.
Texture2D<float4> _LightTextureRead;
SamplerState sampler_LightTextureRead;

// Bloom buffer (independent from light buffer).
RWTexture2D<float4> _BloomTexture;
Texture2D<float4> _BloomTextureRead;
SamplerState sampler_BloomTextureRead;

// Shared helpers that depend on sim buffers.
int to_flat(int2 i) {
    return i.x + i.y * _SimWidth;
}

bool in_bounds(int2 id) {
    return id.x >= 0 && id.x < _SimWidth &&
           id.y >= 0 && id.y < _SimHeight;
}

MaterialProperties get_mat_props(uint cell) {
    return _Materials[get_mat_id(cell)];
}

uint get_cell_data(int2 id) {
    return _SimData[to_flat(id)];
}

// DDA ray-march along a direction, accumulating extinction.
// Being almost entirely integer allows us to run a lot of rays/steps with good performance.
// Softer penumbras would be nice but require more rays and some data we might not easily have.
float3 march_dda(int2 start, int2 light_dir, float3 light_color, float3 ambient_color, int max_steps) {
    float3 light = light_color;
    int2 pos = start;
    const int2 s = sign(light_dir);
    const int2 a = abs(light_dir);

    const bool x_major = a.x >= a.y;
    const int major_len = x_major ? a.x : a.y;
    const int minor_len = x_major ? a.y : a.x;

    int accum = 0;

    [loop]
    for (int i = 0; i < max_steps; i++) {
        if (x_major) pos.x += s.x;
        else         pos.y += s.y;

        accum += minor_len;
        if (accum >= major_len) {
            if (x_major) pos.y += s.y;
            else         pos.x += s.x;
            accum -= major_len;
        }

        if (!in_bounds(pos)) {
            break;
        }
        const MaterialProperties mat_pos = get_mat_props(get_cell_data(pos));

        light *= exp(-mat_pos.extinction.rgb);

        if (light.x + light.y + light.z < 0.01) {
            break;
        }
    }

    return max(light, ambient_color);
}

// 5-tap Gaussian weights (sigma ~1.4).
static const float _BlurWeights[5] = { 0.06136, 0.24477, 0.38774, 0.24477, 0.06136 };

// Reinhard per-channel tonemap for bloom.
float3 tonemap_bloom(float3 c) {
    return c / (1.0 + c);
}
