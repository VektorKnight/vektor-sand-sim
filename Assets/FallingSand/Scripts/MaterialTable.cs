using System.Collections.Generic;
using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Material IDs matching the definition order in MaterialTable.
    /// Maps directly to the 5-bit material ID stored in each cell on the GPU.
    /// Mostly to make reactions and decay easier to configure.
    /// </summary>
    public enum MaterialId {
        // Structural solids.
        Empty      = 0,
        Bedrock    = 1,
        Stone      = 2,
        Glass      = 3,
        Wood       = 4,
        Ice        = 5,

        // Granulars.
        Sand       = 6,
        Gravel     = 7,
        Snow       = 8,
        Gunpowder  = 9,

        // Liquids.
        Water      = 10,
        Oil        = 11,
        Acid       = 12,
        Lava       = 13,

        // Hot.
        Fire       = 14,
        Ember      = 15,
        Plasma     = 16,

        // Gases.
        Steam      = 17,
        Smoke      = 18,
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
    /// 5-bit IDs support up to 32 materials with [0] reserved as empty.
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
            new() { Name = "Empty" },

            // Solids
            new() {
                Name = "Bedrock", Description = "Indestructible. Nothing affects it.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.15f, Opacity = 1.5f,
                Color = new Color(0.28f, 0.26f, 0.32f),
            },
            new() {
                Name = "Stone", Description = "Solid. Dissolved by acid, melted by plasma.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.25f, Opacity = 1f,
                Color = new Color(0.42f, 0.38f, 0.35f),
            },
            new() {
                Name = "Glass", Description = "Transparent solid. Made from sand + lava.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.1f, Opacity = 0.01f,
                Color = new Color(0.7f, 0.85f, 0.9f, 0.5f),
            },
            new() {
                Name = "Wood", Description = "Solid. Burns slowly, ignited by fire, lava, or ember.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.3f, Opacity = 1.5f,
                Color = new Color(0.45f, 0.28f, 0.12f),
            },
            new() {
                Name = "Ice", Description = "Transparent solid. Melts in water and fire.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                Variation = 0.5f, Opacity = 0.06f,
                Color = new Color(0.4f, 0.75f, 0.85f, 0.4f),
            },

            // Granulars
            new() {
                Name = "Sand", Description = "Light granular. Melted into glass by lava.",
                Fluidity = 0, Density = 150, Weight = 128, Drag = 128,
                Variation = 0.5f, Opacity = 0.7f,
                Color = new Color(0.8207547f, 0.5865165f, 0.32907617f),
            },
            new() {
                Name = "Gravel", Description = "Heavy granular. Melted by lava.",
                Fluidity = 0, Density = 200, Weight = 128, Drag = 96,
                Variation = 0.25f, Opacity = 1f,
                Color = new Color(0.45f, 0.44f, 0.42f),
            },
            new() {
                Name = "Snow", Description = "Light powder. Melts in water, freezes on ice.",
                Fluidity = 1, Density = 50, Weight = 128, Drag = 192,
                Variation = 0.5f, Opacity = 0.04f,
                Color = new Color(0.85f, 0.88f, 0.95f),
            },
            new() {
                Name = "Gunpowder", Description = "Flammable granular. Ignites almost instantly.",
                Fluidity = 0, Density = 160, Weight = 128, Drag = 128,
                Variation = 0.15f, Opacity = 1f,
                Color = new Color(0.25f, 0.2f, 0.22f),
            },

            // Liquids
            new() {
                Name = "Water", Description = "Fluid. Extinguishes fire, boils on lava.",
                Fluidity = 255, Density = 100, Weight = 96, Drag = 128,
                Variation = 0.1f, Opacity = 0.01f,
                Color = new Color(0.14509802f, 0.36678076f, 0.7921569f, 0.6f),
            },
            new() {
                Name = "Oil", Description = "Viscous fluid. Floats on water, flammable.",
                Fluidity = 64, Density = 70, Weight = 80, Drag = 128,
                Variation = 0.1f, Opacity = 0.1f,
                Color = new Color(0.43529412f, 0.29143146f, 0.23529412f),
            },
            new() {
                Name = "Acid", Description = "Corrosive fluid. Dissolves most solids. Neutralized by water.",
                Fluidity = 200, Density = 110, Weight = 96, Drag = 128,
                Variation = 0.2f, Opacity = 0.08f,
                EmissionColor = new Color(0.2f, 1f, 0.1f),
                EmissionIntensity = 0.4f,
                Color = new Color(0.3f, 0.85f, 0.15f, 0.7f),
            },
            new() {
                Name = "Lava", Description = "Viscous emissive fluid. Ignites flammables, melts sand into glass.",
                Fluidity = 4, Density = 125, Weight = 128, Drag = 192,
                Variation = 0.05f, Opacity = 0.4f,
                EmissionColor = new Color(1f, 0.5f, 0.1f),
                EmissionIntensity = 3f,
                Color = new Color(0.45f, 0.12f, 0.02f),
            },

            // Hot
            new() {
                Name = "Fire", Description = "Short-lived. Rises, burns out into ember.",
                Fluidity = 1, Density = 5, Weight = -16, Drag = 120,
                Variation = 0.8f, Opacity = 0.05f,
                EmissionColor = new Color(1f, 0.6f, 0.1f),
                EmissionIntensity = 2f,
                Color = new Color(1f, 0.35f, 0f),
            },
            new() {
                Name = "Ember", Description = "Slow-falling glow. Ignites flammables, decays to smoke.",
                Fluidity = 2, Density = 30, Weight = 32, Drag = 220,
                Variation = 0.6f, Opacity = 0.3f,
                EmissionColor = new Color(1f, 0.4f, 0.05f),
                EmissionIntensity = 1.2f,
                Color = new Color(0.8f, 0.25f, 0.02f),
            },
            new() {
                Name = "Plasma", Description = "Extremely hot gas. Melts almost everything.",
                Fluidity = 32, Density = 3, Weight = -24, Drag = 150,
                Variation = 0.5f, Opacity = 0.03f,
                EmissionColor = new Color(0.6f, 0.3f, 1f),
                EmissionIntensity = 4f,
                Color = new Color(0.7f, 0.4f, 1f),
            },

            // Gases
            new() {
                Name = "Steam", Description = "Buoyant gas. Condenses on cold surfaces.",
                Fluidity = 64, Density = 10, Weight = -32, Drag = 220,
                Variation = 0.3f, Opacity = 0.02f,
                Color = new Color(0.7f, 0.85f, 0.9f, 0.3f),
            },
            new() {
                Name = "Smoke", Description = "Buoyant gas. Dissipates over time.",
                Fluidity = 64, Density = 8, Weight = -48, Drag = 230,
                Variation = 0.2f, Opacity = 0.1f,
                Color = new Color(0.15f, 0.15f, 0.15f, 0.4f),
            },
        };

        // --- Reactions ---
        
        private static readonly List<ReactionRule> _reactions = new() {
            // Snow freezes into ice on contact with ice.
            new(MaterialId.Snow, MaterialId.Ice, MaterialId.Ice, 0.01f),

            // Snow melts slowly in water.
            new(MaterialId.Snow, MaterialId.Water, MaterialId.Water, 0.05f),

            // Ice melts very slowly in water.
            new(MaterialId.Ice, MaterialId.Water, MaterialId.Water, 0.005f),

            // Steam melts cold surfaces.
            new(MaterialId.Ice, MaterialId.Steam, MaterialId.Water, 0.01f),
            new(MaterialId.Snow, MaterialId.Steam, MaterialId.Water, 0.04f),

            // Water touching lava boils into steam.
            new(MaterialId.Water, MaterialId.Lava, MaterialId.Steam, 0.8f),

            // Lava touching water/snow/ice cools to stone.
            new(MaterialId.Lava, MaterialId.Water, MaterialId.Stone, 0.6f),
            new(MaterialId.Lava, MaterialId.Snow, MaterialId.Stone, 0.8f),
            new(MaterialId.Lava, MaterialId.Ice, MaterialId.Stone, 0.4f),

            // Snow touching lava vaporizes to steam.
            new(MaterialId.Snow, MaterialId.Lava, MaterialId.Steam, 0.9f),

            // Ice touching lava melts quickly.
            new(MaterialId.Ice, MaterialId.Lava, MaterialId.Water, 0.5f),

            // Sand touching lava melts into glass.
            new(MaterialId.Sand, MaterialId.Lava, MaterialId.Glass, 0.1f),

            // Lava slowly melts gravel.
            new(MaterialId.Gravel, MaterialId.Lava, MaterialId.Lava, 0.02f),

            // Oil ignites on lava, fire, or ember.
            new(MaterialId.Oil, MaterialId.Lava, MaterialId.Fire, 0.7f),
            new(MaterialId.Oil, MaterialId.Fire, MaterialId.Fire, 0.4f),
            new(MaterialId.Oil, MaterialId.Ember, MaterialId.Fire, 0.3f),

            // Water extinguishes fire and ember.
            new(MaterialId.Fire, MaterialId.Water, MaterialId.Steam, 0.9f),
            new(MaterialId.Ember, MaterialId.Water, MaterialId.Steam, 0.9f),

            // Snow/ice touching fire melts to water.
            new(MaterialId.Snow, MaterialId.Fire, MaterialId.Water, 0.6f),
            new(MaterialId.Ice, MaterialId.Fire, MaterialId.Water, 0.3f),

            // Wood catches fire from flames, lava, or ember.
            new(MaterialId.Wood, MaterialId.Fire, MaterialId.Fire, 0.1f),
            new(MaterialId.Wood, MaterialId.Lava, MaterialId.Fire, 0.3f),
            new(MaterialId.Wood, MaterialId.Ember, MaterialId.Fire, 0.08f),

            // Gunpowder ignites instantly from fire, ember, or lava.
            new(MaterialId.Gunpowder, MaterialId.Fire, MaterialId.Fire, 0.9f),
            new(MaterialId.Gunpowder, MaterialId.Ember, MaterialId.Fire, 0.8f),
            new(MaterialId.Gunpowder, MaterialId.Lava, MaterialId.Fire, 0.9f),

            // Acid dissolves stone, gravel, sand, ice, wood, glass.
            new(MaterialId.Stone, MaterialId.Acid, MaterialId.Empty, 0.03f),
            new(MaterialId.Gravel, MaterialId.Acid, MaterialId.Empty, 0.05f),
            new(MaterialId.Sand, MaterialId.Acid, MaterialId.Empty, 0.08f),
            new(MaterialId.Ice, MaterialId.Acid, MaterialId.Water, 0.06f),
            new(MaterialId.Wood, MaterialId.Acid, MaterialId.Empty, 0.04f),
            new(MaterialId.Glass, MaterialId.Acid, MaterialId.Empty, 0.02f),
            
            // Acid is diluted by water.
            new(MaterialId.Acid, MaterialId.Water, MaterialId.Water, 0.05f),

            // Acid boils off on lava, fire, and plasma.
            new(MaterialId.Acid, MaterialId.Lava, MaterialId.Steam, 0.7f),
            new(MaterialId.Acid, MaterialId.Fire, MaterialId.Steam, 0.5f),
            new(MaterialId.Acid, MaterialId.Plasma, MaterialId.Steam, 0.9f),

            // Plasma melts solids that fire can't.
            new(MaterialId.Stone, MaterialId.Plasma, MaterialId.Lava, 0.3f),
            new(MaterialId.Gravel, MaterialId.Plasma, MaterialId.Lava, 0.5f),
            new(MaterialId.Sand, MaterialId.Plasma, MaterialId.Glass, 0.6f),
            new(MaterialId.Ice, MaterialId.Plasma, MaterialId.Steam, 0.9f),
            new(MaterialId.Snow, MaterialId.Plasma, MaterialId.Steam, 0.9f),
            new(MaterialId.Wood, MaterialId.Plasma, MaterialId.Fire, 0.8f),
            new(MaterialId.Glass, MaterialId.Plasma, MaterialId.Lava, 0.2f),

            // Gunpowder and oil ignite from plasma.
            new(MaterialId.Gunpowder, MaterialId.Plasma, MaterialId.Fire, 0.95f),
            new(MaterialId.Oil, MaterialId.Plasma, MaterialId.Fire, 0.9f),

            // Water quenches plasma.
            new(MaterialId.Plasma, MaterialId.Water, MaterialId.Steam, 0.7f),
        };

        // --- Decay ---

        private static readonly List<DecayRule> _decay = new() {
            // Fire burns out into ember.
            new(MaterialId.Fire, MaterialId.Ember, 0.1f),

            // Ember cools into smoke.
            new(MaterialId.Ember, MaterialId.Smoke, 0.1f),

            // Plasma cools into fire quickly.
            new(MaterialId.Plasma, MaterialId.Fire, 0.06f),

            // Smoke dissipates.
            new(MaterialId.Smoke, MaterialId.Empty, 0.01f),

            // Steam dissipates.
            new(MaterialId.Steam, MaterialId.Empty, 0.01f),

            // Acid slowly evaporates.
            new(MaterialId.Acid, MaterialId.Smoke, 0.002f),
        };
    }
}
