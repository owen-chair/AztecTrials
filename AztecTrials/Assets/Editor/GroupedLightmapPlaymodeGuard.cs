// Disabled by default. Define GROUPED_LIGHTMAP_HOOKS to re-enable.
#if UNITY_EDITOR
#if GROUPED_LIGHTMAP_HOOKS
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class GroupedLightmapPlaymodeGuard
{
    private const string PrefKeyPrefix = "GroupedLightmapBaker.";
    static GroupedLightmapPlaymodeGuard()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        try
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                ForceOnDemandGIWorkflow();
                LogLightmaps("Before Play");
                TryRestoreIfWiped("Before Play");
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                LogLightmaps("Entered Play");
                TryRestoreIfWiped("Entered Play");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                LogLightmaps("After Play");
                TryRestoreIfWiped("After Play");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GroupedLightmapPlaymodeGuard] Error: " + ex);
        }
    }

    private static void ForceOnDemandGIWorkflow()
    {
        // This is the main switch Unity uses for Auto Generate vs manual lighting.
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

        // Best-effort: also disable LightingSettings.autoGenerate when available.
        try
        {
            var lightingSettings = Lightmapping.lightingSettings;
            if (lightingSettings != null)
            {
                var prop = lightingSettings.GetType().GetProperty("autoGenerate");
                if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
                {
                    prop.SetValue(lightingSettings, false, null);
                    EditorUtility.SetDirty(lightingSettings);
                }
            }
        }
        catch
        {
            // Ignore if Unity version doesn't expose this.
        }
    }

    private static void LogLightmaps(string label)
    {
        var maps = LightmapSettings.lightmaps;
        int count = (maps != null) ? maps.Length : 0;

        Debug.Log($"[GroupedLightmapPlaymodeGuard] {label}: LightmapSettings.lightmaps = {count}");

        if (maps == null) return;

        for (int i = 0; i < maps.Length; i++)
        {
            var data = maps[i];
            if (data == null)
            {
                Debug.Log($"[GroupedLightmapPlaymodeGuard] {label}: [{i}] <null>");
                continue;
            }

            string c = PathOrNone(data.lightmapColor);
            string d = PathOrNone(data.lightmapDir);
            string s = PathOrNone(data.shadowMask);
            Debug.Log($"[GroupedLightmapPlaymodeGuard] {label}: [{i}] color={c} dir={d} mask={s}");
        }
    }

    private static string PathOrNone(Texture tex)
    {
        if (tex == null) return "<none>";
        string p = AssetDatabase.GetAssetPath(tex);
        return string.IsNullOrEmpty(p) ? ("<no-asset-path>:" + tex.name) : p;
    }

    private static void TryRestoreIfWiped(string label)
    {
        // If Play Mode cleared the references (common with some tooling), restore from deterministic assets.
        var maps = LightmapSettings.lightmaps;
        int count = (maps != null) ? maps.Length : 0;

        bool anyNullTexture = false;
        if (maps != null)
        {
            for (int i = 0; i < maps.Length; i++)
            {
                var d = maps[i];
                if (d == null) { anyNullTexture = true; break; }
                if (d.lightmapColor == null && d.lightmapDir == null && d.shadowMask == null) { anyNullTexture = true; break; }
            }
        }

        if (!anyNullTexture)
        {
            return;
        }

        if (!TryLoadLastBakeInfo(out var folder, out var expectedCount, out var mode))
        {
            Debug.LogWarning("[GroupedLightmapPlaymodeGuard] " + label + ": lightmaps appear wiped, but no bake info found to restore.");
            return;
        }

        if (expectedCount <= 0)
        {
            Debug.LogWarning("[GroupedLightmapPlaymodeGuard] " + label + ": lightmaps appear wiped, but expected count is 0.");
            return;
        }

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

        Debug.Log($"[GroupedLightmapPlaymodeGuard] {label}: restored LightmapSettings.lightmaps to {expectedCount} from {folder} (colors found: {restoredColors})");
        LogLightmaps(label + " (Restored)");
    }

    private static bool TryLoadLastBakeInfo(out string folder, out int expectedCount, out int mode)
    {
        folder = null;
        expectedCount = 0;
        mode = (int)LightmapsMode.NonDirectional;

        // Prefer restoring for the currently active scene.
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);
                if (!string.IsNullOrEmpty(sceneGuid))
                {
                    string sceneFolder = EditorPrefs.GetString(PrefKeyPrefix + sceneGuid + ".Folder", null);
                    int sceneCount = EditorPrefs.GetInt(PrefKeyPrefix + sceneGuid + ".Count", 0);
                    int sceneMode = EditorPrefs.GetInt(PrefKeyPrefix + sceneGuid + ".Mode", (int)LightmapsMode.NonDirectional);

                    if (!string.IsNullOrEmpty(sceneFolder))
                    {
                        folder = sceneFolder;
                        expectedCount = sceneCount;
                        mode = sceneMode;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        // Fallback to the last baked scene.
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

        // Prefer EXR (Unity's default for baked lightmaps), but try common fallbacks.
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
#endif
#endif
