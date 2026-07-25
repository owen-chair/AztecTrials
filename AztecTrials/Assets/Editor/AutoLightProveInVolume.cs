using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Places LightProbeGroup probes on a grid inside a user-authored non-convex "hull" volume.
/// The hull is intended to be edited with ProBuilder (created via ProBuilder menu item).
/// </summary>
public sealed class AutoLightProveInVolume : EditorWindow
{
    [SerializeField] private LightProbeGroup group;
    [SerializeField] private GameObject hull;

    [Header("Hull placement")]
    [SerializeField] private Vector3 hullOffsetFromGroup = new Vector3(5f, 0f, 0f);
    [SerializeField] private Vector3 initialHullSizeMeters = new Vector3(30f, 15f, 30f);

    [Header("Grid fill")]
    [SerializeField] private float horizontalStepMeters = 3f;
    [SerializeField] private float verticalStepMeters = 2f;
    [SerializeField] private int maxProbes = 50000;
    [SerializeField] private bool offsetGridStartByCollisionRadius = true;

    [Header("Terrain floor")]
    [SerializeField] private bool useTerrainFloor = true;
    [SerializeField] private bool snapLowestLayerToTerrain = true;

    [Header("Void check")]
    [SerializeField] private bool rejectIfInVoid = true;
    [SerializeField, Range(4, 128)] private int voidRayCount = 24;
    [SerializeField] private float voidRayDistanceMeters = 200f;
    [SerializeField] private float voidRayOriginLiftMeters = 0.05f;

    [Header("Inside hull")]
    [SerializeField] private float insideEpsilonMeters = 0.001f;
    [SerializeField, Range(1, 5)] private int insideVoteRays = 3;
    [SerializeField] private float insideHitMergeToleranceMeters = 0.0025f;

    [Header("Reject inside other mesh colliders")]
    [SerializeField] private bool rejectIfInsideOtherMeshColliders = true;

    [Header("Collision / skip")]
    [SerializeField] private LayerMask geometryMask = ~0;
    [SerializeField] private float overlapRadiusMeters = 0.15f;
    [SerializeField] private float minSurfaceClearanceMeters = 0.25f;

    private const int IgnoreRaycastLayer = 2;
    private const string HullName = "__LightProbeHull";

    private GameObject testSphereGo;
    private SphereCollider testSphere;
    private readonly Collider[] overlapBuffer = new Collider[256];
    private readonly RaycastHit[] raycastBuffer = new RaycastHit[32];

    [MenuItem("Tools/Lighting/Light Probes In Volume (Hull)...")]
    public static void ShowWindow()
    {
        var window = GetWindow<AutoLightProveInVolume>(true, "Light Probes In Volume", true);
        window.minSize = new Vector2(520, 520);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureTestSphere();
        if (hull != null)
            hull.SetActive(true);
    }

