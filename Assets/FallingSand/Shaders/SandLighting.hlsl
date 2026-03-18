#pragma once

#include "SandUtil.hlsl"

// ============================================================================
// Shared lighting functions and declarations.
// Used by SandLighting.compute for the light/emission/blur/bloom kernels.
// ============================================================================

// --- Constant Buffers ---

cbuffer SimConfig {
    uint2 Dimensions;
    uint  StepsPerFrame;
    uint  Gravity;
    uint  CurrentFrame;
};

cbuffer LightConfig {
    float4 LightColor;
    float4 AmbientColor;    
    int2   LightDir;        
    uint   LightMaxSteps;   
    uint   LightDownscale;  
    uint   EmissionMaxSteps;
    uint   BloomSteps;      
    uint2  LightDimensions; 
};

// Buffers.
StructuredBuffer<MaterialProperties> _Materials;

RWStructuredBuffer<uint> _SimData;
RWStructuredBuffer<float> _SimHeat;

RWTexture2D<float4> _LightTexture;
RWTexture2D<float4> _LightTextureTemp;

RWTexture2D<float4> _BloomTexture;

// Shared helpers that depend on sim buffers.
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

// Blackbody radiation approximation (Tanner Helland fit).
// Returns emission RGB that ramps from dull red at ~500C to white-hot.
float3 blackbody_emission(float temp_celsius) {
    if (temp_celsius < 500.0) return 0;

    const float t = (temp_celsius + 273.0) / 100.0;

    float3 color;
    color.r = (t <= 66.0) ? 1.0 : saturate(1.292 * pow(t - 60.0, -0.1332));
    color.g = (t <= 66.0) ? saturate(0.39 * log(t) - 0.632)
                           : saturate(1.129 * pow(t - 60.0, -0.0755));
    if (t <= 19.0)       color.b = 0.0;
    else if (t <= 66.0)  color.b = saturate(0.543 * log(t - 10.0) - 1.196);
    else                 color.b = 1.0;

    const float intensity = saturate((temp_celsius - 500.0) / 1500.0);
    return color * intensity * intensity;
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
        
        // Stop marching if we're basically "out of light".
        if (light.x + light.y + light.z < 0.01) {
            break;
        }
    }

    return max(light, ambient_color);
}

// 5-tap Gaussian weights (sigma ~1.4).
static const float _BlurWeights[5] = { 0.06136, 0.24477, 0.38774, 0.24477, 0.06136 };
