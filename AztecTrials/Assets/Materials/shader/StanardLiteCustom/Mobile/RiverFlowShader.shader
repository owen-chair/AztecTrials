Shader "VRChat/Mobile/Custom/RiverFlowShader"
{
    Properties
    {
        _FlowMap ("Flow Map", 2D) = "gray" {}
        _FlowUVMap ("Flow UV Map", 2D) = "gray" {}
        _VelocityMap ("Velocity Map", 2D) = "white" {}
        _FoamMask ("Foam Mask", 2D) = "black" {}
        _FoamMotionMap ("Foam Motion Map", 2D) = "black" {}
        _FoamTex ("Foam Texture", 2D) = "white" {}
        _NormalA ("Normal A", 2D) = "bump" {}
        _NormalB ("Normal B", 2D) = "bump" {}

        [HideInInspector] _RiverBoundsMin ("River Bounds Min", Vector) = (0,0,0,0)
        [HideInInspector] _RiverBoundsSize ("River Bounds Size", Vector) = (1,1,1,0)

        _SlowColor ("Slow Color", Color) = (0.03,0.22,0.25,1)
        _FastColor ("Fast Color", Color) = (0.12,0.72,0.68,1)
        _FoamColor ("Foam Color", Color) = (0.92,1.0,0.92,1)
        _HighlightColor ("Highlight Color", Color) = (0.55,0.95,0.9,1)
        _WaterColorVisibility ("Water Color Visibility", Range(0,0.5)) = 0.08
        _HighlightIntensity ("Highlight Intensity", Range(0,2)) = 0.7

        _FlowStrength ("Flow Strength", Range(0,2)) = 0.65
        _FlowSpeed ("Flow Speed", Range(0,4)) = 1.0
        _NormalASpeed ("Normal A Speed", Range(0,4)) = 1.0
        _NormalBSpeed ("Normal B Speed", Range(0,4)) = 0.55
        _NormalAScale ("Normal A Scale", Float) = 0.18
        _NormalBScale ("Normal B Scale", Float) = 0.42
        _NormalStrength ("Normal Strength", Range(0,2)) = 0.7
        _NormalFlowDistortion ("Normal Flow Distortion", Range(0,3)) = 1.0
        _NormalVelocityDistortion ("Normal Velocity Distortion", Range(0,3)) = 0.75
        _FoamIntensity ("Foam Intensity", Range(0,4)) = 1.2
        _FoamThreshold ("Foam Threshold", Range(0,1)) = 0.35
        _FoamScroll ("Foam Scroll", Range(0,2)) = 0.35
        _FoamTexScale ("Foam Texture Scale", Float) = 8.0
        _FoamBubbleNoise ("Foam Bubble Noise", Range(0,1)) = 0.35
        _FoamBubbleScale ("Foam Bubble Scale", Float) = 3.0
        _FoamDistortion ("Foam Distortion", Range(0,1)) = 0.35
        _VelocityContrast ("Velocity Contrast", Range(0,2)) = 1.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.62
        _TimeScale ("Time Scale", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf StandardVRCNoRealtime vertex:vert exclude_path:prepass exclude_path:deferred noforwardadd novertexlights noshadow nodynlightmap nolppv noshadowmask
        #pragma skip_variants LIGHTMAP_SHADOW_MIXING
        #pragma multi_compile_instancing

        #include "VRChat.cginc"

        sampler2D _FlowMap;
        sampler2D _FlowUVMap;
        sampler2D _VelocityMap;
        sampler2D _FoamMask;
        sampler2D _FoamMotionMap;
        sampler2D _FoamTex;
        sampler2D _NormalA;
        sampler2D _NormalB;

        float4 _RiverBoundsMin;
        float4 _RiverBoundsSize;

        half4 _SlowColor;
        half4 _FastColor;
        half4 _FoamColor;
        half4 _HighlightColor;
        half _WaterColorVisibility;
        half _HighlightIntensity;

        half _FlowStrength;
        half _FlowSpeed;
        half _NormalASpeed;
        half _NormalBSpeed;
        half _NormalAScale;
        half _NormalBScale;
        half _NormalStrength;
        half _NormalFlowDistortion;
        half _NormalVelocityDistortion;
        half _FoamIntensity;
        half _FoamThreshold;
        half _FoamScroll;
        half _FoamTexScale;
        half _FoamBubbleNoise;
        half _FoamBubbleScale;
        half _FoamDistortion;
        half _VelocityContrast;
        half _Smoothness;
        half _TimeScale;

        UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct Input
        {
            float2 rawUv;
            float3 worldPos;
            float3 viewDir;
            INTERNAL_DATA
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUv = v.texcoord.xy;
        }

        inline half2 MapUv(float3 worldPos)
        {
            half2 size = max(_RiverBoundsSize.xz, half2(0.001h, 0.001h));
            return saturate((worldPos.xz - _RiverBoundsMin.xz) / size);
        }

        inline half2 DecodeFlowUv(half2 mapUv)
        {
            half2 flow = tex2D(_FlowUVMap, mapUv).rg * 2.0h - 1.0h;
            half len = length(flow);
            if (len < 0.001h)
            {
                return half2(0.0h, 0.0h);
            }

            return flow / len;
        }

        inline half3 ApplyNormalStrength(half3 n)
        {
            n.xy *= _NormalStrength;
            n.z = sqrt(saturate(1.0h - dot(n.xy, n.xy)));
            return normalize(n);
        }

        inline half3 BlendNormalsX(half3 a, half3 b)
        {
            half3 n;
            n.xy = a.xy + b.xy;
            n.z = a.z * b.z;
            return normalize(n);
        }

        inline half Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        inline half ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            half a = Hash21(i);
            half b = Hash21(i + float2(1.0, 0.0));
            half c = Hash21(i + float2(0.0, 1.0));
            half d = Hash21(i + float2(1.0, 1.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        inline half FlowFoamDetail(float2 riverUv, half2 uvFlow, half velocity, half3 motion, float time, half speed)
        {
            float2 rawFlow = float2(uvFlow.x, uvFlow.y);
            float2 T = dot(rawFlow, rawFlow) > 1e-6 ? normalize(rawFlow) : float2(0.0, 1.0);
            float2 B = float2(-T.y, T.x);
            float2 basisUv = float2(dot(riverUv, T), dot(riverUv, B));

            half wake = saturate(motion.r);
            half turbulence = saturate(motion.g);
            half rapid = saturate(motion.b);
            half energy = saturate(max(max(wake, turbulence), rapid) + velocity * 0.35h);
            float scale = max(_FoamTexScale, 0.001h);
            float scroll = time * max(speed, 0.05h) * max(_FoamScroll, 0.0001h) * lerp(0.35, 2.15, energy);
            float swirlNoise = ValueNoise(basisUv * scale * 0.33 + float2(time * 0.17, -time * 0.11)) * 2.0 - 1.0;
            float sideWarp = swirlNoise * _FoamDistortion * (turbulence * 0.85 + wake * 0.35);
            float stretch = lerp(1.15, 2.65, saturate(velocity + rapid * 0.45h));

            float2 foamUvA = float2(basisUv.x * scale / stretch - scroll, basisUv.y * scale * stretch + sideWarp);
            float2 foamUvB = float2(basisUv.x * scale * 1.73 / stretch - scroll * 0.63 + 0.37, basisUv.y * scale * 1.31 * stretch - sideWarp * 0.6 + 0.61);
            half texA = tex2D(_FoamTex, frac(foamUvA)).r;
            half texB = tex2D(_FoamTex, frac(foamUvB)).r;
            half textureFoam = max(texA, texB * 0.78h);
            half bubbleAmount = saturate(_FoamBubbleNoise);
            float bubbleScale = scale * max(_FoamBubbleScale, 0.001h);
            half bubbleA = ValueNoise(basisUv * bubbleScale + float2(time * 0.11, -time * 0.08));
            half bubbleB = ValueNoise(basisUv * bubbleScale * 2.31 + float2(19.7 - time * 0.17, 4.3 + time * 0.13));
            half bubbleField = saturate(bubbleA * 0.68h + bubbleB * 0.46h);
            half bubbleMask = smoothstep(lerp(0.86h, 0.46h, bubbleAmount), 0.98h, bubbleField);
            half bubblyTexture = saturate(lerp(textureFoam, textureFoam * (0.72h + bubbleMask * 0.65h) + bubbleMask * 0.32h, bubbleAmount));
            half breakup = ValueNoise(foamUvA * float2(0.42, 1.8) + float2(13.1, time * 0.19));
            half streaks = smoothstep(0.34h, lerp(0.82h, 0.56h, energy), bubblyTexture * lerp(0.78h, 1.28h, wake) + breakup * turbulence * 0.22h + bubbleMask * bubbleAmount * 0.16h);
            half slowDetail = lerp(0.65h, 1.05h, bubblyTexture);
            return saturate(lerp(slowDetail, streaks, saturate(energy + wake * 0.25h)));
        }

        inline half3 FlowNormalSample(sampler2D normalMap, float2 baseUv, float2 uvFlow, float clock, half distortion)
        {
            float phase0 = frac(clock);
            float phase1 = frac(clock + 0.5);
            half blend = abs(1.0h - 2.0h * phase0);
            float2 flowOffset = uvFlow * max(distortion, 0.0h);
            half3 normal0 = UnpackNormal(tex2D(normalMap, frac(baseUv - flowOffset * phase0)));
            half3 normal1 = UnpackNormal(tex2D(normalMap, frac(baseUv - flowOffset * phase1)));
            return normalize(lerp(normal0, normal1, blend));
        }

        void surf(Input IN, inout SurfaceOutputStandardVRC o)
        {
            half2 mapUv = MapUv(IN.worldPos);
            float2 riverUv = IN.rawUv;
            half2 uvFlow = DecodeFlowUv(mapUv);
            half velocity = saturate(tex2D(_VelocityMap, mapUv).r);
            half velocityTone = saturate((velocity - 0.2h) * _VelocityContrast + 0.2h);

            float time = _Time.y * _TimeScale;
            float speed = _FlowSpeed;
            float normalScaleA = max(_NormalAScale, 0.0001);
            float normalScaleB = max(_NormalBScale, 0.0001);
            half normalDistortion = max(_NormalFlowDistortion, 0.0h) * (1.0h + velocity * max(_NormalVelocityDistortion, 0.0h));

            half3 normalA = FlowNormalSample(_NormalA, riverUv * normalScaleA, uvFlow, time * speed * _FlowStrength * _NormalASpeed, normalDistortion);
            half3 normalB = FlowNormalSample(_NormalB, riverUv * normalScaleB + float2(0.37, 0.61), uvFlow, time * speed * _FlowStrength * _NormalBSpeed * 1.37, normalDistortion);
            half3 riverNormal = ApplyNormalStrength(BlendNormalsX(normalA, normalB));

            half foamBase = tex2D(_FoamMask, mapUv).r;
            half3 foamMotion = tex2D(_FoamMotionMap, mapUv).rgb;
            half wakeSource = saturate(foamMotion.r * 0.55h + foamMotion.g * 0.22h + foamMotion.b * 0.18h);
            half foamSource = smoothstep(_FoamThreshold, 1.0h, saturate(foamBase + wakeSource * 0.28h));
            half foamDetail = FlowFoamDetail(riverUv, uvFlow, velocity, foamMotion, time, speed);
            half foamVelocity = lerp(0.82h, 1.45h, saturate(velocity + foamMotion.b * 0.45h));
            half foam = saturate(foamSource * foamDetail * foamVelocity * _FoamIntensity);

            half3 waterColor = lerp(_SlowColor.rgb, _FastColor.rgb, velocityTone);
            half fresnel = pow(1.0h - saturate(dot(normalize(IN.viewDir), riverNormal)), 3.5h);
            half highlight = fresnel * saturate(velocity * 1.25h) * (1.0h - foam * 0.45h);
            half rippleHighlight = saturate((riverNormal.x * 0.65h + riverNormal.y * 0.35h) * 0.5h + 0.5h) * saturate(velocityTone + 0.25h);
            half highlightIntensity = max(_HighlightIntensity, 0.0h);
            waterColor += _HighlightColor.rgb * highlightIntensity * (highlight * 0.2h + rippleHighlight * 0.035h);
            waterColor = lerp(waterColor, _FoamColor.rgb, foam);

            o.Albedo = waterColor;
            o.Normal = riverNormal;
            o.Alpha = 1.0h;
            o.MinimumBrightness = 0;
            o.Metallic = 0.0h;
            o.Smoothness = saturate(_Smoothness + velocity * 0.18h - foam * 0.32h);
            o.Occlusion = 1.0h;
            o.Emission = waterColor * saturate(_WaterColorVisibility) * (1.0h - foam * 0.55h) + _HighlightColor.rgb * highlightIntensity * (highlight * 0.08h + rippleHighlight * 0.015h) + _FoamColor.rgb * foam * 0.08h;
        }
        ENDCG
    }

    FallBack "VRChat/Mobile/Diffuse"
    CustomEditor "RiverShaderGUI"
}
