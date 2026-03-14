---
name: Architecture status and double-buffer plan
description: Current sim architecture (single-buffer CAS) and planned move to double-buffering to fix bias/race issues
type: project
---

Completed material properties refactor: struct-driven `MaterialProperties` buffer on GPU with per-material weight/drag/fluidity/density/color. Global `_Gravity` uniform scales per-material `weight`. Terminal velocity emerges from gravity/drag equilibrium.

**Current architecture:** Single-buffer red/black checkerboard with CAS swaps. Has fundamental limitations:
- Post-CAS non-atomic write race (diagonal same-pass neighbors can clobber `set_cell_data(from, cell_to)`)
- Read-order bias from thread scheduling (any pre-check of neighbor state observes evaluation order via `mx` mapping)
- Fluid spread forced to pick one random direction per step to avoid bias, halving effective spread rate

**Why:** These issues kept producing directional biases that couldn't be fully canceled by the `mx` reversal. The root cause was found: having two `try_move` calls in fluid spread caused strong bias; but even single `try_move` with pre-read checks introduced subtler bias from evaluation order.

**Planned next step:** Move to double-buffering (read from A, write to B, swap each step) with push/pull model. Eliminates CAS, races, and read-order bias. Can safely read all neighbors and make informed movement decisions.

**How to apply:** This is a significant architectural change. Keep checkerboard or adopt a new conflict resolution strategy. The material properties system, bit-packing, Bresenham stepping, and kernel structure can largely be preserved.
