using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NookTorchPlacer : EditorWindow
{
    private const string GeneratedParentName = "Generated Torches";
    private const float DefaultSurfaceOffset = 0.02f;
    private const float DefaultUvTolerance = 0.0001f;
    private const float NormalEpsilon = 0.000001f;
    private const float GeometryEpsilon = 0.000000000001f;

    [SerializeField] private GameObject m_Target;
    [SerializeField] private GameObject m_Prefab;
    [SerializeField] private Transform m_ParentTransform;
    [SerializeField] private float m_SurfaceOffset = DefaultSurfaceOffset;
    [SerializeField] private float m_UvTolerance = DefaultUvTolerance;
    [SerializeField] private bool m_HasReferenceUv;
    [SerializeField] private Vector2 m_ReferenceUv0;
    [SerializeField] private Vector2 m_ReferenceUv1;
    [SerializeField] private Vector2 m_ReferenceUv2;
    [SerializeField] private Mesh m_ReferenceMesh;
    [SerializeField] private bool m_HasMountReference;
    [SerializeField] private GameObject m_MountReferencePrefab;
    [SerializeField] private MeshFilter m_MountMeshFilter;
    [SerializeField] private string m_MountMeshPath;
    [SerializeField] private int m_MountTriangleIndex = -1;
    [SerializeField] private Vector3 m_LocalTriangleCentre;
    [SerializeField] private Vector3 m_LocalNormal;
    [SerializeField] private Vector3 m_LocalTangent;
    [SerializeField] private Vector3 m_LocalBitangent;
    [SerializeField] private Transform m_GeneratedParent;
    [SerializeField] private PickingMode m_PickingMode;
    [NonSerialized] private bool m_HasSearchedForGeneratedParent;

    private bool IsPicking
    {
        get { return m_PickingMode != PickingMode.None; }
    }

    [MenuItem("Tools/Nook Torch Placer")]
    private static void OpenWindow()
    {
        NookTorchPlacer window = GetWindow<NookTorchPlacer>("Nook Torch Placer");
        window.minSize = new Vector2(360f, 265f);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
        SceneView.duringSceneGui += OnSceneViewGui;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
        m_PickingMode = PickingMode.None;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Nook Torch Placer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        GameObject newTarget = (GameObject)EditorGUILayout.ObjectField(
            "Target GameObject",
            m_Target,
            typeof(GameObject),
            true);
        if (EditorGUI.EndChangeCheck())
        {
            m_Target = newTarget;
            m_GeneratedParent = null;
            m_HasSearchedForGeneratedParent = false;
            ResetReferenceUv();
            StopPicking();
        }

        EditorGUI.BeginChangeCheck();
        GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab to Place",
            m_Prefab,
            typeof(GameObject),
            false);
        if (EditorGUI.EndChangeCheck())
        {
            m_Prefab = newPrefab;
            ResetMountReference();
            StopPicking();
        }

        m_ParentTransform = (Transform)EditorGUILayout.ObjectField(
            "Parent Transform",
            m_ParentTransform,
            typeof(Transform),
            true);
        m_SurfaceOffset = EditorGUILayout.FloatField("Surface Offset", m_SurfaceOffset);
        m_UvTolerance = EditorGUILayout.FloatField("UV Comparison Tolerance", m_UvTolerance);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(m_Target == null || IsPicking))
        {
            if (GUILayout.Button("Pick Reference Face"))
            {
                BeginReferenceFacePicking();
            }
        }

        using (new EditorGUI.DisabledScope(m_Prefab == null || IsPicking))
        {
            if (GUILayout.Button("Pick Mount Face"))
            {
                BeginMountFacePicking();
            }
        }

        using (new EditorGUI.DisabledScope(
                   m_Target == null ||
                   m_Prefab == null ||
                   !m_HasReferenceUv ||
                   !m_HasMountReference ||
                   IsPicking))
        {
            if (GUILayout.Button("Generate Torches"))
            {
                GenerateTorches();
            }
        }

        Transform clearParent = GetClearParent();
        using (new EditorGUI.DisabledScope(clearParent == null || clearParent.childCount == 0 || IsPicking))
        {
            if (GUILayout.Button("Clear Generated"))
            {
                ClearGenerated();
            }
        }

        EditorGUILayout.Space();
        DrawStatus();
    }

    private void DrawStatus()
    {
        if (m_PickingMode == PickingMode.ReferenceFace)
        {
            EditorGUILayout.HelpBox(
                "Click a triangle on the target mesh in the Scene view. Press Escape to cancel.",
                MessageType.Info);
            return;
        }

        if (m_PickingMode == PickingMode.MountFace)
        {
            EditorGUILayout.HelpBox(
                "Click a triangle on the prefab mesh in the Scene view. Press Escape to cancel.",
                MessageType.Info);
            return;
        }

        if (m_HasReferenceUv)
        {
            EditorGUILayout.HelpBox(
                string.Format(
                    "Reference UVs: {0}, {1}, {2}",
                    m_ReferenceUv0.ToString("G6"),
                    m_ReferenceUv1.ToString("G6"),
                    m_ReferenceUv2.ToString("G6")),
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Pick one triangle from a torch mounting quad.", MessageType.None);
        }

        if (m_HasMountReference)
        {
            string meshLabel = string.IsNullOrEmpty(m_MountMeshPath) ? "Prefab Root" : m_MountMeshPath;
            EditorGUILayout.HelpBox(
                string.Format("Mount face: {0}, triangle {1}.", meshLabel, m_MountTriangleIndex),
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Pick the prefab triangle that must sit against the wall.", MessageType.None);
        }
    }

    private void BeginReferenceFacePicking()
    {
        MeshFilter meshFilter;
        Mesh mesh;
        string error;
        if (!TryGetTargetMesh(out meshFilter, out mesh, out error))
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", error, "OK");
            return;
        }

        MeshCollider meshCollider;
        if (!TryGetTargetCollider(mesh, out meshCollider, out error))
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", error, "OK");
            return;
        }

        m_PickingMode = PickingMode.ReferenceFace;
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.Focus();
            sceneView.ShowNotification(new GUIContent("Click a triangle on the target mesh. Escape cancels."));
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private void BeginMountFacePicking()
    {
        string prefabPath;
        string error;
        if (!TryGetPrefabAsset(out prefabPath, out error))
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", error, "OK");
            return;
        }

        m_PickingMode = PickingMode.MountFace;

        PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(m_Prefab);
        if (prefabAssetType != PrefabAssetType.Model && !AssetDatabase.OpenAsset(m_Prefab))
        {
            StopPicking();
            EditorUtility.DisplayDialog(
                "Nook Torch Placer",
                "Unity could not open the assigned prefab in Prefab Mode.",
                "OK");
            return;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.Focus();
            sceneView.ShowNotification(new GUIContent(
                prefabAssetType == PrefabAssetType.Model
                    ? "Click a triangle on a visible instance of the model prefab. Escape cancels."
                    : "Click a triangle on the prefab mesh. Escape cancels."));
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private void StopPicking()
    {
        if (!IsPicking)
        {
            return;
        }

        m_PickingMode = PickingMode.None;
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnSceneViewGui(SceneView sceneView)
    {
        if (!IsPicking)
        {
            return;
        }

        Event currentEvent = Event.current;
        int controlId = GUIUtility.GetControlID(GetHashCode(), FocusType.Passive);
        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(controlId);
        }

        EditorGUIUtility.AddCursorRect(
            new Rect(Vector2.zero, sceneView.position.size),
            MouseCursor.Arrow);

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
        {
            currentEvent.Use();
            StopPicking();
            sceneView.ShowNotification(new GUIContent("Face picking cancelled."));
            return;
        }

        if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || currentEvent.alt)
        {
            return;
        }

        Vector2 mousePosition = currentEvent.mousePosition;
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        currentEvent.Use();

        if (m_PickingMode == PickingMode.ReferenceFace)
        {
            PickReferenceFace(sceneView, ray);
        }
        else if (m_PickingMode == PickingMode.MountFace)
        {
            PickMountFace(sceneView, ray, mousePosition);
        }
    }

    private void PickReferenceFace(SceneView sceneView, Ray ray)
    {
        MeshFilter meshFilter;
        Mesh mesh;
        string error;
        if (!TryGetTargetMesh(out meshFilter, out mesh, out error))
        {
            StopPicking();
            sceneView.ShowNotification(new GUIContent(error));
            return;
        }

        MeshCollider meshCollider;
        if (!TryGetTargetCollider(mesh, out meshCollider, out error))
        {
            StopPicking();
            sceneView.ShowNotification(new GUIContent(error));
            return;
        }

        RaycastHit hit;
        if (!meshCollider.Raycast(ray, out hit, float.PositiveInfinity))
        {
            sceneView.ShowNotification(new GUIContent("Click directly on the target mesh."));
            return;
        }

        int[] triangles = mesh.triangles;
        Vector2[] uvs = mesh.uv;
        int triangleOffset = hit.triangleIndex * 3;
        if (hit.triangleIndex < 0 || triangleOffset < 0 || triangleOffset + 2 >= triangles.Length)
        {
            StopPicking();
            sceneView.ShowNotification(new GUIContent("The collider returned an invalid triangle index."));
            return;
        }

        int vertex0 = triangles[triangleOffset];
        int vertex1 = triangles[triangleOffset + 1];
        int vertex2 = triangles[triangleOffset + 2];
        if (uvs == null || !AreVertexIndicesValid(vertex0, vertex1, vertex2, uvs.Length))
        {
            StopPicking();
            sceneView.ShowNotification(new GUIContent("The target mesh does not contain usable UV0 data."));
            return;
        }

        m_ReferenceUv0 = uvs[vertex0];
        m_ReferenceUv1 = uvs[vertex1];
        m_ReferenceUv2 = uvs[vertex2];
        m_ReferenceMesh = mesh;
        m_HasReferenceUv = true;

        StopPicking();
        Repaint();
        sceneView.ShowNotification(new GUIContent("Reference face captured."));
    }

    private void PickMountFace(SceneView sceneView, Ray ray, Vector2 mousePosition)
    {
        Transform prefabRoot;
        string error;
        if (!TryGetMountPickingRoot(mousePosition, out prefabRoot, out error))
        {
            sceneView.ShowNotification(new GUIContent(error));
            return;
        }

        MeshFilter pickedMeshFilter;
        int triangleIndex;
        if (!TryRaycastPrefabMeshes(prefabRoot, ray, out pickedMeshFilter, out triangleIndex))
        {
            sceneView.ShowNotification(new GUIContent("Click directly on a visible prefab mesh triangle."));
            return;
        }

        if (!TryStoreMountReference(prefabRoot, pickedMeshFilter, triangleIndex, out error))
        {
            sceneView.ShowNotification(new GUIContent(error));
            return;
        }

        StopPicking();
        Repaint();
        sceneView.ShowNotification(new GUIContent("Prefab mount face captured."));
    }

    private void GenerateTorches()
    {
        MeshFilter meshFilter;
        Mesh mesh;
        string error;
        if (!TryGetTargetMesh(out meshFilter, out mesh, out error))
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", error, "OK");
            return;
        }

        if (!m_HasReferenceUv || m_ReferenceMesh != mesh)
        {
            EditorUtility.DisplayDialog(
                "Nook Torch Placer",
                "Pick a reference face on the current target mesh before generating torches.",
                "OK");
            return;
        }

        string prefabPath;
        if (!TryGetPrefabAsset(out prefabPath, out error))
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", error, "OK");
            return;
        }

        if (!m_HasMountReference || m_MountReferencePrefab != m_Prefab)
        {
            EditorUtility.DisplayDialog(
                "Nook Torch Placer",
                "Pick a mount face on the current prefab before generating torches.",
                "OK");
            return;
        }

        if (!IsFinite(m_SurfaceOffset))
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", "Surface Offset must be finite.", "OK");
            return;
        }

        if (!IsFinite(m_UvTolerance) || m_UvTolerance < 0f)
        {
            EditorUtility.DisplayDialog(
                "Nook Torch Placer",
                "UV Comparison Tolerance must be finite and greater than or equal to zero.",
                "OK");
            return;
        }

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;

        if (triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", "The target mesh has no valid triangles.", "OK");
            return;
        }

        if (vertices == null || vertices.Length == 0)
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", "The target mesh has no vertices.", "OK");
            return;
        }

        if (uvs == null || uvs.Length < vertices.Length)
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", "The target mesh needs UV0 data for every vertex.", "OK");
            return;
        }

        if (normals == null || normals.Length < vertices.Length)
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", "The target mesh needs normals for every vertex.", "OK");
            return;
        }

        int triangleCount = triangles.Length / 3;
        bool[] matchingTriangles = new bool[triangleCount];
        float toleranceSquared = m_UvTolerance * m_UvTolerance;
        int matchCount = 0;

        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            int triangleOffset = triangleIndex * 3;
            int vertex0 = triangles[triangleOffset];
            int vertex1 = triangles[triangleOffset + 1];
            int vertex2 = triangles[triangleOffset + 2];
            if (!AreVertexIndicesValid(vertex0, vertex1, vertex2, vertices.Length))
            {
                continue;
            }

            if (!TriangleUvMatches(
                    uvs[vertex0],
                    uvs[vertex1],
                    uvs[vertex2],
                    m_ReferenceUv0,
                    m_ReferenceUv1,
                    m_ReferenceUv2,
                    toleranceSquared))
            {
                continue;
            }

            matchingTriangles[triangleIndex] = true;
            matchCount++;
        }

        if (matchCount == 0)
        {
            EditorUtility.DisplayDialog(
                "Nook Torch Placer",
                "No triangles matched the reference UV signature.",
                "OK");
            return;
        }

        int edgeCapacity = triangleCount <= int.MaxValue / 3 ? triangleCount * 3 : triangleCount;
        Dictionary<EdgeKey, EdgeTriangles> edgeMap =
            new Dictionary<EdgeKey, EdgeTriangles>(edgeCapacity);
        int nonManifoldEdgeCount = BuildEdgeMap(triangles, triangleCount, edgeMap);

        Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
        Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
        HashSet<QuadKey> processedQuads = new HashSet<QuadKey>(matchCount);
        Quaternion inversePrefabBasis = Quaternion.Inverse(
            Quaternion.LookRotation(m_LocalBitangent, m_LocalNormal));

        int createdCount = 0;
        int missingNeighbourCount = 0;
        int invalidBasisCount = 0;
        Transform destinationParent = null;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Nook Torches");

        try
        {
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                if (!matchingTriangles[triangleIndex])
                {
                    continue;
                }

                int neighbourIndex = FindBestNeighbour(
                    triangleIndex,
                    triangles,
                    normals,
                    uvs,
                    normalMatrix,
                    edgeMap);
                if (neighbourIndex < 0)
                {
                    missingNeighbourCount++;
                    continue;
                }

                int quad0;
                int quad1;
                int quad2;
                int quad3;
                if (!TryGetQuadVertices(
                        triangles,
                        triangleIndex,
                        neighbourIndex,
                        out quad0,
                        out quad1,
                        out quad2,
                        out quad3))
                {
                    missingNeighbourCount++;
                    continue;
                }

                QuadKey quadKey = new QuadKey(quad0, quad1, quad2, quad3);
                if (!processedQuads.Add(quadKey))
                {
                    continue;
                }

                Vector3 triangleNormal;
                Vector3 neighbourNormal;
                if (!TryGetTriangleNormalWorld(
                        normals,
                        triangles,
                        triangleIndex,
                        normalMatrix,
                        out triangleNormal) ||
                    !TryGetTriangleNormalWorld(
                        normals,
                        triangles,
                        neighbourIndex,
                        normalMatrix,
                        out neighbourNormal))
                {
                    invalidBasisCount++;
                    continue;
                }

                Vector3 averageNormal = triangleNormal + neighbourNormal;
                if (averageNormal.sqrMagnitude <= NormalEpsilon)
                {
                    invalidBasisCount++;
                    continue;
                }

                averageNormal.Normalize();

                Vector3 quadCentre =
                    (localToWorld.MultiplyPoint3x4(vertices[quad0]) +
                     localToWorld.MultiplyPoint3x4(vertices[quad1]) +
                     localToWorld.MultiplyPoint3x4(vertices[quad2]) +
                     localToWorld.MultiplyPoint3x4(vertices[quad3])) * 0.25f;

                Vector3 wallTangent;
                if (!TryGetLongestQuadEdgeWorld(
                        vertices,
                        triangles,
                        triangleIndex,
                        neighbourIndex,
                        localToWorld,
                        out wallTangent))
                {
                    invalidBasisCount++;
                    continue;
                }

                Vector3 wallBitangent = Vector3.Cross(averageNormal, wallTangent);
                if (wallBitangent.sqrMagnitude <= NormalEpsilon)
                {
                    invalidBasisCount++;
                    continue;
                }

                wallBitangent.Normalize();
                wallTangent = Vector3.Cross(wallBitangent, averageNormal);
                if (wallTangent.sqrMagnitude <= NormalEpsilon)
                {
                    invalidBasisCount++;
                    continue;
                }

                wallTangent.Normalize();

                Quaternion wallBasis = Quaternion.LookRotation(wallBitangent, -averageNormal);
                Quaternion rotation = wallBasis * inversePrefabBasis;
                Vector3 worldMountPoint = rotation * m_LocalTriangleCentre;
                Vector3 position =
                    quadCentre - worldMountPoint + averageNormal * m_SurfaceOffset;

                if (destinationParent == null)
                {
                    destinationParent = GetOrCreateDestinationParent();
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(m_Prefab, destinationParent) as GameObject;
                if (instance == null)
                {
                    Debug.LogError("Nook Torch Placer could not instantiate the assigned prefab.", m_Prefab);
                    continue;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Generate Nook Torches");
                instance.transform.SetPositionAndRotation(position, rotation);
                createdCount++;
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }

        if (destinationParent != null && createdCount > 0)
        {
            Selection.activeTransform = destinationParent;
        }

        string result = string.Format(
            "Generated {0} torch prefab{1} from {2} matching triangle{3}.",
            createdCount,
            createdCount == 1 ? string.Empty : "s",
            matchCount,
            matchCount == 1 ? string.Empty : "s");

        if (missingNeighbourCount > 0 || invalidBasisCount > 0 || nonManifoldEdgeCount > 0)
        {
            result += string.Format(
            " Skipped: {0} without a valid quad neighbour, {1} with an invalid placement basis. Non-manifold edges found: {2}.",
                missingNeighbourCount,
            invalidBasisCount,
                nonManifoldEdgeCount);
        }

        Debug.Log("Nook Torch Placer: " + result, m_Target);
        EditorUtility.DisplayDialog("Nook Torch Placer", result, "OK");
    }

    private void ClearGenerated()
    {
        Transform generatedParent = GetClearParent();
        if (generatedParent == null)
        {
            EditorUtility.DisplayDialog("Nook Torch Placer", "There is no generated parent to clear.", "OK");
            return;
        }

        int childCount = generatedParent.childCount;
        if (childCount == 0)
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear Generated Nook Torches");

        try
        {
            for (int childIndex = generatedParent.childCount - 1; childIndex >= 0; childIndex--)
            {
                Undo.DestroyObjectImmediate(generatedParent.GetChild(childIndex).gameObject);
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }

        Selection.activeTransform = generatedParent;
        Debug.Log(
            string.Format("Nook Torch Placer: Cleared {0} generated object{1}.",
                childCount,
                childCount == 1 ? string.Empty : "s"),
            generatedParent);
    }

    private bool TryGetTargetMesh(out MeshFilter meshFilter, out Mesh mesh, out string error)
    {
        meshFilter = null;
        mesh = null;

        if (m_Target == null)
        {
            error = "Assign a Target GameObject.";
            return false;
        }

        meshFilter = m_Target.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            error = "Target GameObject must contain a MeshFilter.";
            return false;
        }

        mesh = meshFilter.sharedMesh;
        if (mesh == null)
        {
            error = "The target MeshFilter has no mesh assigned.";
            return false;
        }

        if (!mesh.isReadable)
        {
            error = "The target mesh is not readable. Enable Read/Write in its model import settings.";
            return false;
        }

        error = null;
        return true;
    }

    private bool TryGetTargetCollider(Mesh mesh, out MeshCollider meshCollider, out string error)
    {
        meshCollider = m_Target != null ? m_Target.GetComponent<MeshCollider>() : null;
        if (meshCollider == null)
        {
            error = "Target GameObject must contain a MeshCollider for reference-face picking.";
            return false;
        }

        if (meshCollider.sharedMesh != mesh)
        {
            error = "The target MeshCollider must use the same mesh as the target MeshFilter.";
            return false;
        }

        if (!meshCollider.enabled || !meshCollider.gameObject.activeInHierarchy)
        {
            error = "The target MeshCollider must be enabled and active for reference-face picking.";
            return false;
        }

        error = null;
        return true;
    }

    private bool TryGetPrefabAsset(out string prefabPath, out string error)
    {
        prefabPath = null;

        if (m_Prefab == null ||
            !EditorUtility.IsPersistent(m_Prefab) ||
            PrefabUtility.GetPrefabAssetType(m_Prefab) == PrefabAssetType.NotAPrefab)
        {
            error = "Prefab to Place must be a prefab asset from the Project window.";
            return false;
        }

        prefabPath = AssetDatabase.GetAssetPath(m_Prefab);
        if (string.IsNullOrEmpty(prefabPath))
        {
            error = "Unity could not resolve the assigned prefab asset path.";
            return false;
        }

        error = null;
        return true;
    }

    private bool TryGetMountPickingRoot(
        Vector2 mousePosition,
        out Transform prefabRoot,
        out string error)
    {
        prefabRoot = null;

        string prefabPath;
        if (!TryGetPrefabAsset(out prefabPath, out error))
        {
            return false;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null &&
            prefabStage.prefabContentsRoot != null &&
            string.Equals(prefabStage.assetPath, prefabPath, StringComparison.OrdinalIgnoreCase))
        {
            prefabRoot = prefabStage.prefabContentsRoot.transform;
            error = null;
            return true;
        }

        GameObject pickedObject = HandleUtility.PickGameObject(mousePosition, false);
        if (pickedObject == null)
        {
            error = "Click the assigned prefab in Prefab Mode or a visible scene instance.";
            return false;
        }

        Transform candidate = pickedObject.transform;
        while (candidate != null)
        {
            GameObject sourceObject =
                PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject) as GameObject;
            if (sourceObject == m_Prefab)
            {
                prefabRoot = candidate;
                error = null;
                return true;
            }

            candidate = candidate.parent;
        }

        error = "The clicked object is not an instance of the assigned prefab.";
        return false;
    }

    private static bool TryRaycastPrefabMeshes(
        Transform prefabRoot,
        Ray worldRay,
        out MeshFilter pickedMeshFilter,
        out int pickedTriangleIndex)
    {
        pickedMeshFilter = null;
        pickedTriangleIndex = -1;

        Vector3 worldDirection = worldRay.direction;
        if (worldDirection.sqrMagnitude <= GeometryEpsilon)
        {
            return false;
        }

        worldDirection.Normalize();
        worldRay.direction = worldDirection;

        MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(false);
        float bestDistanceSquared = float.PositiveInfinity;

        for (int meshFilterIndex = 0; meshFilterIndex < meshFilters.Length; meshFilterIndex++)
        {
            MeshFilter meshFilter = meshFilters[meshFilterIndex];
            if (meshFilter == null || !meshFilter.gameObject.activeInHierarchy)
            {
                continue;
            }

            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshRenderer == null || !meshRenderer.enabled)
            {
                continue;
            }

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || vertices.Length == 0 ||
                triangles == null || triangles.Length == 0 ||
                triangles.Length % 3 != 0)
            {
                continue;
            }

            Matrix4x4 worldToMesh = meshFilter.transform.worldToLocalMatrix;
            Ray localRay = new Ray(
                worldToMesh.MultiplyPoint3x4(worldRay.origin),
                worldToMesh.MultiplyVector(worldRay.direction));

            int triangleCount = triangles.Length / 3;
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int triangleOffset = triangleIndex * 3;
                int vertex0 = triangles[triangleOffset];
                int vertex1 = triangles[triangleOffset + 1];
                int vertex2 = triangles[triangleOffset + 2];
                if (!AreVertexIndicesValid(vertex0, vertex1, vertex2, vertices.Length))
                {
                    continue;
                }

                float rayDistance;
                if (!RayIntersectsTriangle(
                        localRay,
                        vertices[vertex0],
                        vertices[vertex1],
                        vertices[vertex2],
                        out rayDistance))
                {
                    continue;
                }

                Vector3 localHitPoint = localRay.origin + localRay.direction * rayDistance;
                Vector3 worldHitPoint = meshFilter.transform.TransformPoint(localHitPoint);
                Vector3 worldOffset = worldHitPoint - worldRay.origin;
                if (Vector3.Dot(worldOffset, worldRay.direction) <= 0f)
                {
                    continue;
                }

                float distanceSquared = worldOffset.sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                pickedMeshFilter = meshFilter;
                pickedTriangleIndex = triangleIndex;
            }
        }

        return pickedMeshFilter != null;
    }

    private bool TryStoreMountReference(
        Transform pickingRoot,
        MeshFilter pickedMeshFilter,
        int triangleIndex,
        out string error)
    {
        string meshPath = AnimationUtility.CalculateTransformPath(
            pickedMeshFilter.transform,
            pickingRoot);
        Transform assetMeshTransform = string.IsNullOrEmpty(meshPath)
            ? m_Prefab.transform
            : m_Prefab.transform.Find(meshPath);
        if (assetMeshTransform == null)
        {
            error = "Unity could not map the clicked MeshFilter back to the prefab asset.";
            return false;
        }

        MeshFilter assetMeshFilter = assetMeshTransform.GetComponent<MeshFilter>();
        Mesh mesh = assetMeshFilter != null ? assetMeshFilter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable)
        {
            error = "The clicked prefab mesh must have Read/Write enabled.";
            return false;
        }

        if (pickedMeshFilter.sharedMesh != mesh)
        {
            error = "The clicked scene instance overrides its prefab mesh. Pick the face in Prefab Mode instead.";
            return false;
        }

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        int triangleOffset = triangleIndex * 3;
        if (triangleIndex < 0 ||
            triangleOffset < 0 ||
            triangleOffset + 2 >= triangles.Length)
        {
            error = "The clicked prefab triangle index is invalid.";
            return false;
        }

        int vertex0 = triangles[triangleOffset];
        int vertex1 = triangles[triangleOffset + 1];
        int vertex2 = triangles[triangleOffset + 2];
        if (!AreVertexIndicesValid(vertex0, vertex1, vertex2, vertices.Length))
        {
            error = "The clicked prefab triangle contains invalid vertex indices.";
            return false;
        }

        Matrix4x4 meshToPrefab;
        if (!TryGetRelativeMatrix(assetMeshFilter.transform, m_Prefab.transform, out meshToPrefab))
        {
            error = "The clicked MeshFilter is not part of the assigned prefab hierarchy.";
            return false;
        }

        Vector3 localVertex0 = meshToPrefab.MultiplyPoint3x4(vertices[vertex0]);
        Vector3 localVertex1 = meshToPrefab.MultiplyPoint3x4(vertices[vertex1]);
        Vector3 localVertex2 = meshToPrefab.MultiplyPoint3x4(vertices[vertex2]);
        Vector3 edge1 = localVertex1 - localVertex0;
        Vector3 normal = Vector3.Cross(edge1, localVertex2 - localVertex0);
        if (edge1.sqrMagnitude <= GeometryEpsilon || normal.sqrMagnitude <= GeometryEpsilon)
        {
            error = "The clicked prefab triangle is degenerate and cannot define a mounting basis.";
            return false;
        }

        edge1.Normalize();
        normal.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, edge1);
        if (bitangent.sqrMagnitude <= GeometryEpsilon)
        {
            error = "The clicked prefab triangle could not define a mounting bitangent.";
            return false;
        }

        bitangent.Normalize();

        m_HasMountReference = true;
        m_MountReferencePrefab = m_Prefab;
        m_MountMeshFilter = assetMeshFilter;
        m_MountMeshPath = meshPath;
        m_MountTriangleIndex = triangleIndex;
        m_LocalTriangleCentre = (localVertex0 + localVertex1 + localVertex2) / 3f;
        m_LocalNormal = normal;
        m_LocalTangent = edge1;
        m_LocalBitangent = bitangent;

        error = null;
        return true;
    }

    private static bool TryGetRelativeMatrix(
        Transform descendant,
        Transform ancestor,
        out Matrix4x4 relativeMatrix)
    {
        relativeMatrix = Matrix4x4.identity;
        Transform current = descendant;

        while (current != null && current != ancestor)
        {
            Matrix4x4 localMatrix = Matrix4x4.TRS(
                current.localPosition,
                current.localRotation,
                current.localScale);
            relativeMatrix = localMatrix * relativeMatrix;
            current = current.parent;
        }

        return current == ancestor;
    }

    private static bool RayIntersectsTriangle(
        Ray ray,
        Vector3 vertex0,
        Vector3 vertex1,
        Vector3 vertex2,
        out float rayDistance)
    {
        Vector3 edge1 = vertex1 - vertex0;
        Vector3 edge2 = vertex2 - vertex0;
        Vector3 cross = Vector3.Cross(ray.direction, edge2);
        float determinant = Vector3.Dot(edge1, cross);
        if (Mathf.Abs(determinant) <= GeometryEpsilon)
        {
            rayDistance = 0f;
            return false;
        }

        float inverseDeterminant = 1f / determinant;
        Vector3 originOffset = ray.origin - vertex0;
        float barycentricU = Vector3.Dot(originOffset, cross) * inverseDeterminant;
        if (barycentricU < 0f || barycentricU > 1f)
        {
            rayDistance = 0f;
            return false;
        }

        Vector3 barycentricCross = Vector3.Cross(originOffset, edge1);
        float barycentricV = Vector3.Dot(ray.direction, barycentricCross) * inverseDeterminant;
        if (barycentricV < 0f || barycentricU + barycentricV > 1f)
        {
            rayDistance = 0f;
            return false;
        }

        rayDistance = Vector3.Dot(edge2, barycentricCross) * inverseDeterminant;
        return rayDistance > GeometryEpsilon;
    }

    private static bool TryGetLongestQuadEdgeWorld(
        Vector3[] vertices,
        int[] triangles,
        int firstTriangle,
        int secondTriangle,
        Matrix4x4 localToWorld,
        out Vector3 wallTangent)
    {
        wallTangent = Vector3.zero;
        int firstOffset = firstTriangle * 3;
        int secondOffset = secondTriangle * 3;

        int sharedVertex0 = -1;
        int sharedVertex1 = -1;
        int sharedCount = 0;
        for (int firstIndex = 0; firstIndex < 3; firstIndex++)
        {
            int firstVertex = triangles[firstOffset + firstIndex];
            for (int secondIndex = 0; secondIndex < 3; secondIndex++)
            {
                if (firstVertex != triangles[secondOffset + secondIndex])
                {
                    continue;
                }

                if (sharedCount == 0)
                {
                    sharedVertex0 = firstVertex;
                }
                else if (sharedCount == 1)
                {
                    sharedVertex1 = firstVertex;
                }

                sharedCount++;
                break;
            }
        }

        if (sharedCount != 2)
        {
            return false;
        }

        EdgeKey sharedEdge = new EdgeKey(sharedVertex0, sharedVertex1);
        float longestLengthSquared = -1f;
        int selectedMinVertex = int.MaxValue;
        int selectedMaxVertex = int.MaxValue;

        ConsiderQuadBoundaryEdge(
            triangles[firstOffset],
            triangles[firstOffset + 1],
            sharedEdge,
            vertices,
            localToWorld,
            ref wallTangent,
            ref longestLengthSquared,
            ref selectedMinVertex,
            ref selectedMaxVertex);
        ConsiderQuadBoundaryEdge(
            triangles[firstOffset + 1],
            triangles[firstOffset + 2],
            sharedEdge,
            vertices,
            localToWorld,
            ref wallTangent,
            ref longestLengthSquared,
            ref selectedMinVertex,
            ref selectedMaxVertex);
        ConsiderQuadBoundaryEdge(
            triangles[firstOffset + 2],
            triangles[firstOffset],
            sharedEdge,
            vertices,
            localToWorld,
            ref wallTangent,
            ref longestLengthSquared,
            ref selectedMinVertex,
            ref selectedMaxVertex);
        ConsiderQuadBoundaryEdge(
            triangles[secondOffset],
            triangles[secondOffset + 1],
            sharedEdge,
            vertices,
            localToWorld,
            ref wallTangent,
            ref longestLengthSquared,
            ref selectedMinVertex,
            ref selectedMaxVertex);
        ConsiderQuadBoundaryEdge(
            triangles[secondOffset + 1],
            triangles[secondOffset + 2],
            sharedEdge,
            vertices,
            localToWorld,
            ref wallTangent,
            ref longestLengthSquared,
            ref selectedMinVertex,
            ref selectedMaxVertex);
        ConsiderQuadBoundaryEdge(
            triangles[secondOffset + 2],
            triangles[secondOffset],
            sharedEdge,
            vertices,
            localToWorld,
            ref wallTangent,
            ref longestLengthSquared,
            ref selectedMinVertex,
            ref selectedMaxVertex);

        if (wallTangent.sqrMagnitude <= GeometryEpsilon)
        {
            wallTangent = Vector3.zero;
            return false;
        }

        wallTangent.Normalize();
        return true;
    }

    private static void ConsiderQuadBoundaryEdge(
        int vertex0,
        int vertex1,
        EdgeKey sharedEdge,
        Vector3[] vertices,
        Matrix4x4 localToWorld,
        ref Vector3 wallTangent,
        ref float longestLengthSquared,
        ref int selectedMinVertex,
        ref int selectedMaxVertex)
    {
        if (new EdgeKey(vertex0, vertex1).Equals(sharedEdge))
        {
            return;
        }

        int minVertex = Math.Min(vertex0, vertex1);
        int maxVertex = Math.Max(vertex0, vertex1);
        Vector3 edge =
            localToWorld.MultiplyPoint3x4(vertices[maxVertex]) -
            localToWorld.MultiplyPoint3x4(vertices[minVertex]);
        float lengthSquared = edge.sqrMagnitude;
        if (lengthSquared <= GeometryEpsilon)
        {
            return;
        }

        float comparisonTolerance = Mathf.Max(GeometryEpsilon, longestLengthSquared * 0.000001f);
        bool isLonger = lengthSquared > longestLengthSquared + comparisonTolerance;
        bool isEquivalentAndEarlier =
            Mathf.Abs(lengthSquared - longestLengthSquared) <= comparisonTolerance &&
            (minVertex < selectedMinVertex ||
             (minVertex == selectedMinVertex && maxVertex < selectedMaxVertex));
        if (!isLonger && !isEquivalentAndEarlier)
        {
            return;
        }

        longestLengthSquared = lengthSquared;
        selectedMinVertex = minVertex;
        selectedMaxVertex = maxVertex;
        wallTangent = edge;
    }

    private Transform GetOrCreateDestinationParent()
    {
        if (m_ParentTransform != null)
        {
            return m_ParentTransform;
        }

        Transform existingParent = GetFallbackGeneratedParent();
        if (existingParent != null)
        {
            return existingParent;
        }

        GameObject parentObject = new GameObject(GeneratedParentName);
        Undo.RegisterCreatedObjectUndo(parentObject, "Create Generated Torches Parent");

        if (m_Target != null)
        {
            Scene targetScene = m_Target.scene;
            if (targetScene.IsValid() && targetScene.isLoaded && parentObject.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(parentObject, targetScene);
            }
        }

        m_GeneratedParent = parentObject.transform;
        return m_GeneratedParent;
    }

    private Transform GetClearParent()
    {
        if (m_ParentTransform != null)
        {
            return m_ParentTransform;
        }

        return GetFallbackGeneratedParent();
    }

    private Transform GetFallbackGeneratedParent()
    {
        if (m_GeneratedParent != null)
        {
            return m_GeneratedParent;
        }

        if (m_HasSearchedForGeneratedParent)
        {
            return null;
        }

        m_HasSearchedForGeneratedParent = true;

        if (m_Target == null)
        {
            return null;
        }

        Scene targetScene = m_Target.scene;
        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            return null;
        }

        GameObject[] rootObjects = targetScene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
        {
            GameObject rootObject = rootObjects[rootIndex];
            if (rootObject.name != GeneratedParentName)
            {
                continue;
            }

            m_GeneratedParent = rootObject.transform;
            return m_GeneratedParent;
        }

        return null;
    }

    private void ResetReferenceUv()
    {
        m_HasReferenceUv = false;
        m_ReferenceMesh = null;
        m_ReferenceUv0 = default(Vector2);
        m_ReferenceUv1 = default(Vector2);
        m_ReferenceUv2 = default(Vector2);
    }

    private void ResetMountReference()
    {
        m_HasMountReference = false;
        m_MountReferencePrefab = null;
        m_MountMeshFilter = null;
        m_MountMeshPath = null;
        m_MountTriangleIndex = -1;
        m_LocalTriangleCentre = default(Vector3);
        m_LocalNormal = default(Vector3);
        m_LocalTangent = default(Vector3);
        m_LocalBitangent = default(Vector3);
    }

    private static bool TriangleUvMatches(
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Vector2 reference0,
        Vector2 reference1,
        Vector2 reference2,
        float toleranceSquared)
    {
        return
            (UvMatches(uv0, reference0, toleranceSquared) &&
             UvMatches(uv1, reference1, toleranceSquared) &&
             UvMatches(uv2, reference2, toleranceSquared)) ||
            (UvMatches(uv0, reference0, toleranceSquared) &&
             UvMatches(uv1, reference2, toleranceSquared) &&
             UvMatches(uv2, reference1, toleranceSquared)) ||
            (UvMatches(uv0, reference1, toleranceSquared) &&
             UvMatches(uv1, reference0, toleranceSquared) &&
             UvMatches(uv2, reference2, toleranceSquared)) ||
            (UvMatches(uv0, reference1, toleranceSquared) &&
             UvMatches(uv1, reference2, toleranceSquared) &&
             UvMatches(uv2, reference0, toleranceSquared)) ||
            (UvMatches(uv0, reference2, toleranceSquared) &&
             UvMatches(uv1, reference0, toleranceSquared) &&
             UvMatches(uv2, reference1, toleranceSquared)) ||
            (UvMatches(uv0, reference2, toleranceSquared) &&
             UvMatches(uv1, reference1, toleranceSquared) &&
             UvMatches(uv2, reference0, toleranceSquared));
    }

    private static bool UvMatches(Vector2 first, Vector2 second, float toleranceSquared)
    {
        return (first - second).sqrMagnitude <= toleranceSquared;
    }

    private static int BuildEdgeMap(
        int[] triangles,
        int triangleCount,
        Dictionary<EdgeKey, EdgeTriangles> edgeMap)
    {
        int nonManifoldEdgeCount = 0;
        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            int triangleOffset = triangleIndex * 3;
            int vertex0 = triangles[triangleOffset];
            int vertex1 = triangles[triangleOffset + 1];
            int vertex2 = triangles[triangleOffset + 2];

            if (AddEdge(edgeMap, vertex0, vertex1, triangleIndex))
            {
                nonManifoldEdgeCount++;
            }

            if (AddEdge(edgeMap, vertex1, vertex2, triangleIndex))
            {
                nonManifoldEdgeCount++;
            }

            if (AddEdge(edgeMap, vertex2, vertex0, triangleIndex))
            {
                nonManifoldEdgeCount++;
            }
        }

        return nonManifoldEdgeCount;
    }

    private static bool AddEdge(
        Dictionary<EdgeKey, EdgeTriangles> edgeMap,
        int vertex0,
        int vertex1,
        int triangleIndex)
    {
        if (vertex0 == vertex1)
        {
            return false;
        }

        EdgeKey edgeKey = new EdgeKey(vertex0, vertex1);
        EdgeTriangles edgeTriangles;
        if (!edgeMap.TryGetValue(edgeKey, out edgeTriangles))
        {
            edgeMap.Add(edgeKey, new EdgeTriangles(triangleIndex));
            return false;
        }

        bool becameNonManifold = edgeTriangles.Add(triangleIndex);
        edgeMap[edgeKey] = edgeTriangles;
        return becameNonManifold;
    }

    private static int FindBestNeighbour(
        int triangleIndex,
        int[] triangles,
        Vector3[] normals,
        Vector2[] uvs,
        Matrix4x4 normalMatrix,
        Dictionary<EdgeKey, EdgeTriangles> edgeMap)
    {
        int triangleOffset = triangleIndex * 3;
        int vertex0 = triangles[triangleOffset];
        int vertex1 = triangles[triangleOffset + 1];
        int vertex2 = triangles[triangleOffset + 2];

        Vector3 triangleNormal;
        if (!TryGetTriangleNormalWorld(
                normals,
                triangles,
                triangleIndex,
                normalMatrix,
                out triangleNormal))
        {
            return -1;
        }

        int bestNeighbour = -1;
        float bestNormalAlignment = -1f;
        float bestSharedUvLength = -1f;

        ConsiderNeighbour(
            vertex0,
            vertex1,
            triangleIndex,
            triangleNormal,
            triangles,
            normals,
            uvs,
            normalMatrix,
            edgeMap,
            ref bestNeighbour,
            ref bestNormalAlignment,
            ref bestSharedUvLength);
        ConsiderNeighbour(
            vertex1,
            vertex2,
            triangleIndex,
            triangleNormal,
            triangles,
            normals,
            uvs,
            normalMatrix,
            edgeMap,
            ref bestNeighbour,
            ref bestNormalAlignment,
            ref bestSharedUvLength);
        ConsiderNeighbour(
            vertex2,
            vertex0,
            triangleIndex,
            triangleNormal,
            triangles,
            normals,
            uvs,
            normalMatrix,
            edgeMap,
            ref bestNeighbour,
            ref bestNormalAlignment,
            ref bestSharedUvLength);

        return bestNeighbour;
    }

    private static void ConsiderNeighbour(
        int edgeVertex0,
        int edgeVertex1,
        int triangleIndex,
        Vector3 triangleNormal,
        int[] triangles,
        Vector3[] normals,
        Vector2[] uvs,
        Matrix4x4 normalMatrix,
        Dictionary<EdgeKey, EdgeTriangles> edgeMap,
        ref int bestNeighbour,
        ref float bestNormalAlignment,
        ref float bestSharedUvLength)
    {
        EdgeTriangles edgeTriangles;
        if (!edgeMap.TryGetValue(new EdgeKey(edgeVertex0, edgeVertex1), out edgeTriangles))
        {
            return;
        }

        int neighbourIndex = edgeTriangles.GetOther(triangleIndex);
        if (neighbourIndex < 0 ||
            CountSharedVertexIndices(triangles, triangleIndex, neighbourIndex) != 2)
        {
            return;
        }

        Vector3 neighbourNormal;
        if (!TryGetTriangleNormalWorld(
                normals,
                triangles,
                neighbourIndex,
                normalMatrix,
                out neighbourNormal))
        {
            return;
        }

        float normalAlignment = Mathf.Abs(Vector3.Dot(triangleNormal, neighbourNormal));
        float sharedUvLength = (uvs[edgeVertex0] - uvs[edgeVertex1]).sqrMagnitude;
        const float scoreTolerance = 0.000001f;
        bool hasBetterAlignment = normalAlignment > bestNormalAlignment + scoreTolerance;
        bool hasBetterTieBreak =
            Mathf.Abs(normalAlignment - bestNormalAlignment) <= scoreTolerance &&
            sharedUvLength > bestSharedUvLength;

        if (!hasBetterAlignment && !hasBetterTieBreak)
        {
            return;
        }

        bestNeighbour = neighbourIndex;
        bestNormalAlignment = normalAlignment;
        bestSharedUvLength = sharedUvLength;
    }

    private static int CountSharedVertexIndices(
        int[] triangles,
        int firstTriangle,
        int secondTriangle)
    {
        int firstOffset = firstTriangle * 3;
        int secondOffset = secondTriangle * 3;
        int sharedCount = 0;

        for (int firstVertexIndex = 0; firstVertexIndex < 3; firstVertexIndex++)
        {
            int firstVertex = triangles[firstOffset + firstVertexIndex];
            for (int secondVertexIndex = 0; secondVertexIndex < 3; secondVertexIndex++)
            {
                if (firstVertex != triangles[secondOffset + secondVertexIndex])
                {
                    continue;
                }

                sharedCount++;
                break;
            }
        }

        return sharedCount;
    }

    private static bool TryGetQuadVertices(
        int[] triangles,
        int firstTriangle,
        int secondTriangle,
        out int quad0,
        out int quad1,
        out int quad2,
        out int quad3)
    {
        quad0 = -1;
        quad1 = -1;
        quad2 = -1;
        quad3 = -1;
        int uniqueCount = 0;
        int firstOffset = firstTriangle * 3;
        int secondOffset = secondTriangle * 3;

        AddUniqueVertex(triangles[firstOffset], ref uniqueCount, ref quad0, ref quad1, ref quad2, ref quad3);
        AddUniqueVertex(triangles[firstOffset + 1], ref uniqueCount, ref quad0, ref quad1, ref quad2, ref quad3);
        AddUniqueVertex(triangles[firstOffset + 2], ref uniqueCount, ref quad0, ref quad1, ref quad2, ref quad3);
        AddUniqueVertex(triangles[secondOffset], ref uniqueCount, ref quad0, ref quad1, ref quad2, ref quad3);
        AddUniqueVertex(triangles[secondOffset + 1], ref uniqueCount, ref quad0, ref quad1, ref quad2, ref quad3);
        AddUniqueVertex(triangles[secondOffset + 2], ref uniqueCount, ref quad0, ref quad1, ref quad2, ref quad3);

        return uniqueCount == 4;
    }

    private static void AddUniqueVertex(
        int vertex,
        ref int uniqueCount,
        ref int quad0,
        ref int quad1,
        ref int quad2,
        ref int quad3)
    {
        if (vertex == quad0 || vertex == quad1 || vertex == quad2 || vertex == quad3)
        {
            return;
        }

        if (uniqueCount == 0)
        {
            quad0 = vertex;
        }
        else if (uniqueCount == 1)
        {
            quad1 = vertex;
        }
        else if (uniqueCount == 2)
        {
            quad2 = vertex;
        }
        else if (uniqueCount == 3)
        {
            quad3 = vertex;
        }

        uniqueCount++;
    }

    private static bool TryGetTriangleNormalWorld(
        Vector3[] normals,
        int[] triangles,
        int triangleIndex,
        Matrix4x4 normalMatrix,
        out Vector3 triangleNormal)
    {
        int triangleOffset = triangleIndex * 3;
        Vector3 normal0 = normalMatrix.MultiplyVector(normals[triangles[triangleOffset]]);
        Vector3 normal1 = normalMatrix.MultiplyVector(normals[triangles[triangleOffset + 1]]);
        Vector3 normal2 = normalMatrix.MultiplyVector(normals[triangles[triangleOffset + 2]]);

        if (normal0.sqrMagnitude > NormalEpsilon)
        {
            normal0.Normalize();
        }

        if (normal1.sqrMagnitude > NormalEpsilon)
        {
            normal1.Normalize();
        }

        if (normal2.sqrMagnitude > NormalEpsilon)
        {
            normal2.Normalize();
        }

        triangleNormal = normal0 + normal1 + normal2;
        if (triangleNormal.sqrMagnitude <= NormalEpsilon)
        {
            triangleNormal = Vector3.zero;
            return false;
        }

        triangleNormal.Normalize();
        return true;
    }

    private static bool AreVertexIndicesValid(int vertex0, int vertex1, int vertex2, int vertexCount)
    {
        return vertex0 >= 0 && vertex0 < vertexCount &&
               vertex1 >= 0 && vertex1 < vertexCount &&
               vertex2 >= 0 && vertex2 < vertexCount;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private enum PickingMode
    {
        None,
        ReferenceFace,
        MountFace
    }

    private struct EdgeKey : IEquatable<EdgeKey>
    {
        private readonly int m_MinVertex;
        private readonly int m_MaxVertex;

        public EdgeKey(int vertex0, int vertex1)
        {
            if (vertex0 < vertex1)
            {
                m_MinVertex = vertex0;
                m_MaxVertex = vertex1;
            }
            else
            {
                m_MinVertex = vertex1;
                m_MaxVertex = vertex0;
            }
        }

        public bool Equals(EdgeKey other)
        {
            return m_MinVertex == other.m_MinVertex && m_MaxVertex == other.m_MaxVertex;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey && Equals((EdgeKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (m_MinVertex * 397) ^ m_MaxVertex;
            }
        }
    }

    private struct EdgeTriangles
    {
        private int m_FirstTriangle;
        private int m_SecondTriangle;
        private bool m_IsNonManifold;

        public EdgeTriangles(int firstTriangle)
        {
            m_FirstTriangle = firstTriangle;
            m_SecondTriangle = -1;
            m_IsNonManifold = false;
        }

        public bool Add(int triangleIndex)
        {
            if (triangleIndex == m_FirstTriangle || triangleIndex == m_SecondTriangle)
            {
                return false;
            }

            if (m_SecondTriangle < 0)
            {
                m_SecondTriangle = triangleIndex;
                return false;
            }

            if (m_IsNonManifold)
            {
                return false;
            }

            m_IsNonManifold = true;
            return true;
        }

        public int GetOther(int triangleIndex)
        {
            if (m_IsNonManifold)
            {
                return -1;
            }

            if (m_FirstTriangle == triangleIndex)
            {
                return m_SecondTriangle;
            }

            if (m_SecondTriangle == triangleIndex)
            {
                return m_FirstTriangle;
            }

            return -1;
        }
    }

    private struct QuadKey : IEquatable<QuadKey>
    {
        private readonly int m_Vertex0;
        private readonly int m_Vertex1;
        private readonly int m_Vertex2;
        private readonly int m_Vertex3;

        public QuadKey(int vertex0, int vertex1, int vertex2, int vertex3)
        {
            SortPair(ref vertex0, ref vertex1);
            SortPair(ref vertex2, ref vertex3);
            SortPair(ref vertex0, ref vertex2);
            SortPair(ref vertex1, ref vertex3);
            SortPair(ref vertex1, ref vertex2);

            m_Vertex0 = vertex0;
            m_Vertex1 = vertex1;
            m_Vertex2 = vertex2;
            m_Vertex3 = vertex3;
        }

        public bool Equals(QuadKey other)
        {
            return m_Vertex0 == other.m_Vertex0 &&
                   m_Vertex1 == other.m_Vertex1 &&
                   m_Vertex2 == other.m_Vertex2 &&
                   m_Vertex3 == other.m_Vertex3;
        }

        public override bool Equals(object obj)
        {
            return obj is QuadKey && Equals((QuadKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = m_Vertex0;
                hashCode = (hashCode * 397) ^ m_Vertex1;
                hashCode = (hashCode * 397) ^ m_Vertex2;
                hashCode = (hashCode * 397) ^ m_Vertex3;
                return hashCode;
            }
        }

        private static void SortPair(ref int first, ref int second)
        {
            if (first <= second)
            {
                return;
            }

            int temporary = first;
            first = second;
            second = temporary;
        }
    }
}