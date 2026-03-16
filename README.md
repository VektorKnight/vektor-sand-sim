ow how # vektor-sand-sim

GPU-accelerated falling sand simulation in Unity. Runs entirely on compute shaders with four-color tiling for race-free particle movement.

## Features

- Multiple materials with distinct physics (solids, granulars, fluids, gases)
- Material reactions and decay (lava melts ice, fire spreads through oil, etc.)
- Pure-integer DDA ray-marched directional lighting
- Radial emission for self-illuminating materials (lava, fire, ember)
- Separable Gaussian blur on the combined light buffer
- Adaptive step scaling for consistent speed across refresh rates (experimental, 60Hz is the default)
- In-game UI for material selection, lighting controls, and resolution settings

## Materials

| ID | Name      | Type                |
|----|-----------|---------------------|
| 0  | Empty     | -                   |
| 1  | Bedrock   | Indestructible      |
| 2  | Stone     | Immovable solid     |
| 3  | Glass     | Transparent solid   |
| 4  | Wood      | Flammable solid     |
| 5  | Ice       | Immovable solid     |
| 6  | Sand      | Light granular      |
| 7  | Gravel    | Heavy granular      |
| 8  | Snow      | Light powder        |
| 9  | Gunpowder | Granular flammable  |
| 10 | Water     | Fluid               |
| 11 | Oil       | Viscous fluid       |
| 12 | Acid      | Corrosive fluid     |
| 13 | Lava      | Viscous emissive    |
| 14 | Fire      | Buoyant emissive    |
| 15 | Ember     | Falling emissive    |
| 16 | Plasma    | Hot gas             |
| 17 | Steam     | Buoyant gas         |
| 18 | Smoke     | Buoyant gas         |

5-bit IDs, 32 max. Materials, reactions, and decay are defined in `MaterialTable.cs`.
Some bits are still unused for later expansion.

## Controls

| Input            | Action                       |
|------------------|------------------------------|
| Left click       | Paint selected material      |
| Right click      | Erase                        |
| Scroll wheel     | Change brush radius          |
| Shift + scroll   | Cycle material               |
| Insert           | Toggle replace mode          |
| F12              | Save screenshot              |

## Architecture

### Simulation (SandSimulation.compute)

Single 32-bit uint per cell.

```
[4:0]   Material ID (5 bits)
[7:5]   State flags (3 bits)
[15:8]  X velocity (signed 8-bit)
[23:16] Y velocity (signed 8-bit)
[25:24] Color variant (2 bits)
[31:26] Unused
```

Three kernels:
- **Paint** - stamps brush circles into the grid
- **PrepareFrame** - clears flags, applies gravity/drag, runs reactions and decay
- **Simulate** - four-color tiled movement with CAS for cardinal contention

Movement uses Bresenham-style gating to distribute velocity across steps. Density swapping lets fluids pass through each other based on relative density.

### Lighting (SandLighting.compute)

Post-process pass on the simulation data. Runs at a lower configurable resolution for performance.

- **Light** - DDA ray-march toward the directional light source
- **Emission** - 8-direction radial gather from emissive cells, accumulated into the light buffer
- **BlurH/BlurV** - separable 5-tap Gaussian on the combined buffer
- **Visualize** - composites material color * light, adds self-emission, and draws cursor

### C# Side

| File                       | Role                                            |
|----------------------------|--------------------------------------------------|
| SandSimulation.cs          | Sim dispatch, input, buffers, resolution, timing |
| SandLightingController.cs  | Lighting dispatch, lighting parameters           |
| SandSimUI.cs               | Programmatic uGUI, settings persistence          |
| MaterialTable.cs           | Material definitions, reactions, decay rules      |
| MaterialDefinition.cs      | Material property class                          |
| MaterialProperties.cs      | GPU-side struct, extinction/emission derivation   |

## Requirements

- Unity 2022.3+
- Compute shader support (any modern GPU)

## Setup

1. Open the project in Unity
2. Open `Assets/FallingSand/SandSimulation.unity`
3. Hit play

Runs best as a standalone build. The editor doesn't respect `Screen.SetResolution` so resolution scaling and window sizing won't behave correctly in play mode.
