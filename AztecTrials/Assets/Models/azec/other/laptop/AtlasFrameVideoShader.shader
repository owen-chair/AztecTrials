Shader "Custom/AtlasFrameVideoShader"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Sprite Atlas", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _FPS ("FPS", Float) = 12
        _Columns ("Columns", Int) = 8
        _Rows ("Rows", Int) = 6
        _FrameCount ("Frame Count", Int) = 48
        _StartPause ("Start Pause (Seconds)", Float) = 0
        _EndPause ("End Pause (Seconds)", Float) = 0
        _LCDStartDistance ("LCD Effect Start Distance", Float) = 1.5
        _LCDFullDistance ("LCD Effect Full Strength Distance", Float) = 0.25
        _LCDStartAngle ("LCD Pixel Start Angle", Float) = 55
        _LCDFullAngle ("LCD Pixel Full Strength Angle", Float) = 12
        _LCDPixelDensity ("Pixel Density", Float) = 180
        _LCDStrength ("LCD Strength", Range(0,1)) = 0.45
        _SubpixelStrength ("RGB Subpixel Strength", Range(0,1)) = 0.35
        _GridStrength ("Pixel Grid Strength", Range(0,1)) = 0.35
        _LCDWashoutStrength ("LCD Angle Washout Strength", Range(0,1)) = 0.45
        _LCDContrastLoss ("LCD Contrast Loss", Range(0,1)) = 0.35
        _LCDSaturationLoss ("LCD Saturation Loss", Range(0,1)) = 0.3
        _LCDBlackLift ("LCD Black Lift", Range(0,1)) = 0.12
        _LCDTintShift ("LCD Tint Shift", Range(-1,1)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM

            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float _FPS;
            int _Columns;
            int _Rows;
            int _FrameCount;
            float _StartPause;
            float _EndPause;
            float _LCDStartDistance;
            float _LCDFullDistance;
            float _LCDStartAngle;
            float _LCDFullAngle;
            float _LCDPixelDensity;
            half _LCDStrength;
            half _SubpixelStrength;
            half _GridStrength;
            half _LCDWashoutStrength;
            half _LCDContrastLoss;
            half _LCDSaturationLoss;
            half _LCDBlackLift;
            half _LCDTintShift;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                // Packed varyings: 3 TEXCOORD registers instead of 5 reduces interpolator pressure on Quest 2 tile GPUs.
                float4 uv0BaseUv : TEXCOORD0; // xy = current frame UV, zw = original mesh UV for stable LCD grid.
                float4 uv1BlendLcd : TEXCOORD1; // xy = next frame UV, z = frame blend, w = distance/angle LCD pixel amount.
                half lcdWashout : TEXCOORD2; // Angle-only LCD washout amount; no world position needed per fragment.
                float4 vertex : SV_POSITION;
            };

            float2 AtlasFrameUV(float2 uv, int frame, int columns, int rows, float2 frameSize)
            {
                int row = frame / columns;
                int column = frame - row * columns;
                float2 frameOffset = float2(column, rows - 1 - row) * frameSize;
                return frameOffset + saturate(uv) * frameSize;
            }

            float FastSmooth01(float value)
            {
                value = saturate(value);
                return value * value * (3.0 - 2.0 * value);
            }

            half CheapPixelHash(half2 pixelCell)
            {
                // Replaces the previous sin() hash. frac/dot/mul is much cheaper on Adreno and is stable in UV space.
                half2 p = frac(pixelCell * half2(0.1031h, 0.1137h));
                p += dot(p, p.yx + 19.19h);
                return frac((p.x + p.y) * p.x);
            }

            fixed3 ApplyLCDEffect(fixed3 color, float2 baseUv, half lcdAmount, half washoutAmount)
            {
                // The LCD grid uses original mesh UVs, not atlas UVs, so it remains stable across every animation frame.
                half pixelDensity = max((half)_LCDPixelDensity, 1.0h);
                half2 lcdUv = (half2)saturate(baseUv) * pixelDensity;
                half2 pixelCell = floor(lcdUv);
                half2 pixelUv = frac(lcdUv);

                // Tiny deterministic cell variation gives neighbouring pixels a slightly different brightness without shimmer.
                half pixelVariation = lerp(0.965h, 1.035h, CheapPixelHash(pixelCell));

                // RGB subpixels are vertical stripes inside each virtual pixel, tinting the source instead of replacing it.
                half stripe = frac(pixelUv.x * 3.0h);
                half redStripe = 1.0h - step(0.3333h, pixelUv.x);
                half greenStripe = step(0.3333h, pixelUv.x) * (1.0h - step(0.6667h, pixelUv.x));
                half blueStripe = step(0.6667h, pixelUv.x);
                half stripeCenter = 1.0h - abs(stripe * 2.0h - 1.0h);
                half3 subpixelMask = half3(redStripe, greenStripe, blueStripe) * (0.75h + 0.25h * stripeCenter);
                half3 subpixelTint = lerp(half3(1.0h, 1.0h, 1.0h), lerp(half3(0.94h, 0.94h, 0.94h), 1.12h * subpixelMask, _SubpixelStrength), lcdAmount);

                // Dark gaps are generated with a linear ramp instead of smoothstep to reduce ALU in the fragment shader.
                half2 edgeDistance = min(pixelUv, 1.0h - pixelUv);
                half gridLine = saturate((0.08h - min(edgeDistance.x, edgeDistance.y)) * 15.3846h);
                half gridDarken = 1.0h - gridLine * _GridStrength * lcdAmount * 0.55h;

                // A small contrast lift exaggerates close-up LCD structure while preserving the underlying animation.
                half luminance = dot(color, fixed3(0.299h, 0.587h, 0.114h));
                half3 contrasted = lerp(luminance.xxx, color, 1.0h + 0.12h * lcdAmount);
                half3 lcdColor = saturate(contrasted * subpixelTint * gridDarken * lerp(1.0h, pixelVariation, lcdAmount * 0.45h));

                // Oblique LCD viewing is a color treatment only: no blur or extra texture reads, so text/video stays readable.
                half contrastLoss = _LCDContrastLoss * washoutAmount;
                half saturationLoss = _LCDSaturationLoss * washoutAmount;
                half3 washColor = lerp(0.5h.xxx, lcdColor, 1.0h - contrastLoss);

                half washLuminance = dot(washColor, fixed3(0.299h, 0.587h, 0.114h));
                washColor = lerp(washColor, washLuminance.xxx, saturationLoss);

                // Black lift affects darker pixels more than bright pixels, matching LCD angle wash-out without crushing detail.
                half blackLift = _LCDBlackLift * washoutAmount;
                washColor += (1.0h - washColor) * blackLift * (1.0h - washLuminance);

                // Extremely subtle warm/cool tint shift; positive values warm, negative values cool.
                half tintShift = _LCDTintShift * washoutAmount * 0.04h;
                washColor *= half3(1.0h + tintShift, 1.0h, 1.0h - tintShift);
                washColor += washoutAmount * 0.015h;

                return saturate(washColor);
            }

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);

                o.vertex = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                int columns = max(1, _Columns);
                int rows = max(1, _Rows);
                int gridFrameCount = columns * rows;
                int frameCount = min(max(1, _FrameCount), gridFrameCount);
                int finalFrame = frameCount - 1;
                int transitionCount = max(1, finalFrame);

                float fps = max(_FPS, 0.0001);
                float startPause = max(_StartPause, 0.0);
                float endPause = max(_EndPause, 0.0);
                float playDuration = transitionCount / fps;
                float loopDuration = startPause + playDuration + endPause + playDuration;
                float loopTime = frac(_Time.y / loopDuration) * loopDuration;

                // Timeline: pause on frame 0, interpolate forward, pause on the final frame, then reverse to frame 0.
                float playStart = startPause;
                float playEnd = startPause + playDuration;
                float reverseStart = playEnd + endPause;
                float playMask = step(playStart, loopTime) * (1.0 - step(playEnd, loopTime));
                float endMask = step(playEnd, loopTime) * (1.0 - step(reverseStart, loopTime));
                float reverseMask = step(reverseStart, loopTime);

                float forwardTime = max(loopTime - playStart, 0.0) * fps;
                int forwardFrame = min((int)floor(forwardTime), finalFrame);
                int forwardNextFrame = min(forwardFrame + 1, finalFrame);

                float reverseTime = max(loopTime - reverseStart, 0.0) * fps;
                int reverseFrame = max(finalFrame - (int)floor(reverseTime), 0);
                int reverseNextFrame = max(reverseFrame - 1, 0);
                float blend = frac(forwardTime) * playMask + frac(reverseTime) * reverseMask;

                int currentFrame = (int)(forwardFrame * playMask + finalFrame * endMask + reverseFrame * reverseMask);
                int nextFrame = (int)(forwardNextFrame * playMask + finalFrame * endMask + reverseNextFrame * reverseMask);

                float2 frameSize = 1.0 / float2(columns, rows);

                // Distance fade is evaluated per vertex using squared distance, avoiding per-fragment world position and sqrt().
                float3 viewVector = _WorldSpaceCameraPos - worldPos;
                float viewLengthSq = max(dot(viewVector, viewVector), 0.0001);
                float startDistance = max(_LCDStartDistance, 0.0);
                float fullDistance = max(_LCDFullDistance, 0.0);
                float farDistance = max(startDistance, fullDistance);
                float nearDistance = min(startDistance, fullDistance);
                float farDistanceSq = farDistance * farDistance;
                float nearDistanceSq = nearDistance * nearDistance;
                float distanceFade = FastSmooth01((farDistanceSq - viewLengthSq) / max(farDistanceSq - nearDistanceSq, 0.0001));

                // Viewing-angle fade uses squared facing from dot products, avoiding normalize(), acos(), sin(), and cos().
                float3 worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);
                float normalLengthSq = max(dot(worldNormal, worldNormal), 0.0001);
                float normalViewDot = dot(worldNormal, viewVector);
                float facingSq = saturate((normalViewDot * normalViewDot) / (normalLengthSq * viewLengthSq));

                // Angle properties use a cheap quadratic cosine approximation, keeping degree controls useful without cos().
                float startAngle01 = saturate(_LCDStartAngle / 90.0);
                float fullAngle01 = saturate(_LCDFullAngle / 90.0);
                float startFacing = 1.0 - startAngle01 * startAngle01;
                float fullFacing = 1.0 - fullAngle01 * fullAngle01;
                float lowFacingSq = min(startFacing, fullFacing);
                float highFacingSq = max(startFacing, fullFacing);
                lowFacingSq *= lowFacingSq;
                highFacingSq *= highFacingSq;
                float angleRange = max(highFacingSq - lowFacingSq, 0.0001);
                float angleFade = FastSmooth01((facingSq - lowFacingSq) / angleRange);
                float washoutFade = FastSmooth01((highFacingSq - facingSq) / angleRange) * _LCDWashoutStrength;

                float2 currentUv = AtlasFrameUV(v.uv, currentFrame, columns, rows, frameSize);
                float2 nextUv = AtlasFrameUV(v.uv, nextFrame, columns, rows, frameSize);
                o.uv0BaseUv = float4(currentUv, v.uv);
                o.uv1BlendLcd = float4(nextUv, blend, distanceFade * angleFade * _LCDStrength);
                o.lcdWashout = (half)washoutFade;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample each frame independently, then interpolate colors rather than UVs.
                fixed4 currentFrameColour = tex2D(_MainTex, i.uv0BaseUv.xy);
                fixed4 nextFrameColour = tex2D(_MainTex, i.uv1BlendLcd.xy);
                fixed4 col = lerp(currentFrameColour, nextFrameColour, i.uv1BlendLcd.z) * _Tint;
                col.rgb = ApplyLCDEffect(col.rgb, i.uv0BaseUv.zw, (half)i.uv1BlendLcd.w, i.lcdWashout);
                return col;
            }

            ENDCG
        }
    }

    Fallback Off
}