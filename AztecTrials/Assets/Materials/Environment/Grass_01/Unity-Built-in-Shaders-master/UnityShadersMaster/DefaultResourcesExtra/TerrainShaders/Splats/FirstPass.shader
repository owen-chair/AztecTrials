// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "UnityShadersMaster/Nature/Terrain/Diffuse" {
    Properties {
        // used in fallback on old cards & base map
        [HideInInspector] _MainTex ("BaseMap (RGB)", 2D) = "white" {}
        [HideInInspector] _Color ("Main Color", Color) = (1,1,1,1)
        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}
    }

    CGINCLUDE
        #pragma surface surf BakedOnly vertex:SplatmapVert finalcolor:FinalColorNoFog noshadow noforwardadd novertexlights exclude_path:deferred exclude_path:prepass
        #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

        // This shader never applies fog; avoid generating/transferring fog interpolators.
        #define TERRAIN_NO_FOG 1

        // Project-specific optimization: we only use a single Terrain Layer.
        // This removes the 4-way splat mixing cost (samples/UVs for _Splat1..3).
        #define TERRAIN_SINGLE_LAYER 1
        #include "Assets/Materials/Environment/Grass_01/Unity-Built-in-Shaders-master/UnityShadersMaster/CGIncludes/TerrainSplatmapCommon.cginc"

        // Keep includes minimal: baked GI only, no specular/smoothness.
        #include "Assets/Materials/Environment/Grass_01/Unity-Built-in-Shaders-master/UnityShadersMaster/CGIncludes/UnityLightingCommon.cginc"
        #include "Assets/Materials/Environment/Grass_01/Unity-Built-in-Shaders-master/UnityShadersMaster/CGIncludes/UnityGlobalIllumination.cginc"

        struct SurfaceOutputBakedOnly
        {
            fixed3 Albedo;
            fixed3 Normal;
            fixed3 Emission;
            fixed Alpha;
        };

        inline fixed4 LightingBakedOnly(SurfaceOutputBakedOnly s, UnityGI gi)
        {
            fixed4 c;
            c.rgb = 0;
            c.a = s.Alpha;

            // Baked-only: only indirect diffuse contributes.
            c.rgb += s.Albedo * gi.indirect.diffuse;

            return c;
        }

        inline void LightingBakedOnly_GI(SurfaceOutputBakedOnly s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
        }

        void FinalColorNoFog(Input IN, SurfaceOutputBakedOnly o, inout fixed4 color)
        {
            color *= o.Alpha;
        }

        void surf(Input IN, inout SurfaceOutputBakedOnly o)
        {
            half4 splat_control;
            half weight;
            fixed4 mixedDiffuse;
            SplatmapMix(IN, splat_control, weight, mixedDiffuse, o.Normal);
            o.Albedo = mixedDiffuse.rgb;
            o.Alpha = weight;
        }
    ENDCG

    Category {
        Tags {
            "Queue" = "Geometry-99"
            "RenderType" = "Opaque"
        }
        // TODO: Seems like "#pragma target 3.0 _NORMALMAP" can't fallback correctly on less capable devices?
        // Use two sub-shaders to simulate different features for different targets and still fallback correctly.
        SubShader { // for sm3.0+ targets
            CGPROGRAM
                #pragma target 2.0
                #pragma multi_compile_local __ _ALPHATEST_ON
            ENDCG

            UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
            UsePass "Hidden/Nature/Terrain/Utilities/SELECTION"
        }
        SubShader { // for sm2.0 targets
            CGPROGRAM
            ENDCG
        }
    }

    Dependency "AddPassShader"    = "UnityShadersMaster/Hidden/TerrainEngine/Splatmap/Diffuse-AddPass"
    Dependency "BaseMapShader"    = "UnityShadersMaster/Hidden/TerrainEngine/Splatmap/Diffuse-Base"
    Dependency "BaseMapGenShader" = "UnityShadersMaster/Hidden/TerrainEngine/Splatmap/Diffuse-BaseGen"
    Fallback Off
}
