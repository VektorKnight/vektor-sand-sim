using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace FallingSand.Scripts {
    /// <summary>
    /// Holds the properties for a material within the sand simulation.
    /// This structure is designed to be constructed for mirroring to the compute shader.
    /// Note that velocity is a signed 8-bit value in the simulation. So anything dealing with it must be [-127, 127].
    /// Other properties such as fluidity, density, and gravity are regular unsigned 8-bit values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuMaterialDefinition {
        // --- Physics ---
        public readonly uint Fluidity;
        public readonly uint Density;
        public readonly int Weight;
        public readonly uint Drag;
        
        // --- Thermodynamics ---
        public readonly float InitialTemp;
        public readonly float Conductivity;
        public readonly float HeatCapacity;

        // --- Visuals ---
        public readonly float Variation;
        public readonly float4 Extinction; // Per-channel (RGB), derived from color and opacity.
        public readonly float4 Color;
        public readonly float4 Emission;   // Pre-multiplied emission color (RGB * intensity).

        public GpuMaterialDefinition(
            uint fluidity,
            uint density,
            int weight,
            uint drag,
            float initialTemp,
            float conductivity,
            float heatCapacity,
            float variation,
            float4 extinction,
            float4 color,
            float4 emission
        ) {
            Fluidity     = Math.Min(fluidity, 255);
            Density      = Math.Min(density, 255);
            Weight       = Math.Clamp(weight, -256, 256);
            Drag         = Math.Min(drag, 255);
            InitialTemp  = Math.Clamp(initialTemp, -273f, 10000f);
            Conductivity = Math.Clamp(conductivity, 0f, 1f);
            HeatCapacity = Math.Max(heatCapacity, 0.01f);
            Variation    = Math.Clamp(variation, 0f, 1f);
            Extinction   = extinction;
            Color        = color;
            Emission     = emission;
        }

        public static GpuMaterialDefinition FromDefinition(MaterialDefinition def) {
            var linear = def.Color.linear;

            // Derive per-channel extinction from alpha and color.
            // alpha * (1 - color_linear) gives nice tinted shadows.
            var ext = new float4(
                linear.a * (1f - linear.r),
                linear.a * (1f - linear.g),
                linear.a * (1f - linear.b),
                0f
            );

            // Pre-multiply emission color by intensity in linear space.
            var emLinear = def.EmissionColor.linear;
            var emission = new float4(
                emLinear.r * def.EmissionIntensity,
                emLinear.g * def.EmissionIntensity,
                emLinear.b * def.EmissionIntensity,
                0f
            );

            return new GpuMaterialDefinition(
                (uint)def.Fluidity,
                (uint)def.Density,
                def.Weight,
                (uint)def.Drag,
                def.InitialTemp,
                def.Conductivity,
                def.HeatCapacity,
                def.Variation,
                ext,
                new float4(linear.r, linear.g, linear.b, linear.a),
                emission
            );
        }
    }
}
