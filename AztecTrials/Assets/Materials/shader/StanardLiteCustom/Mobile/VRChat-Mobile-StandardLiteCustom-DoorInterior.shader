Shader "VRChat/Mobile/Standard Lite Custom Door Interior"
{
    Properties
    {
        _MainTex("Albedo(RGB)", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)

        [NoScaleOffset] _MetallicGlossMap("Metallic(R) Map", 2D) = "white" {}
        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 1.0

        _BumpScale("Scale", Float) = 1.0
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

        [NoScaleOffset] _OcclusionMap("Occlusion(G)", 2D) = "white" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0

        [NoScaleOffset] _EmissionMap("Emission(RGB)", 2D) = "white" {}
        _EmissionColor("Emission Color", Color) = (0, 0, 0)

        [Enum(Default,0,MonoSH,1,MonoSH (no highlights),2)] _LightmapType("Lightmap Type", Float) = 0
        [HideInInspector] _UdonTempleDoorInteriorVisibility("Door Interior Visibility", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM

        //#define _DEBUG_VRC
        #ifdef _DEBUG_VRC
            #define DEBUG_COL(rgb) debugCol = half4(rgb, 1)
            #define DEBUG_VAL(val) debugCol = half4(val, val, val, 1)
                half4 debugCol = half4(0,0,0,1);
        #else
            #define DEBUG_COL(rgb)
            #define DEBUG_VAL(val)
        #endif

        #pragma target 3.5
        #pragma shader_feature_fragment _ _MONOSH
        #pragma shader_feature_fragment _ _EMISSION
        #pragma shader_feature_fragment _ _METALLICGLOSSMAP
        #pragma shader_feature_fragment _ _NORMALMAP
        #pragma shader_feature_fragment _ _OCCLUSIONMAP

        #define _GLOSSYREFLECTIONS_OFF 1

        #include "VRChat.cginc"

        #pragma surface surf StandardVRCNoRealtimeDoorInterior vertex:vert exclude_path:prepass exclude_path:deferred noforwardadd novertexlights noshadow nodynlightmap nolppv noshadowmask
        #pragma skip_variants LIGHTMAP_SHADOW_MIXING

        half _UdonTempleDoorInteriorVisibility;

        inline half4 LightingStandardVRCNoRealtimeDoorInterior(
            SurfaceOutputStandardVRC surface,
            float3 viewDirection,
            UnityGI lighting)
        {
            half oneMinusReflectivity = OneMinusReflectivityFromMetallic(surface.Metallic);
            surface.Albedo *= oneMinusReflectivity;

            half outputAlpha;
            surface.Albedo = PreMultiplyAlpha(
                surface.Albedo,
                surface.Alpha,
                oneMinusReflectivity,
                outputAlpha
            );

            lighting.indirect.diffuse *= _UdonTempleDoorInteriorVisibility;
            half4 color = half4(lighting.indirect.diffuse * surface.Albedo, outputAlpha);

            #ifndef _DEBUG_VRC
                return color;
            #else
                return debugCol;
            #endif
        }

        inline void LightingStandardVRCNoRealtimeDoorInterior_GI(
            SurfaceOutputStandardVRC surface,
            UnityGIInput data,
            inout UnityGI lighting)
        {
            LightingStandardVRCNoRealtime_GI(surface, data, lighting);
        }

        struct Input
        {
            float2 texcoord0;
            fixed4 color : COLOR;
        };

        UNITY_DECLARE_TEX2D(_MainTex);
        float4 _MainTex_ST;
        half4 _Color;

        #if defined(_METALLICGLOSSMAP)
        UNITY_DECLARE_TEX2D(_MetallicGlossMap);
        #endif
        uniform half _Metallic;

        #if defined(_NORMALMAP)
        UNITY_DECLARE_TEX2D(_BumpMap);
        #endif
        uniform half _BumpScale;

        #if defined(_OCCLUSIONMAP)
        UNITY_DECLARE_TEX2D(_OcclusionMap);
        #endif
        uniform half _OcclusionStrength;

        #if defined(_EMISSION)
        UNITY_DECLARE_TEX2D(_EmissionMap);
        #endif
        half4 _EmissionColor;

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.texcoord0 = TRANSFORM_TEX(vertex.texcoord.xy, _MainTex);
        }

        void surf(Input input, inout SurfaceOutputStandardVRC output)
        {
            half3 albedoMap = UNITY_SAMPLE_TEX2D(_MainTex, input.texcoord0).rgb * _Color.rgb;
            albedoMap *= input.color.rgb;
            output.Albedo = albedoMap;
            output.Alpha = 1.0h;

            #if defined(_METALLICGLOSSMAP)
            output.Metallic = UNITY_SAMPLE_TEX2D(_MetallicGlossMap, input.texcoord0).r * _Metallic;
            #else
            output.Metallic = _Metallic;
            #endif

            #if defined(_OCCLUSIONMAP)
            half occlusion = UNITY_SAMPLE_TEX2D(_OcclusionMap, input.texcoord0).g;
            output.Occlusion = 1.0h + (occlusion - 1.0h) * _OcclusionStrength;
            #else
            output.Occlusion = 1.0h;
            #endif

            #if defined(_NORMALMAP) && (defined(DIRLIGHTMAP_COMBINED) || defined(UNITY_SHOULD_SAMPLE_SH) || defined(_MONOSH))
            float2 textureDerivativeX = ddx(input.texcoord0);
            float2 textureDerivativeY = ddy(input.texcoord0);
            output.Normal = UnpackScaleNormal(
                SAMPLE_TEXTURE2D_GRAD(
                    _BumpMap,
                    sampler_BumpMap,
                    input.texcoord0,
                    textureDerivativeX,
                    textureDerivativeY
                ),
                _BumpScale
            );
            #else
            output.Normal = half3(0, 0, 1);
            #endif

            #if defined(_EMISSION)
                output.Emission = UNITY_SAMPLE_TEX2D(_EmissionMap, input.texcoord0).rgb * _EmissionColor.rgb;
            #else
                output.Emission = 0;
            #endif
        }
        ENDCG
    }

    FallBack "VRChat/Mobile/Diffuse"
    CustomEditor "StandardLiteCustomShaderGUI"
}
