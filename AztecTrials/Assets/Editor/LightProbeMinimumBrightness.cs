using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class LightProbeMinimumBrightness : EditorWindow
{
    [SerializeField] private float minimumBrightness = 0.02f;
    [SerializeField] private bool clampPerChannel = true;

    // A small, fixed direction set to approximate the minimum over the sphere.
    private static readonly Vector3[] SampleDirections =
    {
        Vector3.right,
        Vector3.left,
        Vector3.up,
        Vector3.down,
        Vector3.forward,
        Vector3.back,
        new Vector3( 1, 1, 1).normalized,
        new Vector3( 1, 1,-1).normalized,
        new Vector3( 1,-1, 1).normalized,
        new Vector3( 1,-1,-1).normalized,
        new Vector3(-1, 1, 1).normalized,
        new Vector3(-1, 1,-1).normalized,
        new Vector3(-1,-1, 1).normalized,
        new Vector3(-1,-1,-1).normalized,
    };

    [MenuItem("Tools/Lighting/Light Probes/Clamp Minimum Brightness...")]
    public static void ShowWindow()
    {
        GetWindow<LightProbeMinimumBrightness>("Light Probe Min");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Bakes can produce fully black probes (e.g., sealed rooms / no ambient). " +
            "This tool post-processes Light Probe SH by adding a constant term so every probe evaluates to at least a minimum brightness.\n\n" +
            "Workflow: Bake lighting → run this → (optional) rebake if you change lighting.",
            MessageType.Info);

        minimumBrightness = Mathf.Max(0f, EditorGUILayout.FloatField("Minimum brightness", minimumBrightness));
        clampPerChannel = EditorGUILayout.ToggleLeft("Clamp per-channel (RGB)", clampPerChannel);

        using (new EditorGUI.DisabledScope(LightmapSettings.lightProbes == null || LightmapSettings.lightProbes.count == 0))
        {
            if (GUILayout.Button("Apply To Baked Probes"))
            {
                Apply(minimumBrightness, clampPerChannel);
            }
        }

        using (new EditorGUI.DisabledScope(LightmapSettings.lightProbes == null || LightmapSettings.lightProbes.count == 0))
        {
            EditorGUILayout.LabelField("Probe count", LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count.ToString() : "0");
        }
    }

    private static void Apply(float minBrightness, bool perChannel)
    {
        var probes = LightmapSettings.lightProbes;
        if (probes == null)
        {
            Debug.LogError("LightProbeMinimumBrightness: No LightProbes present (LightmapSettings.lightProbes is null). Bake lighting first.");
            return;
        }

        var baked = probes.bakedProbes;
        if (baked == null || baked.Length == 0)
        {
            Debug.LogError("LightProbeMinimumBrightness: No baked probes found (LightProbes.bakedProbes is empty). Bake lighting first.");
            return;
        }

        minBrightness = Mathf.Max(0f, minBrightness);

        try
        {
            Undo.RegisterCompleteObjectUndo(probes, "Clamp Light Probe Minimum Brightness");
        }
        catch
        {
            // Some Unity versions may not allow undo on LightProbes; continue without.
        }

        var colors = new Color[SampleDirections.Length];

        int changed = 0;
        for (int i = 0; i < baked.Length; i++)
        {
            var sh = baked[i];
            sh.Evaluate(SampleDirections, colors);

            float minR = float.PositiveInfinity;
            float minG = float.PositiveInfinity;
            float minB = float.PositiveInfinity;

            for (int d = 0; d < colors.Length; d++)
            {
                var c = colors[d];
                minR = Mathf.Min(minR, c.r);
                minG = Mathf.Min(minG, c.g);
                minB = Mathf.Min(minB, c.b);
            }

            if (perChannel)
            {
                float addR = Mathf.Max(0f, minBrightness - minR);
                float addG = Mathf.Max(0f, minBrightness - minG);
                float addB = Mathf.Max(0f, minBrightness - minB);

                if (addR > 0f || addG > 0f || addB > 0f)
                {
                    sh[0, 0] += addR;
                    sh[1, 0] += addG;
                    sh[2, 0] += addB;
                    baked[i] = sh;
                    changed++;
                }
            }
            else
            {
                float minAll = Mathf.Min(minR, Mathf.Min(minG, minB));
                float add = Mathf.Max(0f, minBrightness - minAll);
                if (add > 0f)
                {
                    sh[0, 0] += add;
                    sh[1, 0] += add;
                    sh[2, 0] += add;
                    baked[i] = sh;
                    changed++;
                }
            }
        }

        probes.bakedProbes = baked;

        EditorUtility.SetDirty(probes);
        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(active);
        }

        Debug.Log($"LightProbeMinimumBrightness: updated {changed}/{baked.Length} probes (min={minBrightness}).");
    }
}