    private void OnDisable()
    {
        if (hull != null)
            hull.SetActive(false);

        if (testSphereGo != null)
        {
            DestroyImmediate(testSphereGo);
            testSphereGo = null;
            testSphere = null;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Author a ProBuilder hull, then grid-fill probes inside it.", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        group = (LightProbeGroup)EditorGUILayout.ObjectField("Light Probe Group", group, typeof(LightProbeGroup), true);
        hull = (GameObject)EditorGUILayout.ObjectField("Hull (ProBuilder object)", hull, typeof(GameObject), true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Hull placement", EditorStyles.boldLabel);
        hullOffsetFromGroup = EditorGUILayout.Vector3Field("Offset from group", hullOffsetFromGroup);
        initialHullSizeMeters = EditorGUILayout.Vector3Field("Initial size (m)", initialHullSizeMeters);

        using (new EditorGUI.DisabledScope(group == null))
        {
            if (GUILayout.Button("Create Hull Next To Group (ProBuilder Cube)", GUILayout.Height(24)))
            {
                CreateHullNextToGroup();
            }
        }

        using (new EditorGUI.DisabledScope(hull == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Activate Hull (edit)") && hull != null)
            {
                hull.SetActive(true);
                Selection.activeGameObject = hull;
                EditorGUIUtility.PingObject(hull);
            }
            if (GUILayout.Button("Deactivate Hull") && hull != null)
            {
                hull.SetActive(false);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Grid fill", EditorStyles.boldLabel);
        horizontalStepMeters = Mathf.Max(0.25f, EditorGUILayout.FloatField("Horizontal step (m)", horizontalStepMeters));
        verticalStepMeters = Mathf.Max(0.25f, EditorGUILayout.FloatField("Vertical step (m)", verticalStepMeters));
        maxProbes = Mathf.Clamp(EditorGUILayout.IntField("Max probes", maxProbes), 1, 500000);
        offsetGridStartByCollisionRadius = EditorGUILayout.ToggleLeft("Offset grid start by collision radius", offsetGridStartByCollisionRadius);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Terrain floor", EditorStyles.boldLabel);
        useTerrainFloor = EditorGUILayout.ToggleLeft("Use Terrain floor (keep probes above terrain)", useTerrainFloor);
        using (new EditorGUI.DisabledScope(!useTerrainFloor))
        {
            snapLowestLayerToTerrain = EditorGUILayout.ToggleLeft("Snap lowest layer to terrain floor", snapLowestLayerToTerrain);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Void check", EditorStyles.boldLabel);
        rejectIfInVoid = EditorGUILayout.ToggleLeft("Reject probes that see void (horizontal rays)", rejectIfInVoid);
        using (new EditorGUI.DisabledScope(!rejectIfInVoid))
        {
            voidRayCount = Mathf.Clamp(EditorGUILayout.IntField("Void rays", voidRayCount), 4, 128);
            voidRayDistanceMeters = Mathf.Clamp(EditorGUILayout.FloatField("Void ray distance (m)", voidRayDistanceMeters), 1f, 100000f);
            voidRayOriginLiftMeters = Mathf.Clamp(EditorGUILayout.FloatField("Void ray origin lift (m)", voidRayOriginLiftMeters), 0f, 5f);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Collision / skip", EditorStyles.boldLabel);
        geometryMask = LayerMaskField("Geometry Mask", geometryMask);
        overlapRadiusMeters = Mathf.Clamp(EditorGUILayout.FloatField("Overlap radius (m)", overlapRadiusMeters), 0f, 5f);
        minSurfaceClearanceMeters = Mathf.Clamp(EditorGUILayout.FloatField("Min surface clearance (m)", minSurfaceClearanceMeters), 0f, 5f);

        EditorGUILayout.Space(10);
        using (new EditorGUI.DisabledScope(group == null))
        {
            if (GUILayout.Button("Clear Probes In Group"))
            {
                ClearProbes();
            }
        }

        using (new EditorGUI.DisabledScope(group == null || hull == null))
        {
            if (GUILayout.Button("Generate Probes Inside Hull", GUILayout.Height(32)))
            {
                GenerateProbesInHull();
            }
        }

        if (group == null)
            EditorGUILayout.HelpBox("Assign a LightProbeGroup.", MessageType.Warning);
        if (hull == null)
            EditorGUILayout.HelpBox("Assign a hull GameObject (ideally a ProBuilder mesh). Use the Create button if ProBuilder is installed.", MessageType.Warning);

        EditorGUILayout.HelpBox(
            "Hull notes:\n" +
            "- Hull should be a CLOSED mesh you can edit into a non-convex volume (ProBuilder recommended).\n" +
            "- This tool uses a mesh intersection test (no Physics raycasts) to decide if a grid point is inside.\n" +
            "- Points that overlap geometry (sphere-collider test) are skipped.",
            MessageType.Info);
    }

    private void CreateHullNextToGroup()
    {
        if (group == null)
            return;

        // Prefer reusing an existing hull next to this group.
        if (hull == null)
        {
            Transform parent = group.transform.parent;
            var existing = parent != null ? parent.Find(HullName) : null;
            if (existing != null)
                hull = existing.gameObject;
        }

        if (hull == null)
        {
            // Attempt to create a ProBuilder Cube via menu item.
            // This avoids hard dependencies on ProBuilder APIs (so the project still compiles if ProBuilder is missing).
            var previousSelection = Selection.activeGameObject;

            bool created =
                EditorApplication.ExecuteMenuItem("GameObject/3D Object/ProBuilder Cube") ||
                EditorApplication.ExecuteMenuItem("GameObject/ProBuilder/Cube") ||
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/ProBuilderize") ||
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/New Shape") ||
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/Cube");

            var createdGo = Selection.activeGameObject;
            if (!created || createdGo == null || createdGo == previousSelection)
            {
                EditorUtility.DisplayDialog(
                    "ProBuilder not found",
                    "Could not create a ProBuilder Cube via menu item.\n\n" +
                    "Fix options:\n" +
                    "1) Install/enable ProBuilder (Package Manager).\n" +
                    "2) Manually create a ProBuilder mesh in the scene and assign it to the Hull field.\n",
                    "OK");
                return;
            }

            hull = createdGo;
        }

        Undo.RegisterCompleteObjectUndo(hull.transform, "Position Light Probe Hull");
        hull.name = HullName;
        hull.hideFlags = HideFlags.None;
        hull.layer = IgnoreRaycastLayer;

        hull.transform.SetParent(group.transform.parent, true);
        hull.transform.position = group.transform.position + hullOffsetFromGroup;
        hull.transform.rotation = Quaternion.identity;
        hull.transform.localScale = Vector3.one;

        // Ensure there is a MeshCollider we can raycast against for inside tests.
        var collider = hull.GetComponent<MeshCollider>();
        if (collider == null)
        {
            collider = Undo.AddComponent<MeshCollider>(hull);
        }
        collider.convex = false;
        // Triggers on concave MeshColliders are not supported; keep this non-trigger.
        collider.isTrigger = false;

        // Best-effort initial size (works for most ProBuilder/mesh objects).
        var meshFilter = hull.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            var bounds = meshFilter.sharedMesh.bounds;
            Vector3 bsize = bounds.size;
            float sx = bsize.x <= 0.0001f ? 1f : initialHullSizeMeters.x / bsize.x;
            float sy = bsize.y <= 0.0001f ? 1f : initialHullSizeMeters.y / bsize.y;
            float sz = bsize.z <= 0.0001f ? 1f : initialHullSizeMeters.z / bsize.z;
            hull.transform.localScale = new Vector3(sx, sy, sz);
        }

        hull.SetActive(true);
        Selection.activeGameObject = hull;
        EditorGUIUtility.PingObject(hull);
        MarkSceneDirty(hull.scene);
    }

    private void GenerateProbesInHull()
    {
        if (group == null || hull == null)
            return;

        EnsureTestSphere();

        bool wasActive = hull.activeSelf;
        if (!wasActive)
            hull.SetActive(true);

        try
        {
            // Ensure hull (and any child hulls) are on Ignore Raycast layer.
            // (We don't exclude this layer from geometry checks; this is just to keep hull tidy.)
            hull.layer = IgnoreRaycastLayer;

            if (!TryBuildHullCaches(hull.transform, out var hullCaches, out Bounds b))
            {
                EditorUtility.DisplayDialog(
                    "Hull missing MeshCollider",
                    "The hull (or its children) must have at least one enabled MeshCollider with a sharedMesh.",
                    "OK");
                return;
            }

            List<MeshTriangleCache> otherMeshCaches = null;
            if (rejectIfInsideOtherMeshColliders)
            {
                otherMeshCaches = BuildOtherMeshColliderCaches(hull.transform);
            }

            float sx = horizontalStepMeters;
            float sy = verticalStepMeters;
            float sz = horizontalStepMeters;

            Terrain[] terrains = null;
            bool terrainEnabled = useTerrainFloor;
            if (terrainEnabled)
            {
                terrains = Terrain.activeTerrains;
                if (terrains == null || terrains.Length == 0)
                    terrainEnabled = false;
            }

            int placed = 0;
            int skippedOutside = 0;
            int skippedOverlap = 0;
            int visited = 0;

            // Conservative iteration; user can keep the hull tight.
            float startX = b.min.x;
            float startY = b.min.y;
            float startZ = b.min.z;

            // If the first sampled Y layer is exactly on the hull bottom, the collision sphere will almost always
            // intersect the floor collider and get rejected. With a large vertical step this looks like probes
            // never go near the floor because the next valid layer is at +verticalStepMeters.
            // Lifting the start by the collision radius makes the first layer viable.
            if (offsetGridStartByCollisionRadius)
            {
                float lift = Mathf.Max(0f, Mathf.Max(overlapRadiusMeters, minSurfaceClearanceMeters));
                startY += lift;
            }

            int nx = Mathf.CeilToInt(b.size.x / sx) + 1;
            float ySpan = Mathf.Max(0.0001f, b.max.y - startY);
            int ny = Mathf.CeilToInt(ySpan / sy) + 1;
            int nz = Mathf.CeilToInt(b.size.z / sz) + 1;
            int total = Mathf.Max(1, nx * ny * nz);

            var local = new List<Vector3>(Mathf.Min(maxProbes, 8192));
            var scratchHits = new List<float>(64);

            for (int ix = 0; ix < nx; ix++)
            {
                float x = startX + ix * sx;
                for (int iz = 0; iz < nz; iz++)
                {
                    float z = startZ + iz * sz;
                    for (int iy = 0; iy < ny; iy++)
                    {
                        if (placed >= maxProbes)
                            goto Done;

                        float y = startY + iy * sy;
                        visited++;

                        if ((visited & 0x7FF) == 0)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar(
                                    "Light Probes In Hull",
                                    $"Scanned {visited}/{total}, placed {placed}",
                                    (float)visited / total))
                            {
                                goto Done;
                            }
                        }

                        // Optional: terrain floor clamp. If this sample would be below the terrain surface at (x,z),
                        // (a) snap the lowest sampled layer onto the terrain (plus collision/clearance lift), and
                        // (b) skip any deeper layers so we don't create duplicates.
                        if (terrainEnabled)
                        {
                            Vector3 pXZ = new Vector3(x, 0f, z);
                            if (TryGetTerrainFloorY(pXZ, terrains, out float terrainY))
                            {
                                float lift = Mathf.Max(0f, Mathf.Max(overlapRadiusMeters, minSurfaceClearanceMeters));
                                float minY = terrainY + lift;
                                if (y < minY)
                                {
                                    if (snapLowestLayerToTerrain && iy == 0)
                                    {
                                        y = minY;
                                    }
                                    else
                                    {
                                        // Would end up below terrain; skip.
                                        continue;
                                    }
                                }
                            }
                        }

                        Vector3 p = new Vector3(x, y, z);

                        // Concave MeshColliders do not support ClosestPoint for inside testing.
                        // Use a pure mesh intersection test (no Physics raycasts).
                        if (!IsPointInsideAnyMesh(p, hullCaches, insideEpsilonMeters, insideVoteRays, insideHitMergeToleranceMeters, scratchHits))
                        {
                            skippedOutside++;
                            continue;
                        }

                        // "Create sphere; if it collides with anything (except hull), NO PROBE."
                        if (IsOverlappingGeometry(p, overlapRadiusMeters, hull.transform) ||
                            (minSurfaceClearanceMeters > 0f && IsOverlappingGeometry(p, minSurfaceClearanceMeters, hull.transform)))
                        {
                            skippedOverlap++;
                            continue;
                        }

                        // Optional: reject probes that have an open horizontal ray to nothing (void).
                        if (rejectIfInVoid && IsInVoid(p, hull.transform))
                        {
                            skippedOutside++;
                            continue;
                        }

                        // Optional: reject points fully inside other concave MeshColliders (triangle meshes are surfaces;
                        // overlap tests can miss deep-inside cases).
                        if (rejectIfInsideOtherMeshColliders && otherMeshCaches != null && IsInsideAnyMesh(p, otherMeshCaches, insideEpsilonMeters, insideVoteRays, insideHitMergeToleranceMeters, scratchHits))
                        {
                            skippedOverlap++;
                            continue;
                        }

                        local.Add(group.transform.InverseTransformPoint(p));
                        placed++;
                    }
                }
            }

        Done:
            EditorUtility.ClearProgressBar();

            Undo.RegisterCompleteObjectUndo(group, "Generate Light Probes In Hull");
            group.probePositions = local.ToArray();
            EditorUtility.SetDirty(group);
            MarkSceneDirty(group.gameObject.scene);

            Debug.Log($"[AutoLightProveInVolume] Placed {placed} probes. Skipped: outside={skippedOutside}, overlap={skippedOverlap}.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!wasActive)
                hull.SetActive(false);
        }
    }

    private static bool IsPointInsideMesh(
        Vector3 p,
        MeshTriangleCache cache,
        float epsilonMeters,
        int voteRays,
        float mergeToleranceMeters,
        List<float> scratchHits)
    {
        float e = Mathf.Max(0.000001f, epsilonMeters);

        voteRays = Mathf.Clamp(voteRays, 1, 5);
        float mergeTol = Mathf.Max(1e-6f, mergeToleranceMeters);

        if (!cache.WorldBounds.Contains(p))
            return false;

        // Jitter to reduce edge/corner ambiguity.
        int h = p.GetHashCode();
        Vector3 jitter = new Vector3(
            (((h) & 1023) / 1023f - 0.5f) * e * 4f,
            (((h >> 10) & 1023) / 1023f - 0.5f) * e * 4f,
            (((h >> 20) & 1023) / 1023f - 0.5f) * e * 4f);
        Vector3 origin = p + jitter;

        // Multi-direction parity vote to reduce edge/vertex ambiguity and shared-edge double-counting.
        // Directions are fixed; the jitter makes them effectively unique per point.
        int insideVotes = 0;
        int rays = voteRays;

        // 3 orthogonal directions are usually enough.
        if (rays >= 1)
            insideVotes += (cache.CountRayIntersectionsUnique(origin, Vector3.right, mergeTol, scratchHits) & 1);
        if (rays >= 2)
            insideVotes += (cache.CountRayIntersectionsUnique(origin, Vector3.up, mergeTol, scratchHits) & 1);
        if (rays >= 3)
            insideVotes += (cache.CountRayIntersectionsUnique(origin, Vector3.forward, mergeTol, scratchHits) & 1);

        // Optional extras (diagonals) if you bump voteRays above 3.
        if (rays >= 4)
            insideVotes += (cache.CountRayIntersectionsUnique(origin, (Vector3.right + Vector3.up + Vector3.forward).normalized, mergeTol, scratchHits) & 1);
        if (rays >= 5)
            insideVotes += (cache.CountRayIntersectionsUnique(origin, (Vector3.right + Vector3.up - Vector3.forward).normalized, mergeTol, scratchHits) & 1);

        // Majority vote
        return insideVotes > (rays / 2);
    }

    private static bool IsInsideAnyMesh(
        Vector3 p,
        List<MeshTriangleCache> caches,
        float epsilonMeters,
        int voteRays,
        float mergeToleranceMeters,
        List<float> scratchHits)
    {
        for (int i = 0; i < caches.Count; i++)
        {
            var c = caches[i];
            if (!c.WorldBounds.Contains(p))
                continue;
            if (IsPointInsideMesh(p, c, epsilonMeters, voteRays, mergeToleranceMeters, scratchHits))
                return true;
        }
        return false;
    }

    private static bool IsPointInsideAnyMesh(
        Vector3 p,
        List<MeshTriangleCache> caches,
        float epsilonMeters,
        int voteRays,
        float mergeToleranceMeters,
        List<float> scratchHits)
    {
        if (caches == null || caches.Count == 0)
            return false;

        for (int i = 0; i < caches.Count; i++)
        {
            var c = caches[i];
            if (!c.WorldBounds.Contains(p))
                continue;
            if (IsPointInsideMesh(p, c, epsilonMeters, voteRays, mergeToleranceMeters, scratchHits))
                return true;
        }

        return false;
    }

    private static bool TryBuildHullCaches(Transform hullRoot, out List<MeshTriangleCache> caches, out Bounds unionBounds)
    {
        caches = null;
        unionBounds = default;

        if (hullRoot == null)
            return false;

        var meshColliders = hullRoot.GetComponentsInChildren<MeshCollider>(true);
        if (meshColliders == null || meshColliders.Length == 0)
            return false;

        var list = new List<MeshTriangleCache>(meshColliders.Length);
        bool hasBounds = false;

        for (int i = 0; i < meshColliders.Length; i++)
        {
            var mc = meshColliders[i];
            if (mc == null) continue;
            if (!mc.enabled) continue;
            if (!mc.gameObject.activeInHierarchy) continue;

            // Triggers on concave MeshColliders are not supported; keep this non-trigger.
            mc.convex = false;
            mc.isTrigger = false;

            // ProBuilder edits can update the MeshFilter mesh without updating MeshCollider.sharedMesh.
            var mf = mc.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mc.sharedMesh != mf.sharedMesh)
                mc.sharedMesh = mf.sharedMesh;

            if (mc.sharedMesh == null)
                continue;

            // Ensure hull hierarchy stays on Ignore Raycast layer.
            mc.gameObject.layer = IgnoreRaycastLayer;

            var cache = new MeshTriangleCache(mc.sharedMesh, mc.transform);
            list.Add(cache);

            if (!hasBounds)
            {
                unionBounds = cache.WorldBounds;
                hasBounds = true;
            }
            else
            {
                unionBounds.Encapsulate(cache.WorldBounds.min);
                unionBounds.Encapsulate(cache.WorldBounds.max);
            }
        }

        if (!hasBounds || list.Count == 0)
            return false;

        caches = list;
        return true;
    }

    private static List<MeshTriangleCache> BuildOtherMeshColliderCaches(Transform hullRoot)
    {
        var colliders = UnityEngine.Object.FindObjectsOfType<MeshCollider>();
        var caches = new List<MeshTriangleCache>(Mathf.Min(64, colliders.Length));

        for (int i = 0; i < colliders.Length; i++)
        {
            var mc = colliders[i];
            if (mc == null) continue;
            if (!mc.enabled) continue;
            if (mc.isTrigger) continue;
            if (mc.sharedMesh == null) continue;

            // Ignore the hull itself (and any colliders under it).
            if (hullRoot != null && mc.transform != null && mc.transform.IsChildOf(hullRoot))
                continue;

            caches.Add(new MeshTriangleCache(mc.sharedMesh, mc.transform));
        }

        return caches;
    }

    private readonly struct MeshTriangleCache
    {
        private readonly Vector3[] worldVerts;
        private readonly int[] tris;
        public readonly Bounds WorldBounds;

        public MeshTriangleCache(Mesh mesh, Transform t)
        {
            var v = mesh.vertices;
            worldVerts = new Vector3[v.Length];
            Matrix4x4 l2w = t != null ? t.localToWorldMatrix : Matrix4x4.identity;
            for (int i = 0; i < v.Length; i++)
                worldVerts[i] = l2w.MultiplyPoint3x4(v[i]);

            tris = mesh.triangles;

            if (worldVerts.Length > 0)
            {
                Bounds b = new Bounds(worldVerts[0], Vector3.zero);
                for (int i = 1; i < worldVerts.Length; i++)
                    b.Encapsulate(worldVerts[i]);
                WorldBounds = b;
            }
            else
            {
                WorldBounds = new Bounds(Vector3.zero, Vector3.zero);
            }
        }

        public int CountRayIntersectionsUnique(Vector3 origin, Vector3 dir, float mergeTolerance, List<float> scratchHits)
        {
            if (scratchHits == null)
                scratchHits = new List<float>(32);
            scratchHits.Clear();

            if (!RayIntersectsBounds(origin, dir, WorldBounds))
                return 0;

            for (int ti = 0; ti < tris.Length; ti += 3)
            {
                Vector3 a = worldVerts[tris[ti]];
                Vector3 b = worldVerts[tris[ti + 1]];
                Vector3 c = worldVerts[tris[ti + 2]];

                if (RayIntersectsTriangle(origin, dir, a, b, c, out float dist))
                {
                    // Merge/unique by distance to avoid double counting hits on shared edges/vertices.
                    scratchHits.Add(dist);
                }
            }

            if (scratchHits.Count <= 1)
                return scratchHits.Count;

            scratchHits.Sort();
            int unique = 1;
            float last = scratchHits[0];
            for (int i = 1; i < scratchHits.Count; i++)
            {
                float d = scratchHits[i];
                if (Mathf.Abs(d - last) <= mergeTolerance)
                    continue;
                unique++;
                last = d;
            }

            return unique;
        }

        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2, out float dist)
        {
            const float eps = 1e-7f;
            dist = 0f;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -eps && det < eps) return false;
            float invDet = 1f / det;

            Vector3 t = origin - v0;
            float u = Vector3.Dot(t, p) * invDet;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(t, e1);
            float v = Vector3.Dot(dir, q) * invDet;
            if (v < 0f || (u + v) > 1f) return false;

            dist = Vector3.Dot(e2, q) * invDet;
            return dist > eps;
        }

        private static bool RayIntersectsBounds(Vector3 origin, Vector3 dir, Bounds b)
        {
            // Standard slab test. Handles any direction.
            float tmin = float.NegativeInfinity;
            float tmax = float.PositiveInfinity;

            Vector3 min = b.min;
            Vector3 max = b.max;

            if (!Slab(origin.x, dir.x, min.x, max.x, ref tmin, ref tmax)) return false;
            if (!Slab(origin.y, dir.y, min.y, max.y, ref tmin, ref tmax)) return false;
            if (!Slab(origin.z, dir.z, min.z, max.z, ref tmin, ref tmax)) return false;

            return tmax >= Mathf.Max(0f, tmin);
        }

        private static bool Slab(float o, float d, float min, float max, ref float tmin, ref float tmax)
        {
            const float eps = 1e-9f;
            if (Mathf.Abs(d) < eps)
            {
                // Parallel to slab; must be inside it.
                return o >= min && o <= max;
            }

            float inv = 1f / d;
            float t1 = (min - o) * inv;
            float t2 = (max - o) * inv;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }

            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            return tmin <= tmax;
        }
    }

    private bool IsOverlappingGeometry(Vector3 positionWorld, float radius, Transform hullRoot)
    {
        if (radius <= 0f) return false;
        EnsureTestSphere();

        testSphere.radius = Mathf.Max(0.0001f, radius);
        testSphereGo.transform.position = positionWorld;
        testSphereGo.transform.rotation = Quaternion.identity;

        // IMPORTANT: Do NOT automatically exclude the Ignore Raycast layer from geometry checks.
        // Many scenes put real colliders on that layer; excluding it causes false negatives.
        int mask = geometryMask.value;

        // OverlapSphere gives us candidates; ComputePenetration confirms actual intersection.
        int count = Physics.OverlapSphereNonAlloc(positionWorld, radius, overlapBuffer, mask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null) continue;
            if (!c.enabled) continue;
            if (ReferenceEquals(c, testSphere)) continue;

            // Ignore the hull itself (and any colliders under it).
            if (hullRoot != null && c.transform != null && c.transform.IsChildOf(hullRoot))
                continue;

            // Confirm intersection using the actual sphere collider.
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

    private bool IsInVoid(Vector3 positionWorld, Transform hullRoot)
    {
        int rays = Mathf.Clamp(voidRayCount, 4, 128);
        float dist = Mathf.Max(0.001f, voidRayDistanceMeters);
        float lift = Mathf.Max(0f, voidRayOriginLiftMeters);

        Vector3 origin = positionWorld + Vector3.up * lift;

        // Reuse the same geometry mask; treat triggers as collidable for "not void" detection.
        int mask = geometryMask.value;

        // If ANY ray sees nothing (excluding hull), treat this as void and reject.
        for (int i = 0; i < rays; i++)
        {
            float a = (i / (float)rays) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            int hitCount = Physics.RaycastNonAlloc(origin, dir, raycastBuffer, dist, mask, QueryTriggerInteraction.Collide);
            if (hitCount <= 0)
                return true;

            bool hasValid = false;
            for (int h = 0; h < hitCount; h++)
            {
                var hit = raycastBuffer[h];
                var c = hit.collider;
                if (c == null) continue;
                if (!c.enabled) continue;

                // Ignore the hull itself (and any colliders under it).
                if (hullRoot != null && c.transform != null && c.transform.IsChildOf(hullRoot))
                    continue;

                // Ignore our helper sphere.
                if (ReferenceEquals(c, testSphere))
                    continue;

                // A near-zero distance hit can happen if the origin is on the surface; ignore it.
                if (hit.distance <= 0.0001f)
                    continue;

                hasValid = true;
                break;
            }

            if (!hasValid)
                return true;
        }

        return false;
    }

    private void EnsureTestSphere()
    {
        if (testSphere != null) return;
        testSphereGo = new GameObject("__AutoLightProbeVolumeTestSphere") { hideFlags = HideFlags.HideAndDontSave };
        testSphereGo.layer = IgnoreRaycastLayer;
        testSphereGo.transform.position = Vector3.zero;
        testSphereGo.transform.rotation = Quaternion.identity;
        testSphere = testSphereGo.AddComponent<SphereCollider>();
        testSphere.isTrigger = true;
        testSphere.radius = Mathf.Max(0.0001f, overlapRadiusMeters);
        testSphere.enabled = true;
    }

    private void ClearProbes()
    {
        if (group == null) return;
        Undo.RegisterCompleteObjectUndo(group, "Clear Light Probes");
        group.probePositions = Array.Empty<Vector3>();
        EditorUtility.SetDirty(group);
        MarkSceneDirty(group.gameObject.scene);
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

    private static bool TryGetTerrainFloorY(Vector3 positionWorldXZ, Terrain[] terrains, out float floorY)
    {
        floorY = 0f;
        if (terrains == null || terrains.Length == 0)
            return false;

        bool found = false;
        float best = float.NegativeInfinity;

        for (int i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            if (t == null) continue;
            if (!t.gameObject.activeInHierarchy) continue;

            var data = t.terrainData;
            if (data == null) continue;

            var tc = t.GetComponent<TerrainCollider>();
            if (tc == null || !tc.enabled)
                continue;

            Vector3 tp = t.transform.position;
            Vector3 size = data.size;

            float lx = positionWorldXZ.x - tp.x;
            float lz = positionWorldXZ.z - tp.z;

            if (lx < 0f || lz < 0f || lx > size.x || lz > size.z)
                continue;

            // Use the actual TerrainCollider surface so this respects Terrain holes.
            // (SampleHeight ignores holes and would incorrectly block caves.)
            float rayTopY = tp.y + size.y + 10f;
            var ray = new Ray(new Vector3(positionWorldXZ.x, rayTopY, positionWorldXZ.z), Vector3.down);
            if (tc.Raycast(ray, out var hit, size.y + 25f))
            {
                float y = hit.point.y;
                if (!found || y > best)
                {
                    best = y;
                    found = true;
                }
            }
        }

        if (!found)
            return false;

        floorY = best;
        return true;
    }
}
