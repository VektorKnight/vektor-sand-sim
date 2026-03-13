using System;
using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Inspector-friendly material definition.
    /// See MaterialProperties.cs for what actually gets to the GPU.
    /// </summary>
    [Serializable]
    public class MaterialDefinition {
        /// <summary>
        /// Solids are zero, anything greater is a progressively less viscous fluid / gas.
        /// </summary>
        [Range(0, 255)] public int Fluidity = 0;
        
        /// <summary>
        /// Density determines how materials tend to layer with one another.
        /// Sand should fall through water, oil should float on water, etc.
        /// </summary>
        [Range(0, 255)]
        public int Density = 127;
        
        /// <summary>
        /// Acceleration from gravity for a particle.
        /// A negative gravity will cause a particle to float upwards like a gas.
        /// In a way simulates drag.
        /// </summary>
        [Range(-127, 127)]
        public int Gravity = 16;
        
        /// <summary>
        /// Maximum possible vertical velocity for a particle.
        /// </summary>
        [Range(0, 127)]
        public int TerminalVel = 32;
        
        // Visual properties, currently just color.
        public Color Color = UnityEngine.Color.white;
    }
}