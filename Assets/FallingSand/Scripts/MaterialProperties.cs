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
    public struct MaterialProperties {
        public readonly uint Fluidity;
        public readonly uint Density;
        public readonly int Gravity;
        public readonly uint TerminalVel;
        
        // Visual properties, currently just color.
        public readonly float4 Color;

        public MaterialProperties(
            uint fluidity,
            uint density,
            int gravity,
            uint terminalVel,
            float4 color
        ) {
            Fluidity    = Math.Min(fluidity, 255);
            Density     = Math.Min(density, 255);
            Gravity     = Math.Clamp(gravity, -127, 127);
            TerminalVel = Math.Min(terminalVel, 127);
            
            Color = color;
        }
        
        /// <summary>
        /// Create the GPU-friendly material property structure from the Unity-friendly definition.
        /// TODO: Some extra validation and/or sanitization.
        /// </summary>
        public static MaterialProperties FromDefinition(MaterialDefinition def) {
            return new MaterialProperties(
                (uint)def.Fluidity,
                (uint)def.Density,
                def.Gravity,
                (uint)def.TerminalVel,
                new float4(def.Color.r, def.Color.g, def.Color.b, def.Color.a)
            );
        }
    }
}