using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#if UNITY_EDITOR
public class GenerateLightProbesGrokVersion : EditorWindow
{
    [SerializeField] private Terrain terrain;
    [SerializeField] private Transform boundsRoot;
    [SerializeField] private LightProbeGroup targetGroup;
    [SerializeField] private Collider playableAreaCollider;

    [Header("Sampling")]
    [SerializeField] private float horizontalSpacing = 3f;
    [SerializeField] private float surfaceOffset = 0.5f;
    [SerializeField] private float rayStartPadding = 5f;
    [SerializeField] private LayerMask raycastLayers = ~0;

    [Header("Vertical")]
    [SerializeField] private int verticalProbeCount = 3;
    [SerializeField] private float verticalSpacing = 2f;

    [Header("Safety")]
    [SerializeField] private float insideCheckRadius = 0.05f;
    [SerializeField] private bool requireContributeGI = true;
    [SerializeField] private bool requireBakedLightmap = true;
    [SerializeField] private bool requireInsidePlayableArea = false;
    [SerializeField] private bool clearExistingProbes = true;

    private const float _dedupeEpsilon = 0.01f; // 1 cm

    private GameObject _probeTestSphereGo;
    private SphereCollider _probeTestSphere;

    [MenuItem("Tools/Lighting/Generate Light Probes (Grid)")]
    private static void Open()
    {
        var window = GetWindow<GenerateLightProbesGrokVersion>();
        window.titleContent = new GUIContent("Light Probes");
        window.minSize = new Vector2(420, 420);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Generate Light Probes", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            terrain = (Terrain)EditorGUILayout.ObjectField(new GUIContent("Terrain (optional)"), terrain, typeof(Terrain), true);
            boundsRoot = (Transform)EditorGUILayout.ObjectField(new GUIContent("Bounds Root (optional)", "Renderers under this root will be included in the sampling bounds."), boundsRoot, typeof(Transform), true);
            targetGroup = (LightProbeGroup)EditorGUILayout.ObjectField(new GUIContent("Target LightProbeGroup"), targetGroup, typeof(LightProbeGroup), true);
            playableAreaCollider = (Collider)EditorGUILayout.ObjectField(new GUIContent("Playable Area Collider (optional)", "If enabled below, probes will only be placed inside this collider volume (prevents void placement)."), playableAreaCollider, typeof(Collider), true);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Density", EditorStyles.boldLabel);
            horizontalSpacing = EditorGUILayout.FloatField(new GUIContent("Horizontal Spacing (m)", "Distance between probes in X/Z."), horizontalSpacing);
            verticalProbeCount = EditorGUILayout.IntField(new GUIContent("Vertical Probe Count", "How many probes stacked upward from ground."), verticalProbeCount);
            verticalSpacing = EditorGUILayout.FloatField(new GUIContent("Vertical Spacing (m)", "Distance between vertical probe layers."), verticalSpacing);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            surfaceOffset = EditorGUILayout.FloatField(new GUIContent("Surface Offset (m)", "Lift probes slightly above the hit surface."), surfaceOffset);
            rayStartPadding = EditorGUILayout.FloatField(new GUIContent("Ray Start Padding (m)", "How far above max scene height to start raycasts."), rayStartPadding);
            raycastLayers = LayerMaskField(new GUIContent("Raycast Layers"), raycastLayers);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Constraints", EditorStyles.boldLabel);
            insideCheckRadius = EditorGUILayout.FloatField(new GUIContent("Inside Check Radius (m)", "Used to detect probes placed inside colliders."), insideCheckRadius);
            requireContributeGI = EditorGUILayout.Toggle(new GUIContent("Require Contribute GI", "Only accept ground hits on objects marked Contribute GI / Lightmap Static."), requireContributeGI);
            requireBakedLightmap = EditorGUILayout.Toggle(new GUIContent("Require Baked Lightmap", "Only accept ground hits on surfaces that currently have baked lightmap data (Renderer.lightmapIndex / Terrain.lightmapIndex)."), requireBakedLightmap);
            requireInsidePlayableArea = EditorGUILayout.Toggle(new GUIContent("Require Inside Playable Area", "If enabled, only place probes inside the Playable Area Collider."), requireInsidePlayableArea);
            clearExistingProbes = EditorGUILayout.Toggle(new GUIContent("Clear Existing Probes"), clearExistingProbes);
        }

