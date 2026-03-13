#pragma once

// ============================================================================
// Cell data layout (32-bit uint):
//   [3:0]   Material ID   (4 bits, see ID_* constants)
//   [7:4]   State flags   (4 bits, see MOVED_* constants)
//   [15:8]  X velocity    (8-bit signed, -127 to 127)
//   [23:16] Y velocity    (8-bit signed, -127 to 127)
//   [31:24] Unused
// ============================================================================

// Bit offsets for packed cell fields.
#define OFFSET_FLAGS 4
#define OFFSET_X_VEL 8
#define OFFSET_Y_VEL 16

// Material IDs.
#define ID_EMPTY 0
#define ID_STONE 1
#define ID_SAND 2
#define ID_WATER 3

// State flags.
#define MOVED_LAST_STEP 1   // Cell was moved in a prior red/black pass this step.
#define MOVED_LAST_FRAME 2  // Cell moved at least once during the previous frame.

// Material properties struct mirrored from the C# side.
struct MaterialProperties {
    uint fluidity;
    uint density;
    int gravity;
    uint terminal_vel;
    
    float4 color;
};

bool is_fluid(uint id) {
    switch (id) {
        case ID_WATER: return true;
        default: return false;
    }
}

uint get_density(uint id) {
    switch (id) {
        case ID_EMPTY:  return 0;
        case ID_STONE:  return 255;
        case ID_SAND:   return 128;
        case ID_WATER:  return 64;
        default: return 0;
    }
}

uint get_gravity(uint mat) {
    switch (mat) {
        case ID_EMPTY:  return 0;
        case ID_STONE:  return 0;
        case ID_SAND:   return 1;
        case ID_WATER:  return 1;
        default: return 0;
    }
}

uint get_terminal_vel(uint mat) {
    switch (mat) {
        case ID_SAND:   return 32;
        case ID_WATER:  return 16;
        default: return 127;
    }
}

float4 get_color(uint mat) {
    switch (mat) {
        case ID_EMPTY:  return float4(0,0,0,1);
        case ID_STONE:  return float4(0.125, 0.125, 0.125, 1);
        case ID_SAND:   return float4(0.5, 0.25, 0.1, 1);
        case ID_WATER:  return float4(0.05, 0.15, 0.9, 1);
        default: return float4(1, 0, 1, 1);
    }
}

uint pack_i8(uint packed, int value, uint offset) {
    return (packed & ~(0xFF << offset)) | (((uint)value & 0xFF) << offset);
}

int unpack_i8(uint packed, uint offset) {
    return (int)(packed << (24 - offset)) >> 24;
}

uint get_material(uint cell) {
    return cell & 0xF;
}

uint set_material(uint cell, uint id) {
    return (cell & ~0xF) | (id & 0xF);
}

uint get_flags(uint cell) {
    return (cell >> OFFSET_FLAGS) & 0xF;
}

uint set_flags(uint cell, uint flags) {
    return (cell & ~(0xF << OFFSET_FLAGS)) | ((flags & 0xF) << OFFSET_FLAGS);
}

int get_x_vel(uint cell) {
    return unpack_i8(cell, OFFSET_X_VEL);
}

uint set_x_vel(uint cell, int vel) {
    return pack_i8(cell, vel, OFFSET_X_VEL);
}

int get_y_vel(uint cell) {
    return unpack_i8(cell, OFFSET_Y_VEL);
}

uint set_y_vel(uint cell, int vel) {
    return pack_i8(cell, vel, OFFSET_Y_VEL);
}

// lowbias32-based spatial hash (https://nullprogram.com/blog/2018/07/31/).
uint hash(uint2 cell, uint frame) {
    uint h = cell.x ^ (cell.y * 747796405u) ^ (frame * 2891336453u);
    h ^= h >> 16;
    h *= 0x7feb352du;
    h ^= h >> 15;
    h *= 0x846ca68bu;
    h ^= h >> 16;
    return h;
}