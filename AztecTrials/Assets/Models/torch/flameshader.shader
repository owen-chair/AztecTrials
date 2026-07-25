Shader "Unlit/flameshader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Speed ("Speed", Float) = 1
        _Delta ("Delta Offset", Float) = 0
        _Alpha ("Alpha", Range(0, 2)) = 1
        _YStretch ("Y Stretch", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float3 posOS : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _Speed;
            float _Delta;
            float _Alpha;
            float _YStretch;

            float3 ApplyFlameDeformOS(float3 pos, float delta)
            {
                float offset = 15.0 * sin(delta - 0.5);
                float cosTerm = abs(cos((delta - 0.5) / 0.3));

                if (pos.y < 0.0)
                {
                    float diff = abs(pos.y) - abs(-10.5 - offset);
                    pos.x += ((diff * pos.x) * 0.0175) * cosTerm;
                    pos.z += ((diff * pos.z) * 0.0175) * cosTerm;
                }
                else
                {
                    pos.y += pos.y * _YStretch;
                    pos.x = pos.x * ((80.0 - pos.y) / 80.0);
                    pos.z = pos.z * ((80.0 - pos.y) / 80.0);

                    float diff = abs(pos.y) - abs(-10.5 - offset);
                    pos.x += ((diff * pos.x) * 0.0175) * cosTerm;
                    pos.z += ((diff * pos.z) * 0.0175) * cosTerm;

                    float waveStrength = 5.0;
                    if (pos.y > 55.5)
                    {
                        waveStrength *= (pos.y - 55.5) / 20.0;
                    }

                    float3 wavey = float3(0.0, 0.0, 0.0);
                    wavey.x = waveStrength * sin(delta * 6.283);
                    wavey.z = waveStrength * cos(delta * 6.283);

                    wavey *= (pos.y / 55.0) * (min(pos.y, 10.0) / 10.0);
                    if (pos.y > 55.5)
                    {
                        wavey.x += 5.0 * sin(delta * 6.283);
                        wavey.z += 5.0 * cos(delta * 6.283);
                    }

                    pos += wavey;
                }

                return pos;
            }

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float delta = (_Time.y * _Speed) + _Delta;
                float3 posOS = ApplyFlameDeformOS(v.vertex.xyz, delta);

                float4 posOS4 = float4(posOS, 1.0);
                o.vertex = UnityObjectToClipPos(posOS4);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 worldPos = mul(unity_ObjectToWorld, posOS4).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = worldPos;
                o.worldNormal = worldNormal;
                o.posOS = posOS;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 camPosWS = _WorldSpaceCameraPos;
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    camPosWS = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
                #endif

                float3 viewDirWS = camPosWS - i.worldPos;
                float viewDirLenSq = dot(viewDirWS, viewDirWS);
                viewDirWS = (viewDirLenSq > 1e-8) ? (viewDirWS * rsqrt(viewDirLenSq)) : float3(0.0, 0.0, 1.0);

                float3 n = i.worldNormal;
                float nLenSq = dot(n, n);
                n = (nLenSq > 1e-8) ? (n * rsqrt(nLenSq)) : float3(0.0, 1.0, 0.0);
                float faceDiff = abs(dot(n, viewDirWS));

                float heightTerm = min((abs(i.posOS.y) * 0.3) / 6.0, 1.5);
                float aBase = 0.05 + (0.75 - (min(faceDiff, 0.75) * (1.5 - heightTerm))) * faceDiff;
                float a = saturate(aBase) * _Alpha;

                fixed4 texCol = tex2D(_MainTex, i.uv);
                fixed4 col = fixed4(1.0, faceDiff, 0.0, a);
                col *= texCol;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
