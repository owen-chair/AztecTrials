#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class OcclusionBoundsBakerTool : EditorWindow
{
    private const float DEFAULT_AREA_DISABLE_DISTANCE = 500f;

    private OcclusionManager _target;

    private Vector2 _scrollPos;

    [SerializeField]
    private bool _areaLinksFoldout = true;

    [SerializeField]
    private int _linkAreaIndex = 0;

    [SerializeField]
    private int _linkAddDisplayTargetIndex = 0;

    [SerializeField]
    private int _linkAddDisableTargetIndex = 0;

    [SerializeField]
    private bool _manualExclusionsFoldout = true;

    // Entries can be: Renderer, GameObject, Transform, or any Component.
    // If a GameObject/Transform/Component is provided, all child renderers are excluded.
    [SerializeField]
    private List<UnityEngine.Object> _manualExclusions = new List<UnityEngine.Object>();

    private bool _previewInScene = true;
    private bool _previewUseLiveComputed = false;
    private bool _previewShowLabels = true;

    [SerializeField]
    private int _previewHighlightAreaIndex = -1;

    private bool _diagnosticsFoldout = true;
    private int _diagnosticsAreaIndex = 0;
    private bool _diagnosticsIncludeIgnoredRenderers = false;
    private bool _diagnosticsShowAxisMarkersInScene = true;
    private bool _diagnosticsShowAxisMarkerLabelsInScene = true;

    private bool _diagnosticsAxisFoldoutX = true;
    private bool _diagnosticsAxisFoldoutY = true;
    private bool _diagnosticsAxisFoldoutZ = true;

    private bool _diagnosticsLocalBoundsFoldout = true;
    private bool _diagnosticsShowLocalOBBInScene = true;
    private bool _diagnosticsShowLocalOBBLabelInScene = true;

    private enum OutlierLabelAxis
    {
        None,
        X,
        Y,
        Z,
        All,
    }

    private OutlierLabelAxis _diagnosticsOutlierLabelAxis = OutlierLabelAxis.None;
    private int _diagnosticsOutlierLabelCount = 0;

    private Bounds _diagnosticsCombinedBounds;
    private bool _diagnosticsHasCombinedBounds;

    private Bounds _diagnosticsLocalBounds;
    private bool _diagnosticsHasLocalBounds;

    private Renderer _diagnosticsMinZRenderer;
    private Renderer _diagnosticsMaxZRenderer;
    private float _diagnosticsMinZ;
    private float _diagnosticsMaxZ;

    private Renderer _diagnosticsMinXRenderer;
    private Renderer _diagnosticsMaxXRenderer;
    private float _diagnosticsMinX;
    private float _diagnosticsMaxX;

    private Renderer _diagnosticsMinYRenderer;
    private Renderer _diagnosticsMaxYRenderer;
    private float _diagnosticsMinY;
    private float _diagnosticsMaxY;

    private readonly List<RendererBoundInfo> _diagnosticsMinZOutliers = new List<RendererBoundInfo>();
    private readonly List<RendererBoundInfo> _diagnosticsMaxZOutliers = new List<RendererBoundInfo>();

    private readonly List<RendererBoundInfo> _diagnosticsMinXOutliers = new List<RendererBoundInfo>();
    private readonly List<RendererBoundInfo> _diagnosticsMaxXOutliers = new List<RendererBoundInfo>();

    private readonly List<RendererBoundInfo> _diagnosticsMinYOutliers = new List<RendererBoundInfo>();
    private readonly List<RendererBoundInfo> _diagnosticsMaxYOutliers = new List<RendererBoundInfo>();

    private struct RendererBoundInfo
    {
        public Renderer Renderer;
        public string Path;
        public Bounds Bounds;
    }

    private static bool IsFinite(float v)
    {
        return !(float.IsNaN(v) || float.IsInfinity(v));
    }

    private static bool IsFinite(Vector3 v)
    {
        return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }

    private static bool ShouldIgnoreRenderer(Renderer rr)
    {
        if (rr == null) return true;

        // These commonly have exaggerated/unstable bounds and can blow up an area's AABB.
        if (rr is LineRenderer || rr is TrailRenderer || rr is ParticleSystemRenderer) return true;

        // UI renderers (world-space canvas, TMP, etc.) often shouldn't count toward room bounds
        // and can occasionally produce huge bounds.
        // CanvasRenderer isn't a Renderer, but TMP 3D can be; this catches those cases.
        Transform t = rr.transform;
        if (t != null)
        {
            if (t.GetComponentInParent<Canvas>(true) != null) return true;
            if (t.GetComponentInParent<RectTransform>(true) != null) return true;
        }

        return false;
    }

    private bool IsManuallyExcluded(Renderer rr)
    {
        if (rr == null) return false;
        if (_manualExclusions == null || _manualExclusions.Count == 0) return false;

        Transform rrTransform = rr.transform;

        for (int i = 0; i < _manualExclusions.Count; i++)
        {
            UnityEngine.Object obj = _manualExclusions[i];
            if (obj == null) continue;

            if (obj is Renderer exRenderer)
            {
                if (rr == exRenderer) return true;
                continue;
            }

            Transform exTransform = null;
            if (obj is GameObject go) exTransform = go.transform;
            else if (obj is Transform t) exTransform = t;
            else if (obj is Component c) exTransform = c.transform;

            if (exTransform == null) continue;

            if (rrTransform == exTransform) return true;
            try
            {
                if (rrTransform.IsChildOf(exTransform)) return true;
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    [MenuItem("Tools/Occlusion/Bake Bounds (OcclusionManager)")]
    public static void Open()
    {
        GetWindow<OcclusionBoundsBakerTool>("Occlusion Bounds Baker");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        try
        {
            EditorGUILayout.LabelField("Bakes world-space AABB bounds per area.", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _target = (OcclusionManager)EditorGUILayout.ObjectField("OcclusionManager", _target, typeof(OcclusionManager), true);

            if (_target != null)
            {
                DrawAreaLinksUI();
            }

            DrawManualExclusionsUI();

            EditorGUI.BeginChangeCheck();
            _previewInScene = EditorGUILayout.ToggleLeft("Preview Bounds In Scene View", _previewInScene);
            using (new EditorGUI.DisabledScope(!_previewInScene))
            {
                _previewUseLiveComputed = EditorGUILayout.ToggleLeft("Preview Live (From Renderers)", _previewUseLiveComputed);
                _previewShowLabels = EditorGUILayout.ToggleLeft("Show Labels", _previewShowLabels);
                DrawPreviewHighlightUI();
            }
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            if (_target != null)
            {
                EditorGUILayout.Space();
                DrawDiagnosticsUI();
            }

            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (GUILayout.Button("Bake Bounds"))
                {
                    Bake(_target);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This tool scans Renderers under each Area Root in the editor and writes baked bounds into the OcclusionManager arrays. Runtime does NOT compute bounds.",
                MessageType.Info);
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawPreviewHighlightUI()
    {
        GameObject[] roots = _target != null ? _target.m_AreaRoots : null;
        int count = roots != null ? roots.Length : 0;

        using (new EditorGUI.DisabledScope(count <= 0))
        {
            int suggested = TryGetAreaIndexFromSelection(roots);
            if (suggested >= 0 && suggested < count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Highlight", GUILayout.Width(60));
                    if (GUILayout.Button($"Use Selected ({suggested}: {roots[suggested]?.name})", GUILayout.MaxWidth(280)))
                    {
                        _previewHighlightAreaIndex = suggested;
                        SceneView.RepaintAll();
                    }
                }
            }

            _previewHighlightAreaIndex = Mathf.Clamp(_previewHighlightAreaIndex, -1, count - 1);
            string[] highlightOptions = BuildAreaOptionsWithNone(roots);
            int popupIndex = _previewHighlightAreaIndex + 1;
            int newPopupIndex = EditorGUILayout.Popup("Red Highlight Area", popupIndex, highlightOptions);
            if (newPopupIndex != popupIndex)
            {
                _previewHighlightAreaIndex = newPopupIndex - 1;
                SceneView.RepaintAll();
            }
        }
    }

    private void DrawAreaLinksUI()
    {
        _areaLinksFoldout = EditorGUILayout.Foldout(_areaLinksFoldout, "Area Links (No Re-Bake Required)", true);
        if (!_areaLinksFoldout) return;

        GameObject[] roots = _target != null ? _target.m_AreaRoots : null;
        int count = roots != null ? roots.Length : 0;
        if (count <= 0)
        {
            EditorGUILayout.HelpBox("Assign Area Roots on the OcclusionManager before creating links.", MessageType.Warning);
            return;
        }

        string[] areaOptions = BuildAreaOptions(roots);
        int suggested = TryGetAreaIndexFromSelection(roots);
        if (suggested >= 0 && suggested < count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Source", GUILayout.Width(50));
                if (GUILayout.Button($"Use Selected ({suggested}: {roots[suggested]?.name})", GUILayout.MaxWidth(280)))
                {
                    _linkAreaIndex = suggested;
                }
            }
        }

        _linkAreaIndex = Mathf.Clamp(_linkAreaIndex, 0, count - 1);
        _linkAreaIndex = EditorGUILayout.Popup("Source Area", _linkAreaIndex, areaOptions);

        EditorGUILayout.HelpBox(
            "Only one source area applies links: the smallest area containing the player, or the nearest area if the player is outside all areas. The source must also be active by its baked bounds and disable distance. Display links force targets on; Disable links force targets off and win conflicts. Editing these links only changes the OcclusionManager; it does not re-bake bounds.",
            MessageType.None);

        _linkAddDisplayTargetIndex = NormalizeAddLinkTarget(_target, true, _linkAreaIndex, _linkAddDisplayTargetIndex, count);
        _linkAddDisableTargetIndex = NormalizeAddLinkTarget(_target, false, _linkAreaIndex, _linkAddDisableTargetIndex, count);

        DrawAreaLinkList("Always Display Linked Areas", true, roots, areaOptions, _linkAreaIndex, ref _linkAddDisplayTargetIndex);
        DrawAreaLinkList("Always Disable Linked Areas", false, roots, areaOptions, _linkAreaIndex, ref _linkAddDisableTargetIndex);

        if (HasInvalidAreaLinks(_target, count))
        {
            EditorGUILayout.HelpBox("Some saved links point at missing/out-of-range areas or duplicate another link.", MessageType.Warning);
            if (GUILayout.Button("Clean Invalid / Duplicate Links"))
            {
                CleanInvalidAreaLinks(_target, count);
            }
        }
    }

    private void DrawAreaLinkList(string title, bool displayLinks, GameObject[] roots, string[] areaOptions, int sourceIndex, ref int addTargetIndex)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);

        int[] sources = GetLinkSources(_target, displayLinks);
        int[] targets = GetLinkTargets(_target, displayLinks);
        int linkCount = GetSafeLinkCount(sources, targets);
        int shown = 0;

        for (int linkIndex = 0; linkIndex < linkCount; linkIndex++)
        {
            if (sources[linkIndex] != sourceIndex)
                continue;

            int targetIndex = targets[linkIndex];
            shown++;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{shown}.", GUILayout.Width(22));

                int popupIndex = Mathf.Clamp(targetIndex, 0, areaOptions.Length - 1);
                int newTargetIndex = EditorGUILayout.Popup(popupIndex, areaOptions);
                if (newTargetIndex != targetIndex)
                {
                    if (newTargetIndex == sourceIndex)
                    {
                        ShowNotification(new GUIContent("Links must target another area."));
                    }
                    else if (HasAreaLink(_target, displayLinks, sourceIndex, newTargetIndex, linkIndex))
                    {
                        ShowNotification(new GUIContent("That link already exists."));
                    }
                    else
                    {
                        SetAreaLinkTarget(_target, displayLinks, linkIndex, newTargetIndex);
                    }
                }

                using (new EditorGUI.DisabledScope(targetIndex < 0 || targetIndex >= roots.Length || roots[targetIndex] == null))
                {
                    if (GUILayout.Button("Ping", GUILayout.Width(45)))
                    {
                        EditorGUIUtility.PingObject(roots[targetIndex]);
                        Selection.activeObject = roots[targetIndex];
                    }
                }

                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    RemoveAreaLink(_target, displayLinks, linkIndex);
                    GUIUtility.ExitGUI();
                }
            }
        }

        if (shown == 0)
        {
            EditorGUILayout.LabelField("No links from this source area.", EditorStyles.miniLabel);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Add", GUILayout.Width(32));
            addTargetIndex = EditorGUILayout.Popup(addTargetIndex, areaOptions);

            bool canAdd = addTargetIndex >= 0 && addTargetIndex < roots.Length &&
                          addTargetIndex != sourceIndex &&
                          !HasAreaLink(_target, displayLinks, sourceIndex, addTargetIndex, -1);
            using (new EditorGUI.DisabledScope(!canAdd))
            {
                if (GUILayout.Button("Add", GUILayout.Width(60)))
                {
                    AddAreaLink(_target, displayLinks, sourceIndex, addTargetIndex);
                    addTargetIndex = NormalizeAddLinkTarget(_target, displayLinks, sourceIndex, addTargetIndex, roots.Length);
                }
            }
        }
    }

    private static string[] BuildAreaOptions(GameObject[] roots)
    {
        int count = roots != null ? roots.Length : 0;
        string[] options = new string[count];
        for (int i = 0; i < count; i++)
        {
            string name = roots[i] != null ? roots[i].name : "(null)";
            options[i] = $"{i}: {name}";
        }

        return options;
    }

    private static string[] BuildAreaOptionsWithNone(GameObject[] roots)
    {
        string[] areaOptions = BuildAreaOptions(roots);
        string[] options = new string[areaOptions.Length + 1];
        options[0] = "None";
        for (int i = 0; i < areaOptions.Length; i++)
        {
            options[i + 1] = areaOptions[i];
        }

        return options;
    }

    private static float[] BuildAreaDisableDistancesForBake(GameObject[] roots, float[] currentDistances)
    {
        int count = roots != null ? roots.Length : 0;
        float[] distances = new float[count];
        for (int i = 0; i < count; i++)
        {
            distances[i] = DEFAULT_AREA_DISABLE_DISTANCE;
        }

        if (currentDistances == null) return distances;

        int copyCount = Mathf.Min(count, currentDistances.Length);
        for (int i = 0; i < copyCount; i++)
        {
            distances[i] = currentDistances[i];
        }

        return distances;
    }

    private static int NormalizeAddLinkTarget(OcclusionManager mgr, bool displayLinks, int sourceIndex, int targetIndex, int areaCount)
    {
        if (areaCount <= 1) return Mathf.Clamp(targetIndex, 0, Mathf.Max(0, areaCount - 1));

        if (targetIndex >= 0 && targetIndex < areaCount &&
            targetIndex != sourceIndex &&
            !HasAreaLink(mgr, displayLinks, sourceIndex, targetIndex, -1))
        {
            return targetIndex;
        }

        for (int i = 0; i < areaCount; i++)
        {
            if (i == sourceIndex) continue;
            if (HasAreaLink(mgr, displayLinks, sourceIndex, i, -1)) continue;
            return i;
        }

        return Mathf.Clamp(targetIndex, 0, areaCount - 1);
    }

    private static int GetSafeLinkCount(int[] sources, int[] targets)
    {
        if (sources == null || targets == null) return 0;
        return Mathf.Min(sources.Length, targets.Length);
    }

    private static int[] GetLinkSources(OcclusionManager mgr, bool displayLinks)
    {
        if (mgr == null) return null;
        return displayLinks ? mgr.m_AlwaysDisplayLinkSourceAreaIndices : mgr.m_AlwaysDisableLinkSourceAreaIndices;
    }

    private static int[] GetLinkTargets(OcclusionManager mgr, bool displayLinks)
    {
        if (mgr == null) return null;
        return displayLinks ? mgr.m_AlwaysDisplayLinkTargetAreaIndices : mgr.m_AlwaysDisableLinkTargetAreaIndices;
    }

    private static void SetLinkArrays(OcclusionManager mgr, bool displayLinks, int[] sources, int[] targets)
    {
        if (displayLinks)
        {
            mgr.m_AlwaysDisplayLinkSourceAreaIndices = sources;
            mgr.m_AlwaysDisplayLinkTargetAreaIndices = targets;
        }
        else
        {
            mgr.m_AlwaysDisableLinkSourceAreaIndices = sources;
            mgr.m_AlwaysDisableLinkTargetAreaIndices = targets;
        }
    }

    private static bool HasAreaLink(OcclusionManager mgr, bool displayLinks, int sourceIndex, int targetIndex, int ignoreLinkIndex)
    {
        int[] sources = GetLinkSources(mgr, displayLinks);
        int[] targets = GetLinkTargets(mgr, displayLinks);
        int count = GetSafeLinkCount(sources, targets);

        for (int i = 0; i < count; i++)
        {
            if (i == ignoreLinkIndex) continue;
            if (sources[i] == sourceIndex && targets[i] == targetIndex) return true;
        }

        return false;
    }

    private static void AddAreaLink(OcclusionManager mgr, bool displayLinks, int sourceIndex, int targetIndex)
    {
        if (mgr == null) return;
        if (sourceIndex == targetIndex) return;
        if (HasAreaLink(mgr, displayLinks, sourceIndex, targetIndex, -1)) return;

        int[] sources = GetLinkSources(mgr, displayLinks);
        int[] targets = GetLinkTargets(mgr, displayLinks);
        int count = GetSafeLinkCount(sources, targets);

        int[] newSources = new int[count + 1];
        int[] newTargets = new int[count + 1];
        for (int i = 0; i < count; i++)
        {
            newSources[i] = sources[i];
            newTargets[i] = targets[i];
        }

        newSources[count] = sourceIndex;
        newTargets[count] = targetIndex;

        Undo.RecordObject(mgr, "Edit Occlusion Area Links");
        SetLinkArrays(mgr, displayLinks, newSources, newTargets);
        MarkOcclusionManagerDirty(mgr);
    }

    private static void RemoveAreaLink(OcclusionManager mgr, bool displayLinks, int linkIndex)
    {
        if (mgr == null) return;

        int[] sources = GetLinkSources(mgr, displayLinks);
        int[] targets = GetLinkTargets(mgr, displayLinks);
        int count = GetSafeLinkCount(sources, targets);
        if (linkIndex < 0 || linkIndex >= count) return;

        int[] newSources = new int[count - 1];
        int[] newTargets = new int[count - 1];
        int write = 0;
        for (int i = 0; i < count; i++)
        {
            if (i == linkIndex) continue;
            newSources[write] = sources[i];
            newTargets[write] = targets[i];
            write++;
        }

        Undo.RecordObject(mgr, "Edit Occlusion Area Links");
        SetLinkArrays(mgr, displayLinks, newSources, newTargets);
        MarkOcclusionManagerDirty(mgr);
    }

    private static void SetAreaLinkTarget(OcclusionManager mgr, bool displayLinks, int linkIndex, int newTargetIndex)
    {
        if (mgr == null) return;

        int[] sources = GetLinkSources(mgr, displayLinks);
        int[] targets = GetLinkTargets(mgr, displayLinks);
        int count = GetSafeLinkCount(sources, targets);
        if (linkIndex < 0 || linkIndex >= count) return;

        int[] newSources = new int[count];
        int[] newTargets = new int[count];
        for (int i = 0; i < count; i++)
        {
            newSources[i] = sources[i];
            newTargets[i] = targets[i];
        }

        newTargets[linkIndex] = newTargetIndex;

        Undo.RecordObject(mgr, "Edit Occlusion Area Links");
        SetLinkArrays(mgr, displayLinks, newSources, newTargets);
        MarkOcclusionManagerDirty(mgr);
    }

    private static bool HasInvalidAreaLinks(OcclusionManager mgr, int areaCount)
    {
        return HasInvalidAreaLinks(mgr, true, areaCount) || HasInvalidAreaLinks(mgr, false, areaCount);
    }

    private static bool HasInvalidAreaLinks(OcclusionManager mgr, bool displayLinks, int areaCount)
    {
        int[] sources = GetLinkSources(mgr, displayLinks);
        int[] targets = GetLinkTargets(mgr, displayLinks);
        int count = GetSafeLinkCount(sources, targets);

        if ((sources != null && sources.Length != count) || (targets != null && targets.Length != count)) return true;

        for (int i = 0; i < count; i++)
        {
            int source = sources[i];
            int target = targets[i];
            if (source < 0 || source >= areaCount) return true;
            if (target < 0 || target >= areaCount) return true;
            if (source == target) return true;
            if (HasAreaLink(mgr, displayLinks, source, target, i)) return true;
        }

        return false;
    }

    private static void CleanInvalidAreaLinks(OcclusionManager mgr, int areaCount)
    {
        if (mgr == null) return;

        Undo.RecordObject(mgr, "Clean Occlusion Area Links");
        CleanInvalidAreaLinks(mgr, true, areaCount);
        CleanInvalidAreaLinks(mgr, false, areaCount);
        MarkOcclusionManagerDirty(mgr);
    }

    private static void CleanInvalidAreaLinks(OcclusionManager mgr, bool displayLinks, int areaCount)
    {
        int[] sources = GetLinkSources(mgr, displayLinks);
        int[] targets = GetLinkTargets(mgr, displayLinks);
        int count = GetSafeLinkCount(sources, targets);

        var cleanSources = new List<int>(count);
        var cleanTargets = new List<int>(count);

        for (int i = 0; i < count; i++)
        {
            int source = sources[i];
            int target = targets[i];
            if (source < 0 || source >= areaCount) continue;
            if (target < 0 || target >= areaCount) continue;
            if (source == target) continue;

            bool duplicate = false;
            for (int existing = 0; existing < cleanSources.Count; existing++)
            {
                if (cleanSources[existing] == source && cleanTargets[existing] == target)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate) continue;

            cleanSources.Add(source);
            cleanTargets.Add(target);
        }

        SetLinkArrays(mgr, displayLinks, cleanSources.ToArray(), cleanTargets.ToArray());
    }

    private static void MarkOcclusionManagerDirty(OcclusionManager mgr)
    {
        if (mgr == null) return;

        EditorUtility.SetDirty(mgr);
        PrefabUtility.RecordPrefabInstancePropertyModifications(mgr);
        if (mgr.gameObject != null && mgr.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
        }
    }

    private void DrawDiagnosticsUI()
    {
        _diagnosticsFoldout = EditorGUILayout.Foldout(_diagnosticsFoldout, "Diagnostics (Find Bounds Offenders)", true);
        if (!_diagnosticsFoldout) return;

        GameObject[] roots = _target != null ? _target.m_AreaRoots : null;
        int count = (roots != null) ? roots.Length : 0;
        if (count <= 0)
        {
            EditorGUILayout.HelpBox("No Area Roots on the OcclusionManager.", MessageType.Warning);
            return;
        }

        int suggested = TryGetAreaIndexFromSelection(roots);
        if (suggested >= 0 && suggested < count)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Area", GUILayout.Width(40));
                if (GUILayout.Button($"Use Selected ({suggested}: {roots[suggested]?.name})", GUILayout.MaxWidth(260)))
                {
                    _diagnosticsAreaIndex = suggested;
                    AnalyzeArea(_diagnosticsAreaIndex);
                    SceneView.RepaintAll();
                }
            }
        }

        _diagnosticsAreaIndex = Mathf.Clamp(_diagnosticsAreaIndex, 0, count - 1);
        string[] options = new string[count];
        for (int i = 0; i < count; i++)
        {
            string nm = roots[i] != null ? roots[i].name : "(null)";
            options[i] = $"{i}: {nm}";
        }
        int newIdx = EditorGUILayout.Popup("Area Index", _diagnosticsAreaIndex, options);
        if (newIdx != _diagnosticsAreaIndex)
        {
            _diagnosticsAreaIndex = newIdx;
            _diagnosticsHasCombinedBounds = false;
            _diagnosticsMinZOutliers.Clear();
            _diagnosticsMaxZOutliers.Clear();
            SceneView.RepaintAll();
        }

        _diagnosticsIncludeIgnoredRenderers = EditorGUILayout.ToggleLeft("Include Ignored Renderers (particles/line/trail/UI)", _diagnosticsIncludeIgnoredRenderers);
        _diagnosticsShowAxisMarkersInScene = EditorGUILayout.ToggleLeft("Show Axis Markers In Scene", _diagnosticsShowAxisMarkersInScene);
        using (new EditorGUI.DisabledScope(!_diagnosticsShowAxisMarkersInScene))
        {
            _diagnosticsShowAxisMarkerLabelsInScene = EditorGUILayout.ToggleLeft("Show Marker Labels", _diagnosticsShowAxisMarkerLabelsInScene);
            _diagnosticsOutlierLabelAxis = (OutlierLabelAxis)EditorGUILayout.EnumPopup("Label Top Outliers", _diagnosticsOutlierLabelAxis);
            using (new EditorGUI.DisabledScope(_diagnosticsOutlierLabelAxis == OutlierLabelAxis.None))
            {
                _diagnosticsOutlierLabelCount = EditorGUILayout.IntSlider("Outlier Label Count", _diagnosticsOutlierLabelCount, 0, 10);
            }
        }

        _diagnosticsLocalBoundsFoldout = EditorGUILayout.Foldout(_diagnosticsLocalBoundsFoldout, "Local-Space Bounds (Oriented Box)", true);
        if (_diagnosticsLocalBoundsFoldout)
        {
            _diagnosticsShowLocalOBBInScene = EditorGUILayout.ToggleLeft("Show Local OBB In Scene", _diagnosticsShowLocalOBBInScene);
            using (new EditorGUI.DisabledScope(!_diagnosticsShowLocalOBBInScene))
            {
                _diagnosticsShowLocalOBBLabelInScene = EditorGUILayout.ToggleLeft("Show OBB Label", _diagnosticsShowLocalOBBLabelInScene);
            }
            EditorGUILayout.HelpBox(
                "World-space bounds are axis-aligned (AABB). If your hallway/room is diagonal or the Area Root is rotated, the world AABB can look 'stretched' in Z even when all renderers are on the path. Local-space bounds show the same content measured in the Area Root's coordinate system.",
                MessageType.None);
        }

        using (new EditorGUI.DisabledScope(_target == null || roots[_diagnosticsAreaIndex] == null))
        {
            if (GUILayout.Button("Analyze Area Bounds (Live From Renderers)"))
            {
                AnalyzeArea(_diagnosticsAreaIndex);
                SceneView.RepaintAll();
            }
        }

        if (_diagnosticsHasCombinedBounds)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Combined Bounds", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Center", _diagnosticsCombinedBounds.center.ToString("F3"));
            EditorGUILayout.LabelField("Size", _diagnosticsCombinedBounds.size.ToString("F3"));

            if (_diagnosticsHasLocalBounds)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Local-Space Bounds (Root)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Local Center", _diagnosticsLocalBounds.center.ToString("F3"));
                EditorGUILayout.LabelField("Local Size", _diagnosticsLocalBounds.size.ToString("F3"));
            }

            _diagnosticsAxisFoldoutX = EditorGUILayout.Foldout(_diagnosticsAxisFoldoutX, "X Extremes / Outliers", true);
            if (_diagnosticsAxisFoldoutX)
            {
                EditorGUILayout.LabelField("MinX / MaxX", $"{_diagnosticsMinX:F3} / {_diagnosticsMaxX:F3}");
                DrawRendererRow("MinX", _diagnosticsMinXRenderer);
                DrawRendererRow("MaxX", _diagnosticsMaxXRenderer);
                DrawOutlierList("Smallest min.x", _diagnosticsMinXOutliers, 10);
                DrawOutlierList("Largest max.x", _diagnosticsMaxXOutliers, 10);
            }

            _diagnosticsAxisFoldoutY = EditorGUILayout.Foldout(_diagnosticsAxisFoldoutY, "Y Extremes / Outliers", true);
            if (_diagnosticsAxisFoldoutY)
            {
                EditorGUILayout.LabelField("MinY / MaxY", $"{_diagnosticsMinY:F3} / {_diagnosticsMaxY:F3}");
                DrawRendererRow("MinY", _diagnosticsMinYRenderer);
                DrawRendererRow("MaxY", _diagnosticsMaxYRenderer);
                DrawOutlierList("Smallest min.y", _diagnosticsMinYOutliers, 10);
                DrawOutlierList("Largest max.y", _diagnosticsMaxYOutliers, 10);
            }

            _diagnosticsAxisFoldoutZ = EditorGUILayout.Foldout(_diagnosticsAxisFoldoutZ, "Z Extremes / Outliers", true);
            if (_diagnosticsAxisFoldoutZ)
            {
                EditorGUILayout.LabelField("MinZ / MaxZ", $"{_diagnosticsMinZ:F3} / {_diagnosticsMaxZ:F3}");
                DrawRendererRow("MinZ", _diagnosticsMinZRenderer);
                DrawRendererRow("MaxZ", _diagnosticsMaxZRenderer);
                DrawOutlierList("Smallest min.z", _diagnosticsMinZOutliers, 10);
                DrawOutlierList("Largest max.z", _diagnosticsMaxZOutliers, 10);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Click 'Analyze Area Bounds' to list which renderer is stretching Z.", MessageType.Info);
        }
    }

    private void DrawManualExclusionsUI()
    {
        _manualExclusionsFoldout = EditorGUILayout.Foldout(_manualExclusionsFoldout, "Manual Exclusions", true);
        if (!_manualExclusionsFoldout) return;

        EditorGUILayout.HelpBox(
            "Drop Renderers or parent GameObjects here to exclude them from Bake / Live Preview / Diagnostics. Useful for meshes with inflated bounds.",
            MessageType.None);

        if (_manualExclusions == null) _manualExclusions = new List<UnityEngine.Object>();

        bool changed = false;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selected", GUILayout.Width(110)))
            {
                AddSelectedToExclusions();
                changed = true;
            }

            if (GUILayout.Button("Add Empty", GUILayout.Width(110)))
            {
                _manualExclusions.Add(null);
                changed = true;
            }

            using (new EditorGUI.DisabledScope(_manualExclusions.Count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    _manualExclusions.Clear();
                    changed = true;
                }
            }
        }

        int removeAt = -1;
        for (int i = 0; i < _manualExclusions.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                UnityEngine.Object prev = _manualExclusions[i];
                UnityEngine.Object next = EditorGUILayout.ObjectField(prev, typeof(UnityEngine.Object), true);
                if (next != prev)
                {
                    _manualExclusions[i] = next;
                    changed = true;
                }

                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    removeAt = i;
                }
            }
        }

        if (removeAt >= 0 && removeAt < _manualExclusions.Count)
        {
            _manualExclusions.RemoveAt(removeAt);
            changed = true;
        }

        if (changed)
        {
            SceneView.RepaintAll();
        }
    }

    private void AddSelectedToExclusions()
    {
        UnityEngine.Object[] sel = Selection.objects;
        if (sel == null || sel.Length == 0) return;

        if (_manualExclusions == null) _manualExclusions = new List<UnityEngine.Object>();

        for (int i = 0; i < sel.Length; i++)
        {
            UnityEngine.Object o = sel[i];
            if (o == null) continue;

            // Only keep types that make sense for hierarchy matching.
            if (!(o is Renderer) && !(o is GameObject) && !(o is Transform) && !(o is Component))
            {
                continue;
            }

            if (!_manualExclusions.Contains(o))
            {
                _manualExclusions.Add(o);
            }
        }
    }

    private static void DrawRendererRow(string label, Renderer rr)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(45));
            EditorGUILayout.ObjectField(rr, typeof(Renderer), true);
            using (new EditorGUI.DisabledScope(rr == null))
            {
                if (GUILayout.Button("Ping", GUILayout.Width(45)))
                {
                    EditorGUIUtility.PingObject(rr.gameObject);
                    Selection.activeObject = rr.gameObject;
                }
            }
        }
    }

    private void DrawOutlierList(string title, List<RendererBoundInfo> list, int maxRows)
    {
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        int n = Mathf.Min(maxRows, list.Count);
        for (int i = 0; i < n; i++)
        {
            RendererBoundInfo info = list[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(18));
                EditorGUILayout.ObjectField(info.Renderer, typeof(Renderer), true);
                if (GUILayout.Button("Ping", GUILayout.Width(45)))
                {
                    if (info.Renderer != null)
                    {
                        EditorGUIUtility.PingObject(info.Renderer.gameObject);
                        Selection.activeObject = info.Renderer.gameObject;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Path", GUILayout.Width(35));
                EditorGUILayout.SelectableLabel(info.Path ?? "(unknown)", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            EditorGUILayout.LabelField("Bounds", $"c={info.Bounds.center:F2} e={info.Bounds.extents:F2}");
        }
    }

    private int TryGetAreaIndexFromSelection(GameObject[] roots)
    {
        Transform sel = Selection.activeTransform;
        if (sel == null || roots == null) return -1;

        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null) continue;
            try
            {
                if (sel == root.transform || sel.IsChildOf(root.transform)) return i;
            }
            catch
            {
                // ignore
            }
        }

        return -1;
    }

    private void AnalyzeArea(int areaIndex)
    {
        _diagnosticsHasCombinedBounds = false;
        _diagnosticsHasLocalBounds = false;
        _diagnosticsMinZOutliers.Clear();
        _diagnosticsMaxZOutliers.Clear();
        _diagnosticsMinXOutliers.Clear();
        _diagnosticsMaxXOutliers.Clear();
        _diagnosticsMinYOutliers.Clear();
        _diagnosticsMaxYOutliers.Clear();

        _diagnosticsMinXRenderer = null;
        _diagnosticsMaxXRenderer = null;
        _diagnosticsMinYRenderer = null;
        _diagnosticsMaxYRenderer = null;
        _diagnosticsMinZRenderer = null;
        _diagnosticsMaxZRenderer = null;

        _diagnosticsMinX = 0f;
        _diagnosticsMaxX = 0f;
        _diagnosticsMinY = 0f;
        _diagnosticsMaxY = 0f;
        _diagnosticsMinZ = 0f;
        _diagnosticsMaxZ = 0f;

        if (_target == null) return;
        GameObject[] roots = _target.m_AreaRoots;
        if (roots == null || areaIndex < 0 || areaIndex >= roots.Length) return;
        GameObject root = roots[areaIndex];
        if (root == null) return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
        Bounds localCombined = new Bounds(Vector3.zero, Vector3.zero);
        bool localAny = false;

        float minZ = 0f;
        float maxZ = 0f;
        Renderer minZrr = null;
        Renderer maxZrr = null;
        string minZPath = null;
        string maxZPath = null;

        float minX = 0f;
        float maxX = 0f;
        Renderer minXrr = null;
        Renderer maxXrr = null;
        string minXPath = null;
        string maxXPath = null;

        float minY = 0f;
        float maxY = 0f;
        Renderer minYrr = null;
        Renderer maxYrr = null;
        string minYPath = null;
        string maxYPath = null;

        var minZCandidates = new List<RendererBoundInfo>(renderers.Length);
        var maxZCandidates = new List<RendererBoundInfo>(renderers.Length);

        var minXCandidates = new List<RendererBoundInfo>(renderers.Length);
        var maxXCandidates = new List<RendererBoundInfo>(renderers.Length);

        var minYCandidates = new List<RendererBoundInfo>(renderers.Length);
        var maxYCandidates = new List<RendererBoundInfo>(renderers.Length);

        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rr = renderers[r];
            if (rr == null) continue;
            if (IsManuallyExcluded(rr)) continue;
            if (!_diagnosticsIncludeIgnoredRenderers && ShouldIgnoreRenderer(rr)) continue;

            Bounds rb = rr.bounds;
            if (!IsFinite(rb.center) || !IsFinite(rb.extents)) continue;

            string rrPath = null;
            try
            {
                rrPath = AnimationUtility.CalculateTransformPath(rr.transform, root.transform);
            }
            catch
            {
                rrPath = rr.gameObject != null ? rr.gameObject.name : "(unknown)";
            }

            if (!any)
            {
                combined = rb;
                any = true;
                minZ = rb.min.z;
                maxZ = rb.max.z;
                minZrr = rr;
                maxZrr = rr;
                minZPath = rrPath;
                maxZPath = rrPath;

                minX = rb.min.x;
                maxX = rb.max.x;
                minXrr = rr;
                maxXrr = rr;
                minXPath = rrPath;
                maxXPath = rrPath;

                minY = rb.min.y;
                maxY = rb.max.y;
                minYrr = rr;
                maxYrr = rr;
                minYPath = rrPath;
                maxYPath = rrPath;
            }
            else
            {
                combined.Encapsulate(rb);
                float thisMinZ = rb.min.z;
                float thisMaxZ = rb.max.z;
                if (thisMinZ < minZ)
                {
                    minZ = thisMinZ;
                    minZrr = rr;
                    minZPath = rrPath;
                }
                if (thisMaxZ > maxZ)
                {
                    maxZ = thisMaxZ;
                    maxZrr = rr;
                    maxZPath = rrPath;
                }

                float thisMinX = rb.min.x;
                float thisMaxX = rb.max.x;
                if (thisMinX < minX)
                {
                    minX = thisMinX;
                    minXrr = rr;
                    minXPath = rrPath;
                }
                if (thisMaxX > maxX)
                {
                    maxX = thisMaxX;
                    maxXrr = rr;
                    maxXPath = rrPath;
                }

                float thisMinY = rb.min.y;
                float thisMaxY = rb.max.y;
                if (thisMinY < minY)
                {
                    minY = thisMinY;
                    minYrr = rr;
                    minYPath = rrPath;
                }
                if (thisMaxY > maxY)
                {
                    maxY = thisMaxY;
                    maxYrr = rr;
                    maxYPath = rrPath;
                }
            }

            // Local-space combined bounds (in root's coordinate system)
            // Useful to diagnose when world AABB is inflated by rotation/diagonal geometry.
            if (TryEncapsulateRendererWorldBoundsInRootLocal(root.transform, rb, ref localCombined, ref localAny))
            {
                // encapsulated
            }

            RendererBoundInfo info = new RendererBoundInfo { Renderer = rr, Path = rrPath, Bounds = rb };
            minZCandidates.Add(info);
            maxZCandidates.Add(info);
            minXCandidates.Add(info);
            maxXCandidates.Add(info);
            minYCandidates.Add(info);
            maxYCandidates.Add(info);
        }

        if (!any) return;

        minZCandidates.Sort((a, b) => a.Bounds.min.z.CompareTo(b.Bounds.min.z));
        maxZCandidates.Sort((a, b) => b.Bounds.max.z.CompareTo(a.Bounds.max.z));

        minXCandidates.Sort((a, b) => a.Bounds.min.x.CompareTo(b.Bounds.min.x));
        maxXCandidates.Sort((a, b) => b.Bounds.max.x.CompareTo(a.Bounds.max.x));

        minYCandidates.Sort((a, b) => a.Bounds.min.y.CompareTo(b.Bounds.min.y));
        maxYCandidates.Sort((a, b) => b.Bounds.max.y.CompareTo(a.Bounds.max.y));

        _diagnosticsCombinedBounds = combined;
        _diagnosticsHasCombinedBounds = true;

        if (localAny)
        {
            _diagnosticsLocalBounds = localCombined;
            _diagnosticsHasLocalBounds = true;
        }

        _diagnosticsMinZ = minZ;
        _diagnosticsMaxZ = maxZ;
        _diagnosticsMinZRenderer = minZrr;
        _diagnosticsMaxZRenderer = maxZrr;

        _diagnosticsMinX = minX;
        _diagnosticsMaxX = maxX;
        _diagnosticsMinXRenderer = minXrr;
        _diagnosticsMaxXRenderer = maxXrr;

        _diagnosticsMinY = minY;
        _diagnosticsMaxY = maxY;
        _diagnosticsMinYRenderer = minYrr;
        _diagnosticsMaxYRenderer = maxYrr;

        _diagnosticsMinZOutliers.AddRange(minZCandidates);
        _diagnosticsMaxZOutliers.AddRange(maxZCandidates);

        _diagnosticsMinXOutliers.AddRange(minXCandidates);
        _diagnosticsMaxXOutliers.AddRange(maxXCandidates);

        _diagnosticsMinYOutliers.AddRange(minYCandidates);
        _diagnosticsMaxYOutliers.AddRange(maxYCandidates);

        if (minZrr != null && maxZrr != null)
        {
            Debug.Log(
                $"Occlusion bounds diagnostics: Area '{root.name}' index {areaIndex} combined size={combined.size} center={combined.center}." +
            $"\n  MinX={minX:F3} from '{minXPath}'" +
            $"\n  MaxX={maxX:F3} from '{maxXPath}'" +
            $"\n  MinY={minY:F3} from '{minYPath}'" +
            $"\n  MaxY={maxY:F3} from '{maxYPath}'" +
            $"\n  MinZ={minZ:F3} from '{minZPath}'" +
            $"\n  MaxZ={maxZ:F3} from '{maxZPath}'");

            if (_diagnosticsHasLocalBounds)
            {
                Debug.Log($"Occlusion bounds diagnostics: Area '{root.name}' local-bounds size={_diagnosticsLocalBounds.size} center={_diagnosticsLocalBounds.center} (root-local). Root rotation={root.transform.rotation.eulerAngles}.");
            }
        }
    }

    private void Bake(OcclusionManager mgr)
    {
        if (mgr == null) return;

        GameObject[] roots = mgr.m_AreaRoots;
        if (roots == null || roots.Length == 0)
        {
            EditorUtility.DisplayDialog("Bake Occlusion Bounds", "No Area Roots set on OcclusionManager.", "OK");
            return;
        }

        int n = roots.Length;
        Vector3[] centers = new Vector3[n];
        Vector3[] extents = new Vector3[n];

        Undo.RecordObject(mgr, "Bake Occlusion Bounds");

        mgr.m_AreaDisableDistances = BuildAreaDisableDistancesForBake(roots, mgr.m_AreaDisableDistances);

        int missingCount = 0;
        for (int i = 0; i < n; i++)
        {
            GameObject root = roots[i];
            if (root == null)
            {
                centers[i] = Vector3.zero;
                extents[i] = Vector3.zero;
                missingCount++;
                continue;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);

            float largestExtentsSqr = -1f;
            Vector3 largestExtents = Vector3.zero;
            string largestRendererPath = null;

            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f, minZ = 0f, maxZ = 0f;
            string minXPath = null, maxXPath = null, minYPath = null, maxYPath = null, minZPath = null, maxZPath = null;

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer rr = renderers[r];
                if (rr == null) continue;

                if (IsManuallyExcluded(rr)) continue;

                if (ShouldIgnoreRenderer(rr)) continue;

                Bounds rb = rr.bounds;
                if (!IsFinite(rb.center) || !IsFinite(rb.extents)) continue;

                string rrPath = null;
                try
                {
                    rrPath = AnimationUtility.CalculateTransformPath(rr.transform, root.transform);
                }
                catch
                {
                    rrPath = rr.gameObject != null ? rr.gameObject.name : "(unknown)";
                }

                if (!any)
                {
                    b = rb;
                    any = true;

                    Vector3 mn = rb.min;
                    Vector3 mx = rb.max;
                    minX = mn.x; maxX = mx.x;
                    minY = mn.y; maxY = mx.y;
                    minZ = mn.z; maxZ = mx.z;
                    minXPath = rrPath; maxXPath = rrPath;
                    minYPath = rrPath; maxYPath = rrPath;
                    minZPath = rrPath; maxZPath = rrPath;
                }
                else
                {
                    b.Encapsulate(rb);

                    Vector3 mn = rb.min;
                    Vector3 mx = rb.max;

                    if (mn.x < minX) { minX = mn.x; minXPath = rrPath; }
                    if (mx.x > maxX) { maxX = mx.x; maxXPath = rrPath; }

                    if (mn.y < minY) { minY = mn.y; minYPath = rrPath; }
                    if (mx.y > maxY) { maxY = mx.y; maxYPath = rrPath; }

                    if (mn.z < minZ) { minZ = mn.z; minZPath = rrPath; }
                    if (mx.z > maxZ) { maxZ = mx.z; maxZPath = rrPath; }
                }

                float extSqr = rb.extents.sqrMagnitude;
                if (extSqr > largestExtentsSqr)
                {
                    largestExtentsSqr = extSqr;
                    largestExtents = rb.extents;
                    largestRendererPath = rrPath;
                }
            }

            if (!any)
            {
                centers[i] = root.transform.position;
                extents[i] = Vector3.zero;
                continue;
            }

            centers[i] = b.center;
            extents[i] = b.extents;

            // Sanity warning: if extents dwarf the configured disable distance, this area's AABB may include spawn/origin.
            float dist = (mgr.m_AreaDisableDistances != null && mgr.m_AreaDisableDistances.Length == n) ? mgr.m_AreaDisableDistances[i] : DEFAULT_AREA_DISABLE_DISTANCE;
            if (!IsFinite(dist) || dist <= 0f) dist = DEFAULT_AREA_DISABLE_DISTANCE;
            float maxReasonable = Mathf.Max(50f, dist * 4f);
            if (Mathf.Abs(extents[i].x) > maxReasonable || Mathf.Abs(extents[i].y) > maxReasonable || Mathf.Abs(extents[i].z) > maxReasonable)
            {
                string largestInfo = (largestRendererPath != null)
                    ? $" Largest contributor (by extents): '{largestRendererPath}' extents={largestExtents}."
                    : "";

                string extremesInfo = (any)
                    ? $"\n  Combined bounds min=({minX:F3}, {minY:F3}, {minZ:F3}) max=({maxX:F3}, {maxY:F3}, {maxZ:F3})" +
                      $"\n  MinX from '{minXPath}', MaxX from '{maxXPath}'" +
                      $"\n  MinY from '{minYPath}', MaxY from '{maxYPath}'" +
                      $"\n  MinZ from '{minZPath}', MaxZ from '{maxZPath}'"
                    : "";

                Debug.LogWarning(
                    $"Occlusion bounds bake: Area '{root.name}' (index {i}) baked very large extents={extents[i]} center={centers[i]} with disableDistance={dist}." +
                    " This can cause the area to enable at spawn even when far away (player ends up inside the AABB)." +
                    largestInfo +
                    extremesInfo +
                    " Consider reparenting/excluding large VFX/geo from the Area Root and re-bake.");
            }
        }

        mgr.m_AreaBoundsCenter = centers;
        mgr.m_AreaBoundsExtents = extents;

        EditorUtility.SetDirty(mgr);

        if (missingCount > 0)
        {
            Debug.LogWarning("Occlusion bounds bake: some Area Roots were null; baked zeros for those indices.");
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!_previewInScene) return;
        if (_target == null) return;

        GameObject[] roots = _target.m_AreaRoots;
        if (roots == null || roots.Length == 0) return;

        bool hasBaked = _target.m_AreaBoundsCenter != null
                        && _target.m_AreaBoundsExtents != null
                        && _target.m_AreaBoundsCenter.Length == roots.Length
                        && _target.m_AreaBoundsExtents.Length == roots.Length;

        Transform sel = Selection.activeTransform;

        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null) continue;

            bool isSelected = false;
            if (sel != null)
            {
                try
                {
                    isSelected = sel == root.transform || sel.IsChildOf(root.transform);
                }
                catch
                {
                    isSelected = false;
                }
            }

            Bounds b;
            bool any;

            if (_previewUseLiveComputed || !hasBaked)
            {
                any = TryComputeBoundsFromRenderers(root, out b);
            }
            else
            {
                Vector3 c = _target.m_AreaBoundsCenter[i];
                Vector3 e = _target.m_AreaBoundsExtents[i];
                b = new Bounds(c, e * 2f);
                any = IsFinite(c) && IsFinite(e) && (e.x > 0f || e.y > 0f || e.z > 0f);
            }

            if (!any) continue;

            bool isHighlighted = i == _previewHighlightAreaIndex;
            Color cWire = isHighlighted ? new Color(1f, 0.1f, 0.1f, 1f) :
                isSelected ? new Color(1f, 0.8f, 0.2f, 1f) : new Color(0.2f, 0.9f, 0.3f, 1f);
            Handles.color = cWire;
            Handles.DrawWireCube(b.center, b.size);

            if (_previewShowLabels)
            {
                string label = $"{i}: {root.name}  size=({b.size.x:F1},{b.size.y:F1},{b.size.z:F1})";
                Handles.Label(b.center, label);
            }
        }

        if (_diagnosticsShowAxisMarkersInScene && _diagnosticsHasCombinedBounds && roots != null && _diagnosticsAreaIndex >= 0 && _diagnosticsAreaIndex < roots.Length)
        {
            GameObject diagRoot = roots[_diagnosticsAreaIndex];
            if (diagRoot != null)
            {
                Bounds b = _diagnosticsCombinedBounds;
                DrawAxisMarker(new Vector3(b.min.x, b.center.y, b.center.z), new Color(1f, 0.2f, 0.8f, 1f), _diagnosticsShowAxisMarkerLabelsInScene ? $"MinX {b.min.x:F1}" : null);
                DrawAxisMarker(new Vector3(b.max.x, b.center.y, b.center.z), new Color(0.7f, 0.2f, 1f, 1f), _diagnosticsShowAxisMarkerLabelsInScene ? $"MaxX {b.max.x:F1}" : null);

                DrawAxisMarker(new Vector3(b.center.x, b.min.y, b.center.z), new Color(1f, 0.6f, 0.2f, 1f), _diagnosticsShowAxisMarkerLabelsInScene ? $"MinY {b.min.y:F1}" : null);
                DrawAxisMarker(new Vector3(b.center.x, b.max.y, b.center.z), new Color(1f, 0.8f, 0.2f, 1f), _diagnosticsShowAxisMarkerLabelsInScene ? $"MaxY {b.max.y:F1}" : null);

                DrawAxisMarker(new Vector3(b.center.x, b.center.y, b.min.z), new Color(1f, 0.2f, 0.2f, 1f), _diagnosticsShowAxisMarkerLabelsInScene ? $"MinZ {b.min.z:F1}" : null);
                DrawAxisMarker(new Vector3(b.center.x, b.center.y, b.max.z), new Color(0.2f, 0.6f, 1f, 1f), _diagnosticsShowAxisMarkerLabelsInScene ? $"MaxZ {b.max.z:F1}" : null);

                DrawOutlierLabelsInScene();
            }
        }

        if (_diagnosticsShowLocalOBBInScene && _diagnosticsHasLocalBounds && roots != null && _diagnosticsAreaIndex >= 0 && _diagnosticsAreaIndex < roots.Length)
        {
            GameObject diagRoot = roots[_diagnosticsAreaIndex];
            if (diagRoot != null)
            {
                Matrix4x4 old = Handles.matrix;
                try
                {
                    Handles.matrix = diagRoot.transform.localToWorldMatrix;
                    Handles.color = new Color(1f, 1f, 1f, 0.8f);
                    Handles.DrawWireCube(_diagnosticsLocalBounds.center, _diagnosticsLocalBounds.size);
                    if (_diagnosticsShowLocalOBBLabelInScene)
                    {
                        Handles.Label(_diagnosticsLocalBounds.center, $"Local OBB size={_diagnosticsLocalBounds.size:F1}");
                    }
                }
                finally
                {
                    Handles.matrix = old;
                }
            }
        }
    }

    private static bool TryEncapsulateRendererWorldBoundsInRootLocal(Transform root, Bounds worldBounds, ref Bounds localCombined, ref bool localAny)
    {
        if (root == null) return false;
        if (!IsFinite(worldBounds.center) || !IsFinite(worldBounds.extents)) return false;

        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;

        // 8 corners of the world AABB
        Vector3[] corners = new Vector3[8]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z),
        };

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 lp = root.InverseTransformPoint(corners[i]);
            if (!IsFinite(lp)) continue;

            if (!localAny)
            {
                localCombined = new Bounds(lp, Vector3.zero);
                localAny = true;
            }
            else
            {
                localCombined.Encapsulate(lp);
            }
        }

        return localAny;
    }

    private static void DrawAxisMarker(Vector3 position, Color color, string label)
    {
        Handles.color = color;
        float size = HandleUtility.GetHandleSize(position) * 0.08f;
        Handles.SphereHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
        if (!string.IsNullOrEmpty(label))
        {
            Handles.Label(position, label);
        }
    }

    private void DrawOutlierLabelsInScene()
    {
        if (_diagnosticsOutlierLabelAxis == OutlierLabelAxis.None) return;
        if (_diagnosticsOutlierLabelCount <= 0) return;

        int n = _diagnosticsOutlierLabelCount;

        if (_diagnosticsOutlierLabelAxis == OutlierLabelAxis.X || _diagnosticsOutlierLabelAxis == OutlierLabelAxis.All)
        {
            DrawTopOutlierLabels(_diagnosticsMinXOutliers, n, "MinX");
            DrawTopOutlierLabels(_diagnosticsMaxXOutliers, n, "MaxX");
        }

        if (_diagnosticsOutlierLabelAxis == OutlierLabelAxis.Y || _diagnosticsOutlierLabelAxis == OutlierLabelAxis.All)
        {
            DrawTopOutlierLabels(_diagnosticsMinYOutliers, n, "MinY");
            DrawTopOutlierLabels(_diagnosticsMaxYOutliers, n, "MaxY");
        }

        if (_diagnosticsOutlierLabelAxis == OutlierLabelAxis.Z || _diagnosticsOutlierLabelAxis == OutlierLabelAxis.All)
        {
            DrawTopOutlierLabels(_diagnosticsMinZOutliers, n, "MinZ");
            DrawTopOutlierLabels(_diagnosticsMaxZOutliers, n, "MaxZ");
        }
    }

    private static void DrawTopOutlierLabels(List<RendererBoundInfo> list, int n, string prefix)
    {
        if (list == null || list.Count == 0) return;
        int count = Mathf.Min(n, list.Count);
        for (int i = 0; i < count; i++)
        {
            RendererBoundInfo info = list[i];
            if (info.Renderer == null) continue;
            Vector3 p = info.Bounds.center;
            string nm = info.Renderer.gameObject != null ? info.Renderer.gameObject.name : "(null)";
            Handles.Label(p, $"{prefix}#{i + 1} {nm}");
        }
    }

    private bool TryComputeBoundsFromRenderers(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);

        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rr = renderers[r];
            if (rr == null) continue;
            if (IsManuallyExcluded(rr)) continue;
            if (ShouldIgnoreRenderer(rr)) continue;

            Bounds rb = rr.bounds;
            if (!IsFinite(rb.center) || !IsFinite(rb.extents)) continue;

            if (!any)
            {
                b = rb;
                any = true;
            }
            else
            {
                b.Encapsulate(rb);
            }
        }

        if (!any) return false;

        bounds = b;
        return true;
    }
}
#endif