        EditorGUILayout.Space(8);

        var boundsOk = TryComputeSamplingBounds(out var samplingBounds, out var maxHeight);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.LabelField("Computed Bounds", boundsOk ? samplingBounds.ToString() : "(not available)");
            EditorGUILayout.FloatField("Max Scene Height", boundsOk ? maxHeight : 0f);
        }

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(!CanGenerate(boundsOk)))
        {
            if (GUILayout.Button("Generate Probes", GUILayout.Height(36)))
            {
                GenerateIntoTarget();
            }
        }

        if (!boundsOk)
        {
            EditorGUILayout.HelpBox("Assign a Terrain and/or a Bounds Root that contains scene renderers, so the tool can compute where to sample.", MessageType.Info);
        }

        if (targetGroup == null)
        {
            EditorGUILayout.HelpBox("Assign the LightProbeGroup you want to write probe positions into.", MessageType.Info);
        }

        if (requireInsidePlayableArea && playableAreaCollider == null)
        {
            EditorGUILayout.HelpBox("'Require Inside Playable Area' is enabled but no Playable Area Collider is assigned.", MessageType.Warning);
        }

        if (surfaceOffset < insideCheckRadius)
        {
            EditorGUILayout.HelpBox("Surface Offset is smaller than Inside Check Radius. Probes may be rejected as intersecting the floor collider.", MessageType.Info);
        }
    }

    private void OnEnable()
    {
        EnsureProbeTestSphere();
    }

    private void OnDisable()
    {
        if (_probeTestSphereGo != null)
        {
            DestroyImmediate(_probeTestSphereGo);
            _probeTestSphereGo = null;
            _probeTestSphere = null;
        }
    }

    private void EnsureProbeTestSphere()
    {
        if (_probeTestSphere != null) return;
        _probeTestSphereGo = new GameObject("__LightProbeTestSphere") { hideFlags = HideFlags.HideAndDontSave };
        _probeTestSphereGo.transform.position = Vector3.zero;
        _probeTestSphereGo.transform.rotation = Quaternion.identity;
        _probeTestSphere = _probeTestSphereGo.AddComponent<SphereCollider>();
        _probeTestSphere.isTrigger = true;
        _probeTestSphere.radius = Mathf.Max(0.0001f, insideCheckRadius);
        _probeTestSphere.enabled = true;
    }

    private bool CanGenerate(bool boundsOk)
    {
        if (!boundsOk) return false;
        if (targetGroup == null) return false;
        if (horizontalSpacing <= 0f) return false;
        if (verticalProbeCount <= 0) return false;
        if (verticalSpacing < 0f) return false;
        if (insideCheckRadius < 0f) return false;
        return true;
    }

    private void GenerateIntoTarget()
    {
        EnsureProbeTestSphere();

        if (!TryComputeSamplingBounds(out var samplingBounds, out var maxHeight))
        {
            EditorUtility.DisplayDialog("Light Probes", "Could not compute sampling bounds. Assign Terrain and/or Bounds Root.", "OK");
            return;
        }

        if (requireInsidePlayableArea && playableAreaCollider == null)
        {
            EditorUtility.DisplayDialog("Light Probes", "'Require Inside Playable Area' is enabled but no collider is assigned.", "OK");
            return;
        }

        var minHeight = samplingBounds.min.y;
        var rayStartY = maxHeight + Mathf.Max(0f, rayStartPadding);
        var rayDistance = (rayStartY - minHeight) + 10f;

        var worldPositions = new List<Vector3>(4096);
        var dedupe = new HashSet<Vector3Int>();

        int skippedVoid = 0;
        int skippedInside = 0;
        int skippedGi = 0;
        int skippedUnbaked = 0;
        int added = 0;

        var collidersBuffer = new Collider[128];
        var hitsBuffer = new RaycastHit[128];
        const float minUpNormalY = 0.25f; // accept floors + reasonably sloped surfaces

        int xSteps = Mathf.Max(1, Mathf.FloorToInt(samplingBounds.size.x / horizontalSpacing) + 1);
        int zSteps = Mathf.Max(1, Mathf.FloorToInt(samplingBounds.size.z / horizontalSpacing) + 1);
        int totalColumns = xSteps * zSteps;
        int columnIndex = 0;

        try
        {
            for (float x = samplingBounds.min.x; x <= samplingBounds.max.x + 0.0001f; x += horizontalSpacing)
            {
                for (float z = samplingBounds.min.z; z <= samplingBounds.max.z + 0.0001f; z += horizontalSpacing)
                {
                    columnIndex++;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Generating Light Probes",
                        $"Sampling {columnIndex}/{totalColumns}",
                        totalColumns > 0 ? (float)columnIndex / totalColumns : 0f))
                    {
                        return;
                    }

                    var origin = new Vector3(x, rayStartY, z);
                    int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, hitsBuffer, rayDistance, raycastLayers, QueryTriggerInteraction.Ignore);
                    if (hitCount <= 0)
                    {
                        skippedVoid++;
                        continue;
                    }

                    // Unity does not guarantee hit order for NonAlloc raycasts.
                    Array.Sort(hitsBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);

                    bool anyAcceptedFloorInColumn = false;
                    bool anyGiRejectedInColumn = false;

                    for (int h = 0; h < hitCount; h++)
                    {
                        var hit = hitsBuffer[h];
                        if (hit.collider == null)
                            continue;

                        // We only seed probes from floor-like surfaces. This avoids wasting work on walls/ceilings.
                        if (hit.normal.y < minUpNormalY)
                            continue;

                        if (requireContributeGI && !IsContributeGI(hit.collider))
                        {
                            anyGiRejectedInColumn = true;
                            continue;
                        }

                        if (requireBakedLightmap && !IsBakedLightmapped(hit.collider))
                        {
                            skippedUnbaked++;
                            continue;
                        }

                        anyAcceptedFloorInColumn = true;

                        float baseY = hit.point.y + Mathf.Max(0f, surfaceOffset);
                        for (int i = 0; i < verticalProbeCount; i++)
                        {
                            float y = baseY + i * Mathf.Max(0f, verticalSpacing);
                            if (y > maxHeight - 0.0001f)
                                break; // must not be placed higher than highest mesh extends

                            var pos = new Vector3(x, y, z);

                            if (requireInsidePlayableArea && !IsPointInsideCollider(playableAreaCollider, pos))
                            {
                                // Stop stacking when we leave the playable volume.
                                break;
                            }

                            if (IsOverlappingAnyCollider(pos, insideCheckRadius, collidersBuffer, playableAreaCollider))
                            {
                                skippedInside++;
                                // Stop stacking when we hit geometry. Prevents popping through ceilings into solids/void.
                                break;
                            }

                            var key = Quantize(pos, _dedupeEpsilon);
                            if (!dedupe.Add(key))
                                continue;

                            worldPositions.Add(pos);
                            added++;
                        }
                    }

                    if (!anyAcceptedFloorInColumn)
                    {
                        // We hit something, but didn't find a valid GI floor surface to seed probes.
                        if (anyGiRejectedInColumn)
                            skippedGi++;
                        else
                            skippedVoid++;
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.RecordObject(targetGroup, "Generate Light Probes");

        if (clearExistingProbes)
        {
            targetGroup.probePositions = Array.Empty<Vector3>();
        }

        var local = new Vector3[worldPositions.Count];
        for (int i = 0; i < worldPositions.Count; i++)
        {
            local[i] = targetGroup.transform.InverseTransformPoint(worldPositions[i]);
        }

        targetGroup.probePositions = local;
        EditorUtility.SetDirty(targetGroup);
        EditorSceneManager.MarkSceneDirty(targetGroup.gameObject.scene);

        EditorUtility.DisplayDialog(
            "Light Probes",
            $"Generated {added} probes.\n\nSkipped:\n- Void (no baked/playable surface hit): {skippedVoid}\n- Inside mesh/collider: {skippedInside}\n- Not Contribute GI: {skippedGi}\n- Not baked lightmap: {skippedUnbaked}",
            "OK");
    }

    private bool TryComputeSamplingBounds(out Bounds bounds, out float maxHeight)
    {
        bool hasAny = false;
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        maxHeight = float.MinValue;

        if (terrain != null && terrain.terrainData != null)
        {
            // TerrainData.bounds is local-space around (0,0,0). Convert to world bounds.
            var tb = terrain.terrainData.bounds;
            var worldCenter = terrain.transform.TransformPoint(tb.center);
            var worldSize = Vector3.Scale(tb.size, AbsVec3(terrain.transform.lossyScale));
            var b = new Bounds(worldCenter, worldSize);
            bounds = hasAny ? Encapsulate(bounds, b) : b;
            hasAny = true;
            maxHeight = Mathf.Max(maxHeight, b.max.y);
        }

        if (boundsRoot != null)
        {
            var renderers = boundsRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                // Ignore renderers with zero size (rare, but can happen with particles/etc)
                var b = r.bounds;
                if (b.size.sqrMagnitude <= 0f) continue;
                bounds = hasAny ? Encapsulate(bounds, b) : b;
                hasAny = true;
                maxHeight = Mathf.Max(maxHeight, b.max.y);
            }
        }

        if (!hasAny)
        {
            maxHeight = 0f;
            return false;
        }

        return true;
    }

    private static bool IsInsideAnyCollider(Vector3 worldPos, float radius, Collider[] buffer)
    {
        // Deprecated by IsOverlappingAnyCollider (kept to avoid breaking external references).
        radius = Mathf.Max(0.0001f, radius);
        int count = Physics.OverlapSphereNonAlloc(worldPos, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
        return count > 0;
    }

    private static bool IsContributeGI(Collider collider)
    {
        if (collider == null) return false;
        var go = collider.gameObject;
        var flags = GameObjectUtility.GetStaticEditorFlags(go);
        // ContributeGI is the modern flag; LightmapStatic is older Unity versions.
        return (flags & StaticEditorFlags.ContributeGI) != 0 || (flags & StaticEditorFlags.ContributeGI) != 0;
    }

    private static bool IsBakedLightmapped(Collider collider)
    {
        if (collider == null) return false;

        // Terrain: baked lightmap index is stored on the Terrain component.
        var terrain = collider.GetComponent<Terrain>() ?? collider.GetComponentInParent<Terrain>();
        if (terrain != null)
        {
            return terrain.lightmapIndex >= 0;
        }

        // Mesh/other renderers: baked lightmap index is stored on the Renderer.
        var r = collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>();
        if (r == null) return false;
        return r.lightmapIndex >= 0;
    }

    private bool IsOverlappingAnyCollider(Vector3 worldPos, float radius, Collider[] buffer, Collider ignore)
    {
        if (_probeTestSphere == null)
            return false;

        radius = Mathf.Max(0.0001f, radius);
        _probeTestSphere.radius = radius;
        _probeTestSphereGo.transform.position = worldPos;
        _probeTestSphereGo.transform.rotation = Quaternion.identity;

        int count = Physics.OverlapSphereNonAlloc(worldPos, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = buffer[i];
            if (c == null) continue;
            if (ReferenceEquals(c, _probeTestSphere)) continue;
            if (ignore != null && ReferenceEquals(c, ignore)) continue;

            if (Physics.ComputePenetration(
                    _probeTestSphere, worldPos, Quaternion.identity,
                    c, c.transform.position, c.transform.rotation,
                    out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideCollider(Collider volume, Vector3 worldPos)
    {
        if (volume == null) return true;
        var closest = volume.ClosestPoint(worldPos);
        return (closest - worldPos).sqrMagnitude < 1e-10f;
    }

    private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
    {
        public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    private static Bounds Encapsulate(Bounds a, Bounds b)
    {
        a.Encapsulate(b.min);
        a.Encapsulate(b.max);
        return a;
    }

    private static Vector3 AbsVec3(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private static Vector3Int Quantize(Vector3 v, float epsilon)
    {
        epsilon = Mathf.Max(1e-6f, epsilon);
        return new Vector3Int(
            Mathf.RoundToInt(v.x / epsilon),
            Mathf.RoundToInt(v.y / epsilon),
            Mathf.RoundToInt(v.z / epsilon));
    }

    private static LayerMask LayerMaskField(GUIContent label, LayerMask selected)
    {
        var layers = new List<string>();
        var layerNumbers = new List<int>();
        for (int i = 0; i < 32; i++)
        {
            var layerName = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(layerName))
                continue;
            layerNumbers.Add(i);
            layers.Add(layerName);
        }

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            int layer = layerNumbers[i];
            if (((1 << layer) & selected.value) != 0)
                maskWithoutEmpty |= 1 << i;
        }

        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers.ToArray());

        int mask = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if ((maskWithoutEmpty & (1 << i)) != 0)
                mask |= 1 << layerNumbers[i];
        }

        selected.value = mask;
        return selected;
    }
}
#endif
