#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LightProbeDebug
{
    [MenuItem("Tools/Lighting/Debug/Log Probe At Selected Renderer")]
    private static void LogProbeAtSelectedRenderer()
    {
        var go = Selection.activeGameObject;
        if (!go)
        {
            Debug.LogWarning("[LightProbeDebug] No selection.");
            return;
        }

        // Prefer the renderer on the selected object (so you can click the exact LOD renderer),
        // otherwise fall back to first child renderer.
        var r = go.GetComponent<Renderer>();
        if (!r) r = go.GetComponentInChildren<Renderer>();

        if (!r)
        {
            Debug.LogWarning("[LightProbeDebug] Selected object has no Renderer.");
            return;
        }

        var anchor = r.probeAnchor ? r.probeAnchor.position : (Vector3?)null;
        var center = r.bounds.center;

        Debug.Log(
            "[LightProbeDebug] --- Renderer state ---\n" +
            $"name='{r.name}' type={r.GetType().Name}\n" +
            $"probeAnchor={(r.probeAnchor ? r.probeAnchor.name : "<none>")} anchorPos={(anchor.HasValue ? anchor.Value.ToString() : "<n/a>")}\n" +
            $"boundsCenter={center} boundsExtents={r.bounds.extents}\n" +
            $"lightProbeUsage={r.lightProbeUsage} reflectionProbeUsage={r.reflectionProbeUsage}\n" +
            $"lightmapIndex={r.lightmapIndex} realtimeLightmapIndex={r.realtimeLightmapIndex}\n" +
            $"isPartOfStaticBatch={r.isPartOfStaticBatch}\n" +
            $"QualitySettings.shadowmaskMode={QualitySettings.shadowmaskMode}\n" +
            $"LightmapSettings.lightmapsMode={LightmapSettings.lightmapsMode}\n"
        );

        // Sample at anchor (if any), center, and corners to detect "some part outside tetra" or per-vertex-ish issues.
        SampleAndLog(r, anchor ?? center, "SamplePos (anchor or center)");
        SampleAndLog(r, center, "Bounds center");

        var b = r.bounds;
        var e = b.extents;
        var corners = new[]
        {
            b.center + new Vector3(+e.x, +e.y, +e.z),
            b.center + new Vector3(+e.x, +e.y, -e.z),
            b.center + new Vector3(+e.x, -e.y, +e.z),
            b.center + new Vector3(+e.x, -e.y, -e.z),
            b.center + new Vector3(-e.x, +e.y, +e.z),
            b.center + new Vector3(-e.x, +e.y, -e.z),
            b.center + new Vector3(-e.x, -e.y, +e.z),
            b.center + new Vector3(-e.x, -e.y, -e.z),
        };

        for (int i = 0; i < corners.Length; i++)
            SampleAndLog(r, corners[i], $"Bounds corner {i}");

        Debug.Log("[LightProbeDebug] --- End ---");
    }

    private static void SampleAndLog(Renderer r, Vector3 samplePos, string label)
    {
        LightProbes.GetInterpolatedProbe(samplePos, r, out var sh);
        var ambient = RenderSettings.ambientProbe;

        // Also fetch occlusion probe (shadowmask) at this position.
        var posArr = new[] { samplePos };
        var shArr = new SphericalHarmonicsL2[1];
        var occArr = new Vector4[1];
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(posArr, shArr, occArr);
        var occ = occArr[0];

        // Evaluate a few directions.
        var dirs = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        float minProbe = float.PositiveInfinity;
        float minAmbient = float.PositiveInfinity;
        float maxAbsDelta = 0f;

        foreach (var d in dirs)
        {
            var cProbe = EvalSH(sh, d.normalized);
            var cAmb = EvalSH(ambient, d.normalized);

            float lProbe = Mathf.Max(cProbe.r, Mathf.Max(cProbe.g, cProbe.b));
            float lAmb = Mathf.Max(cAmb.r, Mathf.Max(cAmb.g, cAmb.b));

            minProbe = Mathf.Min(minProbe, lProbe);
            minAmbient = Mathf.Min(minAmbient, lAmb);
            maxAbsDelta = Mathf.Max(maxAbsDelta, Mathf.Abs(lProbe - lAmb));
        }

        // Heuristic: if probe ~= ambient, you're outside tetra OR otherwise getting fallback-like behavior.
        bool probeLooksLikeAmbient = maxAbsDelta < 0.01f;

        Debug.Log(
            $"[LightProbeDebug] {label}\n" +
            $"pos={samplePos}\n" +
            $"minProbe={minProbe:0.000000} minAmbient={minAmbient:0.000000} maxAbsDelta={maxAbsDelta:0.000000} probeLooksLikeAmbient={probeLooksLikeAmbient}\n" +
            $"occlusionProbe(shadowmask)={occ}  (very small values here can kill mixed direct light)\n"
        );
    }

    private static Color EvalSH(SphericalHarmonicsL2 sh, Vector3 dir)
    {
        var dirs = new[] { dir };
        var cols = new Color[1];
        sh.Evaluate(dirs, cols);
        return cols[0];
    }
}
#endif