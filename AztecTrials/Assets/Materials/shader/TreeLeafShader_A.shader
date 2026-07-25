Shader "Custom/TreeLeafShader_A"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Leaf Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        _Tint ("Tint", Color) = (1,1,1,1)

        _LightDir ("Normalized Fake Light Direction", Vector) = (0.3312946,0.8834522,0.3312946,0)
        _LightColor ("Light Color", Color) = (1,1,1,1)
        _LightStrength ("Light Strength", Range(0,100)) = 1

        _SkyColor ("Sky Color", Color) = (1.0,1.0,1.0,1)
        _GroundColor ("Ground Color", Color) = (0.5,0.5,0.5,1)
        _HemiStrength ("Hemisphere Strength", Range(0,3)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
        }

        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling maxcount:1024 force_same_maxcount_for_gl
            #pragma target 3.5
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma force_concat_matrix
            #define _GLOSSYREFLECTIONS_OFF 1
            #include "StanardLiteCustom/Mobile/VRChat.cginc"

            sampler2D _MainTex;

            fixed4 _Tint;

            fixed _Cutoff;

            half4 _LightDir;
            fixed4 _LightColor;
            half _LightStrength;

            fixed4 _SkyColor;
            fixed4 _GroundColor;
            half _HemiStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                half2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                half2 uv : TEXCOORD0;
                half3 lighting : TEXCOORD1;
                fixed3 color : COLOR;
                float4 vertex : SV_POSITION;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);

                o.vertex = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                o.uv = v.uv;

                half3 normalWS = UnityObjectToWorldNormal(v.normal);
                half hemiFactor = saturate(normalWS.y * 0.35h + 0.65h);
                half3 hemi = lerp(_GroundColor.rgb, _SkyColor.rgb, hemiFactor) * _HemiStrength;
                half direct = abs(dot(normalWS, _LightDir.xyz)) * _LightStrength;

                o.lighting = hemi * (0.5h + _LightColor.rgb * direct);
                o.color = v.color.rgb * _Tint.rgb;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);

                clip(tex.a - _Cutoff);

                half3 finalColor = (tex.rgb * i.color) * i.lighting;

                fixed4 col = fixed4(finalColor, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }

            ENDCG
        }
    }

    Fallback Off
}