using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

public static class StaticToBakedSettings
{
#if UNITY_EDITOR
    private static void SetReceiveGIToLightmaps(Renderer renderer)
    {
        if (!renderer) { return; }

        // `Renderer.receiveGI` is not available in some Unity versions.
        // The inspector field "Receive Global Illumination" is serialized as `m_ReceiveGI`.
        var so = new SerializedObject(renderer);
        var prop = so.FindProperty("m_ReceiveGI");
        if (prop == null)
        {
            return;
        }

        if (prop.propertyType == SerializedPropertyType.Enum)
        {
            // Prefer selecting by name so we don't depend on enum numeric values.
            int idx = System.Array.IndexOf(prop.enumNames, "Lightmaps");
            if (idx >= 0)
            {
                prop.enumValueIndex = idx;
            }
        }
        else
        {
            // Best-effort fallback.
            prop.intValue = 1;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("Tools/Lighting/Apply Baked Settings to Static Renderers (Selected)")]
    private static void ApplyBakedSettingsToSelection()
    {
        var roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0)
        {
            Debug.LogWarning("No GameObjects selected.");
            return;
        }

        int rendererCount = 0;
        int changedCount = 0;

        foreach (var root in roots)
        {
            if (!root) { continue; }

            // Include inactive children.
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (!t) { continue; }

                var go = t.gameObject;
                if (!go || !go.isStatic) { continue; }

                var renderer = go.GetComponent<Renderer>();
                if (!renderer) { continue; }

                rendererCount++;

                // Renderer settings for baked lighting.
                renderer.shadowCastingMode = ShadowCastingMode.On; // Cast Shadows: On
                renderer.receiveShadows = true; // Receive Shadows: On
                SetReceiveGIToLightmaps(renderer); // Receive Global Illumination: Lightmaps
                renderer.lightProbeUsage = LightProbeUsage.Off; // Light Probes: Off
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox; // Reflection Probes: Blend and Skybox
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera; // Motion Vectors: Camera Motion Only
                renderer.allowOcclusionWhenDynamic = false; // Dynamic Occlusion: Off

                EditorUtility.SetDirty(renderer);

                // Contribute GI: On (static flag).
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.ContributeGI) == 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.ContributeGI);
                    EditorUtility.SetDirty(go);
                }

                changedCount++;
            }
        }

        Debug.Log($"Baked settings applied to {changedCount}/{rendererCount} static renderers under {roots.Length} selected root(s).");
    }

    [MenuItem("Tools/Lighting/Apply Baked Settings to Static Renderers (Selected)", true)]
    private static bool ApplyBakedSettingsToSelection_Validate()
        => Selection.gameObjects != null && Selection.gameObjects.Length > 0;
#endif
}
