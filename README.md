# vektor-sand-sim

A GPU-accelerated falling sand sim built in Unity. Four-color tiling keeps parallel threads from stomping each other, Bresenham gating handles variable-speed particles, and a Beer's law raymarch pass does lighting with emission and soft shadows.
Currently on hold pending a port to Rust with wgpu as a backend. While Unity works, it's a lot of baggage and overhead for something like this and iteration has become somewhat annoying because of it. The plan is to bring the Rust port up to parity before continuing feature work and explorations.

## Features

- Multiple materials with distinct physics (solids, granulars, fluids, gases)
- Material reactions and decay (lava melts ice, fire spreads through oil, etc.)
- Fast and pretty raymarched lighting engine
- Radial emission for emissive materials (lava, fire, ember)
- Adaptive step scaling for consistent speed across refresh rates (experimental, 60Hz is the default)
- In-game UI for material selection, lighting controls, and resolution settings

## Materials

| ID | Name      | Type                 |
|----|-----------|----------------------|
| 0  | Empty     | -                    |
| 1  | Bedrock   | Indestructible       |
| 2  | Stone     | Immovable solid      |
| 3  | Glass     | Transparent solid    |
| 4  | Wood      | Flammable solid      |
| 5  | Ice       | Immovable solid      |
| 6  | Sand      | Light granular       |
| 7  | Gravel    | Heavy granular       |
| 8  | Snow      | Light powder         |
| 9  | Gunpowder | Granular flammable   |
| 10 | Water     | Fluid                |
| 11 | Oil       | Viscous fluid        |
| 12 | Acid      | Corrosive fluid      |
| 13 | Lava      | Viscous emissive     |
| 14 | Fire      | Buoyant emissive     |
| 15 | Ember     | Falling emissive     |
| 16 | Plasma    | Hot gas              |
| 17 | Steam     | Buoyant gas          |
| 18 | Smoke     | Buoyant gas          |
| 19 | Algae     | Grows on water|      |
| 20 | Sludge    | Dead biomass, burns. |
| 20 | Copper    | Good heat conductor. |
| 21 | Liquid N2 | Cryogenic fluid.     |

5-bit IDs, 32 max. Materials, reactions, and decay are defined in `MaterialTable.cs`.
Some bits are still unused for later expansion. Could probably bump IDs to 8-bit for 256 total.

## Controls

| Input            | Action                       |
|------------------|------------------------------|
| Left click       | Paint selected material      |
| Right click      | Erase                        |
| Scroll wheel     | Change brush radius          |
| Shift + scroll   | Cycle material               |
| Insert           | Toggle replace mode          |
| F12              | Save screenshot              |
| F3               | Debug mode to inspect cells  |
| H                | Thermal view mode            |

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

Kernels:
- **Paint** - stamps brush circles into the grid
- **PrepareFrame** - clears flags, applies gravity/drag, runs reactions and decay
- **Simulate** - four-color tiled movement with CAS for cardinal contention
- **Diffuse** - Diffuses heat through cells

Movement uses Bresenham-style gating to distribute velocity across steps.
Density swapping is based on fluidity and density. Making it probabilistic breaks up ugly patterns.

### Lighting (SandLighting.compute)

Post-process pass on the simulation data. Runs at a lower configurable resolution for performance.

- **Light** - DDA ray-march toward the directional light source
- **Emission** - 8-direction radial gather from emissive cells, accumulated into the light buffer
- **BlurH/BlurV** - separable 5-tap Gaussian on the combined buffer
- **Visualize** - composites material color * light, adds self-emission, and draws cursor

### C# Side

| File                        | Role                                            |
|-----------------------------|--------------------------------------------------|
| SandSimulationController.cs | Sim dispatch, input, buffers, resolution, timing |
| SandLightingController.cs   | Lighting dispatch, lighting parameters           |
| SandSimUI.cs                | Programmatic uGUI, settings persistence          |
| MaterialTable.cs            | Material definitions, reactions, decay rules      |
| MaterialDefinition.cs       | Material property class                          |
| MaterialProperties.cs       | GPU-side struct, extinction/emission derivation   |

## Requirements

- Unity 2022.3+
- Compute shader support (any modern GPU)

## Setup

1. Open the project in Unity
2. Open `Assets/FallingSand/SandSimulation.unity`
3. Hit play

Runs best as a standalone build. The editor doesn't respect `Screen.SetResolution` so resolution scaling and window sizing won't behave correctly in play mode.
