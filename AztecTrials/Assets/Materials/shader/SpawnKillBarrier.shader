Shader "Custom/SpawnKillBarrier"
{
    Properties
    {
        _BaseColor      ("Base Color", Color) = (0, 0, 0, 0)
        _LineColor      ("Line Color", Color) = (0.2, 0.8, 1, 1)
        _LineFrequency  ("Line Frequency", Range(1, 128)) = 16
        _LineWidth      ("Line Width", Range(0.001, 0.2)) = 0.03
        _LineIntensity  ("Line Emission", Range(0, 10)) = 2

        _FadeStart      ("Fade Start Distance", Range(0, 200)) = 10
        _FadeEnd        ("Fade End Distance", Range(0, 500)) = 50

        _Glossiness ("Smoothness", Range(0,1)) = 0.8
        _Metallic   ("Metallic", Range(0,1))   = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 300

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        half _Glossiness;
        half _Metallic;
        fixed4 _BaseColor;
        fixed4 _LineColor;
        half _LineFrequency;
        half _LineWidth;
        half _LineIntensity;
        half _FadeStart;
        half _FadeEnd;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float _LineMask(float2 coord, float freq, float width)
        {
            // Grid-space coordinates (world-space)
            float2 g = coord * freq;
            float2 f = frac(g);
            // Distance to nearest grid line in each axis
            float2 d = min(f, 1.0 - f);

            // Antialias: widen transition by derivative.
            float2 aa = max(fwidth(g), 0.00001);

            // 1 inside line region, 0 elsewhere.
            float2 m = smoothstep(width + aa, width - aa, d);
            return saturate(max(m.x, m.y));
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Choose projection plane based on dominant normal axis so all faces get a proper grid.
            float3 n = abs(normalize(IN.worldNormal));
            float2 coord;
            if (n.y >= n.x && n.y >= n.z)
            {
                // Top/bottom faces
                coord = IN.worldPos.xz;
            }
            else if (n.x >= n.z)
            {
                // +/-X faces
                coord = IN.worldPos.zy;
            }
            else
            {
                // +/-Z faces
                coord = IN.worldPos.xy;
            }

            float lines = _LineMask(coord, _LineFrequency, _LineWidth);

            // Distance fade for line opacity: 1 at/before FadeStart, 0 at/after FadeEnd.
            float dist = distance(_WorldSpaceCameraPos, IN.worldPos);
            float denom = max(_FadeEnd - _FadeStart, 0.0001);
            float fade = saturate((_FadeEnd - dist) / denom);

            fixed3 baseCol = _BaseColor.rgb;
            fixed3 lineCol = _LineColor.rgb;

            o.Albedo = lerp(baseCol, lineCol, lines);
            o.Emission = lineCol * lines * _LineIntensity * fade;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            // Black/background becomes transparent; only the lines show.
            o.Alpha = lerp(_BaseColor.a, _LineColor.a * fade, lines);
            
        }
        ENDCG
    }
    FallBack "Diffuse"
}