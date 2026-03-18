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

// Attempt at a blackbody radiation approximation.
// Based on Tanner Helland's fit, adapted for Celsius input.
// Returns RGB color and intensity that ramps up from ~500C (dull red glow) to white-hot.
float3 blackbody_emission(float temp_celsius) {
    // No visible glow below ~500C.
    if (temp_celsius < 500.0) return 0;

    const float t = (temp_celsius + 273.0) / 100.0; // hectokelvin

    float3 color;

    // Red.
    color.r = (t <= 66.0) ? 1.0 : saturate(1.292 * pow(t - 60.0, -0.1332));

    // Green.
    color.g = (t <= 66.0) ? saturate(0.39 * log(t) - 0.632)
                           : saturate(1.129 * pow(t - 60.0, -0.0755));

    // Blue.
    if (t <= 19.0)       color.b = 0.0;
    else if (t <= 66.0)  color.b = saturate(0.543 * log(t - 10.0) - 1.196);
    else                 color.b = 1.0;

    // Intensity ramps from 0 at 500C to full at ~2000C.
    const float intensity = saturate((temp_celsius - 500.0) / 1500.0);

    return color * intensity * intensity;
}
