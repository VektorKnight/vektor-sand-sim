#pragma once

#include "SandUtil.hlsl"

// ============================================================================
// Shared declarations for visualization kernels.
// ============================================================================

cbuffer SimConfig {
    uint2 Dimensions;
    uint  StepsPerFrame;
    uint  Gravity;
    uint  CurrentFrame;
};

cbuffer VisConfig {
    int2  CursorPos;
    uint  CursorRadius;
    uint  CursorMaterial;
    uint  LightEnabled;
    float BloomIntensity;
};

StructuredBuffer<MaterialProperties> _Materials;
RWStructuredBuffer<uint> _SimData;
RWStructuredBuffer<float> _SimHeat;
RWTexture2D<float4> _VisTexture;

Texture2D<float4> _LightTextureRead;
SamplerState sampler_LightTextureRead;

Texture2D<float4> _BloomTextureRead;
SamplerState sampler_BloomTextureRead;

// Heat visualization ramp (1D gradient, sampled by normalized temperature).
Texture2D<float4> _HeatRamp;
SamplerState sampler_HeatRamp;

int to_flat(int2 i) {
    return i.x + i.y * (int)Dimensions.x;
}

bool in_bounds(int2 id) {
    return id.x >= 0 && id.x < (int)Dimensions.x &&
           id.y >= 0 && id.y < (int)Dimensions.y;
}

MaterialProperties get_mat_props(uint cell) {
    return _Materials[get_mat_id(cell)];
}

uint get_cell_data(int2 id) {
    return _SimData[to_flat(id)];
}

float get_heat(int2 id) {
    return _SimHeat[to_flat(id)];
}

// Luminance-based Reinhard preserves hue/saturation better than per-channel.
float3 tonemap_bloom(float3 c) {
    const float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
    if (lum <= 0.0) return 0;
    const float mapped = lum / (1.0 + lum);
    return c * (mapped / lum);
}

// Sample the blackbody emission ramp (row 1 of _HeatRamp) for a given temperature.
// Uses sqrt mapping matching the heat view gradient.
float3 blackbody_emission(float temp_celsius) {
    const float linear_t = saturate((temp_celsius - MIN_TEMP) / (MAX_TEMP - MIN_TEMP));
    return _HeatRamp.SampleLevel(sampler_HeatRamp, float2(sqrt(linear_t), 0.75), 0).rgb;
}
