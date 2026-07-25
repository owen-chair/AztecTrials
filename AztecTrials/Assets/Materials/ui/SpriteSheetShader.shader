Shader "Custom/SpriteSheetShader"
{
    Properties
    {
        _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Alpha ("Alpha", Range(0,1)) = 1

        // Fixed size in world units (does NOT scale with distance).
        _SizeWorld ("Size (World Units)", Vector) = (0.25, 0.25, 0, 0)

        // Sprite sheet grid.
        _Columns ("Columns", Float) = 1
        _Rows ("Rows", Float) = 1

        // Frames per second.
        _FPS ("FPS", Float) = 12
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
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            half _Alpha;
            float4 _SizeWorld;
            float _Columns;
            float _Rows;
            float _FPS;

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

                // Raw 0..1 quad UV (do NOT apply _MainTex_ST to frame selection).
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                int cols = max(1, (int)floor(_Columns + 0.5));
                int rows = max(1, (int)floor(_Rows + 0.5));
                int total = max(1, cols * rows);

                float fpsRaw = max(0.0, _FPS);
                float fpsActive = step(0.0001, fpsRaw); // 1 when fps > 0, else 0
                float fps = max(0.0001, fpsRaw);
                float invCols = 1.0 / (float)cols;
                float invRows = 1.0 / (float)rows;
                float2 frameSizeUV = float2(invCols, invRows);

                // Sample inside the frame. Add a half-texel inset to reduce bleeding.
                // Clamp inset so it can't exceed the frame size.
                float2 inset = _MainTex_TexelSize.xy * 0.5;
                inset = min(inset, frameSizeUV * 0.49);

                // Animation timing:
                // - Play frames 0..(total-2) at FPS (with per-frame blending).
                // - When reaching the last frame (total-1), blend last->first over 2 seconds.
                float frameDur = 1.0 / fps;
                float preDuration = (float)max(0, total - 1) * frameDur;
                const float loopBlendSeconds = 2.0;
                float cycleDuration = preDuration + loopBlendSeconds;

                // Efficient modulo for positive time.
                float tInCycle = frac(_Time.y / cycleDuration) * cycleDuration;

                // Branchless selection between normal playback and loop crossfade.
                float hasMany = step(1.5, (float)total);
                float inLoop = fpsActive * hasMany * step(preDuration, tInCycle);
                float inPre = 1.0 - inLoop;

                // Normal playback values.
                float frameF = tInCycle * fps;
                float frameFloor = floor(frameF);
                float blendPre = saturate(frameF - frameFloor);

                int totalMinus1 = total - 1;
                int totalMinus2 = max(0, total - 2);
                int idx0Pre = (int)frameFloor;
                idx0Pre = max(0, min(idx0Pre, totalMinus2));
                int idx1Pre = min(idx0Pre + 1, totalMinus1);

                // Loop crossfade values.
                int idx0Loop = totalMinus1;
                int idx1Loop = 0;
                float blendLoop = saturate((tInCycle - preDuration) / loopBlendSeconds);

                // Select indices and blend.
                float idx0F = ((float)idx0Pre) * inPre + ((float)idx0Loop) * inLoop;
                float idx1F = ((float)idx1Pre) * inPre + ((float)idx1Loop) * inLoop;
                float blend = blendPre * inPre + blendLoop * inLoop;

                // If FPS is 0, force static frame 0.
                idx0F *= fpsActive;
                idx1F *= fpsActive;
                blend *= fpsActive;

                int idx0 = (int)idx0F;
                int idx1 = (int)idx1F;

                // Frame 0 UV
                int cy0Raw = idx0 / cols;
                int cx0 = idx0 - (cy0Raw * cols);
                int cy0 = (rows - 1) - cy0Raw;
                float2 baseUV0 = float2((float)cx0 * invCols, (float)cy0 * invRows);
                float2 uv0 = lerp(baseUV0 + inset, baseUV0 + frameSizeUV - inset, saturate(i.uv));
                uv0 = TRANSFORM_TEX(uv0, _MainTex);

                // Frame 1 UV
                int cy1Raw = idx1 / cols;
                int cx1 = idx1 - (cy1Raw * cols);
                int cy1 = (rows - 1) - cy1Raw;
                float2 baseUV1 = float2((float)cx1 * invCols, (float)cy1 * invRows);
                float2 uv1 = lerp(baseUV1 + inset, baseUV1 + frameSizeUV - inset, saturate(i.uv));
                uv1 = TRANSFORM_TEX(uv1, _MainTex);

                fixed4 tex0 = tex2D(_MainTex, uv0);
                fixed4 tex1 = tex2D(_MainTex, uv1);
                fixed4 tex = lerp(tex0, tex1, blend);

                fixed4 col = tex * _Color;
                col.a *= _Alpha;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
