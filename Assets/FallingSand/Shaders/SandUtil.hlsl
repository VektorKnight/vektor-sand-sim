#pragma once

// ============================================================================
// Cell data layout (32-bit uint):
//   [3:0]   Material ID   (4 bits, see ID_* constants)
//   [7:4]   State flags   (4 bits, see MOVED_* constants)
//   [15:8]  X velocity    (8-bit signed, -127 to 127)
//   [23:16] Y velocity    (8-bit signed, -127 to 127)
//   [25:24] Color variant (2 bits, 0-3, set at paint time)
//   [31:26] Unused
// ============================================================================

// Bit offsets for packed cell fields.
#define OFFSET_FLAGS 4
#define OFFSET_X_VEL 8
#define OFFSET_Y_VEL 16
#define OFFSET_VARIANT 24

// Material IDs.
#define ID_EMPTY 0

// State flags.
#define MOVED_LAST_STEP 1   // Cell was moved in a prior red/black pass this step.
#define MOVED_LAST_FRAME 2  // Cell moved at least once during the previous frame.

// Material properties for a particle type.
// Values are expected to be valid following sanitization on the C# side.
// Note that the first index in the buffer is expected to be empty/air.
struct MaterialProperties {
    uint fluidity;
    uint density;
    int weight;     // Scales global _Gravity. 256 = normal, 0 = weightless, negative = buoyant.
    uint drag;      // Proportional velocity decay. Terminal vel emerges from weight/drag balance.

    float variation; // Brightness spread for color variants. 0 = flat, >0 = per-particle variation.
    float extinction; // Light absorption per cell for ray-marching. 0 = transparent, higher = more opaque.
    float4 color;
};

uint pack_i8(uint packed, int value, uint offset) {
    return (packed & ~(0xFF << offset)) | (((uint)value & 0xFF) << offset);
}

int unpack_i8(uint packed, uint offset) {
    return (int)(packed << (24 - offset)) >> 24;
}

uint get_mat_id(uint cell) {
    return cell & 0xF;
}

bool is_empty(uint cell) {
    return get_mat_id(cell) == ID_EMPTY;
}

uint get_flags(uint cell) {
    return (cell >> OFFSET_FLAGS) & 0xF;
}

uint set_flags(uint cell, uint flags) {
    return (cell & ~(0xF << OFFSET_FLAGS)) | ((flags & 0xF) << OFFSET_FLAGS);
}

bool has_flag(uint cell, uint flag) {
    return (get_flags(cell) & flag) != 0;
}

uint add_flag(uint cell, uint flag) {
    return set_flags(cell, get_flags(cell) | flag);
}

uint clear_flag(uint cell, uint flag) {
    return set_flags(cell, get_flags(cell) & ~flag);
}

uint add_vel(uint cell, int delta, uint offset) {
    int vel = clamp(unpack_i8(cell, offset) + delta, -127, 127);
    return pack_i8(cell, vel, offset);
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

uint get_variant(uint cell) {
    return (cell >> OFFSET_VARIANT) & 0x3;
}

uint set_variant(uint cell, uint variant) {
    return (cell & ~(0x3 << OFFSET_VARIANT)) | ((variant & 0x3) << OFFSET_VARIANT);
}

// Proportional drag: decays velocity toward zero scaled by magnitude.
// Higher velocity = more drag removed, like air resistance.
// Low velocities are naturally unaffected (integer truncation),
// preventing drag from overpowering weak gravity.
int apply_drag(int vel, int drag) {
    if (vel == 0 || drag == 0) return vel;
    int amount = abs(vel) * drag / 256;
    if (amount == 0) return vel;
    return vel > 0 ? max(vel - amount, 0) : min(vel + amount, 0);
}

uint add_x_vel(uint cell, int delta) {
    return add_vel(cell, delta, OFFSET_X_VEL);
}

uint add_y_vel(uint cell, int delta) {
    return add_vel(cell, delta, OFFSET_Y_VEL);
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