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
RWTexture2D<float4> _VisTexture;

Texture2D<float4> _LightTextureRead;
SamplerState sampler_LightTextureRead;
Texture2D<float4> _BloomTextureRead;
SamplerState sampler_BloomTextureRead;

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

float3 tonemap_bloom(float3 c) {
    return c / (1.0 + c);
}
