using UnityEngine;

using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
#endif

public static class DisableReflectionProbesOnAllObjects
{
#if UNITY_EDITOR
    [MenuItem("Tools/Lighting/Disable Reflection Probes (Renderer Usage Off)")]
    public static void DisableReflectionProbeUsageInActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogWarning("[DisableReflectionProbes] No valid active scene.");
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        int changed = 0;
        int visited = 0;

        // Collect first, then apply in one Undo group. This avoids generating thousands of separate Undo steps.
        List<Renderer> toChange = new List<Renderer>(1024);

        for (int r = 0; r < roots.Length; r++)
        {
            GameObject root = roots[r];
            if (root == null) { continue; }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer ren = renderers[i];
                if (ren == null) { continue; }
                visited++;

                if (ren.reflectionProbeUsage != ReflectionProbeUsage.Off)
                {
                    toChange.Add(ren);
                }
            }
        }

        if (toChange.Count > 0)
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Disable Reflection Probe Usage");

            // One Undo operation for all changed renderers.
            Undo.RecordObjects(toChange.ToArray(), "Disable Reflection Probe Usage");

            for (int i = 0; i < toChange.Count; i++)
            {
                Renderer ren = toChange[i];
                if (ren == null) { continue; }
                ren.reflectionProbeUsage = ReflectionProbeUsage.Off;
                EditorUtility.SetDirty(ren);
                changed++;
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[DisableReflectionProbes] Active scene '{scene.name}': set reflectionProbeUsage=Off on {changed}/{visited} Renderers.");
    }
#endif
}
