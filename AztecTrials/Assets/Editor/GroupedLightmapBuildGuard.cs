// Disabled by default. Define GROUPED_LIGHTMAP_HOOKS to re-enable.
#if UNITY_EDITOR
#if GROUPED_LIGHTMAP_HOOKS
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// VRChat SDK builds can reload/modify scenes and sometimes lighting references end up cleared in the editor.
/// This guard restores LightmapSettings from the deterministic Lightmap-<i>_comp_* assets recorded by GroupedLightmapBaker.
/// </summary>
[InitializeOnLoad]
internal static class GroupedLightmapBuildGuard
{
    private const string PrefKeyPrefix = "GroupedLightmapBaker.";

    static GroupedLightmapBuildGuard()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;

        // Also do a one-shot restore after scripts reload.
        EditorApplication.delayCall += () =>
        {
            try { TryRestoreIfMissing("DelayCall"); } catch { }
        };
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        try { TryRestoreIfMissing("SceneOpened"); } catch { }
    }

    internal static void TryRestoreIfMissing(string reason)
    {
        var maps = LightmapSettings.lightmaps;
        bool missing = (maps == null || maps.Length == 0);

        if (!missing)
        {
            // Also treat "present but fully null" as missing.
            bool anyNonNull = false;
            for (int i = 0; i < maps.Length; i++)
            {
                var d = maps[i];
                if (d == null) continue;
                if (d.lightmapColor != null || d.lightmapDir != null || d.shadowMask != null)
                {
                    anyNonNull = true;
                    break;
                }
            }
            missing = !anyNonNull;
        }

        if (!missing) return;

        if (!TryLoadBakeInfoForActiveScene(out var folder, out var expectedCount, out var mode))
        {
            return;
        }

        if (expectedCount <= 0) return;

        var restored = new LightmapData[expectedCount];
        int restoredColors = 0;
        for (int i = 0; i < expectedCount; i++)
        {
            var lm = new LightmapData();
            lm.lightmapColor = LoadTexture(folder, $"Lightmap-{i}_comp_light");
            lm.lightmapDir = LoadTexture(folder, $"Lightmap-{i}_comp_dir");
            lm.shadowMask = LoadTexture(folder, $"Lightmap-{i}_comp_shadowmask");
            if (lm.lightmapColor != null) restoredColors++;
            restored[i] = lm;
        }

        LightmapSettings.lightmapsMode = (LightmapsMode)mode;
        LightmapSettings.lightmaps = restored;

        Debug.Log($"[GroupedLightmapBuildGuard] {reason}: restored LightmapSettings.lightmaps to {expectedCount} from {folder} (colors found: {restoredColors})");
    }

    private static bool TryLoadBakeInfoForActiveScene(out string folder, out int expectedCount, out int mode)
    {
        folder = null;
        expectedCount = 0;
        mode = (int)LightmapsMode.NonDirectional;

        // Prefer active scene
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);
                if (!string.IsNullOrEmpty(sceneGuid))
                {
                    folder = EditorPrefs.GetString(PrefKeyPrefix + sceneGuid + ".Folder", null);
                    expectedCount = EditorPrefs.GetInt(PrefKeyPrefix + sceneGuid + ".Count", 0);
                    mode = EditorPrefs.GetInt(PrefKeyPrefix + sceneGuid + ".Mode", (int)LightmapsMode.NonDirectional);
                    if (!string.IsNullOrEmpty(folder)) return true;
                }
            }
        }
        catch { }

        // Fallback to last
        if (!EditorPrefs.HasKey(PrefKeyPrefix + "LastSceneGuid")) return false;
        string guid = EditorPrefs.GetString(PrefKeyPrefix + "LastSceneGuid", "");
        if (string.IsNullOrEmpty(guid)) return false;

        folder = EditorPrefs.GetString(PrefKeyPrefix + guid + ".Folder", null);
        expectedCount = EditorPrefs.GetInt(PrefKeyPrefix + guid + ".Count", 0);
        mode = EditorPrefs.GetInt(PrefKeyPrefix + guid + ".Mode", (int)LightmapsMode.NonDirectional);
        return !string.IsNullOrEmpty(folder);
    }

    private static Texture2D LoadTexture(string folder, string baseName)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(baseName)) return null;

        string p = folder.TrimEnd('/') + "/" + baseName;
        Texture2D tex;

        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p + ".exr");
        if (tex != null) return tex;
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p + ".png");
        if (tex != null) return tex;
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p + ".hdr");
        if (tex != null) return tex;
        return null;
    }
}

internal sealed class GroupedLightmapBuildGuard_BuildCallbacks : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => int.MinValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        try { GroupedLightmapBuildGuard.TryRestoreIfMissing("PreBuild"); } catch (Exception ex) { Debug.LogWarning("[GroupedLightmapBuildGuard] PreBuild error: " + ex); }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        try { GroupedLightmapBuildGuard.TryRestoreIfMissing("PostBuild"); } catch (Exception ex) { Debug.LogWarning("[GroupedLightmapBuildGuard] PostBuild error: " + ex); }
    }
}
#endif
#endif
