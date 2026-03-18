using System.Collections.Generic;
using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Material IDs matching the definition order in MaterialTable.
    /// Maps directly to the 5-bit material ID stored in each cell on the GPU.
    /// Mostly to make reactions and decay easier to configure.
    /// </summary>
    public enum MatId {
        // Solids
        Empty      = 0,
        Bedrock    = 1,
        Stone      = 2,
        Glass      = 3,
        Wood       = 4,
        Ice        = 5,

        // Granulars
        Sand       = 6,
        Gravel     = 7,
        Snow       = 8,
        Gunpowder  = 9,

        // Liquids
        Water      = 10,
        Oil        = 11,
        Acid       = 12,
        Lava       = 13,

        // Hot
        Fire       = 14,
        Ember      = 15,
        Plasma     = 16,

        // Gases
        Steam      = 17,
        Smoke      = 18,

        // Organics
        Algae      = 19,
        Sludge     = 20,
        
        // Metals
        Copper     = 21,

        // Cryogenics
        LiquidN2   = 22,
    }

    /// <summary>
    /// Defines a reaction between materials given a source material and trigger neighbor.
    /// Probability is the chance per neighboring cell per step.
    /// </summary>
    public readonly struct ReactionRule {
        public readonly MatId Source;
        public readonly MatId Trigger;
        public readonly MatId Result;
        public readonly float Probability;

        public ReactionRule(MatId source, MatId trigger, MatId result, float probability) {
            Source = source;
            Trigger = trigger;
            Result = result;
            Probability = probability;
        }
    }

    /// <summary>
    /// Defines spontaneous material transformation over time.
    /// Probability is the chance per step that the source decays into the result.
    /// </summary>
    public readonly struct DecayRule {
        public readonly MatId Source;
        public readonly MatId Result;
        public readonly float Probability;

        public DecayRule(MatId source, MatId result, float probability) {
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
            new() { Name = "Empty", Label = "EMPT", Conductivity = 0f, HeatCapacity = 0.01f },

            // Solids
            new() {
                Name = "Bedrock", Label = "BDRK", Category = MaterialCategory.Solids,
                Description = "Indestructible. Nothing affects it.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                InitialTemp = 22f, Conductivity = 0.47f, HeatCapacity = 4f,
                Variation = 0.15f, Opacity = 1.5f,
                Color = new Color(0.28f, 0.26f, 0.32f),
            },
            new() {
                Name = "Stone", Label = "STNE", Category = MaterialCategory.Solids,
                Description = "Solid. Dissolved by acid, melted by plasma.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                InitialTemp = 22f, Conductivity = 0.4f, HeatCapacity = 2f,
                Variation = 0.25f, Opacity = 1f,
                Color = new Color(0.42f, 0.38f, 0.35f),
            },
            new() {
                Name = "Glass", Label = "GLAS", Category = MaterialCategory.Solids,
                Description = "Transparent solid. Made from sand and lava.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                InitialTemp = 22f, Conductivity = 0.24f, HeatCapacity = 1.5f,
                Variation = 0.1f, Opacity = 0.01f,
                Color = new Color(0.7f, 0.85f, 0.9f, 0.5f),
            },
            new() {
                Name = "Wood", Label = "WOOD", Category = MaterialCategory.Solids,
                Description = "Solid. Burns slowly, ignited by fire, lava, or ember.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                InitialTemp = 22f, Conductivity = 0.06f, HeatCapacity = 2.5f,
                Variation = 0.3f, Opacity = 1.5f,
                Color = new Color(0.45f, 0.28f, 0.12f),
            },
            new() {
                Name = "Ice", Label = "ICE", Category = MaterialCategory.Solids,
                Description = "Transparent solid. Melts in water and fire.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                InitialTemp = -10f, Conductivity = 0.47f, HeatCapacity = 2f,
                Variation = 0.5f, Opacity = 0.06f,
                Color = new Color(0.4f, 0.75f, 0.85f, 0.6f),
            },
            // Powders
            new() {
                Name = "Sand", Label = "SAND", Category = MaterialCategory.Powders,
                Description = "Light granular. Melted into glass by lava.",
                Fluidity = 0, Density = 160, Weight = 128, Drag = 128,
                InitialTemp = 22f, Conductivity = 0.16f, HeatCapacity = 1.5f,
                Variation = 0.5f, Opacity = 0.7f,
                Color = new Color(0.8207547f, 0.5865165f, 0.32907617f),
            },
            new() {
                Name = "Gravel", Label = "GRVL", Category = MaterialCategory.Powders,
                Description = "Heavy granular. Slowly melted by lava.",
                Fluidity = 0, Density = 200, Weight = 128, Drag = 96,
                InitialTemp = 22f, Conductivity = 0.31f, HeatCapacity = 2f,
                Variation = 0.25f, Opacity = 1f,
                Color = new Color(0.45f, 0.44f, 0.42f),
            },
            new() {
                Name = "Snow", Label = "SNOW", Category = MaterialCategory.Powders,
                Description = "Light powder. Melts in water, freezes on ice.",
                Fluidity = 1, Density = 50, Weight = 128, Drag = 192,
                InitialTemp = -10f, Conductivity = 0.08f, HeatCapacity = 1f,
                Variation = 0.5f, Opacity = 0.04f,
                Color = new Color(0.75f, 0.84f, 0.95f),
            },
            new() {
                Name = "Gunpowder", Label = "GNPD", Category = MaterialCategory.Powders,
                Description = "Flammable granular. Ignites almost instantly.",
                Fluidity = 0, Density = 160, Weight = 128, Drag = 128,
                InitialTemp = 22f, Conductivity = 0.12f, HeatCapacity = 1f,
                Variation = 0.15f, Opacity = 1f,
                Color = new Color(0.27f, 0.25f, 0.22f),
            },

            // Liquids
            new() {
                Name = "Water", Label = "WATR", Category = MaterialCategory.Liquids,
                Description = "Fluid. Extinguishes fire, boils on lava.",
                Fluidity = 255, Density = 100, Weight = 96, Drag = 128,
                InitialTemp = 22f, Conductivity = 0.31f, HeatCapacity = 4f,
                Variation = 0.1f, Opacity = 0.01f,
                Color = new Color(0.14509802f, 0.36678076f, 0.7921569f, 0.7f),
            },
            new() {
                Name = "Oil", Label = "OIL", Category = MaterialCategory.Liquids,
                Description = "Viscous fluid. Floats on water, flammable.",
                Fluidity = 64, Density = 70, Weight = 80, Drag = 128,
                InitialTemp = 22f, Conductivity = 0.12f, HeatCapacity = 2f,
                Variation = 0.1f, Opacity = 0.1f,
                Color = new Color(0.43529412f, 0.29143146f, 0.23529412f),
            },
            new() {
                Name = "Acid", Label = "ACID", Category = MaterialCategory.Liquids,
                Description = "Corrosive fluid. Dissolves most solids. Neutralized by water.",
                Fluidity = 200, Density = 110, Weight = 96, Drag = 128,
                InitialTemp = 22f, Conductivity = 0.24f, HeatCapacity = 2f,
                Variation = 0.2f, Opacity = 0.08f,
                EmissionColor = new Color(0.2f, 1f, 0.1f),
                EmissionIntensity = 0.4f,
                Color = new Color(0.3f, 0.85f, 0.15f, 0.8f),
            },
            new() {
                Name = "Lava", Label = "LAVA", Category = MaterialCategory.Liquids,
                Description = "Hot viscous fluid. Ignites flammables, melts sand into glass.",
                Fluidity = 8, Density = 190, Weight = 128, Drag = 192,
                InitialTemp = 1200f, Conductivity = 0.6f, HeatCapacity = 5f,
                Variation = 0.1f, Opacity = 0.4f,
                EmissionColor = new Color(1f, 0.4f, 0.1f),
                EmissionIntensity = 3f,
                Color = new Color(0.75f, 0.12f, 0.02f),
            },

            // Energy
            new() {
                Name = "Fire", Label = "FIRE", Category = MaterialCategory.Energy,
                Description = "Hot, ignites flammables, burns out into ember.",
                Fluidity = 1, Density = 5, Weight = -16, Drag = 120,
                InitialTemp = 800f, Conductivity = 0.8f, HeatCapacity = 0.2f,
                Variation = 0.8f, Opacity = 0.05f,
                EmissionColor = new Color(1f, 0.6f, 0.1f),
                EmissionIntensity = 2f,
                Color = new Color(1f, 0.35f, 0f),
            },
            new() {
                Name = "Ember", Label = "EMBR", Category = MaterialCategory.Energy,
                Description = "Slow falling. Ignites flammables, decays to smoke.",
                Fluidity = 2, Density = 30, Weight = 32, Drag = 220,
                InitialTemp = 500f, Conductivity = 0.6f, HeatCapacity = 0.5f,
                Variation = 0.6f, Opacity = 0.3f,
                EmissionColor = new Color(1f, 0.4f, 0.05f),
                EmissionIntensity = 1.2f,
                Color = new Color(0.8f, 0.25f, 0.02f),
            },
            new() {
                Name = "Plasma", Label = "PLSM", Category = MaterialCategory.Energy,
                Description = "Extremely hot. Melts and burns almost everything.",
                Fluidity = 32, Density = 3, Weight = -32, Drag = 100,
                InitialTemp = 5000f, Conductivity = 1f, HeatCapacity = 8f,
                Variation = 0.2f, Opacity = 0.03f,
                EmissionColor = new Color(0.5f, 0.3f, 1f),
                EmissionIntensity = 2f,
                Color = new Color(0.4f, 0.3f, 0.9f),
            },

            // Gases
            new() {
                Name = "Steam", Label = "STEM", Category = MaterialCategory.Gases,
                Description = "Buoyant gas. Condenses on cold surfaces.",
                Fluidity = 64, Density = 10, Weight = -32, Drag = 220,
                InitialTemp = 100f, Conductivity = 0.04f, HeatCapacity = 0.5f,
                Variation = 0.3f, Opacity = 0.02f,
                Color = new Color(0.7f, 0.85f, 0.9f, 0.3f),
            },
            new() {
                Name = "Smoke", Label = "SMKE", Category = MaterialCategory.Gases,
                Description = "Buoyant gas. Dissipates over time.",
                Fluidity = 128, Density = 8, Weight = -32, Drag = 128,
                InitialTemp = 200f, Conductivity = 0.03f, HeatCapacity = 0.3f,
                Variation = 0.2f, Opacity = 0.05f,
                Color = new Color(0.2f, 0.2f, 0.15f, 0.7f),
            },

            // Life
            new() {
                Name = "Algae", Label = "ALGE", Category = MaterialCategory.Life,
                Description = "Grows on water. Decays into sludge.",
                Fluidity = 4, Density = 60, Weight = 64, Drag = 192,
                InitialTemp = 22f, Conductivity = 0.08f, HeatCapacity = 1.5f,
                Variation = 0.4f, Opacity = 0.04f,
                Color = new Color(0.15f, 0.45f, 0.1f),
            },
            new() {
                Name = "Sludge", Label = "SLDG", Category = MaterialCategory.Life,
                Description = "Dead biomass. Burns slowly.",
                Fluidity = 8, Density = 150, Weight = 96, Drag = 200,
                InitialTemp = 22f, Conductivity = 0.1f, HeatCapacity = 2f,
                Variation = 0.2f, Opacity = 0.8f,
                Color = new Color(0.3f, 0.22f, 0.12f),
            },

            // Metals
            new() {
                Name = "Copper", Label = "COPR", Category = MaterialCategory.Solids,
                Description = "Highly conductive metal. Spreads heat almost instantly.",
                Fluidity = 0, Density = 255, Weight = 0, Drag = 0,
                InitialTemp = 22f, Conductivity = 0.97f, HeatCapacity = 1.5f,
                Variation = 0.15f, Opacity = 1.5f,
                Color = new Color(0.72f, 0.45f, 0.20f),
            },

            // Cryogenics
            new() {
                Name = "Liquid Nitrogen", Label = "LN2", Category = MaterialCategory.Liquids,
                Description = "Cryogenic fluid. Freezes almost everything on contact.",
                Fluidity = 255, Density = 80, Weight = 96, Drag = 96,
                InitialTemp = -200f, Conductivity = 0.1f, HeatCapacity = 2f,
                Variation = 0.15f, Opacity = 0.005f,
                Color = new Color(0.55f, 0.70f, 0.90f, 0.35f),
            },
        };

        // --- Reactions ---
        
        // Source -> Trigger -> Result with probability determining reaction rate.
        // Uploaded as a [MAX_MATERIALS * MAX_MATERIALS] LUT on the GPU.
        private static readonly List<ReactionRule> _reactions = new() {
            // Stone
            new(MatId.Stone, MatId.Acid,      MatId.Empty, 0.03f),
            new(MatId.Stone, MatId.Plasma,    MatId.Lava,  0.1f),

            // Glass
            new(MatId.Glass, MatId.Acid,      MatId.Empty, 0.02f),
            new(MatId.Glass, MatId.Plasma,    MatId.Lava,  0.1f),
            new(MatId.Glass, MatId.Lava,      MatId.Lava,  0.005f),

            // Wood
            new(MatId.Wood, MatId.Fire,    MatId.Fire,  0.1f),
            new(MatId.Wood, MatId.Ember,   MatId.Fire,  0.08f),
            new(MatId.Wood, MatId.Lava,    MatId.Fire,  0.3f),
            new(MatId.Wood, MatId.Plasma,  MatId.Fire,  0.8f),
            new(MatId.Wood, MatId.Acid,    MatId.Empty, 0.04f),

            // Ice
            new(MatId.Ice, MatId.Water,   MatId.Water, 0.005f),
            new(MatId.Ice, MatId.Steam,   MatId.Water, 0.01f),
            new(MatId.Ice, MatId.Fire,    MatId.Water, 0.1f),
            new(MatId.Ice, MatId.Lava,    MatId.Water, 0.5f),
            new(MatId.Ice, MatId.Plasma,  MatId.Steam, 0.9f),
            new(MatId.Ice, MatId.Acid,    MatId.Water, 0.06f),

            // Sand
            new(MatId.Sand, MatId.Lava,   MatId.Glass, 0.1f),
            new(MatId.Sand, MatId.Plasma,  MatId.Glass, 0.6f),
            new(MatId.Sand, MatId.Acid,    MatId.Empty, 0.08f),

            // Gravel
            new(MatId.Gravel, MatId.Lava,  MatId.Lava,  0.02f),
            new(MatId.Gravel, MatId.Plasma, MatId.Lava,  0.5f),
            new(MatId.Gravel, MatId.Acid,  MatId.Empty, 0.05f),

            // Copper
            new(MatId.Copper, MatId.Acid,   MatId.Empty, 0.02f),
            new(MatId.Copper, MatId.Plasma, MatId.Lava,  0.15f),

            // Liquid Nitrogen — the *target* material transforms, not the LN2 itself.
            new(MatId.Water, MatId.LiquidN2, MatId.Ice,   0.5f),
            new(MatId.Fire,  MatId.LiquidN2, MatId.Empty, 0.8f),
            new(MatId.Ember, MatId.LiquidN2, MatId.Empty, 0.6f),
            new(MatId.Lava,  MatId.LiquidN2, MatId.Stone, 0.3f),

            // Snow
            new(MatId.Snow, MatId.Ice,      MatId.Ice,   0.01f),
            new(MatId.Snow, MatId.Water,    MatId.Water, 0.05f),
            new(MatId.Snow, MatId.Steam,    MatId.Water, 0.04f),
            new(MatId.Snow, MatId.Fire,     MatId.Water, 0.4f),
            new(MatId.Snow, MatId.Lava,     MatId.Steam, 0.9f),
            new(MatId.Snow, MatId.Plasma,   MatId.Steam, 0.9f),

            // Gunpowder
            new(MatId.Gunpowder, MatId.Fire,   MatId.Fire, 1.0f),
            new(MatId.Gunpowder, MatId.Ember,  MatId.Fire, 1.0f),
            new(MatId.Gunpowder, MatId.Lava,   MatId.Fire, 1.0f),
            new(MatId.Gunpowder, MatId.Plasma, MatId.Fire, 1.0f),

            // Water
            new(MatId.Water, MatId.Lava,   MatId.Steam, 0.8f),
            new(MatId.Water, MatId.Algae,  MatId.Algae, 0.005f),

            // Oil
            new(MatId.Oil, MatId.Lava,    MatId.Fire,  0.7f),
            new(MatId.Oil, MatId.Fire,    MatId.Fire,  0.4f),
            new(MatId.Oil, MatId.Ember,   MatId.Fire,  0.3f),
            new(MatId.Oil, MatId.Plasma,  MatId.Fire,  0.9f),

            // Acid
            new(MatId.Acid, MatId.Water,  MatId.Water, 0.05f),
            new(MatId.Acid, MatId.Lava,   MatId.Steam, 0.7f),
            new(MatId.Acid, MatId.Fire,   MatId.Steam, 0.5f),
            new(MatId.Acid, MatId.Plasma, MatId.Steam, 0.9f),

            // Lava
            new(MatId.Lava, MatId.Water,  MatId.Stone, 0.4f),
            new(MatId.Lava, MatId.Snow,   MatId.Stone, 0.5f),
            new(MatId.Lava, MatId.Ice,    MatId.Stone, 0.6f),

            // Fire
            new(MatId.Fire, MatId.Water,  MatId.Steam, 0.9f),

            // Ember
            new(MatId.Ember, MatId.Water, MatId.Steam, 0.9f),

            // Plasma
            new(MatId.Plasma, MatId.Water, MatId.Steam, 0.7f),

            // Sludge
            new(MatId.Sludge, MatId.Lava,   MatId.Fire, 0.2f),
            new(MatId.Sludge, MatId.Fire,   MatId.Fire, 0.07f),
            new(MatId.Sludge, MatId.Ember,  MatId.Fire, 0.05f),
            new(MatId.Sludge, MatId.Plasma, MatId.Fire, 0.8f),

            // Algae
            new(MatId.Algae, MatId.Fire,  MatId.Smoke, 0.1f),
            new(MatId.Algae, MatId.Ember, MatId.Smoke, 0.05f),
            new(MatId.Algae, MatId.Lava,  MatId.Smoke, 0.9f),
            new(MatId.Algae, MatId.Acid,  MatId.Empty, 0.1f),
            
            new(MatId.Algae, MatId.Snow, MatId.Sludge, 0.05f),
            new(MatId.Algae, MatId.Ice, MatId.Sludge, 0.05f),
        };

        // --- Decay ---

        private static readonly List<DecayRule> _decay = new() {
            // Fire burns out into ember.
            new(MatId.Fire, MatId.Ember, 0.1f),

            // Ember cools into smoke.
            new(MatId.Ember, MatId.Smoke, 0.1f),

            // Plasma rapidly cools and dissipates.
            new(MatId.Plasma, MatId.Empty, 0.1f),

            // Smoke dissipates.
            new(MatId.Smoke, MatId.Empty, 0.02f),

            // Steam dissipates.
            new(MatId.Steam, MatId.Empty, 0.02f),

            // Acid slowly evaporates.
            new(MatId.Acid, MatId.Smoke, 0.002f),
            
            // Algae slowly dies off into mud.
            new(MatId.Algae, MatId.Sludge, 0.0001f),

            // LN2 boils off rapidly — real boiling point is -196C.
            new(MatId.LiquidN2, MatId.Empty, 0.1f),
        };
    }
}
