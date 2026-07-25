using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class AutoLightProbe : EditorWindow
{
    [SerializeField] private LightProbeGroup group;
    [SerializeField] private Terrain terrain;

    [Header("Terrain bounds")]
    [SerializeField] private float terrainBoundsPaddingPercent = 25f;

    [Header("Walk grid (density)")]
    [SerializeField] private float horizontalStepMeters = 3f;
    [SerializeField] private float verticalStepMeters = 2f;
    [SerializeField] private bool includeVerticalExpansion = true;

    [Header("Limits")]
    [SerializeField] private int maxNodesVisited = 20000;
    [SerializeField] private int maxProbes = 20000;

    [Header("Void test")]
    [SerializeField] private int voidRayCount = 24;
    [SerializeField] private float voidRayDistanceMeters = 12f;
    [SerializeField, Range(0f, 1f)] private float requiredHitRatio = 0.25f;
    [SerializeField] private bool excludeUpwardRays = true;

    [Header("Collision / safety")]
    [SerializeField] private LayerMask geometryMask = ~0;
    [SerializeField] private float overlapRadiusMeters = 0.15f;
    [SerializeField] private float minSurfaceClearanceMeters = 0.25f;

    [Header("Post filters")]
    [SerializeField] private bool simplifyVerticalStacks = false;
    [SerializeField] private int simplifyMinConsecutive = 10;
    [SerializeField] private int simplifyTargetCount = 3;

    [SerializeField] private bool snapLowestInStackToTerrain = false;
    [SerializeField] private float snapLowestToTerrainClearanceMeters = 0.5f;

    [SerializeField] private bool addAboveSnappedTerrainPoints = false;
    [SerializeField] private float aboveSnappedExtraHeightMeters = 0f;

    [SerializeField] private bool mergeClosePoints = false;
    [SerializeField] private float mergeCloseDistanceMeters = 2f;

    [SerializeField] private bool rejectIfNotVisibleFromAdjacent = false;
    [SerializeField] private float adjacentVisibilityRayEpsilonMeters = 0.02f;
    [SerializeField] private int adjacentVisibilitySampleCount = 12;
    [SerializeField] private int adjacentVisibilityMinVisibleRays = 1;
    [SerializeField] private float adjacentVisibilityRadiusMultiplier = 1.01f;

    private GameObject testSphereGo;
    private SphereCollider testSphere;
    private readonly Collider[] overlapBuffer = new Collider[256];
    private List<Vector3> voidDirections;

    private GameObject voidBoundaryGo;
    private BoxCollider voidBoundary;
    private const int IgnoreRaycastLayer = 2;

    [MenuItem("Tools/Lighting/Auto Light Probes (Space Walk)...")]
    public static void ShowWindow()
    {
        var window = GetWindow<AutoLightProbe>(true, "Auto Light Probes", true);
        window.minSize = new Vector2(460, 520);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureTestSphere();
        RebuildVoidDirections();
    }

    private void OnDisable()
    {
        if (testSphereGo != null)
        {
            DestroyImmediate(testSphereGo);
            testSphereGo = null;
            testSphere = null;
        }

        DestroyVoidBoundary();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Generates probes via BFS walk from the LightProbeGroup position.", EditorStyles.boldLabel);
        EditorGUILayout.Space(8);

        group = (LightProbeGroup)EditorGUILayout.ObjectField("Light Probe Group", group, typeof(LightProbeGroup), true);
        terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Terrain bounds", EditorStyles.boldLabel);
        terrainBoundsPaddingPercent = Mathf.Clamp(EditorGUILayout.FloatField("Terrain Bounds Padding (%)", terrainBoundsPaddingPercent), 0f, 200f);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Walk grid (density)", EditorStyles.boldLabel);
        horizontalStepMeters = Mathf.Max(0.25f, EditorGUILayout.FloatField("Horizontal Step (m)", horizontalStepMeters));
        verticalStepMeters = Mathf.Max(0.25f, EditorGUILayout.FloatField("Vertical Step (m)", verticalStepMeters));
        includeVerticalExpansion = EditorGUILayout.ToggleLeft("Expand vertically (±Y)", includeVerticalExpansion);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Void test", EditorStyles.boldLabel);
        voidRayCount = Mathf.Clamp(EditorGUILayout.IntField("Ray Count", voidRayCount), 6, 256);
        voidRayDistanceMeters = Mathf.Max(0.1f, EditorGUILayout.FloatField("Ray Distance (m)", voidRayDistanceMeters));
        requiredHitRatio = Mathf.Clamp01(EditorGUILayout.Slider("Required Hit Ratio", requiredHitRatio, 0f, 1f));
        excludeUpwardRays = EditorGUILayout.ToggleLeft("Exclude upward rays", excludeUpwardRays);

        if (GUILayout.Button("Rebuild Void Directions"))
        {
            RebuildVoidDirections();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Collision / safety", EditorStyles.boldLabel);
        geometryMask = LayerMaskField("Geometry Mask", geometryMask);
        overlapRadiusMeters = Mathf.Clamp(EditorGUILayout.FloatField("Overlap Radius (m)" , overlapRadiusMeters), 0f, 5f);
        minSurfaceClearanceMeters = Mathf.Clamp(EditorGUILayout.FloatField("Min Surface Clearance (m)", minSurfaceClearanceMeters), 0f, 5f);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Post filters", EditorStyles.boldLabel);
        simplifyVerticalStacks = EditorGUILayout.ToggleLeft("Simplify long vertical stacks", simplifyVerticalStacks);
        using (new EditorGUI.DisabledScope(!simplifyVerticalStacks))
        {
            simplifyMinConsecutive = Mathf.Clamp(EditorGUILayout.IntField("Min Consecutive (in a stack)", simplifyMinConsecutive), 2, 1000000);
            simplifyTargetCount = Mathf.Clamp(EditorGUILayout.IntField("Simplified Stack Count", simplifyTargetCount), 1, 1000000);
        }

        snapLowestInStackToTerrain = EditorGUILayout.ToggleLeft("Snap lowest in each stack to Terrain", snapLowestInStackToTerrain);
        using (new EditorGUI.DisabledScope(!snapLowestInStackToTerrain))
        {
            snapLowestToTerrainClearanceMeters = Mathf.Clamp(
                EditorGUILayout.FloatField("Terrain clearance (m)", snapLowestToTerrainClearanceMeters),
                0f,
                1000f);

            addAboveSnappedTerrainPoints = EditorGUILayout.ToggleLeft("Add one probe above snapped points", addAboveSnappedTerrainPoints);

            using (new EditorGUI.DisabledScope(!addAboveSnappedTerrainPoints))
            {
                aboveSnappedExtraHeightMeters = Mathf.Clamp(
                    EditorGUILayout.FloatField("Above-snapped extra height (m)", aboveSnappedExtraHeightMeters),
                    0f,
                    1000f);
            }
        }

        mergeClosePoints = EditorGUILayout.ToggleLeft("Merge probes within distance", mergeClosePoints);
        using (new EditorGUI.DisabledScope(!mergeClosePoints))
        {
            mergeCloseDistanceMeters = Mathf.Clamp(
                EditorGUILayout.FloatField("Merge distance (m)", mergeCloseDistanceMeters),
                0.01f,
                1000f);
        }

        rejectIfNotVisibleFromAdjacent = EditorGUILayout.ToggleLeft("Reject probes not visible from adjacent", rejectIfNotVisibleFromAdjacent);
        using (new EditorGUI.DisabledScope(!rejectIfNotVisibleFromAdjacent))
        {
            adjacentVisibilityRayEpsilonMeters = Mathf.Clamp(
                EditorGUILayout.FloatField("Ray start epsilon (m)", adjacentVisibilityRayEpsilonMeters),
                0f,
                1f);

            adjacentVisibilitySampleCount = Mathf.Clamp(
                EditorGUILayout.IntField("Sample neighbor probes", adjacentVisibilitySampleCount),
                1,
                256);

            adjacentVisibilityMinVisibleRays = Mathf.Clamp(
                EditorGUILayout.IntField("Min rays that must hit sphere", adjacentVisibilityMinVisibleRays),
                1,
                256);

            adjacentVisibilityRadiusMultiplier = Mathf.Clamp(
                EditorGUILayout.FloatField("Neighbor radius multiplier", adjacentVisibilityRadiusMultiplier),
                0.5f,
                5f);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Limits", EditorStyles.boldLabel);
        maxNodesVisited = Mathf.Clamp(EditorGUILayout.IntField("Max Nodes Visited", maxNodesVisited), 1, 10000000);
        maxProbes = Mathf.Clamp(EditorGUILayout.IntField("Max Probes", maxProbes), 1, 500000);

        EditorGUILayout.Space(10);
        using (new EditorGUI.DisabledScope(group == null))
        {
            if (GUILayout.Button("Clear All Probes in Group"))
            {
                ClearProbes();
            }
        }

        using (new EditorGUI.DisabledScope(group == null || terrain == null))
        {
            if (GUILayout.Button("Generate Probes (BFS Walk)", GUILayout.Height(32)))
            {
                GenerateProbesBfs();
            }
        }

        if (group == null)
            EditorGUILayout.HelpBox("Assign a LightProbeGroup.", MessageType.Warning);
        if (terrain == null)
            EditorGUILayout.HelpBox("Assign a Terrain (used for bounds + padding).", MessageType.Warning);

        EditorGUILayout.HelpBox(
            "Rules enforced:\n" +
            "- Must be within padded terrain XZ bounds\n" +
            "- Must not be above highest scene collider bound\n" +
            "- Must not overlap / be inside geometry\n" +
            "- Must not be void (escape to boundary cube on side/bottom)",
            MessageType.Info);
    }

    private void ClearProbes()
    {
        if (group == null) return;
        Undo.RegisterCompleteObjectUndo(group, "Clear Light Probes");
        group.probePositions = Array.Empty<Vector3>();
        EditorUtility.SetDirty(group);
        MarkSceneDirty(group.gameObject.scene);
    }

    private void GenerateProbesBfs()
    {
        if (group == null || terrain == null || terrain.terrainData == null)
            return;

        EnsureTestSphere();
        if (voidDirections == null || voidDirections.Count == 0)
            RebuildVoidDirections();

        Bounds terrainBounds = GetTerrainBoundsWithPadding(terrain, terrainBoundsPaddingPercent);
        float sceneMaxY = ComputeSceneMaxY(terrainBounds.max.y);
        Vector3 originWorld = group.transform.position;

        // Build a temporary boundary volume so "ray misses" can be classified reliably.
        // Rays that reach the boundary on side/bottom faces are treated as void.
        // Rays that reach the boundary on the top face are ignored (sky).
        CreateVoidBoundary(terrainBounds);

        float stepX = horizontalStepMeters;
        float stepY = verticalStepMeters;
        float stepZ = horizontalStepMeters;

        var visited = new HashSet<Vector3Int>(Math.Min(maxNodesVisited, 10000000));
        var q = new Queue<Vector3Int>(Math.Min(maxNodesVisited, 4096));
        var probeKeys = new List<Vector3Int>(Math.Min(maxProbes, 8192));

        var startKey = WorldToGrid(originWorld, originWorld, stepX, stepY, stepZ);
        visited.Add(startKey);
        q.Enqueue(startKey);

        int nodesProcessed = 0;
        int rejectedVoid = 0;
        int rejectedBounds = 0;
        int rejectedHeight = 0;
        int rejectedOverlap = 0;

        try
        {
            while (q.Count > 0)
            {
                if (nodesProcessed >= maxNodesVisited)
                    break;
                if (probeKeys.Count >= maxProbes)
                    break;

                nodesProcessed++;
                if ((nodesProcessed & 0xFF) == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Auto Light Probes (BFS)",
                        $"Visited {nodesProcessed} nodes, placed {probeKeys.Count}",
                        maxNodesVisited > 0 ? (float)nodesProcessed / maxNodesVisited : 0f))
                    {
                        return;
                    }
                }

                var key = q.Dequeue();
                Vector3 p = GridToWorld(originWorld, key, stepX, stepY, stepZ);

                // Expand neighbors regardless of whether this node becomes a probe.
                // This prevents a slightly-bad seed position from halting the entire walk.
                EnqueueNeighbor(key + new Vector3Int(1, 0, 0));
                EnqueueNeighbor(key + new Vector3Int(-1, 0, 0));
                EnqueueNeighbor(key + new Vector3Int(0, 0, 1));
                EnqueueNeighbor(key + new Vector3Int(0, 0, -1));
                if (includeVerticalExpansion)
                {
                    EnqueueNeighbor(key + new Vector3Int(0, 1, 0));
                    EnqueueNeighbor(key + new Vector3Int(0, -1, 0));
                }

                if (p.y > sceneMaxY)
                {
                    rejectedHeight++;
                    continue;
                }

                if (!IsWithinTerrainXZ(terrainBounds, p))
                {
                    rejectedBounds++;
                    continue;
                }

                // Not-inside-mesh + clearance checks (physics/colliders).
                if (IsOverlappingGeometry(p, overlapRadiusMeters) ||
                    (minSurfaceClearanceMeters > 0f && IsOverlappingGeometry(p, minSurfaceClearanceMeters)))
                {
                    rejectedOverlap++;
                    continue;
                }

                if (IsMostlyVoid(p))
                {
                    rejectedVoid++;
                    continue;
                }

                probeKeys.Add(key);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            DestroyVoidBoundary();
        }

        int removedByStackSimplify = 0;
        if (simplifyVerticalStacks)
        {
            removedByStackSimplify = SimplifyVerticalStacks(probeKeys, simplifyMinConsecutive, simplifyTargetCount);
        }

        int adjustedToTerrain = 0;
        Dictionary<Vector3Int, Vector3> worldOverrides = null;
        HashSet<Vector3Int> terrainSnappedKeys = null;
        if (snapLowestInStackToTerrain)
        {
            adjustedToTerrain = SnapLowestInEachStackToTerrain(
                probeKeys,
                originWorld,
                stepX,
                stepY,
                stepZ,
                terrain,
                geometryMask,
                snapLowestToTerrainClearanceMeters,
            out worldOverrides,
            out terrainSnappedKeys);
        }

        int removedByAdjacentVisibility = 0;
        if (rejectIfNotVisibleFromAdjacent)
        {
            float baseRadius = Mathf.Max(stepX, Mathf.Max(stepY, stepZ));
            float neighborRadius = baseRadius * Mathf.Max(0.0001f, adjacentVisibilityRadiusMultiplier);
            removedByAdjacentVisibility = RejectNotVisibleFromAdjacent(
                probeKeys,
                originWorld,
                stepX,
                stepY,
                stepZ,
                geometryMask,
                adjacentVisibilityRayEpsilonMeters,
                neighborRadius,
                adjacentVisibilitySampleCount,
                adjacentVisibilityMinVisibleRays,
                worldOverrides);
        }

        int addedAboveSnapped = 0;
        if (snapLowestInStackToTerrain && addAboveSnappedTerrainPoints && terrainSnappedKeys != null && terrainSnappedKeys.Count > 0)
        {
            if (worldOverrides == null)
                worldOverrides = new Dictionary<Vector3Int, Vector3>();

            addedAboveSnapped = AddAboveSnappedTerrainPoints(
                probeKeys,
                worldOverrides,
                terrainSnappedKeys,
                stepY,
                aboveSnappedExtraHeightMeters,
                overlapRadiusMeters,
                minSurfaceClearanceMeters);
        }

        int removedByMerge = 0;
        if (mergeClosePoints && mergeCloseDistanceMeters > 0f)
        {
            removedByMerge = MergePointsWithinDistance(
                probeKeys,
                originWorld,
                stepX,
                stepY,
                stepZ,
                worldOverrides,
                mergeCloseDistanceMeters);
        }

        Undo.RegisterCompleteObjectUndo(group, "Generate Light Probes");
        var local = new Vector3[probeKeys.Count];
        for (int i = 0; i < probeKeys.Count; i++)
        {
            Vector3Int key = probeKeys[i];
            Vector3 p = (worldOverrides != null && worldOverrides.TryGetValue(key, out var overrideWorld))
                ? overrideWorld
                : GridToWorld(originWorld, key, stepX, stepY, stepZ);
            local[i] = group.transform.InverseTransformPoint(p);
        }

        group.probePositions = local;
        EditorUtility.SetDirty(group);
        MarkSceneDirty(group.gameObject.scene);

        Debug.Log(
            $"[AutoLightProbe] BFS visited {nodesProcessed}, placed {probeKeys.Count}" +
            (removedByStackSimplify > 0 ? $" (stack-simplified -{removedByStackSimplify})" : "") +
            (adjustedToTerrain > 0 ? $" (terrain-snapped {adjustedToTerrain})" : "") +
            (removedByAdjacentVisibility > 0 ? $" (adjacent-visibility -{removedByAdjacentVisibility})" : "") +
            (addedAboveSnapped > 0 ? $" (above-snapped +{addedAboveSnapped})" : "") +
            (removedByMerge > 0 ? $" (merged -{removedByMerge})" : "") +
            $". Rejected: void={rejectedVoid}, bounds={rejectedBounds}, height={rejectedHeight}, overlap={rejectedOverlap}.");

        void EnqueueNeighbor(Vector3Int neighbor)
        {
            if (visited.Count >= maxNodesVisited) return;
            if (visited.Add(neighbor))
                q.Enqueue(neighbor);
        }
    }

    private bool IsMostlyVoid(Vector3 p)
    {
        if (voidDirections == null || voidDirections.Count == 0)
            return true;

        int boundaryLayerMask = 1 << IgnoreRaycastLayer;
        int geometryMaskNoBoundary = geometryMask.value & ~boundaryLayerMask;

        for (int i = 0; i < voidDirections.Count; i++)
        {
            Vector3 dir = voidDirections[i];

            // User rule: only downward/horizontal rays should reject as void.
            // Upward rays (sky) should not cause rejection.
            if (excludeUpwardRays && dir.y > 0f)
                continue;

            bool hitLocalGeometry = Physics.Raycast(p, dir, voidRayDistanceMeters, geometryMaskNoBoundary, QueryTriggerInteraction.Ignore);
            if (hitLocalGeometry)
            {
                continue;
            }

            // No nearby geometry. If the ray escapes to the boundary cube on ANY side/bottom face,
            // this point is void. Only the top face is allowed (sky).
            if (voidBoundary != null)
            {
                if (Physics.Raycast(p, dir, out RaycastHit boundaryHit, Mathf.Infinity, boundaryLayerMask, QueryTriggerInteraction.Ignore))
                {
                    // Robust "top face" detection: ignore if it hits near the boundary's max-Y face.
                    float topY = voidBoundary.bounds.max.y;
                    if (boundaryHit.point.y >= topY - 0.01f)
                        continue;

                    return true;
                }

                // Should not happen, but if it does, treat as void.
                return true;
            }

            // No boundary available: treat as void.
            return true;
        }

        // No ray escaped to a side/bottom boundary face.
        return false;
    }

    private static int SimplifyVerticalStacks(List<Vector3Int> keys, int minConsecutive, int targetCount)
    {
        if (keys == null || keys.Count == 0)
            return 0;

        minConsecutive = Mathf.Max(2, minConsecutive);
        targetCount = Mathf.Max(1, targetCount);

        var keep = new HashSet<Vector3Int>(keys);
        var columns = new Dictionary<Vector2Int, List<int>>();

        // Group by XZ; collect Y indices.
        foreach (var k in keep)
        {
            var col = new Vector2Int(k.x, k.z);
            if (!columns.TryGetValue(col, out var ys))
            {
                ys = new List<int>();
                columns.Add(col, ys);
            }
            ys.Add(k.y);
        }

        foreach (var kvp in columns)
        {
            Vector2Int col = kvp.Key;
            List<int> ys = kvp.Value;
            ys.Sort();

            int idx = 0;
            while (idx < ys.Count)
            {
                int start = idx;
                int end = idx;
                while (end + 1 < ys.Count && ys[end + 1] == ys[end] + 1)
                    end++;

                int runLen = end - start + 1;
                if (runLen >= minConsecutive && targetCount < runLen)
                {
                    var selected = new HashSet<int>();
                    if (targetCount == 1)
                    {
                        selected.Add(ys[start + (runLen / 2)]);
                    }
                    else
                    {
                        for (int i = 0; i < targetCount; i++)
                        {
                            float t = i / (float)(targetCount - 1);
                            int pick = Mathf.RoundToInt(t * (runLen - 1));
                            selected.Add(ys[start + pick]);
                        }
                    }

                    for (int yIndex = start; yIndex <= end; yIndex++)
                    {
                        int y = ys[yIndex];
                        if (selected.Contains(y))
                            continue;
                        keep.Remove(new Vector3Int(col.x, y, col.y));
                    }
                }

                idx = end + 1;
            }
        }

        int before = keys.Count;
        int w = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            if (!keep.Contains(k))
                continue;
            keys[w++] = k;
        }
        if (w < keys.Count)
            keys.RemoveRange(w, keys.Count - w);

        return before - keys.Count;
    }

    private static int SnapLowestInEachStackToTerrain(
        List<Vector3Int> keys,
        Vector3 originWorld,
        float stepX,
        float stepY,
        float stepZ,
        Terrain terrain,
        LayerMask geometryMask,
        float clearanceMeters,
        out Dictionary<Vector3Int, Vector3> overridesWorld,
        out HashSet<Vector3Int> snappedKeys)
    {
        overridesWorld = null;
        snappedKeys = null;
        if (keys == null || keys.Count == 0)
            return 0;
        if (terrain == null)
            return 0;

        var terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
            return 0;

        int boundaryLayerMask = 1 << IgnoreRaycastLayer;
        int geometryMaskNoBoundary = geometryMask.value & ~boundaryLayerMask;

        // Group by XZ; collect Y indices.
        var columns = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            var col = new Vector2Int(k.x, k.z);
            if (!columns.TryGetValue(col, out var ys))
            {
                ys = new List<int>();
                columns.Add(col, ys);
            }
            ys.Add(k.y);
        }

        int adjusted = 0;
        overridesWorld = new Dictionary<Vector3Int, Vector3>();
        snappedKeys = new HashSet<Vector3Int>();

        foreach (var kvp in columns)
        {
            Vector2Int col = kvp.Key;
            List<int> ys = kvp.Value;
            ys.Sort();

            int idx = 0;
            while (idx < ys.Count)
            {
                int start = idx;
                int end = idx;
                while (end + 1 < ys.Count && ys[end + 1] == ys[end] + 1)
                    end++;

                int lowestY = ys[start];
                var lowestKey = new Vector3Int(col.x, lowestY, col.y);

                Vector3 p = GridToWorld(originWorld, lowestKey, stepX, stepY, stepZ);
                Vector3 rayOrigin = p + Vector3.up * 0.02f;

                if (Physics.Raycast(
                        rayOrigin,
                        Vector3.down,
                        out RaycastHit hit,
                        Mathf.Infinity,
                        geometryMaskNoBoundary,
                        QueryTriggerInteraction.Ignore))
                {
                    // Only snap when the first hit is the selected Terrain (not a mesh).
                    if (ReferenceEquals(hit.collider, terrainCollider))
                    {
                        overridesWorld[lowestKey] = new Vector3(p.x, hit.point.y + Mathf.Max(0f, clearanceMeters), p.z);
                        adjusted++;
                        snappedKeys.Add(lowestKey);
                    }
                }

                idx = end + 1;
            }
        }

        if (adjusted == 0)
        {
            overridesWorld = null;
            snappedKeys = null;
        }

        return adjusted;
    }

    private int AddAboveSnappedTerrainPoints(
        List<Vector3Int> keys,
        Dictionary<Vector3Int, Vector3> worldOverrides,
        HashSet<Vector3Int> snappedKeys,
        float stepY,
        float extraHeightMeters,
        float overlapRadius,
        float clearanceRadius)
    {
        if (keys == null || keys.Count == 0) return 0;
        if (worldOverrides == null) return 0;
        if (snappedKeys == null || snappedKeys.Count == 0) return 0;

        float dy = Mathf.Max(0.0001f, stepY + Mathf.Max(0f, extraHeightMeters));

        var existing = new HashSet<Vector3Int>(keys);
        int added = 0;

        foreach (var snappedKey in snappedKeys)
        {
            if (!worldOverrides.TryGetValue(snappedKey, out var snappedWorld))
                continue;

            var aboveKey = snappedKey + new Vector3Int(0, 1, 0);
            if (existing.Contains(aboveKey))
                continue;

            Vector3 aboveWorld = snappedWorld + Vector3.up * dy;

            // Keep it simple: only enforce overlap/clearance here (void was already handled for the snapped base point).
            if (IsOverlappingGeometry(aboveWorld, overlapRadius) ||
                (clearanceRadius > 0f && IsOverlappingGeometry(aboveWorld, clearanceRadius)))
            {
                continue;
            }

            keys.Add(aboveKey);
            existing.Add(aboveKey);
            worldOverrides[aboveKey] = aboveWorld;
            added++;
        }

        return added;
    }

    private static int MergePointsWithinDistance(
        List<Vector3Int> keys,
        Vector3 originWorld,
        float stepX,
        float stepY,
        float stepZ,
        Dictionary<Vector3Int, Vector3> worldOverrides,
        float mergeDistanceMeters)
    {
        if (keys == null || keys.Count <= 1)
            return 0;

        float r = Mathf.Max(0.0001f, mergeDistanceMeters);
        float r2 = r * r;
        float cell = r;

        var keptWorld = new List<Vector3>(keys.Count);
        var keptKeys = new List<Vector3Int>(keys.Count);
        var grid = new Dictionary<Vector3Int, List<int>>(keys.Count);

        int before = keys.Count;
        int w = 0;

        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            Vector3 p = (worldOverrides != null && worldOverrides.TryGetValue(k, out var o))
                ? o
                : GridToWorld(originWorld, k, stepX, stepY, stepZ);

            var cellKey = new Vector3Int(
                Mathf.FloorToInt(p.x / cell),
                Mathf.FloorToInt(p.y / cell),
                Mathf.FloorToInt(p.z / cell));

            bool tooClose = false;
            for (int cx = -1; cx <= 1 && !tooClose; cx++)
            for (int cy = -1; cy <= 1 && !tooClose; cy++)
            for (int cz = -1; cz <= 1 && !tooClose; cz++)
            {
                var neighborCell = new Vector3Int(cellKey.x + cx, cellKey.y + cy, cellKey.z + cz);
                if (!grid.TryGetValue(neighborCell, out var idxs))
                    continue;
                for (int ii = 0; ii < idxs.Count; ii++)
                {
                    int keptIdx = idxs[ii];
                    if ((keptWorld[keptIdx] - p).sqrMagnitude <= r2)
                    {
                        tooClose = true;
                        break;
                    }
                }
            }

            if (tooClose)
            {
                if (worldOverrides != null)
                    worldOverrides.Remove(k);
                continue;
            }

            // Keep
            keys[w++] = k;
            int newIdx = keptWorld.Count;
            keptWorld.Add(p);
            keptKeys.Add(k);

            if (!grid.TryGetValue(cellKey, out var list))
            {
                list = new List<int>();
                grid.Add(cellKey, list);
            }
            list.Add(newIdx);
        }

        if (w < keys.Count)
            keys.RemoveRange(w, keys.Count - w);

        return before - keys.Count;
    }

    private static int RejectNotVisibleFromAdjacent(
        List<Vector3Int> keys,
        Vector3 originWorld,
        float stepX,
        float stepY,
        float stepZ,
        LayerMask geometryMask,
        float rayEpsilonMeters,
        float neighborRadiusMeters,
        int maxNeighborSamples,
        int minVisibleRays,
        Dictionary<Vector3Int, Vector3> worldOverrides)
    {
        if (keys == null || keys.Count == 0)
            return 0;

        neighborRadiusMeters = Mathf.Max(0.0001f, neighborRadiusMeters);
        maxNeighborSamples = Mathf.Clamp(maxNeighborSamples, 1, 2048);
        minVisibleRays = Mathf.Clamp(minVisibleRays, 1, 2048);

        // Precompute world positions (after overrides) once.
        int nKeys = keys.Count;
        var worldPos = new Vector3[nKeys];
        for (int i = 0; i < nKeys; i++)
        {
            Vector3Int k = keys[i];
            worldPos[i] = (worldOverrides != null && worldOverrides.TryGetValue(k, out var o))
                ? o
                : GridToWorld(originWorld, k, stepX, stepY, stepZ);
        }

        // Spatial hash to find nearby probes within radius.
        float cellSize = neighborRadiusMeters;
        var cells = new Dictionary<Vector3Int, List<int>>(nKeys);
        for (int i = 0; i < nKeys; i++)
        {
            Vector3 p = worldPos[i];
            var cell = new Vector3Int(
                Mathf.FloorToInt(p.x / cellSize),
                Mathf.FloorToInt(p.y / cellSize),
                Mathf.FloorToInt(p.z / cellSize));
            if (!cells.TryGetValue(cell, out var list))
            {
                list = new List<int>();
                cells.Add(cell, list);
            }
            list.Add(i);
        }

        // Create one temporary standard Sphere primitive (scale 0.37) and move it to each probe.
        // This matches the requested behavior while staying performant.
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "__AutoLightProbeAdjacentSphere";
        sphere.hideFlags = HideFlags.HideAndDontSave;
        sphere.layer = 0; // Default
        sphere.transform.localScale = Vector3.one * 0.37f;

        // Keep collider for ray hits; disable renderer so it doesn't visually spam the scene.
        var renderer = sphere.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = false;
        var sphereCollider = sphere.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            UnityEngine.Object.DestroyImmediate(sphere);
            return 0;
        }

        // Exclude the boundary cube layer from visibility checks.
        int boundaryLayerMask = 1 << IgnoreRaycastLayer;
        int geometryMaskNoBoundary = geometryMask.value & ~boundaryLayerMask;
        int defaultLayerMask = 1 << 0;
        int rayMask = geometryMaskNoBoundary | defaultLayerMask;

        rayEpsilonMeters = Mathf.Clamp(rayEpsilonMeters, 0f, 1f);
        const float endSlack = 0.05f;
        float r2 = neighborRadiusMeters * neighborRadiusMeters;

        var candidateIdx = new List<int>(256);

        int before = keys.Count;
        int w = 0;

        try
        {
            for (int i = 0; i < keys.Count; i++)
            {
                Vector3Int key = keys[i];
                Vector3 p = worldPos[i];

                sphere.transform.position = p;

                candidateIdx.Clear();

                // Gather neighbor probes within radius (excluding self).
                var centerCell = new Vector3Int(
                    Mathf.FloorToInt(p.x / cellSize),
                    Mathf.FloorToInt(p.y / cellSize),
                    Mathf.FloorToInt(p.z / cellSize));

                for (int cx = -1; cx <= 1; cx++)
                for (int cy = -1; cy <= 1; cy++)
                for (int cz = -1; cz <= 1; cz++)
                {
                    var c = new Vector3Int(centerCell.x + cx, centerCell.y + cy, centerCell.z + cz);
                    if (!cells.TryGetValue(c, out var list))
                        continue;
                    for (int li = 0; li < list.Count; li++)
                    {
                        int j = list[li];
                        if (j == i) continue;
                        Vector3 d = worldPos[j] - p;
                        if (d.sqrMagnitude <= r2)
                            candidateIdx.Add(j);
                    }
                }

                if (candidateIdx.Count == 0)
                    continue;

                // Prefer closer candidates; sample the closest N.
                candidateIdx.Sort((a, b) =>
                {
                    float da = (worldPos[a] - p).sqrMagnitude;
                    float db = (worldPos[b] - p).sqrMagnitude;
                    return da.CompareTo(db);
                });

                int sampleCount = Mathf.Min(maxNeighborSamples, candidateIdx.Count);
                int visibleHits = 0;
                for (int s = 0; s < sampleCount; s++)
                {
                    int j = candidateIdx[s];
                    Vector3 n = worldPos[j];

                    Vector3 delta = p - n;
                    float dist = delta.magnitude;
                    if (dist <= 0.0001f)
                    {
                        visibleHits++;
                        if (visibleHits >= minVisibleRays)
                            break;
                        continue;
                    }

                    Vector3 dir = delta / dist;
                    Vector3 start = n + dir * rayEpsilonMeters;
                    float maxDist = dist + endSlack;

                    if (Physics.Raycast(start, dir, out RaycastHit hit, maxDist, rayMask, QueryTriggerInteraction.Ignore))
                    {
                        if (ReferenceEquals(hit.collider, sphereCollider))
                        {
                            visibleHits++;
                            if (visibleHits >= minVisibleRays)
                                break;
                        }
                    }
                }

                bool reject = visibleHits < minVisibleRays;
                if (reject)
                    continue;

                keys[w++] = key;
                continue;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sphere);
        }

        if (w < keys.Count)
            keys.RemoveRange(w, keys.Count - w);

        return before - keys.Count;
    }

    private bool IsOverlappingGeometry(Vector3 positionWorld, float radius)
    {
        if (radius <= 0f) return false;
        EnsureTestSphere();

        testSphere.radius = Mathf.Max(0.0001f, radius);
        testSphereGo.transform.position = positionWorld;
        testSphereGo.transform.rotation = Quaternion.identity;

        int boundaryLayerMask = 1 << IgnoreRaycastLayer;
        int geometryMaskNoBoundary = geometryMask.value & ~boundaryLayerMask;
        int count = Physics.OverlapSphereNonAlloc(positionWorld, radius, overlapBuffer, geometryMaskNoBoundary, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null) continue;
            if (!c.enabled) continue;
            if (c.isTrigger) continue;
            if (ReferenceEquals(c, testSphere)) continue;

            if (Physics.ComputePenetration(
                testSphere, positionWorld, Quaternion.identity,
                c, c.transform.position, c.transform.rotation,
                out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureTestSphere()
    {
        if (testSphere != null) return;
        testSphereGo = new GameObject("__AutoLightProbeTestSphere") { hideFlags = HideFlags.HideAndDontSave };
        testSphereGo.transform.position = Vector3.zero;
        testSphereGo.transform.rotation = Quaternion.identity;
        testSphere = testSphereGo.AddComponent<SphereCollider>();
        testSphere.isTrigger = true;
        testSphere.radius = Mathf.Max(0.0001f, overlapRadiusMeters);
        testSphere.enabled = true;
    }

    private void RebuildVoidDirections()
    {
        voidDirections = GenerateDirections(voidRayCount, excludeUpwardRays);

        // Always include stable cardinal directions + down.
        EnsureDirection(voidDirections, Vector3.down);
        EnsureDirection(voidDirections, Vector3.forward);
        EnsureDirection(voidDirections, Vector3.back);
        EnsureDirection(voidDirections, Vector3.left);
        EnsureDirection(voidDirections, Vector3.right);

        // If excluding upward rays, strip any upward directions that might have slipped in.
        if (excludeUpwardRays)
        {
            for (int i = voidDirections.Count - 1; i >= 0; i--)
            {
                if (voidDirections[i].y > 0f)
                    voidDirections.RemoveAt(i);
            }
        }

        // Clamp to requested count (while keeping the 5 stable directions at the front).
        if (voidDirections.Count > voidRayCount)
        {
            voidDirections.RemoveRange(voidRayCount, voidDirections.Count - voidRayCount);
        }
    }

    private static void EnsureDirection(List<Vector3> list, Vector3 dir)
    {
        dir.Normalize();
        const float sameDirDot = 0.9995f;
        for (int i = 0; i < list.Count; i++)
        {
            if (Vector3.Dot(list[i], dir) >= sameDirDot)
                return;
        }
        list.Insert(0, dir);
    }

    private void CreateVoidBoundary(Bounds terrainBounds)
    {
        DestroyVoidBoundary();

        // User requested ~10000 cube around the terrain. If the terrain is larger, expand.
        float size = Mathf.Max(10000f, Mathf.Max(terrainBounds.size.x, Mathf.Max(terrainBounds.size.y, terrainBounds.size.z)) + 1000f);

        voidBoundaryGo = new GameObject("__AutoLightProbeVoidBoundary") { hideFlags = HideFlags.HideAndDontSave };
        voidBoundaryGo.layer = IgnoreRaycastLayer;
        voidBoundaryGo.transform.position = terrainBounds.center;
        voidBoundaryGo.transform.rotation = Quaternion.identity;

        voidBoundary = voidBoundaryGo.AddComponent<BoxCollider>();
        voidBoundary.isTrigger = false;
        voidBoundary.center = Vector3.zero;
        voidBoundary.size = new Vector3(size, size, size);
        voidBoundary.enabled = true;
    }

    private void DestroyVoidBoundary()
    {
        if (voidBoundaryGo != null)
        {
            DestroyImmediate(voidBoundaryGo);
            voidBoundaryGo = null;
            voidBoundary = null;
        }
    }

    private static List<Vector3> GenerateDirections(int count, bool excludeUp)
    {
        count = Mathf.Clamp(count, 1, 1024);
        var dirs = new List<Vector3>(count);
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

        int i = 0;
        int attempts = 0;
        while (dirs.Count < count && attempts < count * 20)
        {
            float t = (count == 1) ? 0.5f : (i / (float)(count - 1));
            float y = 1f - 2f * t;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = goldenAngle * i;
            float x = Mathf.Cos(theta) * r;
            float z = Mathf.Sin(theta) * r;
            var v = new Vector3(x, y, z);

            if (!excludeUp || v.y <= 0f)
                dirs.Add(v.normalized);

            i++;
            attempts++;
        }

        return dirs;
    }

    private static Bounds GetTerrainBoundsWithPadding(Terrain t, float paddingPercent)
    {
        var td = t.terrainData;
        Vector3 terrainPos = t.transform.position;
        Vector3 terrainSize = td.size;

        float padX = terrainSize.x * (Mathf.Clamp(paddingPercent, 0f, 200f) * 0.01f);
        float padZ = terrainSize.z * (Mathf.Clamp(paddingPercent, 0f, 200f) * 0.01f);

        float minX = terrainPos.x - padX;
        float maxX = terrainPos.x + terrainSize.x + padX;
        float minZ = terrainPos.z - padZ;
        float maxZ = terrainPos.z + terrainSize.z + padZ;
        float minY = terrainPos.y;
        float maxY = terrainPos.y + terrainSize.y;

        return new Bounds(
            new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f),
            new Vector3(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxY - minY), Mathf.Max(0.01f, maxZ - minZ)));
    }

    private static bool IsWithinTerrainXZ(Bounds terrainBounds, Vector3 p)
    {
        return p.x >= terrainBounds.min.x && p.x <= terrainBounds.max.x &&
               p.z >= terrainBounds.min.z && p.z <= terrainBounds.max.z;
    }

    private static Vector3Int WorldToGrid(Vector3 originWorld, Vector3 p, float stepX, float stepY, float stepZ)
    {
        Vector3 d = p - originWorld;
        return new Vector3Int(
            Mathf.RoundToInt(d.x / Mathf.Max(0.0001f, stepX)),
            Mathf.RoundToInt(d.y / Mathf.Max(0.0001f, stepY)),
            Mathf.RoundToInt(d.z / Mathf.Max(0.0001f, stepZ)));
    }

    private static Vector3 GridToWorld(Vector3 originWorld, Vector3Int grid, float stepX, float stepY, float stepZ)
    {
        return originWorld + new Vector3(grid.x * stepX, grid.y * stepY, grid.z * stepZ);
    }

    private static float ComputeSceneMaxY(float fallback)
    {
        float maxY = float.NegativeInfinity;
        var colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null) continue;
            if (!col.enabled) continue;
            if (col.isTrigger) continue;

            float y = col.bounds.max.y;
            if (y > maxY) maxY = y;
        }

        return float.IsNegativeInfinity(maxY) ? fallback : maxY;
    }

    private static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene)
    {
        if (!scene.IsValid()) return;
        if (!scene.isLoaded) return;
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static LayerMask LayerMaskField(string label, LayerMask selected)
    {
        var layers = new List<string>();
        var layerNumbers = new List<int>();

        for (int i = 0; i < 32; i++)
        {
            string layerName = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(layerName))
            {
                layers.Add(layerName);
                layerNumbers.Add(i);
            }
        }

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            int layerNumber = layerNumbers[i];
            if (((1 << layerNumber) & selected.value) != 0)
                maskWithoutEmpty |= (1 << i);
        }

        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers.ToArray());

        int mask = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if ((maskWithoutEmpty & (1 << i)) != 0)
                mask |= (1 << layerNumbers[i]);
        }

        selected.value = mask;
        return selected;
    }
}

