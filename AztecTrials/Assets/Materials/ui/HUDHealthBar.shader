Shader "Custom/HUDHealthBar"
{
    Properties
    {
        _Percent ("Health Percent", Range(0,100)) = 100

        _Alpha ("Alpha", Range(0,1)) = 1

        // Constant apparent size in VR (angular size in degrees).
        _AngularSizeDeg ("Angular Size (Degrees)", Vector) = (4, 4, 0, 0)

        // Moves the bar down by N * (bar height). Keeps separation consistent as it scales with distance.
        _OffsetDownHeights ("Offset Down (Bar Heights)", Range(0,5)) = 1

        // Disable the down-offset when very close (meters).
        _OffsetStartDistance ("Offset Starts At Distance", Range(0,100)) = 10
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        ZTest Always
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

            half _Percent;
            half _Alpha;
            float4 _AngularSizeDeg;
            half _OffsetDownHeights;
            float _OffsetStartDistance;

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

                // Project the object's origin, then build a world-up-aligned billboard
                // so head-roll doesn't roll the HUD element.
                float3 worldCenter = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 toCam = _WorldSpaceCameraPos - worldCenter;
                float dist = max(length(toCam), 1e-3);
                float3 viewDir = toCam / dist;

                float3 worldUp = float3(0, 1, 0);
                // Cylindrical billboard: keep vertical aligned to worldUp (no head-roll).
                float3 right = cross(worldUp, viewDir);
                float rightLenSq = dot(right, right);
                right = (rightLenSq > 1e-6) ? (right * rsqrt(rightLenSq)) : float3(1, 0, 0);
                float3 up = worldUp;

                // Quad corners from UVs (uv 0..1 => -1..1)
                float2 quad = v.uv * 2.0 - 1.0;

                // Constant angular size: world half-size scales with distance.
                float2 halfAngleRad = radians(_AngularSizeDeg.xy * 0.5);
                float2 halfSizeWorld = tan(halfAngleRad) * dist;

                // Push down (world-up aligned) so it doesn't overlap other HUD elements as distance changes.
                // Use a clamped distance for the OFFSET ONLY, so it won't flick at a threshold and it won't
                // slide upward when very close.
                float distForOffset = max(dist, _OffsetStartDistance);
                float2 halfSizeWorldForOffset = tan(halfAngleRad) * distForOffset;
                worldCenter -= up * (halfSizeWorldForOffset.y * 2.0) * _OffsetDownHeights;

                float3 worldPos = worldCenter + right * (quad.x * halfSizeWorld.x) + up * (quad.y * halfSizeWorld.y);
                o.vertex = UnityWorldToClipPos(worldPos);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Thin horizontal line centered vertically.
                // "Thin" is relative to the quad height; tweak here if needed.
                const half thickness = 0.12h;
                half inBand = step(abs(i.uv.y - 0.5h), thickness * 0.5h);

                // Fill from left to right.
                half t = saturate(_Percent / 100.0h);
                // Fill from right to left.
                half filled = step(1.0h - t, i.uv.x);

                // Color: red at 0%, green at 100%.
                half3 colRgb = lerp(half3(1, 0, 0), half3(0, 1, 0), t);

                half a = inBand * filled;
                a *= _Alpha;
                return fixed4(colRgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}