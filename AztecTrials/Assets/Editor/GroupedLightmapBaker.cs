#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GroupedLightmapBaker : EditorWindow
{
    private const string PrefKeyPrefix = "GroupedLightmapBaker.";
    [Serializable]
    private class Group
    {
        public string name;
        public Transform root;

        [Tooltip("Optional explicit LightProbeGroups to include when baking this group. If none are assigned across all groups, the baker falls back to enabling probe groups by hierarchy (under the group Root).")]
        public LightProbeGroup[] lightProbeGroups;
    }

    [SerializeField] private List<Group> m_Groups = new List<Group>();
    [SerializeField] private bool m_StoreLightmapsInSceneFolder = true;
    [SerializeField] private string m_OutputRootFolder = "Assets/GroupedLightmaps";
    [SerializeField] private bool m_BakeLightProbesAtEnd = true;
    [SerializeField] private bool m_ForceOnlyActiveGroupRootEnabled = true;
    [SerializeField] private bool m_AutoSaveSceneOnFinish = true;
    [SerializeField] private bool m_SaveSnapshotAsset = true;

    private bool m_IsRunning;
    private int m_CurrentGroupIndex;

    private readonly Dictionary<int, bool> m_OriginalGroupRootActive = new Dictionary<int, bool>();

    private readonly Dictionary<int, bool> m_OriginalLightProbeGroupEnabled = new Dictionary<int, bool>();

    private List<LightmapData> m_CombinedLightmaps = new List<LightmapData>();
    private LightmapsMode m_LightmapsMode = LightmapsMode.NonDirectional;

    private readonly Dictionary<int, StaticEditorFlags> m_OriginalStaticFlags = new Dictionary<int, StaticEditorFlags>();

    private struct RendererAssignment
    {
        public Renderer renderer;
        public int lightmapIndex;
        public Vector4 scaleOffset;
    }

    private struct TerrainAssignment
    {
        public Terrain terrain;
        public int lightmapIndex;
        public Vector4 scaleOffset;
    }

    private readonly List<RendererAssignment> m_FinalRendererAssignments = new List<RendererAssignment>();
    private readonly List<TerrainAssignment> m_FinalTerrainAssignments = new List<TerrainAssignment>();

    [MenuItem("Tools/Lighting/Grouped Lightmap Baker")]
    public static void Open()
    {
        GetWindow<GroupedLightmapBaker>("Grouped Lightmap Baker");
    }

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(m_IsRunning))
        {
            EditorGUILayout.LabelField("Bakes each group separately, then merges the lightmaps and reassigns indices so each group stays on its own lightmap set.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            m_StoreLightmapsInSceneFolder = EditorGUILayout.Toggle(new GUIContent("Store Lightmaps In Scene Folder", "Recommended. Copies per-group lightmaps under the active scene folder to avoid Unity duplicating/moving them on Play Mode."), m_StoreLightmapsInSceneFolder);

            using (new EditorGUI.DisabledScope(m_StoreLightmapsInSceneFolder))
            {
                m_OutputRootFolder = EditorGUILayout.TextField(new GUIContent("Output Root", "Folder under Assets/ where per-group lightmaps will be copied (when not storing in scene folder)."), m_OutputRootFolder);
            }

            m_BakeLightProbesAtEnd = EditorGUILayout.Toggle(new GUIContent("Bake Light Probes At End", "Attempts a probe-only bake after merge. Some Unity versions do not expose a safe probe-only API; this tool will skip rather than overwrite merged lightmaps."), m_BakeLightProbesAtEnd);
            m_ForceOnlyActiveGroupRootEnabled = EditorGUILayout.Toggle(new GUIContent("Activate Only Current Group Root", "Temporarily activates only the current group's Root while baking. This helps when your levels are usually disabled/enabled."), m_ForceOnlyActiveGroupRootEnabled);
            m_AutoSaveSceneOnFinish = EditorGUILayout.Toggle(new GUIContent("Auto-Save Scene On Finish", "Saves the active scene after applying merged lightmaps so Play Mode doesn't revert lighting changes."), m_AutoSaveSceneOnFinish);
            m_SaveSnapshotAsset = EditorGUILayout.Toggle(new GUIContent("Save Snapshot Asset", "Saves a snapshot asset (lightmap textures + per-object indices) so you can restore without rebaking."), m_SaveSnapshotAsset);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Groups (bake order = lightmap index order)", EditorStyles.boldLabel);

            int newCount = Mathf.Max(0, EditorGUILayout.IntField("Group Count", m_Groups.Count));
            while (m_Groups.Count < newCount) m_Groups.Add(new Group { name = "Group" + m_Groups.Count });
            while (m_Groups.Count > newCount) m_Groups.RemoveAt(m_Groups.Count - 1);

            for (int i = 0; i < m_Groups.Count; i++)
            {
                var g = m_Groups[i];
                EditorGUILayout.BeginVertical("box");
                g.name = EditorGUILayout.TextField("Name", g.name);
                g.root = (Transform)EditorGUILayout.ObjectField("Root", g.root, typeof(Transform), true);

                int probeCount = (g.lightProbeGroups != null) ? g.lightProbeGroups.Length : 0;
                int newProbeCount = Mathf.Max(0, EditorGUILayout.IntField("Light Probe Groups", probeCount));
                if (g.lightProbeGroups == null || g.lightProbeGroups.Length != newProbeCount)
                {
                    var newArr = new LightProbeGroup[newProbeCount];
                    for (int j = 0; j < newProbeCount; j++)
                    {
                        if (g.lightProbeGroups != null && j < g.lightProbeGroups.Length) newArr[j] = g.lightProbeGroups[j];
                    }
                    g.lightProbeGroups = newArr;
                }

                if (g.lightProbeGroups != null)
                {
                    for (int j = 0; j < g.lightProbeGroups.Length; j++)
                    {
                        g.lightProbeGroups[j] = (LightProbeGroup)EditorGUILayout.ObjectField("  [" + j + "]", g.lightProbeGroups[j], typeof(LightProbeGroup), true);
                    }
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Bake + Merge"))
            {
                StartBake();
            }
        }

        if (m_IsRunning)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("Baking… see progress bar. Don’t edit scene during this.", MessageType.Info);
            if (GUILayout.Button("Cancel (restores static flags)"))
            {
                CancelBake();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Notes:\n" +
            "- This tool toggles only the Contribute GI static flag to isolate bakes.\n" +
                "- Light Probes are global for the scene; enabling/disabling probe GameObjects at runtime does not swap baked probe data.\n" +
                "- You can explicitly assign LightProbeGroups per group to verify what's included.\n" +
                "- If enabled, this tool bakes Light Probes once at the end (assigned probe groups enabled).\n" +
            "- All groups must use the same lightmap mode (Directional/NonDirectional/Shadowmask settings).",
            MessageType.None);
    }

    private void StartBake()
    {
        if (m_IsRunning) return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("Grouped Lightmap Baker", "No active scene.", "OK");
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Exit play mode before baking.", "OK");
            return;
        }

        if (m_Groups == null || m_Groups.Count == 0)
        {
            EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Add at least one group.", "OK");
            return;
        }

        for (int i = 0; i < m_Groups.Count; i++)
        {
            if (m_Groups[i] == null || m_Groups[i].root == null)
            {
                EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Every group needs a Root Transform.", "OK");
                return;
            }
            if (string.IsNullOrEmpty(m_Groups[i].name)) m_Groups[i].name = "Group" + i;
        }

        if (m_StoreLightmapsInSceneFolder)
        {
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Active scene is not saved yet. Save the scene first so lightmaps can be stored under the scene folder.", "OK");
                return;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(m_OutputRootFolder) || !m_OutputRootFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Output Root must be under Assets/ (e.g. Assets/GroupedLightmaps).", "OK");
                return;
            }
        }

        // Ensure lighting isn't auto-regenerated mid-process.
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

        // Capture and later restore ContributeGI flags for all relevant objects.
        CacheOriginalStaticFlags();
        CacheOriginalLightProbeGroupEnabled();
        CacheOriginalGroupRootActive();

        m_CombinedLightmaps.Clear();
        m_FinalRendererAssignments.Clear();
        m_FinalTerrainAssignments.Clear();

        m_IsRunning = true;
        m_CurrentGroupIndex = 0;

        // Run first bake.
        BakeCurrentGroupAsync();
    }

    private string GetUnitySceneLightmapFolder()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) return null;

        string sceneDir = System.IO.Path.GetDirectoryName(scene.path);
        if (string.IsNullOrEmpty(sceneDir)) sceneDir = "Assets";
        sceneDir = sceneDir.Replace('\\', '/');

        string unitySceneLightmapFolder = CombineAssetPath(sceneDir, scene.name);
        EnsureFolderExists(unitySceneLightmapFolder);
        return unitySceneLightmapFolder;
    }

    private string GetPerGroupOutputFolder(string groupName)
    {
        if (m_StoreLightmapsInSceneFolder)
        {
            // Put everything under Unity's auto lightmap folder for the active scene:
            // e.g. Assets/Scenes/MyScene/...
            // This is the folder Unity tends to normalize/copy to on Play Mode.
            // IMPORTANT: store textures directly next to LightingData.asset (no subfolders).
            // Note: we still avoid using Unity's Lightmap-0... filenames during the per-group bake loop,
            // because Unity will overwrite them on every bake. We generate final Unity-style names only once at the end.
            return GetUnitySceneLightmapFolder();
        }

        string safeGroup = SanitizeFolderName(groupName);

        string sceneName = SceneManager.GetActiveScene().name;
        string sceneFolder = CombineAssetPath(m_OutputRootFolder, sceneName);
        string outFolder = CombineAssetPath(sceneFolder, safeGroup);
        EnsureFolderExists(m_OutputRootFolder);
        EnsureFolderExists(sceneFolder);
        EnsureFolderExists(outFolder);
        return outFolder;
    }

    private void CancelBake()
    {
        if (!m_IsRunning) return;

        try
        {
            Lightmapping.Cancel();
        }
        catch
        {
            // ignore
        }

        RestoreOriginalStaticFlags();
        RestoreOriginalLightProbeGroupEnabled();
        RestoreOriginalGroupRootActive();
        EditorUtility.ClearProgressBar();

        m_IsRunning = false;
        m_CurrentGroupIndex = 0;

        EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Cancelled. Static flags restored.", "OK");
    }

    private void CacheOriginalLightProbeGroupEnabled()
    {
        m_OriginalLightProbeGroupEnabled.Clear();
        Scene scene = SceneManager.GetActiveScene();
        var probeGroups = Resources.FindObjectsOfTypeAll<LightProbeGroup>();
        for (int i = 0; i < probeGroups.Length; i++)
        {
            var g = probeGroups[i];
            if (g == null) continue;
            if (g.gameObject == null) continue;
            if (g.gameObject.scene != scene) continue;
            int id = g.GetInstanceID();
            if (!m_OriginalLightProbeGroupEnabled.ContainsKey(id))
            {
                m_OriginalLightProbeGroupEnabled[id] = g.enabled;
            }
        }
    }

    private void CacheOriginalGroupRootActive()
    {
        m_OriginalGroupRootActive.Clear();
        if (m_Groups == null) return;
        for (int i = 0; i < m_Groups.Count; i++)
        {
            var g = m_Groups[i];
            if (g == null || g.root == null) continue;
            var go = g.root.gameObject;
            if (go == null) continue;
            int id = go.GetInstanceID();
            if (!m_OriginalGroupRootActive.ContainsKey(id))
            {
                m_OriginalGroupRootActive[id] = go.activeSelf;
            }
        }
    }

    private void RestoreOriginalGroupRootActive()
    {
        foreach (var kvp in m_OriginalGroupRootActive)
        {
            var go = EditorUtility.InstanceIDToObject(kvp.Key) as GameObject;
            if (go == null) continue;
            go.SetActive(kvp.Value);
            EditorUtility.SetDirty(go);
        }
        m_OriginalGroupRootActive.Clear();
    }

    private void RestoreOriginalLightProbeGroupEnabled()
    {
        foreach (var kvp in m_OriginalLightProbeGroupEnabled)
        {
            var g = EditorUtility.InstanceIDToObject(kvp.Key) as LightProbeGroup;
            if (g == null) continue;
            g.enabled = kvp.Value;
            EditorUtility.SetDirty(g);
        }

        m_OriginalLightProbeGroupEnabled.Clear();
    }

    private void CacheOriginalStaticFlags()
    {
        m_OriginalStaticFlags.Clear();

        Scene scene = SceneManager.GetActiveScene();
        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (r.gameObject == null) continue;
            if (r.gameObject.scene != scene) continue;

            int id = r.gameObject.GetInstanceID();
            if (!m_OriginalStaticFlags.ContainsKey(id))
            {
                m_OriginalStaticFlags[id] = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
            }
        }

        var terrains = Resources.FindObjectsOfTypeAll<Terrain>();
        for (int i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            if (t == null) continue;
            if (t.gameObject == null) continue;
            if (t.gameObject.scene != scene) continue;

            int id = t.gameObject.GetInstanceID();
            if (!m_OriginalStaticFlags.ContainsKey(id))
            {
                m_OriginalStaticFlags[id] = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
            }
        }
    }

    private void RestoreOriginalStaticFlags()
    {
        foreach (var kvp in m_OriginalStaticFlags)
        {
            var obj = EditorUtility.InstanceIDToObject(kvp.Key) as GameObject;
            if (obj == null) continue;
            GameObjectUtility.SetStaticEditorFlags(obj, kvp.Value);
        }

        m_OriginalStaticFlags.Clear();
    }

    private void BakeCurrentGroupAsync()
    {
        if (!m_IsRunning) return;

        if (m_CurrentGroupIndex < 0 || m_CurrentGroupIndex >= m_Groups.Count)
        {
            FinishAndApply();
            return;
        }

        var group = m_Groups[m_CurrentGroupIndex];
        if (group == null || group.root == null)
        {
            CancelBake();
            return;
        }

        ApplyGroupRootActiveForBake(m_CurrentGroupIndex);

        // Set ContributeGI only for objects in this group.
        ApplyContributeGIForGroup(group.root);
        ApplyLightProbeGroupEnabledForGroup(group);

        // Bake.
        EditorUtility.DisplayProgressBar(
            "Grouped Lightmap Baker",
            "Baking group: " + group.name + " (" + (m_CurrentGroupIndex + 1) + "/" + m_Groups.Count + ")",
            Mathf.Clamp01((float)m_CurrentGroupIndex / Mathf.Max(1, m_Groups.Count))
        );

        Lightmapping.completed -= OnLightmappingCompleted;
        Lightmapping.completed += OnLightmappingCompleted;

        bool started = Lightmapping.BakeAsync();
        if (!started)
        {
            Lightmapping.completed -= OnLightmappingCompleted;
            EditorUtility.DisplayDialog("Grouped Lightmap Baker", "BakeAsync failed to start. Is another bake already running?", "OK");
            CancelBake();
        }
    }

    private void OnLightmappingCompleted()
    {
        Lightmapping.completed -= OnLightmappingCompleted;

        if (!m_IsRunning) return;

        var group = m_Groups[m_CurrentGroupIndex];
        try
        {
            CaptureGroupResult(group);
        }
        catch (Exception ex)
        {
            Debug.LogError("[GroupedLightmapBaker] Capture failed: " + ex);
            EditorUtility.DisplayDialog("Grouped Lightmap Baker", "Capture failed. See Console for details.\n\n" + ex.Message, "OK");
            CancelBake();
            return;
        }

        m_CurrentGroupIndex++;
        BakeCurrentGroupAsync();
    }

    private void ApplyLightProbeGroupEnabledForGroup(Group activeGroup)
    {
        // During per-group bakes, only include LightProbeGroup components for the active group.
        // If any group has explicit probe assignments, use that mapping; otherwise fall back to hierarchy.
        Scene scene = SceneManager.GetActiveScene();

        bool useManual = AnyManualProbeAssignments();

        HashSet<int> allowedIds = null;
        Transform activeRoot = null;

        if (useManual)
        {
            allowedIds = new HashSet<int>();
            if (activeGroup != null && activeGroup.lightProbeGroups != null)
            {
                for (int i = 0; i < activeGroup.lightProbeGroups.Length; i++)
                {
                    var pg = activeGroup.lightProbeGroups[i];
                    if (pg == null) continue;
                    allowedIds.Add(pg.GetInstanceID());
                }
            }
        }
        else
        {
            activeRoot = (activeGroup != null) ? activeGroup.root : null;
        }

        var probeGroups = Resources.FindObjectsOfTypeAll<LightProbeGroup>();
        for (int i = 0; i < probeGroups.Length; i++)
        {
            var g = probeGroups[i];
            if (g == null) continue;
            if (g.gameObject == null) continue;
            if (g.gameObject.scene != scene) continue;
            if (g.transform == null) continue;

            bool enabled;
            if (useManual)
            {
                enabled = allowedIds != null && allowedIds.Contains(g.GetInstanceID());
            }
            else
            {
                enabled = activeRoot != null && g.transform.IsChildOf(activeRoot);
            }

            g.enabled = enabled;
            EditorUtility.SetDirty(g);
        }
    }

    private bool AnyManualProbeAssignments()
    {
        if (m_Groups == null) return false;
        for (int i = 0; i < m_Groups.Count; i++)
        {
            var g = m_Groups[i];
            if (g == null) continue;
            if (g.lightProbeGroups == null) continue;
            for (int j = 0; j < g.lightProbeGroups.Length; j++)
            {
                if (g.lightProbeGroups[j] != null) return true;
            }
        }
        return false;
    }

    private void ApplyContributeGIForGroup(Transform activeGroupRoot)
    {
        Scene scene = SceneManager.GetActiveScene();

        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (r.gameObject == null) continue;
            if (r.gameObject.scene != scene) continue;

            bool inGroup = r.transform != null && activeGroupRoot != null && r.transform.IsChildOf(activeGroupRoot);
            SetContributeGI(r.gameObject, inGroup);
        }

        var terrains = Resources.FindObjectsOfTypeAll<Terrain>();
        for (int i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            if (t == null) continue;
            if (t.gameObject == null) continue;
            if (t.gameObject.scene != scene) continue;

            bool inGroup = t.transform != null && activeGroupRoot != null && t.transform.IsChildOf(activeGroupRoot);
            SetContributeGI(t.gameObject, inGroup);
        }
    }

    private void SetContributeGI(GameObject go, bool enabled)
    {
        if (go == null) return;

        StaticEditorFlags flags;
        int id = go.GetInstanceID();

        if (m_OriginalStaticFlags.TryGetValue(id, out flags))
        {
            // Use cached original as the baseline.
        }
        else
        {
            // Fallback if somehow missing.
            flags = GameObjectUtility.GetStaticEditorFlags(go);
            m_OriginalStaticFlags[id] = flags;
        }

        if (enabled) flags |= StaticEditorFlags.ContributeGI;
        else flags &= ~StaticEditorFlags.ContributeGI;

        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    private void CaptureGroupResult(Group group)
    {
        if (group == null || group.root == null) return;

        // Capture mode (directional vs non-directional). Must be consistent across all bakes.
        if (m_CurrentGroupIndex == 0)
        {
            m_LightmapsMode = LightmapSettings.lightmapsMode;
        }
        else
        {
            if (LightmapSettings.lightmapsMode != m_LightmapsMode)
            {
                throw new InvalidOperationException("LightmapsMode changed between bakes. Ensure all groups use the same lighting settings.");
            }
        }

        // Copy current bake lightmaps to a safe folder and append them to combined array.
        string groupFolder = GetPerGroupOutputFolder(group.name);
        string groupPrefix = SanitizeFolderName(group.name);

        LightmapData[] baked = LightmapSettings.lightmaps;
        if (baked == null) baked = new LightmapData[0];

        if (baked.Length == 0)
        {
            Debug.LogWarning("[GroupedLightmapBaker] Group '" + group.name + "' produced 0 lightmaps. Common causes: the group root is disabled, no bakeable lights, or no Contribute GI renderers in that group.");
        }

        int baseIndex = m_CombinedLightmaps.Count;
        for (int i = 0; i < baked.Length; i++)
        {
            var src = baked[i];
            if (src == null)
            {
                m_CombinedLightmaps.Add(null);
                continue;
            }

            bool storeInSceneFolder = m_StoreLightmapsInSceneFolder;
            int globalIndex = baseIndex + i;

            var dst = new LightmapData();

            // During the per-group bake loop, NEVER use Unity's Lightmap-0... names.
            // Unity will overwrite those on every bake, which destroys previously captured groups.
            // We'll generate the final Unity-style names once at the end.
            string scratchPrefix = storeInSceneFolder
                ? ("GLM_" + groupPrefix + "_g" + m_CurrentGroupIndex + "_i" + i + "_L" + globalIndex)
                : (groupPrefix + "_Lightmap");

            string colorBaseName = scratchPrefix + "_comp_light";
            string dirBaseName = scratchPrefix + "_comp_dir";
            string maskBaseName = scratchPrefix + "_comp_shadowmask";

            dst.lightmapColor = CopyTextureAsset(src.lightmapColor, groupFolder, colorBaseName, overwriteExisting: false);
            dst.lightmapDir = CopyTextureAsset(src.lightmapDir, groupFolder, dirBaseName, overwriteExisting: false);
            dst.shadowMask = CopyTextureAsset(src.shadowMask, groupFolder, maskBaseName, overwriteExisting: false);
            m_CombinedLightmaps.Add(dst);
        }

        // Capture assignments for renderers/terrains within this group.
        Scene scene = SceneManager.GetActiveScene();

        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (r.gameObject == null) continue;
            if (r.gameObject.scene != scene) continue;
            if (r.transform == null) continue;
            if (!r.transform.IsChildOf(group.root)) continue;

            int idx = r.lightmapIndex;
            if (idx < 0) continue;

            m_FinalRendererAssignments.Add(new RendererAssignment
            {
                renderer = r,
                lightmapIndex = baseIndex + idx,
                scaleOffset = r.lightmapScaleOffset
            });
        }

        var terrains = Resources.FindObjectsOfTypeAll<Terrain>();
        for (int i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            if (t == null) continue;
            if (t.gameObject == null) continue;
            if (t.gameObject.scene != scene) continue;
            if (t.transform == null) continue;
            if (!t.transform.IsChildOf(group.root)) continue;

            int idx = t.lightmapIndex;
            if (idx < 0) continue;

            m_FinalTerrainAssignments.Add(new TerrainAssignment
            {
                terrain = t,
                lightmapIndex = baseIndex + idx,
                scaleOffset = t.lightmapScaleOffset
            });
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void FinishAndApply()
    {
        if (!m_IsRunning) return;

        try
        {
            // Restore original static flags.
            RestoreOriginalStaticFlags();
            RestoreOriginalLightProbeGroupEnabled();
            RestoreOriginalGroupRootActive();

            // Apply combined lightmaps.
            LightmapSettings.lightmapsMode = m_LightmapsMode;

            var finalLightmaps = BuildFinalLightmapsForScene();
            LightmapSettings.lightmaps = finalLightmaps;

            // Record what we expect so we can restore quickly if Play Mode (or ClientSim) wipes lighting.
            try
            {
                SaveExpectedLightmapInfo(finalLightmaps);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GroupedLightmapBaker] Failed to save expected lightmap info: " + ex);
            }

            // Apply final per-object assignments.
            for (int i = 0; i < m_FinalRendererAssignments.Count; i++)
            {
                var a = m_FinalRendererAssignments[i];
                if (a.renderer == null) continue;
                a.renderer.lightmapIndex = a.lightmapIndex;
                a.renderer.lightmapScaleOffset = a.scaleOffset;
                EditorUtility.SetDirty(a.renderer);
            }

            for (int i = 0; i < m_FinalTerrainAssignments.Count; i++)
            {
                var a = m_FinalTerrainAssignments[i];
                if (a.terrain == null) continue;
                a.terrain.lightmapIndex = a.lightmapIndex;
                a.terrain.lightmapScaleOffset = a.scaleOffset;
                EditorUtility.SetDirty(a.terrain);
            }

#if UNITY_2020_1_OR_NEWER
            string snapshotPath = null;
            if (m_SaveSnapshotAsset)
            {
                try
                {
                    snapshotPath = SaveSnapshotAsset(finalLightmaps);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[GroupedLightmapBaker] Failed to save snapshot asset: " + ex);
                }
            }
#endif

            EditorUtility.ClearProgressBar();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            bool probesBaked = false;
            if (m_BakeLightProbesAtEnd)
            {
                // Enable probe groups to include in the final probe bake.
                // If any manual assignments exist, bake only those; otherwise, bake all probe groups.
                bool useManual = AnyManualProbeAssignments();
                HashSet<int> allowedIds = null;
                if (useManual)
                {
                    allowedIds = new HashSet<int>();
                    for (int gi = 0; gi < m_Groups.Count; gi++)
                    {
                        var gg = m_Groups[gi];
                        if (gg == null || gg.lightProbeGroups == null) continue;
                        for (int pj = 0; pj < gg.lightProbeGroups.Length; pj++)
                        {
                            var pg = gg.lightProbeGroups[pj];
                            if (pg == null) continue;
                            allowedIds.Add(pg.GetInstanceID());
                        }
                    }
                }

                var probeGroups = Resources.FindObjectsOfTypeAll<LightProbeGroup>();
                for (int i = 0; i < probeGroups.Length; i++)
                {
                    var g = probeGroups[i];
                    if (g == null) continue;
                    if (g.gameObject == null) continue;
                    if (g.gameObject.scene != scene) continue;

                    bool enabled = !useManual || (allowedIds != null && allowedIds.Contains(g.GetInstanceID()));
                    g.enabled = enabled;
                    EditorUtility.SetDirty(g);
                }

                EditorUtility.DisplayProgressBar("Grouped Lightmap Baker", "Baking Light Probes Only…", 0.98f);
                try
                {
                    probesBaked = TryBakeLightProbesOnly();
                    if (!probesBaked)
                    {
                        Debug.LogWarning("[GroupedLightmapBaker] This Unity version does not support probe-only baking via public API. Skipping probe-only bake to avoid overwriting merged lightmaps.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[GroupedLightmapBaker] Probe-only bake failed: " + ex);
                }
                EditorUtility.ClearProgressBar();
            }

            bool saved = false;
            if (m_AutoSaveSceneOnFinish)
            {
                // If the scene hasn't been saved to disk yet (no path), SaveScene will fail.
                if (!string.IsNullOrEmpty(scene.path))
                {
                    saved = EditorSceneManager.SaveScene(scene);
                }
                else
                {
                    Debug.LogWarning("[GroupedLightmapBaker] Active scene has no path (unsaved/untitled). Save the scene manually to preserve merged lightmaps.");
                }
            }

            EditorUtility.DisplayDialog(
                "Grouped Lightmap Baker",
                "Done. Combined lightmaps: " + m_CombinedLightmaps.Count + "\n" +
                "Renderer assignments: " + m_FinalRendererAssignments.Count + "\n" +
                "Terrain assignments: " + m_FinalTerrainAssignments.Count + "\n\n" +
                (m_BakeLightProbesAtEnd ? (probesBaked ? "Light Probes: baked once at end (assigned groups)." : "Light Probes: probe-only bake skipped (API not available).") : "Light Probes: last group bake may win.") + "\n" +
                (m_AutoSaveSceneOnFinish ? (saved ? "Scene: saved." : "Scene: NOT saved (save manually).") : "Scene: not auto-saved (save manually).") +
#if UNITY_2020_1_OR_NEWER
                (m_SaveSnapshotAsset ? (string.IsNullOrEmpty(snapshotPath) ? "\nSnapshot: NOT saved." : ("\nSnapshot: saved at " + snapshotPath)) : "\nSnapshot: not saved."),
#else
                "",
#endif
                "OK");
        }
        finally
        {
            m_IsRunning = false;
            m_CurrentGroupIndex = 0;
        }
    }

    private LightmapData[] BuildFinalLightmapsForScene()
    {
        if (!m_StoreLightmapsInSceneFolder)
        {
            return m_CombinedLightmaps.ToArray();
        }

        string folder = GetUnitySceneLightmapFolder();
        if (string.IsNullOrEmpty(folder))
        {
            return m_CombinedLightmaps.ToArray();
        }

        // Now that all group bakes are done, write deterministic Unity-style filenames.
        // These won't be overwritten by subsequent bakes (because there are no more bakes).
        var finalList = new LightmapData[m_CombinedLightmaps.Count];
        for (int i = 0; i < m_CombinedLightmaps.Count; i++)
        {
            var src = m_CombinedLightmaps[i];
            if (src == null)
            {
                finalList[i] = null;
                continue;
            }

            var dst = new LightmapData();
            dst.lightmapColor = CopyTextureAsset(src.lightmapColor, folder, "Lightmap-" + i + "_comp_light", overwriteExisting: true);
            dst.lightmapDir = CopyTextureAsset(src.lightmapDir, folder, "Lightmap-" + i + "_comp_dir", overwriteExisting: true);
            dst.shadowMask = CopyTextureAsset(src.shadowMask, folder, "Lightmap-" + i + "_comp_shadowmask", overwriteExisting: true);
            finalList[i] = dst;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return finalList;
    }

    private void SaveExpectedLightmapInfo(LightmapData[] finalLightmaps)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) return;

        string guid = AssetDatabase.AssetPathToGUID(scene.path);
        if (string.IsNullOrEmpty(guid)) return;

        string folder = GetUnitySceneLightmapFolder();
        if (string.IsNullOrEmpty(folder)) return;

        int count = (finalLightmaps != null) ? finalLightmaps.Length : 0;

        EditorPrefs.SetString(PrefKeyPrefix + guid + ".ScenePath", scene.path);
        EditorPrefs.SetString(PrefKeyPrefix + guid + ".Folder", folder);
        EditorPrefs.SetInt(PrefKeyPrefix + guid + ".Count", count);
        EditorPrefs.SetInt(PrefKeyPrefix + guid + ".Mode", (int)m_LightmapsMode);
        EditorPrefs.SetString(PrefKeyPrefix + "LastSceneGuid", guid);
    }

#if UNITY_2020_1_OR_NEWER
    private string SaveSnapshotAsset(LightmapData[] finalLightmaps)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) return null;

        string outFolder = GetPerGroupOutputFolder("_Snapshot");
        // Put snapshot next to the generated lightmaps.
        string assetPath = CombineAssetPath(outFolder, "LightmapSnapshot.asset");
        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

        var snapshot = ScriptableObject.CreateInstance<GroupedLightmapSnapshot>();
        snapshot.lightmapsMode = m_LightmapsMode;

        snapshot.lightmaps.Clear();
        if (finalLightmaps == null) finalLightmaps = LightmapSettings.lightmaps;
        if (finalLightmaps == null) finalLightmaps = new LightmapData[0];

        for (int i = 0; i < finalLightmaps.Length; i++)
        {
            var lm = finalLightmaps[i];
            if (lm == null)
            {
                snapshot.lightmaps.Add(default);
                continue;
            }
            snapshot.lightmaps.Add(new GroupedLightmapSnapshot.LightmapEntry
            {
                color = lm.lightmapColor,
                dir = lm.lightmapDir,
                shadowMask = lm.shadowMask
            });
        }

        snapshot.rendererAssignments.Clear();
        for (int i = 0; i < m_FinalRendererAssignments.Count; i++)
        {
            var a = m_FinalRendererAssignments[i];
            if (a.renderer == null) continue;
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(a.renderer);
            snapshot.rendererAssignments.Add(new GroupedLightmapSnapshot.ObjectAssignment
            {
                globalObjectId = gid.ToString(),
                lightmapIndex = a.lightmapIndex,
                scaleOffset = a.scaleOffset
            });
        }

        snapshot.terrainAssignments.Clear();
        for (int i = 0; i < m_FinalTerrainAssignments.Count; i++)
        {
            var a = m_FinalTerrainAssignments[i];
            if (a.terrain == null) continue;
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(a.terrain);
            snapshot.terrainAssignments.Add(new GroupedLightmapSnapshot.ObjectAssignment
            {
                globalObjectId = gid.ToString(),
                lightmapIndex = a.lightmapIndex,
                scaleOffset = a.scaleOffset
            });
        }

        AssetDatabase.CreateAsset(snapshot, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return assetPath;
    }
