#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GroupedLightmapRestorer : EditorWindow
{
    [SerializeField] private GroupedLightmapSnapshot m_Snapshot;

    [SerializeField] private Vector2 m_Scroll;

    [SerializeField] private LightmapsMode m_ManualMode = LightmapsMode.NonDirectional;
    [SerializeField] private List<GroupedLightmapSnapshot.LightmapEntry> m_ManualLightmaps = new List<GroupedLightmapSnapshot.LightmapEntry>();

#if UNITY_2020_1_OR_NEWER
    [NonSerialized] private bool m_RestoreRunning;
    [NonSerialized] private GroupedLightmapSnapshot m_RestoreSnapshot;
    [NonSerialized] private int m_RestorePhase; // 0=renderers, 1=terrains
    [NonSerialized] private int m_RestoreIndex;
    [NonSerialized] private int m_AppliedRenderers;
    [NonSerialized] private int m_AppliedTerrains;
#endif

    [MenuItem("Tools/Lighting/Restore Lightmaps")]
    public static void Open()
    {
        GetWindow<GroupedLightmapRestorer>("Restore Lightmaps");
    }

    private void OnGUI()
    {
        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
        try
        {
            EditorGUILayout.LabelField("Restore existing lightmaps without rebaking.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            DrawSnapshotSection();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Manual", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Manual mode only changes LightmapSettings.lightmaps (+ mode). It does not restore per-renderer indices unless they already match.", MessageType.Info);

            m_ManualMode = (LightmapsMode)EditorGUILayout.EnumPopup("Lightmaps Mode", m_ManualMode);

            int newCount = Mathf.Max(0, EditorGUILayout.IntField("Lightmap Count", m_ManualLightmaps.Count));
            while (m_ManualLightmaps.Count < newCount) m_ManualLightmaps.Add(default);
            while (m_ManualLightmaps.Count > newCount) m_ManualLightmaps.RemoveAt(m_ManualLightmaps.Count - 1);

            for (int i = 0; i < m_ManualLightmaps.Count; i++)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Lightmap [{i}]", EditorStyles.boldLabel);
                var e = m_ManualLightmaps[i];
                e.color = (Texture2D)EditorGUILayout.ObjectField("Color", e.color, typeof(Texture2D), false);
                e.dir = (Texture2D)EditorGUILayout.ObjectField("Dir", e.dir, typeof(Texture2D), false);
                e.shadowMask = (Texture2D)EditorGUILayout.ObjectField("Shadow Mask", e.shadowMask, typeof(Texture2D), false);
                m_ManualLightmaps[i] = e;
                EditorGUILayout.EndVertical();
            }

            using (new EditorGUI.DisabledScope(m_ManualLightmaps.Count == 0))
            {
                if (GUILayout.Button("Apply Manual Lightmaps"))
                {
                    ApplyManual();
                }
            }
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawSnapshotSection()
    {
        EditorGUILayout.LabelField("Snapshot", EditorStyles.boldLabel);
#if UNITY_2020_1_OR_NEWER
        m_Snapshot = (GroupedLightmapSnapshot)EditorGUILayout.ObjectField("Snapshot Asset", m_Snapshot, typeof(GroupedLightmapSnapshot), false);
        using (new EditorGUI.DisabledScope(m_Snapshot == null || m_RestoreRunning))
        {
            if (GUILayout.Button("Apply Snapshot (Lightmaps + Indices)"))
            {
                ApplySnapshot();
            }
        }
        using (new EditorGUI.DisabledScope(!m_RestoreRunning))
        {
            if (GUILayout.Button("Cancel Snapshot Restore"))
            {
                CancelRestore();
            }
        }
#else
        EditorGUILayout.HelpBox("Snapshot restore requires Unity 2020.1+ (GlobalObjectId).", MessageType.Warning);
#endif
    }

    private static void ForceOnDemandGIWorkflow()
    {
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
    }

    private void ApplyManual()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Restore Lightmaps", "Exit Play Mode before restoring.", "OK");
            return;
        }

        ForceOnDemandGIWorkflow();

        var list = new LightmapData[m_ManualLightmaps.Count];
        for (int i = 0; i < m_ManualLightmaps.Count; i++)
        {
            var e = m_ManualLightmaps[i];
            list[i] = new LightmapData
            {
                lightmapColor = e.color,
                lightmapDir = e.dir,
                shadowMask = e.shadowMask
            };
        }

        LightmapSettings.lightmapsMode = m_ManualMode;
        LightmapSettings.lightmaps = list;

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);

        EditorUtility.DisplayDialog("Restore Lightmaps", $"Applied {list.Length} lightmaps to LightmapSettings.", "OK");
    }

