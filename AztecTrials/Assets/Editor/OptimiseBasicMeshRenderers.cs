using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class OptimiseBasicMeshRenderers
{
    private static void SetContributeGIFlag(GameObject go, bool contribute)
    {
        if (go == null) return;

        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
        flags = SetStaticFlagByName(flags, "ContributeGI", contribute);
        // Older Unity versions used LightmapStatic for the same checkbox.
        flags = SetStaticFlagByName(flags, "LightmapStatic", contribute);
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    private static StaticEditorFlags SetStaticFlagByName(StaticEditorFlags flags, string name, bool enabled)
    {
        if (string.IsNullOrEmpty(name)) return flags;

        object parsed;
        try
        {
            parsed = System.Enum.Parse(typeof(StaticEditorFlags), name);
        }
        catch
        {
            return flags;
        }

        StaticEditorFlags flag = (StaticEditorFlags)parsed;
        return enabled ? (flags | flag) : (flags & ~flag);
    }

    [MenuItem("Tools/Optimize Static MeshRenderers")]
    public static void OptimizeStaticMeshes()
    {
#if UNITY_2022_2_OR_NEWER
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        MeshRenderer[] renderers = Object.FindObjectsOfType<MeshRenderer>(true);
#endif
        int count = 0;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        foreach (MeshRenderer mr in renderers)
        {
            if (!mr || !mr.gameObject.isStatic)
                continue;

            // Record both: renderer settings and Contribute GI flag on the GameObject.
            Undo.RecordObject(mr, "Optimize Static MeshRenderer");
            Undo.RecordObject(mr.gameObject, "Optimize Static MeshRenderer");

            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            mr.allowOcclusionWhenDynamic = false;

            SetContributeGIFlag(mr.gameObject, false);

            EditorUtility.SetDirty(mr);
            EditorUtility.SetDirty(mr.gameObject);
            count++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"Optimized {count} static MeshRenderer(s).", Selection.activeObject);
    }
}
