Shader "VRChat/Mobile/Custom/RGBA_TextureMixShader"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)

        _SplatMap("RGB Control Map", 2D) = "black" {}
        _TexR("Mud Layer", 2D) = "white" {}
        _TexG("Leaf Litter Layer", 2D) = "white" {}
        _TexB("Stones Layer", 2D) = "white" {}

        _TexR_ST("Mud Layer Tiling", Vector) = (1,1,0,0)
        _TexG_ST("Leaf Litter Layer Tiling", Vector) = (1,1,0,0)
        _TexB_ST("Stones Layer Tiling", Vector) = (1,1,0,0)

        [NoScaleOffset] _RootsTex("Roots Layer", 2D) = "white" {}
        _TriplanarBlendSharpness("Triplanar Blend Sharpness", Range(1.0, 16.0)) = 4.0
        _RootsTriplanarTiling("Roots Triplanar Tiling", Float) = 1.0
        _RootStartAngle("Root Start Angle", Range(0.0, 90.0)) = 45.0
        _RootFullAngle("Root Full Angle", Range(0.0, 90.0)) = 80.0

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

        #pragma target 3.0
        #pragma shader_feature_fragment _ _MONOSH
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
            float3 worldPos;
            half3 rootsWorldNormal;
            fixed4 color : COLOR;
        };

        UNITY_DECLARE_TEX2D(_SplatMap);
        UNITY_DECLARE_TEX2D(_TexR);
        UNITY_DECLARE_TEX2D(_TexG);
        UNITY_DECLARE_TEX2D(_TexB);
        UNITY_DECLARE_TEX2D(_RootsTex);
        float4 _TexR_ST;
        float4 _TexG_ST;
        float4 _TexB_ST;
        half4 _Color;

        half _TriplanarBlendSharpness;
        float _RootsTriplanarTiling;
        half _RootStartAngle;
        half _RootFullAngle;


        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        // -------------------------------------
	void vert(inout appdata_full v, out Input o)
	{
	    UNITY_INITIALIZE_OUTPUT(Input,o);
            o.texcoord0 = v.texcoord.xy; // Always source from UV0 for the splat map and layers
	    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        o.rootsWorldNormal = UnityObjectToWorldNormal(v.normal);
	}

        half3 GetTriplanarWeights(half3 worldNormal)
        {
            half3 blendWeights = abs(worldNormal);
            blendWeights = pow(blendWeights, max(_TriplanarBlendSharpness, 0.001h));
            blendWeights /= max(blendWeights.x + blendWeights.y + blendWeights.z, 1e-4h);
            return blendWeights;
        }

        half4 SampleRootsTriplanar(float3 worldPos, half3 blendWeights)
        {
            float3 scaledPos = worldPos * _RootsTriplanarTiling;
            half4 xProjection = UNITY_SAMPLE_TEX2D(_RootsTex, scaledPos.zy);
            half4 yProjection = UNITY_SAMPLE_TEX2D(_RootsTex, scaledPos.xz);
            half4 zProjection = UNITY_SAMPLE_TEX2D(_RootsTex, scaledPos.xy);
            return xProjection * blendWeights.x + yProjection * blendWeights.y + zProjection * blendWeights.z;
        }

        half GetRootAmount(half3 worldNormal)
        {
            const half degreesToRadians = 0.01745329252h;
            half verticality = saturate(1.0h - abs(worldNormal.y));
            half startVerticality = saturate(1.0h - cos(_RootStartAngle * degreesToRadians));
            half fullVerticality = saturate(1.0h - cos(_RootFullAngle * degreesToRadians));
            return smoothstep(startVerticality, max(fullVerticality, startVerticality + 0.001h), verticality);
        }

        void surf(Input IN, inout SurfaceOutputStandardVRC o)
        {
            float2 uv0 = IN.texcoord0;

            half3 weights = saturate(UNITY_SAMPLE_TEX2D(_SplatMap, uv0).rgb);

            float2 uvR = TRANSFORM_TEX(uv0, _TexR);
            float2 uvG = TRANSFORM_TEX(uv0, _TexG);
            float2 uvB = TRANSFORM_TEX(uv0, _TexB);

            half4 texR = UNITY_SAMPLE_TEX2D(_TexR, uvR);
            half4 texG = UNITY_SAMPLE_TEX2D(_TexG, uvG);
            half4 texB = UNITY_SAMPLE_TEX2D(_TexB, uvB);

            half4 groundMap = texR * weights.r + texG * weights.g + texB * weights.b;

            half3 worldNormal = normalize(IN.rootsWorldNormal);
            half3 triplanarWeights = GetTriplanarWeights(worldNormal);
            half4 rootsMap = SampleRootsTriplanar(IN.worldPos, triplanarWeights);

            half rootAmount = GetRootAmount(worldNormal);

            half4 albedoMap = lerp(groundMap, rootsMap, rootAmount) * _Color;
            albedoMap *= IN.color;
            o.Albedo = albedoMap.rgb;
            o.Alpha = albedoMap.a;

            o.Metallic = 0.0h;
            o.Occlusion = 1.0h;
            o.Normal = half3(0, 0, 1);
        }
        ENDCG
    }

    FallBack "VRChat/Mobile/Diffuse"
    CustomEditor "RGBA_TextureMixShaderShaderGUI"
}