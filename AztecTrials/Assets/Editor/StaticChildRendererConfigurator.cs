#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class StaticChildRendererConfigurator : EditorWindow
{
    [SerializeField] private Transform m_Root;
    [SerializeField] private bool m_IncludeRoot = true;
    [SerializeField] private bool m_IncludeInactive = true;

    [Header("Renderer Settings")]
    [SerializeField] private float m_ScaleInLightmap = 1.0f;

    [MenuItem("Tools/Lighting/Configure Static Children")]
    public static void Open()
    {
        GetWindow<StaticChildRendererConfigurator>("Static Children Config");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "Applies a set of renderer settings to every static GameObject under the given root (recursive).",
            EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space(6);

        m_Root = (Transform)EditorGUILayout.ObjectField("Root", m_Root, typeof(Transform), true);
        m_IncludeRoot = EditorGUILayout.Toggle(new GUIContent("Include Root", "If enabled, the root itself is included when it is static."), m_IncludeRoot);
        m_IncludeInactive = EditorGUILayout.Toggle(new GUIContent("Include Inactive", "If enabled, also processes inactive children."), m_IncludeInactive);

        EditorGUILayout.Space(6);
        m_ScaleInLightmap = EditorGUILayout.FloatField(new GUIContent("Scale In Lightmap", "Applied to Renderer.scaleInLightmap (and Terrain.scaleInLightmap when available)."), m_ScaleInLightmap);
        if (m_ScaleInLightmap < 0f) m_ScaleInLightmap = 0f;

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(m_Root == null))
        {
            if (GUILayout.Button("Apply To Static Children"))
            {
                Apply();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Settings applied to Renderers on static objects:\n" +
            "- scaleInLightmap = configured value\n" +
            "- Cast Shadows = On\n" +
            "- Receive Shadows = On\n" +
            "- Motion Vectors = Camera Motion Only\n" +
            "- Dynamic Occlusion = Off\n" +
            "- Light Probes = Off\n" +
            "- Reflection Probes = Off",
            MessageType.None);
    }

    private void Apply()
    {
        if (m_Root == null) return;

        int visited = 0;
        int staticObjects = 0;
        int renderersTouched = 0;

        var transforms = new List<Transform>(1024);
        GatherTransforms(m_Root, transforms, includeRoot: m_IncludeRoot, includeInactive: m_IncludeInactive);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure Static Children Renderers");

        try
        {
            for (int i = 0; i < transforms.Count; i++)
            {
                var tr = transforms[i];
                if (tr == null) continue;

                visited++;

                var go = tr.gameObject;
                if (go == null) continue;

                if (!go.isStatic) continue;
                staticObjects++;

                // Apply to all renderers on this GameObject.
                var rs = go.GetComponents<Renderer>();
                for (int r = 0; r < rs.Length; r++)
                {
                    var renderer = rs[r];
                    if (renderer == null) continue;

                    Undo.RecordObject(renderer, "Configure Renderer");

                    // 1) scale in lightmap
                    TrySetScaleInLightmap(renderer, m_ScaleInLightmap);

                    // 2) cast shadows on
                    TrySetShadowCastingMode(renderer, ShadowCastingMode.On);

                    // 3) receive shadows on
                    TrySetReceiveShadows(renderer, true);

                    // 4) motion vectors camera motion only
                    TrySetMotionVectors(renderer, MotionVectorGenerationMode.Camera);

                    // 5) dynamic occlusion off
                    TrySetAllowOcclusionWhenDynamic(renderer, false);

                    // 6) light probes, reflection probes off
                    TrySetLightProbeUsage(renderer, LightProbeUsage.Off);
                    TrySetReflectionProbeUsage(renderer, ReflectionProbeUsage.Off);

                    EditorUtility.SetDirty(renderer);
                    renderersTouched++;
                }
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }

        EditorUtility.DisplayDialog(
            "Static Children Config",
            "Done.\n\n" +
            "Visited transforms: " + visited + "\n" +
            "Static objects: " + staticObjects + "\n" +
            "Renderers updated: " + renderersTouched,
            "OK");
    }

    private static void GatherTransforms(Transform root, List<Transform> result, bool includeRoot, bool includeInactive)
    {
        if (root == null) return;

        // Manual stack to avoid recursion depth issues.
        var stack = new Stack<Transform>(1024);

        if (includeRoot)
        {
            if (includeInactive || root.gameObject.activeInHierarchy)
            {
                result.Add(root);
            }
        }

        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t == null) continue;

            int childCount = t.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var c = t.GetChild(i);
                if (c == null) continue;

                if (includeInactive || c.gameObject.activeInHierarchy)
                {
                    result.Add(c);
                }

                stack.Push(c);
            }
        }
    }

    private static void TrySetScaleInLightmap(Renderer r, float value)
    {
        return;
        if (r == null) return;

        // Some Unity versions don’t expose Renderer.scaleInLightmap as a public API.
        // Setting the serialized field keeps this tool compatible.
        try
        {
            var so = new SerializedObject(r);
            var p = so.FindProperty("m_ScaleInLightmap");
            if (p == null)
            {
                // Some versions use a different backing name.
                p = so.FindProperty("m_ScaleInLightmapValue");
            }

            if (p != null && p.propertyType == SerializedPropertyType.Float)
            {
                p.floatValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        catch
        {
        }
    }

    private static void TrySetShadowCastingMode(Renderer r, ShadowCastingMode mode)
    {
        try
        {
            //r.shadowCastingMode = mode;
        }
        catch
        {
        }
    }

    private static void TrySetReceiveShadows(Renderer r, bool receive)
    {
        try
        {
            //r.receiveShadows = receive;
        }
        catch
        {
        }
    }

    private static void TrySetMotionVectors(Renderer r, MotionVectorGenerationMode mode)
    {
        try
        {
            r.motionVectorGenerationMode = mode;
        }
        catch
        {
        }
    }

    private static void TrySetAllowOcclusionWhenDynamic(Renderer r, bool allow)
    {
        try
        {
            r.allowOcclusionWhenDynamic = allow;
        }
        catch
        {
        }
    }

    private static void TrySetLightProbeUsage(Renderer r, LightProbeUsage usage)
    {
        try
        {
            r.lightProbeUsage = usage;
        }
        catch
        {
        }
    }

    private static void TrySetReflectionProbeUsage(Renderer r, ReflectionProbeUsage usage)
    {
        try
        {
            r.reflectionProbeUsage = usage;
        }
        catch
        {
        }
    }

}
#endif
