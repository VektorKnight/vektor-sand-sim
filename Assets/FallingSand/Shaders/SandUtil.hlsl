#pragma once

// ============================================================================
// Shared utilities for the falling-sand simulation.
//
// Cell data layout (32-bit uint):
//   [4:0]   Material ID   (MAT_ID_BITS)
//   [7:5]   State flags   (3 bits, see MOVED_* constants)
//   [15:8]  X velocity    (8-bit signed, -127 to 127)
//   [23:16] Y velocity    (8-bit signed, -127 to 127)
//   [25:24] Color variant (2 bits, 0-3, set at paint time)
//   [31:26] Unused
//
// Interaction LUTs:
//   Reactions - MAX_MATERIALS * MAX_MATERIALS, indexed [source * MAX_MATERIALS + trigger]
//   Decay     - MAX_MATERIALS LUT, indexed by material ID
//   Probability of zero in either table indicates "no rule".
// ============================================================================

// Material ID sizing.
#define MAT_ID_BITS 5
#define MAT_ID_MASK ((1u << MAT_ID_BITS) - 1)
#define MAX_MATERIALS (1u << MAT_ID_BITS)

// Bit offsets for packed cell fields.
#define OFFSET_FLAGS 5
#define OFFSET_X_VEL 8
#define OFFSET_Y_VEL 16
#define OFFSET_VARIANT 24

// ID:0 is expected to be empty/air.
// C# side could violate this, but it's considered a programmer error to do so.
#define ID_EMPTY 0

// State flags.
#define MOVED_LAST_STEP 1   // Cell was moved in a prior color pass this step.
#define MOVED_LAST_FRAME 2  // Cell moved at least once during the previous frame.

// Neighbor offset table.
// First 4 are cardinal, last 4 are diagonal.
static const int2 NEIGHBORS[8] = {
    int2(0, 1), int2(1, 0), int2(0, -1), int2(-1, 0),
    int2(1, 1), int2(1, -1), int2(-1, -1), int2(-1, 1)
};

// Material properties for a particle type.
// Values are expected to be valid following sanitization on the C# side.
// Note that the first index in the buffer is expected to be empty/air.
struct MaterialProperties {
    uint fluidity;
    uint density;
    int weight;     // Scales global _Gravity. 256 = normal, 0 = weightless, negative = buoyant.
    uint drag;      // Proportional velocity decay. Terminal velocity emerges from weight/drag balance.

    float variation;    // Brightness spread for color variants. 0 = flat, >0 = per-particle variation.
    float4 extinction;  // Per-channel light absorption (RGB). Derived from opacity and color on CPU.
    float4 color;
    float4 emission;    // Pre-multiplied self-illumination (RGB * intensity).
};

// Reaction LUT entry.
struct ReactionEntry {
    uint result;
    float probability;
};

// Decay LUT entry indexed by material ID.
struct DecayEntry {
    uint result;
    float probability;
};

uint pack_i8(uint packed, int value, uint offset) {
    return (packed & ~(0xFF << offset)) | (((uint)value & 0xFF) << offset);
}

int unpack_i8(uint packed, uint offset) {
    return (int)(packed << (24 - offset)) >> 24;
}

uint get_mat_id(uint cell) {
    return cell & MAT_ID_MASK;
}

uint set_mat_id(uint cell, uint mat_id) {
    return (cell & ~MAT_ID_MASK) | (mat_id & MAT_ID_MASK);
}

bool is_empty(uint cell) {
    return get_mat_id(cell) == ID_EMPTY;
}

uint get_flags(uint cell) {
    return (cell >> OFFSET_FLAGS) & 0x7;
}

uint set_flags(uint cell, uint flags) {
    return (cell & ~(0x7 << OFFSET_FLAGS)) | ((flags & 0x7) << OFFSET_FLAGS);
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

// Proportional drag decays velocity toward zero scaled by magnitude.
// Higher velocity = more drag.
// Low velocities are naturally unaffected (integer truncation), preventing drag from overpowering weak gravity.
int apply_drag(int vel, int drag) {
    if (vel == 0 || drag == 0) {
        return vel;
    }
    
    int amount = abs(vel) * drag / 256;
    if (amount == 0) {
        return vel;
    }
    
    return vel > 0 ? max(vel - amount, 0) : min(vel + amount, 0);
}

uint add_x_vel(uint cell, int delta) {
    return add_vel(cell, delta, OFFSET_X_VEL);
}

uint add_y_vel(uint cell, int delta) {
    return add_vel(cell, delta, OFFSET_Y_VEL);
}

// Drives most of the random behavior in the simulation.
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