#endif

    private void ApplyGroupRootActiveForBake(int activeGroupIndex)
    {
        if (!m_ForceOnlyActiveGroupRootEnabled) return;
        if (m_Groups == null) return;

        for (int i = 0; i < m_Groups.Count; i++)
        {
            var g = m_Groups[i];
            if (g == null || g.root == null) continue;
            var go = g.root.gameObject;
            if (go == null) continue;
            bool shouldBeActive = (i == activeGroupIndex);
            if (go.activeSelf != shouldBeActive)
            {
                go.SetActive(shouldBeActive);
                EditorUtility.SetDirty(go);
            }
        }
    }

    private static Texture2D CopyTextureAsset(Texture2D src, string dstFolder, string baseName, bool overwriteExisting = false)
    {
        if (src == null) return null;

        string srcPath = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(srcPath))
        {
            // Most lightmaps are imported assets; if Unity gives us a non-asset texture, we can't reliably duplicate it.
            Debug.LogWarning("[GroupedLightmapBaker] Texture has no asset path, skipping copy: " + src.name);
            return src;
        }

        string ext = System.IO.Path.GetExtension(srcPath);
        if (string.IsNullOrEmpty(ext)) ext = ".exr";

        string dstPath = CombineAssetPath(dstFolder, baseName + ext);

        // If the source is already at the destination path, do not try to overwrite it.
        // (Overwriting would delete the file and then fail the copy.)
        if (string.Equals(srcPath, dstPath, StringComparison.OrdinalIgnoreCase))
        {
            return src;
        }

        if (!overwriteExisting)
        {
            dstPath = AssetDatabase.GenerateUniqueAssetPath(dstPath);
            bool ok = AssetDatabase.CopyAsset(srcPath, dstPath);
            if (!ok)
            {
                Debug.LogWarning("[GroupedLightmapBaker] Failed to copy lightmap texture: " + srcPath + " -> " + dstPath);
                return src;
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dstPath);
        }

        // Keep a deterministic path/name. IMPORTANT: do NOT delete+recreate the destination asset.
        // Deleting will change the GUID and any LightingDataAsset references will become <none>.
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(dstPath);
        if (existing != null)
        {
            try
            {
                string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                string srcAbs = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, srcPath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                string dstAbs = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, dstPath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

                if (System.IO.File.Exists(srcAbs))
                {
                    System.IO.File.Copy(srcAbs, dstAbs, true);
                    AssetDatabase.ImportAsset(dstPath, ImportAssetOptions.ForceUpdate);
                    return existing;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GroupedLightmapBaker] Failed to overwrite lightmap file in-place: " + srcPath + " -> " + dstPath + "\n" + ex);
            }

            // If in-place overwrite failed, fall back to leaving the existing asset as-is.
            return existing;
        }

        // Destination doesn't exist yet: create it normally.
        {
            bool ok = AssetDatabase.CopyAsset(srcPath, dstPath);
            if (!ok)
            {
                Debug.LogWarning("[GroupedLightmapBaker] Failed to copy lightmap texture: " + srcPath + " -> " + dstPath);
                return src;
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dstPath);
        }
    }

    private static void EnsureFolderExists(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;

        string parent = System.IO.Path.GetDirectoryName(assetFolder);
        if (string.IsNullOrEmpty(parent)) parent = "Assets";
        parent = parent.Replace('\\', '/');

        string leaf = System.IO.Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(leaf)) return;

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolderExists(parent);
        }

        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static string CombineAssetPath(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return (a.TrimEnd('/') + "/" + b.TrimStart('/')).Replace('\\', '/');
    }

    private static string SanitizeFolderName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Group";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c.ToString(), "_");
        }
        return s;
    }

    private static bool TryBakeLightProbesOnly()
    {
        // Newer Unity versions deprecate BakeLightProbesOnly (sometimes as an error),
        // and there is no safe public replacement that won't also rebake/overwrite lightmaps.
        // We attempt reflection to keep compatibility with versions where it's still present.
        try
        {
            MethodInfo m = typeof(Lightmapping).GetMethod("BakeLightProbesOnly", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return false;
            m.Invoke(null, null);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
#endif
