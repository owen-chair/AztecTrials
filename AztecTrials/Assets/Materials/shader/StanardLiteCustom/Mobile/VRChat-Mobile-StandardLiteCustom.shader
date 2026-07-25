Shader "VRChat/Mobile/Standard Lite Custom"
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

        [Enum(Default,0,MonoSH,1,MonoSH (no highlights),2)] _LightmapType ("Lightmap Type", Float) = 0

        // _DETAIL and _BICUBIC features have been stripped from this project.
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
        //SDK-SYNC-IGNORE-LINE - unused variants in SDK projects - #pragma multi_compile_fragment _ FORCE_UNITY_DLDR_LIGHTMAP_ENCODING FORCE_UNITY_RGBM_LIGHTMAP_ENCODING FORCE_UNITY_LIGHTMAP_FULL_HDR_ENCODING UNITY_LIGHTMAP_NONE

        // Project assumption: never use reflection probes / glossy reflections.
        // Force the OFF path at compile-time so we don't build extra variants and don't execute probe sampling code.
        #define _GLOSSYREFLECTIONS_OFF 1

        #include "VRChat.cginc"

        #pragma surface surf StandardVRCNoRealtime vertex:vert exclude_path:prepass exclude_path:deferred noforwardadd novertexlights noshadow nodynlightmap nolppv noshadowmask
        #pragma skip_variants LIGHTMAP_SHADOW_MIXING

        // -------------------------------------

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


        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        //UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        //UNITY_INSTANCING_BUFFER_END(Props)

        // -------------------------------------
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
                o.texcoord0 = TRANSFORM_TEX(v.texcoord.xy, _MainTex); // Always source from uv0
        }

        void surf(Input IN, inout SurfaceOutputStandardVRC o)
        {
            // Albedo comes from a texture tinted by color
            half3 albedoMap = UNITY_SAMPLE_TEX2D(_MainTex, IN.texcoord0).rgb * _Color.rgb;
            albedoMap *= IN.color.rgb;
            o.Albedo = albedoMap;
            o.Alpha = 1.0h;

            #if defined(_METALLICGLOSSMAP)
            o.Metallic = UNITY_SAMPLE_TEX2D(_MetallicGlossMap, IN.texcoord0).r * _Metallic;
            #else
            o.Metallic = _Metallic;
            #endif

            // Occlusion is sampled from the Green channel to match up with Standard. Can be packed to Metallic if you insert it into multiple slots.
            #if defined(_OCCLUSIONMAP)
            half occlusion = UNITY_SAMPLE_TEX2D(_OcclusionMap, IN.texcoord0).g;
            o.Occlusion = 1.0h + (occlusion - 1.0h) * _OcclusionStrength;
            #else
            o.Occlusion = 1.0h;
            #endif

            #if defined(_NORMALMAP) && (defined(DIRLIGHTMAP_COMBINED) || defined(UNITY_SHOULD_SAMPLE_SH) || defined(_MONOSH))
            float2 dx0 = ddx(IN.texcoord0);
            float2 dy0 = ddy(IN.texcoord0);
            o.Normal = UnpackScaleNormal(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, IN.texcoord0, dx0, dy0), _BumpScale);
            #else
            o.Normal = half3(0, 0, 1);
            #endif

            #if defined(_EMISSION) //SDK-SYNC-IGNORE-LINE
                o.Emission = UNITY_SAMPLE_TEX2D(_EmissionMap, IN.texcoord0).rgb * _EmissionColor.rgb; //SDK-SYNC-IGNORE-LINE
            #else
                o.Emission = 0;
            #endif //SDK-SYNC-IGNORE-LINE
        }
        ENDCG
    }

    FallBack "VRChat/Mobile/Diffuse"
    CustomEditor "StandardLiteCustomShaderGUI"
}