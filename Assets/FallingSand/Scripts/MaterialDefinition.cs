using System;
using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Inspector-friendly material definition.
    /// See MaterialProperties.cs for what actually gets to the GPU.
    /// </summary>
    [Serializable]
    public class MaterialDefinition {
        public string Name = "Material";

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
        /// Scales global gravity for this material.
        /// 256 = normal, 0 = weightless (e.g. stone), negative = buoyant (e.g. gas).
        /// Terminal velocity emerges from the weight/drag balance.
        /// </summary>
        [Range(-256, 256)]
        public int Weight = 256;

        /// <summary>
        /// Proportional velocity decay per frame (like air resistance).
        /// Higher values = more deceleration = lower terminal velocity.
        /// Applied toward zero on both axes.
        /// </summary>
        [Range(0, 255)]
        public int Drag = 16;

        /// <summary>
        /// Per-particle brightness spread. 0 = flat color, higher = more variation.
        /// Each particle gets one of 4 brightness offsets set at paint time.
        /// </summary>
        [Range(0f, 1f)]
        public float Variation = 0f;

        /// <summary>
        /// Light absorption per cell for ray-marching visualization.
        /// 0 = fully transparent, higher = more opaque.
        /// </summary>
        [Range(0f, 10f)]
        public float Extinction = 1f;

        public Color Color = UnityEngine.Color.white;
    }
}