#if UNITY_2020_1_OR_NEWER
    private void ApplySnapshot()
    {
        if (m_Snapshot == null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Restore Lightmaps", "Exit Play Mode before restoring.", "OK");
            return;
        }

        if (m_RestoreRunning)
        {
            EditorUtility.DisplayDialog("Restore Lightmaps", "A restore is already running.", "OK");
            return;
        }

        // Defer actual work to after the button click event finishes to avoid UI stalls.
        var snapshot = m_Snapshot;
        EditorApplication.delayCall += () => StartRestore(snapshot);
    }

    private void StartRestore(GroupedLightmapSnapshot snapshot)
    {
        if (snapshot == null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (m_RestoreRunning) return;

        ForceOnDemandGIWorkflow();

        var list = new LightmapData[snapshot.lightmaps.Count];
        for (int i = 0; i < snapshot.lightmaps.Count; i++)
        {
            var e = snapshot.lightmaps[i];
            list[i] = new LightmapData
            {
                lightmapColor = e.color,
                lightmapDir = e.dir,
                shadowMask = e.shadowMask
            };
        }

        LightmapSettings.lightmapsMode = snapshot.lightmapsMode;
        LightmapSettings.lightmaps = list;

        m_RestoreSnapshot = snapshot;
        m_RestoreRunning = true;
        m_RestorePhase = 0;
        m_RestoreIndex = 0;
        m_AppliedRenderers = 0;
        m_AppliedTerrains = 0;

        EditorApplication.update -= RestoreUpdate;
        EditorApplication.update += RestoreUpdate;
        Repaint();
    }

    private void CancelRestore()
    {
        if (!m_RestoreRunning) return;
        StopRestore(showDialog: false);
        EditorUtility.DisplayDialog("Restore Lightmaps", "Snapshot restore cancelled.", "OK");
    }

    private void RestoreUpdate()
    {
        if (!m_RestoreRunning || m_RestoreSnapshot == null)
        {
            StopRestore(showDialog: false);
            return;
        }

        int totalRenderers = m_RestoreSnapshot.rendererAssignments != null ? m_RestoreSnapshot.rendererAssignments.Count : 0;
        int totalTerrains = m_RestoreSnapshot.terrainAssignments != null ? m_RestoreSnapshot.terrainAssignments.Count : 0;
        int total = totalRenderers + totalTerrains;
        int done = (m_RestorePhase == 0) ? m_RestoreIndex : (totalRenderers + m_RestoreIndex);
        float progress = (total > 0) ? Mathf.Clamp01((float)done / total) : 1f;

        string phaseLabel = (m_RestorePhase == 0) ? "Renderers" : "Terrains";
        bool cancel = EditorUtility.DisplayCancelableProgressBar(
            "Restore Lightmaps",
            $"Restoring {phaseLabel}… {done}/{total}",
            progress);

        if (cancel)
        {
            StopRestore(showDialog: false);
            EditorUtility.DisplayDialog("Restore Lightmaps", "Snapshot restore cancelled.", "OK");
            return;
        }

        const int batchSize = 200;
        int processedThisFrame = 0;
        while (processedThisFrame < batchSize)
        {
            if (m_RestorePhase == 0)
            {
                if (m_RestoreIndex >= totalRenderers)
                {
                    m_RestorePhase = 1;
                    m_RestoreIndex = 0;
                    continue;
                }

                var a = m_RestoreSnapshot.rendererAssignments[m_RestoreIndex++];
                if (TryGetObjectFromGlobalId(a.globalObjectId, out var obj))
                {
                    var r = obj as Renderer;
                    if (r != null)
                    {
                        r.lightmapIndex = a.lightmapIndex;
                        r.lightmapScaleOffset = a.scaleOffset;
                        EditorUtility.SetDirty(r);
                        m_AppliedRenderers++;
                    }
                }
            }
            else
            {
                if (m_RestoreIndex >= totalTerrains)
                {
                    StopRestore(showDialog: true);
                    return;
                }

                var a = m_RestoreSnapshot.terrainAssignments[m_RestoreIndex++];
                if (TryGetObjectFromGlobalId(a.globalObjectId, out var obj))
                {
                    var t = obj as Terrain;
                    if (t != null)
                    {
                        t.lightmapIndex = a.lightmapIndex;
                        t.lightmapScaleOffset = a.scaleOffset;
                        EditorUtility.SetDirty(t);
                        m_AppliedTerrains++;
                    }
                }
            }

            processedThisFrame++;
        }
    }

    private void StopRestore(bool showDialog)
    {
        EditorApplication.update -= RestoreUpdate;
        m_RestoreRunning = false;
        var snapshot = m_RestoreSnapshot;
        m_RestoreSnapshot = null;
        EditorUtility.ClearProgressBar();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);

        if (showDialog)
        {
            int lmCount = (LightmapSettings.lightmaps != null) ? LightmapSettings.lightmaps.Length : 0;
            EditorUtility.DisplayDialog(
                "Restore Lightmaps",
                $"Applied snapshot. Lightmaps: {lmCount}\nRenderers restored: {m_AppliedRenderers}\nTerrains restored: {m_AppliedTerrains}",
                "OK");
        }

        Repaint();
    }

    private static bool TryGetObjectFromGlobalId(string globalIdString, out UnityEngine.Object obj)
    {
        obj = null;
        if (string.IsNullOrEmpty(globalIdString)) return false;

        if (!GlobalObjectId.TryParse(globalIdString, out var gid)) return false;
        obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
        return obj != null;
    }
#endif
}
#endif
