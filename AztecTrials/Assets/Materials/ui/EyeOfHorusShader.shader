Shader "Custom/EyeOfHorusShader"
{
    Properties
    {
        _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Alpha ("Alpha", Range(0,1)) = 1

        // Fixed size in world units (does NOT scale with distance).
        _SizeWorld ("Size (World Units)", Vector) = (0.25, 0.25, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        // Must NOT show through other meshes.
        ZTest LEqual
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Alpha;
            float4 _SizeWorld;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Billboard around the object's origin. Uses world-up so head-roll doesn't roll the element.
                float3 worldCenter = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 toCam = _WorldSpaceCameraPos - worldCenter;
                float dist = max(length(toCam), 1e-3);
                float3 viewDir = toCam / dist;

                float3 worldUp = float3(0, 1, 0);
                float3 right = cross(worldUp, viewDir);
                float rightLenSq = dot(right, right);
                right = (rightLenSq > 1e-6) ? (right * rsqrt(rightLenSq)) : float3(1, 0, 0);
                float3 up = normalize(cross(viewDir, right));

                // Quad corners from UVs (uv 0..1 => -1..1)
                float2 quad = v.uv * 2.0 - 1.0;

                // Fixed world size (no distance scaling).
                float2 halfSizeWorld = _SizeWorld.xy * 0.5;

                float3 worldPos = worldCenter + right * (quad.x * halfSizeWorld.x) + up * (quad.y * halfSizeWorld.y);
                o.vertex = UnityWorldToClipPos(worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = tex * _Color;
                col.a *= _Alpha;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
