using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace FallingSand.Scripts {
    /// <summary>
    /// Contains shader property IDs and constant buffer structs for the sand simulation.
    /// </summary>
    public static class SandBindings {
        // Must match MAT_ID_BITS in SandUtil.hlsl.
        public const int MAX_MATERIALS = 1 << 5;

        // Kernel name constants.
        public const string KERNEL_PAINT    = "Paint";
        
        public const string KERNEL_PREPARE  = "PrepareFrame";
        public const string KERNEL_SIMULATE = "Simulate";
        public const string KERNEL_HEAT     = "DiffuseHeat";
        
        public const string KERNEL_LIGHT    = "Light";
        public const string KERNEL_EMISSION = "Emission";
        public const string KERNEL_BLUR_H   = "BlurH";
        public const string KERNEL_BLUR_V   = "BlurV";
        
        public const string KERNEL_BLOOM_GATHER = "BloomGather";
        
        public const string KERNEL_VISUALIZE      = "Visualize";
        public const string KERNEL_VISUALIZE_HEAT = "VisualizeHeat";

        // --- Constant buffer structs ---

        [StructLayout(LayoutKind.Sequential)]
        public struct SimConfigBuffer {
            public uint2 Dimensions;
            public uint StepsPerFrame;
            public uint Gravity;
            public uint CurrentFrame;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SimPaintBuffer {
            public int2 PaintOrigin;
            public uint PaintRadius;
            public uint PaintMaterial;
            public uint PaintReplace;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LightConfigBuffer {
            public float4 LightColor;       
            public float4 AmbientColor;     
            public int2 LightDir;           
            public uint LightMaxSteps;      
            public uint LightDownscale;     
            public uint EmissionMaxSteps;   
            public uint BloomSteps;         
            public uint2 LightDimensions; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VisConfigBuffer {
            public int2 CursorPos;
            public uint CursorRadius;
            public uint CursorMaterial;
            public uint LightEnabled;
            public float BloomIntensity;
        }

        // --- Uniform IDs ---
        public static readonly int ID_SIM_STEP      = Shader.PropertyToID("_SimStep");
        public static readonly int ID_SIM_PASS      = Shader.PropertyToID("_SimPass");
        public static readonly int ID_HEAT_PARITY   = Shader.PropertyToID("_HeatParity");

        // --- CBuffer IDs ---
        public static readonly int ID_CB_SIM_CONFIG     = Shader.PropertyToID("SimConfig");
        public static readonly int ID_CB_SIM_PAINT      = Shader.PropertyToID("SimPaint");
        public static readonly int ID_CB_LIGHT_CONFIG   = Shader.PropertyToID("LightConfig");
        public static readonly int ID_CB_VIS_CONFIG     = Shader.PropertyToID("VisConfig");

        // --- Buffer / Texture IDs ---
        public static readonly int ID_SIM_DATA  = Shader.PropertyToID("_SimData");
        public static readonly int ID_SIM_HEAT  = Shader.PropertyToID("_SimHeat");
        
        public static readonly int ID_MATERIALS = Shader.PropertyToID("_Materials");
        public static readonly int ID_REACTIONS = Shader.PropertyToID("_Reactions");
        public static readonly int ID_DECAYS    = Shader.PropertyToID("_Decay");
        
        public static readonly int ID_VIS_TEXTURE           = Shader.PropertyToID("_VisTexture");
        
        public static readonly int ID_LIGHT_TEXTURE         = Shader.PropertyToID("_LightTexture");
        public static readonly int ID_LIGHT_TEXTURE_TEMP    = Shader.PropertyToID("_LightTextureTemp");
        public static readonly int ID_LIGHT_TEXTURE_READ    = Shader.PropertyToID("_LightTextureRead");
        
        public static readonly int ID_BLOOM_TEXTURE         = Shader.PropertyToID("_BloomTexture");
        public static readonly int ID_BLOOM_TEXTURE_READ    = Shader.PropertyToID("_BloomTextureRead");

        public static readonly int ID_HEAT_GRAD             = Shader.PropertyToID("_HeatRamp");
    }
}
