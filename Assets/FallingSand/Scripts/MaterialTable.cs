using System.Collections.Generic;
using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Material IDs matching the definition order in MaterialTable.
    /// Maps directly to the 5-bit material ID stored in each cell on the GPU.
    /// Mostly to make reactions and decay easier to configure.
    /// </summary>
    public enum MaterialId {
        Empty   = 0,
        Stone   = 1,
        Gravel  = 2,
        Sand    = 3,
        Snow    = 4,
        Ice     = 5,
        Water   = 6,
        Oil     = 7,
        Lava    = 8,
        Steam   = 9,
        Fire    = 10,
        Smoke   = 11,
        Wood    = 12,
        Ember   = 13,
    }

    /// <summary>
    /// Defines a reaction between materials given a source material and trigger neighbor.
    /// Probability is the chance per neighboring cell per step.
    /// </summary>
    public readonly struct ReactionRule {
        public readonly MaterialId Source;
        public readonly MaterialId Trigger;
        public readonly MaterialId Result;
        public readonly float Probability;

        public ReactionRule(MaterialId source, MaterialId trigger, MaterialId result, float probability) {
            Source = source;
            Trigger = trigger;
            Result = result;
            Probability = probability;
        }
    }

    /// <summary>
    /// Defines spontaneous material transformation over time with no neighbor trigger.
    /// Probability is the chance per step that the source decays into the result.
    /// </summary>
    public readonly struct DecayRule {
        public readonly MaterialId Source;
        public readonly MaterialId Result;
        public readonly float Probability;

        public DecayRule(MaterialId source, MaterialId result, float probability) {
            Source = source;
            Result = result;
            Probability = probability;
        }
    }

    /// <summary>
    /// Defines the materials within the simulation as well as interactions and decay products.
    /// Inspector exposure was convenient until we needed tables and such. Code is fine.
    /// 5-bit IDs support up to 32 materials with [0] reserved as empty.
    /// Overwriting "Empty" won't technically break anything, but you should probably leave it alone.
    /// Keep the enum and list in the same order.
    /// </summary>
    public static class MaterialTable {
        /// <summary>
        /// All material definitions. Index 0 is Empty, the rest match MaterialId.
        /// </summary>
        public static IReadOnlyList<MaterialDefinition> Materials => _materials;
        public static IReadOnlyList<ReactionRule> Reactions => _reactions;
        public static IReadOnlyList<DecayRule> Decay => _decay;

        private static readonly List<MaterialDefinition> _materials = new() {
            // [0] Empty - reserved, do not overwrite.
            new() { Name = "Empty" },

            // [1] Stone - immovable solid.
            new() {
                Name = "Stone",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.25f, Opacity = 1f,
                Color = new Color(0.38679248f, 0.38679248f, 0.38679248f),
            },

            // [2] Gravel - heavier than sand, melted by lava.
            new() {
                Name = "Gravel",
                Fluidity = 0, Density = 200, Weight = 128, Drag = 96,
                Variation = 0.1f, Opacity = 1f,
                Color = new Color(0.28f, 0.28f, 0.28f),
            },

            // [3] Sand - lighter granular, semi-transparent.
            new() {
                Name = "Sand",
                Fluidity = 0, Density = 150, Weight = 128, Drag = 128,
                Variation = 0.5f, Opacity = 0.7f,
                Color = new Color(0.8207547f, 0.5865165f, 0.32907617f),
            },

            // [4] Snow - barely fluid, very light, low opacity.
            new() {
                Name = "Snow",
                Fluidity = 1, Density = 50, Weight = 128, Drag = 192,
                Variation = 0.5f, Opacity = 0.04f,
                Color = new Color(0.85f, 0.88f, 0.95f),
            },

            // [5] Ice - immovable solid, near-transparent.
            new() {
                Name = "Ice",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.5f, Opacity = 0.06f,
                Color = new Color(0.4f, 0.75f, 0.85f),
            },

            // [6] Water - fully fluid, near-transparent.
            new() {
                Name = "Water",
                Fluidity = 255, Density = 100, Weight = 96, Drag = 128,
                Variation = 0.1f, Opacity = 0.01f,
                Color = new Color(0.14509802f, 0.36678076f, 0.7921569f),
            },

            // [7] Oil - viscous fluid, floats on water, burns.
            new() {
                Name = "Oil",
                Fluidity = 64, Density = 70, Weight = 80, Drag = 128,
                Variation = 0.1f, Opacity = 0.1f,
                Color = new Color(0.43529412f, 0.29143146f, 0.23529412f),
            },

            // [8] Lava - very viscous, emissive, ignites flammables.
            new() {
                Name = "Lava",
                Fluidity = 4, Density = 125, Weight = 128, Drag = 192,
                Variation = 0.05f, Opacity = 0.4f,
                EmissionColor = new Color(1f, 0.5f, 0.1f),
                EmissionIntensity = 3f,
                Color = new Color(0.45f, 0.12f, 0.02f),
            },

            // [9] Steam - buoyant gas, rises and dissipates.
            new() {
                Name = "Steam",
                Fluidity = 64, Density = 10, Weight = -32, Drag = 220,
                Variation = 0.3f, Opacity = 0.02f,
                Color = new Color(0.8f, 0.85f, 0.9f),
            },

            // [10] Fire - short-lived, buoyant, bright emissive. Burns out into ember and smoke.
            new() {
                Name = "Fire",
                Fluidity = 1, Density = 5, Weight = -16, Drag = 120,
                Variation = 0.8f, Opacity = 0.05f,
                EmissionColor = new Color(1f, 0.6f, 0.1f),
                EmissionIntensity = 2f,
                Color = new Color(1f, 0.35f, 0f),
            },

            // [11] Smoke - buoyant, dark, semi-transparent. Dissipates over time.
            new() {
                Name = "Smoke",
                Fluidity = 64, Density = 8, Weight = -48, Drag = 230,
                Variation = 0.2f, Opacity = 0.1f,
                Color = new Color(0.15f, 0.15f, 0.15f),
            },

            // [12] Wood - immovable solid, flammable.
            new() {
                Name = "Wood",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.3f, Opacity = 1.5f,
                Color = new Color(0.45f, 0.28f, 0.12f),
            },

            // [13] Ember - slow-falling glowing particle, ignites flammables, decays to smoke.
            new() {
                Name = "Ember",
                Fluidity = 2, Density = 30, Weight = 32, Drag = 220,
                Variation = 0.6f, Opacity = 0.3f,
                EmissionColor = new Color(1f, 0.4f, 0.05f),
                EmissionIntensity = 1.2f,
                Color = new Color(0.8f, 0.25f, 0.02f),
            },
        };

        // --- Reactions ---

        private static readonly List<ReactionRule> _reactions = new() {
            // Snow melts slowly in water.
            new(MaterialId.Snow, MaterialId.Water, MaterialId.Water, 0.05f),

            // Ice melts very slowly in water.
            new(MaterialId.Ice, MaterialId.Water, MaterialId.Water, 0.005f),

            // Water touching lava boils into steam.
            new(MaterialId.Water, MaterialId.Lava, MaterialId.Steam, 0.8f),

            // Lava touching water cools to stone.
            new(MaterialId.Lava, MaterialId.Water, MaterialId.Stone, 0.6f),

            // Lava touching snow cools to stone.
            new(MaterialId.Lava, MaterialId.Snow, MaterialId.Stone, 0.8f),

            // Snow touching lava vaporizes to steam.
            new(MaterialId.Snow, MaterialId.Lava, MaterialId.Steam, 0.9f),

            // Ice touching lava melts quickly.
            new(MaterialId.Ice, MaterialId.Lava, MaterialId.Water, 0.5f),

            // Lava touching ice rapidly cools to stone.
            new(MaterialId.Lava, MaterialId.Ice, MaterialId.Stone, 0.4f),

            // Oil ignites on lava.
            new(MaterialId.Oil, MaterialId.Lava, MaterialId.Fire, 0.7f),

            // Oil ignites on fire or ember.
            new(MaterialId.Oil, MaterialId.Fire, MaterialId.Fire, 0.4f),
            new(MaterialId.Oil, MaterialId.Ember, MaterialId.Fire, 0.3f),

            // Water extinguishes fire.
            new(MaterialId.Fire, MaterialId.Water, MaterialId.Steam, 0.9f),

            // Snow touching fire melts to water.
            new(MaterialId.Snow, MaterialId.Fire, MaterialId.Water, 0.6f),

            // Ice touching fire melts to water.
            new(MaterialId.Ice, MaterialId.Fire, MaterialId.Water, 0.3f),

            // Wood catches fire slowly from flames.
            new(MaterialId.Wood, MaterialId.Fire, MaterialId.Fire, 0.1f),

            // Lava ignites wood.
            new(MaterialId.Wood, MaterialId.Lava, MaterialId.Fire, 0.3f),

            // Ember ignites wood (this is the downward fire spread).
            new(MaterialId.Wood, MaterialId.Ember, MaterialId.Fire, 0.08f),

            // Water extinguishes ember.
            new(MaterialId.Ember, MaterialId.Water, MaterialId.Steam, 0.9f),

            // Lava slowly melts gravel.
            new(MaterialId.Gravel, MaterialId.Lava, MaterialId.Lava, 0.02f),
        };

        /// <summary>
        /// Decay products based on probability.
        /// Using probability gives us effective lifetimes without using more bits.
        /// </summary>
        private static readonly List<DecayRule> _decay = new() {
            // Fire burns out into ember.
            new(MaterialId.Fire, MaterialId.Ember, 0.1f),

            // Ember cools into smoke.
            new(MaterialId.Ember, MaterialId.Smoke, 0.1f),

            // Smoke dissipates into nothing.
            new(MaterialId.Smoke, MaterialId.Empty, 0.008f),

            // Steam dissipates into nothing.
            new(MaterialId.Steam, MaterialId.Empty, 0.005f),
        };
    }
}
