using System;
using UnityEngine;

namespace FallingSand.Scripts {
    public enum MaterialCategory {
        Solids,
        Powders,
        Liquids,
        Gases,
        Energy,
        Life,
    }

    /// <summary>
    /// Material definition. Converted to MaterialProperties for the GPU.
    /// </summary>
    [Serializable]
    public class MaterialDefinition {
        public string Name = "Material";
        public string Label = "";
        public string Description = "";
        public MaterialCategory Category = MaterialCategory.Solids;

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
        /// How opaque the material is to light. 0 = fully transparent, higher = denser.
        /// Per-channel extinction is derived from color on the CPU side.
        /// </summary>
        [Range(0f, 10f)]
        public float Opacity = 1f;

        /// <summary>
        /// Self-illumination color. Combined with EmissionIntensity to produce
        /// light that is added after shading, so emissive materials glow even in shadow.
        /// </summary>
        public Color EmissionColor = Color.black;

        /// <summary>
        /// Brightness multiplier for EmissionColor. 0 = no emission.
        /// </summary>
        [Range(0f, 10f)]
        public float EmissionIntensity = 0f;

        public Color Color = Color.white;

        public static MaterialDefinition Empty() {
            return new MaterialDefinition {
                Name = "Empty",
                Fluidity = 0,
                Density = 0,
                Weight = 0,
                Drag = 0,
                Variation = 0f,
                Opacity = 0f,
                EmissionColor = Color.black,
                EmissionIntensity = 0f,
                Color = Color.clear,
            };
        }
    }
}
