using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class BoulderPathFromSurfaceTool : EditorWindow
{
    private const string ControlPointPrefix = "SplineControl_";
    private const string BakedPointPrefix = "BakedBoulderPoint_";

    [SerializeField] private NetworkedBoulderSpawner m_NetworkedSpawner;
    [SerializeField] private Boulder m_Boulder;
    [SerializeField] private LocalBoulderRunOnce m_LocalBoulder;
    [SerializeField] private Transform m_PathParent;
    [SerializeField] private Transform m_ControlPointParent;
    [SerializeField] private Collider m_PathSurface;
    [SerializeField] private List<Transform> m_ControlPoints = new List<Transform>();

    [SerializeField] private bool m_AutoProjectControls = true;
    [SerializeField] private Vector3 m_ProjectionDirection = Vector3.down;
    [SerializeField] private float m_ProjectionDistance = 1000f;
    [SerializeField] private float m_NormalOffsetDistance = 0.25f;
    [SerializeField] private int m_SubdivisionsPerSegment = 20;
    [SerializeField] private int m_BakeSubdivisionsPerSegment = 50;
    [SerializeField] private float m_BakeSpacing = 1f;
    [SerializeField] private bool m_ReplaceExistingBakedPoints = true;
    [SerializeField] private bool m_InterpolateControlMetadata;
    [SerializeField] private Vector3 m_BakedWaypointEulerPerTick = Vector3.zero;
    [SerializeField] private Vector3 m_BakedWaypointScale = Vector3.one;
    [SerializeField] private int m_SelectedPointIndex = -1;
    [SerializeField] private Vector2 m_ScrollPosition;

    [MenuItem("Tools/Level/Boulder Spline Path Tool")]
    [MenuItem("Tools/Level/Generate Boulder Path From Surface")]
    private static void Open()
    {
        GetWindow<BoulderPathFromSurfaceTool>("Boulder Spline Path");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
        SceneView.duringSceneGui += OnSceneViewGui;
        Undo.undoRedoPerformed -= OnUndoRedo;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    private void OnUndoRedo()
    {
        RemoveNullControlReferences();
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnGUI()
    {
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

        EditorGUILayout.LabelField("Boulder Spline Path", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Runtime Targets", EditorStyles.boldLabel);
        m_NetworkedSpawner = (NetworkedBoulderSpawner)EditorGUILayout.ObjectField(
            "Networked Spawner", m_NetworkedSpawner, typeof(NetworkedBoulderSpawner), true);
        m_Boulder = (Boulder)EditorGUILayout.ObjectField(
            "Looping Boulder", m_Boulder, typeof(Boulder), true);
        m_LocalBoulder = (LocalBoulderRunOnce)EditorGUILayout.ObjectField(
            "One-Shot Boulder", m_LocalBoulder, typeof(LocalBoulderRunOnce), true);
        m_PathParent = (Transform)EditorGUILayout.ObjectField(
            "Baked Path Parent", m_PathParent, typeof(Transform), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Authoring", EditorStyles.boldLabel);
        m_ControlPointParent = (Transform)EditorGUILayout.ObjectField(
            "Control Point Parent", m_ControlPointParent, typeof(Transform), true);
        m_PathSurface = (Collider)EditorGUILayout.ObjectField(
            "Path Surface", m_PathSurface, typeof(Collider), true);

        m_AutoProjectControls = EditorGUILayout.Toggle(
            "Auto Project Controls", m_AutoProjectControls);
        m_ProjectionDirection = EditorGUILayout.Vector3Field(
            "Projection Direction", m_ProjectionDirection);
        m_ProjectionDistance = EditorGUILayout.FloatField(
            "Projection Distance", m_ProjectionDistance);
        m_NormalOffsetDistance = EditorGUILayout.FloatField(
            "Normal Offset", m_NormalOffsetDistance);
        m_SubdivisionsPerSegment = EditorGUILayout.IntSlider(
            "Preview Subdivisions", m_SubdivisionsPerSegment, 2, 100);
        m_BakeSubdivisionsPerSegment = EditorGUILayout.IntSlider(
            "Bake Subdivisions", m_BakeSubdivisionsPerSegment, 4, 200);

        EditorGUILayout.Space();
        DrawControlPointActions();
        DrawControlPointList();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);
        m_BakeSpacing = EditorGUILayout.FloatField("Waypoint Spacing", m_BakeSpacing);
        m_ReplaceExistingBakedPoints = EditorGUILayout.Toggle(
            "Replace Existing Baked Points", m_ReplaceExistingBakedPoints);
        m_InterpolateControlMetadata = EditorGUILayout.Toggle(
            "Interpolate Control Metadata", m_InterpolateControlMetadata);
        m_BakedWaypointEulerPerTick = EditorGUILayout.Vector3Field(
            "Waypoint Euler Per Tick", m_BakedWaypointEulerPerTick);
        m_BakedWaypointScale = EditorGUILayout.Vector3Field(
            "Waypoint Local Scale", m_BakedWaypointScale);

        bool canBake =
            m_PathParent != null &&
            HasRuntimeTarget() &&
            CountValidControlPoints() >= 2 &&
            IsFinite(m_BakeSpacing) &&
            m_BakeSpacing > 0f;

        using (new EditorGUI.DisabledScope(!canBake))
        {
            if (GUILayout.Button("Bake Evenly Spaced Waypoints", GUILayout.Height(30f)))
            {
                BakeWaypoints();
            }
        }

        using (new EditorGUI.DisabledScope(m_PathParent == null))
        {
            if (GUILayout.Button("Clear Baked Waypoints"))
            {
                ClearBakedWaypoints();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Move the selected control in the Scene view. Cyan midpoint handles insert controls. " +
            "The spline is editor-only; Bake writes the exact Transform path used by every boulder in the networked spawner pool, Boulder, and LocalBoulderRunOnce. " +
            "Control metadata preserves per-point rotation and scale; rotation is interpreted by the boulder as Euler spin per tick.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawControlPointActions()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Point"))
        {
            AddControlPoint();
        }

        using (new EditorGUI.DisabledScope(!IsSelectedPointValid()))
        {
            if (GUILayout.Button("Insert After Selected"))
            {
                InsertControlPointAfter(m_SelectedPointIndex);
            }

            if (GUILayout.Button("Delete Selected"))
            {
                DeleteSelectedControlPoint();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(m_PathSurface == null || !IsSelectedPointValid()))
        {
            if (GUILayout.Button("Project Selected"))
            {
                ProjectSelectedControlPoint();
            }
        }

        using (new EditorGUI.DisabledScope(m_PathSurface == null || CountValidControlPoints() == 0))
        {
            if (GUILayout.Button("Project All"))
            {
                ProjectAllControlPoints();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(m_ControlPointParent == null))
        {
            if (GUILayout.Button("Load Controls From Parent"))
            {
                LoadControlPointsFromParent();
            }
        }

        Transform[] sourcePoints = GetCurrentRuntimePath();
        using (new EditorGUI.DisabledScope(sourcePoints == null || sourcePoints.Length == 0))
        {
            if (GUILayout.Button("Create Controls From Current Path"))
            {
                CreateControlsFromCurrentPath(sourcePoints);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawControlPointList()
    {
        RemoveNullControlReferences();

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(
            "Control Points (" + m_ControlPoints.Count + ")",
            EditorStyles.boldLabel);

        for (int index = 0; index < m_ControlPoints.Count; index++)
        {
            EditorGUILayout.BeginHorizontal();

            bool selected = index == m_SelectedPointIndex;
            if (GUILayout.Toggle(selected, (index + 1).ToString(), "Button", GUILayout.Width(38f)) != selected)
            {
                m_SelectedPointIndex = index;
                Selection.activeTransform = m_ControlPoints[index];
                SceneView.RepaintAll();
            }

            Transform newPoint = (Transform)EditorGUILayout.ObjectField(
                m_ControlPoints[index], typeof(Transform), true);
            if (newPoint != m_ControlPoints[index])
            {
                Undo.RecordObject(this, "Change Boulder Spline Control");
                m_ControlPoints[index] = newPoint;
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("Up", GUILayout.Width(34f)))
                {
                    MoveControlPoint(index, index - 1);
                }
            }

            using (new EditorGUI.DisabledScope(index >= m_ControlPoints.Count - 1))
            {
                if (GUILayout.Button("Down", GUILayout.Width(45f)))
                {
                    MoveControlPoint(index, index + 1);
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    private void OnSceneViewGui(SceneView sceneView)
    {
        RemoveNullControlReferences();
        int count = m_ControlPoints.Count;
        if (count == 0) { return; }

        DrawSplinePreview();

        for (int index = 0; index < count; index++)
        {
            Transform point = m_ControlPoints[index];
            if (point == null) { continue; }

            float size = HandleUtility.GetHandleSize(point.position);
            Handles.color = index == m_SelectedPointIndex
                ? new Color(1f, 0.65f, 0.1f)
                : new Color(0.95f, 0.85f, 0.25f);

            if (Handles.Button(
                    point.position,
                    Quaternion.identity,
                    size * 0.075f,
                    size * 0.1f,
                    Handles.SphereHandleCap))
            {
                m_SelectedPointIndex = index;
                Selection.activeTransform = point;
                Repaint();
            }

            Handles.Label(point.position + Vector3.up * size * 0.12f, "P" + index);
        }

        for (int segmentIndex = 0; segmentIndex < count - 1; segmentIndex++)
        {
            Vector3 midpoint = GetProjectedPreviewPosition(
                EvaluateSplineSegment(segmentIndex, 0.5f));
            float size = HandleUtility.GetHandleSize(midpoint);
            Handles.color = Color.cyan;
            if (Handles.Button(
                    midpoint,
                    Quaternion.identity,
                    size * 0.045f,
                    size * 0.075f,
                    Handles.DotHandleCap))
            {
                InsertControlPointAfter(segmentIndex);
                return;
            }
        }

        if (!IsSelectedPointValid()) { return; }

        Transform selectedPoint = m_ControlPoints[m_SelectedPointIndex];
        EditorGUI.BeginChangeCheck();
        Vector3 movedPosition = Handles.PositionHandle(
            selectedPoint.position, selectedPoint.rotation);
        if (!EditorGUI.EndChangeCheck()) { return; }

        Undo.RecordObject(selectedPoint, "Move Boulder Spline Control");
        if (m_AutoProjectControls)
        {
            Vector3 projectedPosition;
            Vector3 projectedNormal;
            if (TryProjectPoint(movedPosition, out projectedPosition, out projectedNormal))
            {
                movedPosition = projectedPosition;
            }
        }

        selectedPoint.position = movedPosition;
        EditorUtility.SetDirty(selectedPoint);
        Repaint();
    }

    private void DrawSplinePreview()
    {
        int count = m_ControlPoints.Count;
        if (count < 2) { return; }

        int subdivisions = Mathf.Clamp(m_SubdivisionsPerSegment, 2, 100);
        Handles.color = new Color(0.15f, 0.85f, 1f, 1f);

        Vector3 previous = GetProjectedPreviewPosition(EvaluateSplineSegment(0, 0f));
        for (int segmentIndex = 0; segmentIndex < count - 1; segmentIndex++)
        {
            for (int step = 1; step <= subdivisions; step++)
            {
                float t = (float)step / subdivisions;
                Vector3 current = GetProjectedPreviewPosition(
                    EvaluateSplineSegment(segmentIndex, t));
                Handles.DrawAAPolyLine(3f, previous, current);
                previous = current;
            }
        }
    }

    private Vector3 EvaluateSplineSegment(int segmentIndex, float t)
    {
        int count = m_ControlPoints.Count;
        if (count == 0) { return Vector3.zero; }
        if (count == 1) { return m_ControlPoints[0].position; }

        segmentIndex = Mathf.Clamp(segmentIndex, 0, count - 2);
        t = Mathf.Clamp01(t);

        Vector3 point1 = m_ControlPoints[segmentIndex].position;
        Vector3 point2 = m_ControlPoints[segmentIndex + 1].position;
        Vector3 point0 = segmentIndex > 0
            ? m_ControlPoints[segmentIndex - 1].position
            : point1;
        Vector3 point3 = segmentIndex + 2 < count
            ? m_ControlPoints[segmentIndex + 2].position
            : point2;

        float tSquared = t * t;
        float tCubed = tSquared * t;
        return 0.5f *
               ((2f * point1) +
                (-point0 + point2) * t +
                (2f * point0 - 5f * point1 + 4f * point2 - point3) * tSquared +
                (-point0 + 3f * point1 - 3f * point2 + point3) * tCubed);
    }

    private Vector3 GetProjectedPreviewPosition(Vector3 position)
    {
        if (m_PathSurface == null) { return position; }

        Vector3 projectedPosition;
        Vector3 projectedNormal;
        return TryProjectPoint(position, out projectedPosition, out projectedNormal)
            ? projectedPosition
            : position;
    }

    private bool TryProjectPoint_old2(
        Vector3 sourcePosition,
        out Vector3 projectedPosition,
        out Vector3 projectedNormal)
    {
        projectedPosition = sourcePosition; 
        projectedNormal = Vector3.up;

        if (m_PathSurface == null || !m_PathSurface.enabled)
            return false;

        Vector3 direction = m_ProjectionDirection;
        if (!IsFinite(direction) || direction.sqrMagnitude < 0.000001f)
            direction = Vector3.down;

        direction.Normalize();

        float distance = IsFinite(m_ProjectionDistance)
            ? Mathf.Max(0.01f, m_ProjectionDistance)
            : 1000f;

       Ray ray = new Ray(sourcePosition, direction);

        RaycastHit[] hits = Physics.RaycastAll(ray, distance * 2f);

System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

RaycastHit? bestHit = null;
int upwardHits = 0;

foreach (RaycastHit hit in hits)
{
    if (hit.collider != m_PathSurface)
        continue;

    if (hit.normal.y > 0.25f)
    {
        upwardHits++;

        if (upwardHits == 2)
        {
            bestHit = hit;
            break;
        }
    }
}

// Fallback to the first upward-facing hit if there wasn't a second.
if (!bestHit.HasValue)
{
    foreach (RaycastHit hit in hits)
    {
        if (hit.collider == m_PathSurface && hit.normal.y > 0.25f)
        {
            bestHit = hit;
            break;
        }
    }
}

        if (!bestHit.HasValue)
            return false;

        float offset = IsFinite(m_NormalOffsetDistance)
            ? m_NormalOffsetDistance
            : 0f;

        projectedNormal = bestHit.Value.normal.sqrMagnitude > 0.000001f
            ? bestHit.Value.normal.normalized
            : Vector3.up;

        projectedPosition = bestHit.Value.point + projectedNormal * offset;

        return true;
    }

    private bool TryProjectPoint(
        Vector3 sourcePosition,
        out Vector3 projectedPosition,
        out Vector3 projectedNormal)
    {
        projectedPosition = sourcePosition;
        projectedNormal = Vector3.up;
        if (m_PathSurface == null || !m_PathSurface.enabled) { return false; }

        Vector3 direction = m_ProjectionDirection;
if (!IsFinite(direction) || direction.sqrMagnitude < 0.000001f)
{
    direction = Vector3.down;
}
direction.Normalize();

float distance = IsFinite(m_ProjectionDistance)
    ? Mathf.Max(0.01f, m_ProjectionDistance)
    : 1000f;

Ray ray = new Ray(sourcePosition, direction);

RaycastHit hit;
if (!m_PathSurface.Raycast(ray, out hit, distance))
{
    return false;
}

        float offset = IsFinite(m_NormalOffsetDistance)
            ? m_NormalOffsetDistance
            : 0f;
        projectedNormal = hit.normal.sqrMagnitude > 0.000001f
            ? hit.normal.normalized
            : Vector3.up;
        projectedPosition = hit.point + projectedNormal * offset;
        return true;
    }

    private bool TryProjectPoint_Old(
        Vector3 sourcePosition,
        out Vector3 projectedPosition,
        out Vector3 projectedNormal)
    {
        projectedPosition = sourcePosition;
        projectedNormal = Vector3.up;
        if (m_PathSurface == null || !m_PathSurface.enabled) { return false; }

        Vector3 direction = m_ProjectionDirection;
        if (!IsFinite(direction) || direction.sqrMagnitude < 0.000001f)
        {
            direction = Vector3.down;
        }
        direction.Normalize();

        float distance = IsFinite(m_ProjectionDistance)
            ? Mathf.Max(0.01f, m_ProjectionDistance)
            : 1000f;
        Ray ray = new Ray(sourcePosition - direction * distance, direction);

        RaycastHit hit;
        if (!m_PathSurface.Raycast(ray, out hit, distance * 2f))
        {
            return false;
        }

        float offset = IsFinite(m_NormalOffsetDistance)
            ? m_NormalOffsetDistance
            : 0f;
        projectedNormal = hit.normal.sqrMagnitude > 0.000001f
            ? hit.normal.normalized
            : Vector3.up;
        projectedPosition = hit.point + projectedNormal * offset;
        return true;
    }

    private void AddControlPoint()
    {
        Vector3 position;
        int count = m_ControlPoints.Count;
        if (count >= 2)
        {
            position = m_ControlPoints[count - 1].position +
                       (m_ControlPoints[count - 1].position - m_ControlPoints[count - 2].position);
        }
        else if (count == 1)
        {
            position = m_ControlPoints[0].position + Vector3.forward * 2f;
        }
        else if (SceneView.lastActiveSceneView != null)
        {
            position = SceneView.lastActiveSceneView.pivot;
        }
        else if (m_PathParent != null)
        {
            position = m_PathParent.position;
        }
        else
        {
            position = Vector3.zero;
        }

        CreateControlPoint(position, count);
    }

    private void InsertControlPointAfter(int index)
    {
        if (m_ControlPoints.Count == 0)
        {
            AddControlPoint();
            return;
        }

        index = Mathf.Clamp(index, 0, m_ControlPoints.Count - 1);
        Vector3 position;
        if (index < m_ControlPoints.Count - 1)
        {
            position = EvaluateSplineSegment(index, 0.5f);
        }
        else if (m_ControlPoints.Count >= 2)
        {
            position = m_ControlPoints[index].position +
                       (m_ControlPoints[index].position - m_ControlPoints[index - 1].position) * 0.5f;
        }
        else
        {
            position = m_ControlPoints[index].position + Vector3.forward;
        }

        CreateControlPoint(position, index + 1);
    }

    private void CreateControlPoint(Vector3 position, int insertIndex)
    {
        Transform parent = GetOrCreateControlPointParent();
        if (parent == null)
        {
            EditorUtility.DisplayDialog(
                "Boulder Spline Path",
                "Assign a Control Point Parent or Baked Path Parent first.",
                "OK");
            return;
        }

        if (m_AutoProjectControls)
        {
            Vector3 projectedPosition;
            Vector3 projectedNormal;
            if (TryProjectPoint(position, out projectedPosition, out projectedNormal))
            {
                position = projectedPosition;
            }
        }

        Undo.RecordObject(this, "Add Boulder Spline Control");
        GameObject pointObject = new GameObject(ControlPointPrefix + (insertIndex + 1));
        Undo.RegisterCreatedObjectUndo(pointObject, "Add Boulder Spline Control");
        pointObject.transform.SetParent(parent, true);
        pointObject.transform.position = position;
        pointObject.transform.localScale = Vector3.one;

        insertIndex = Mathf.Clamp(insertIndex, 0, m_ControlPoints.Count);
        m_ControlPoints.Insert(insertIndex, pointObject.transform);
        m_SelectedPointIndex = insertIndex;
        RenameControlPointsSequentially();
        Selection.activeTransform = pointObject.transform;

        Repaint();
        SceneView.RepaintAll();
    }

    private Transform GetOrCreateControlPointParent()
    {
        if (m_ControlPointParent != null) { return m_ControlPointParent; }
        if (m_PathParent == null) { return null; }

        GameObject parentObject = new GameObject("Boulder Spline Controls");
        Undo.RegisterCreatedObjectUndo(parentObject, "Create Boulder Spline Controls");
        parentObject.transform.SetParent(m_PathParent, false);
        m_ControlPointParent = parentObject.transform;
        return m_ControlPointParent;
    }

    private void DeleteSelectedControlPoint()
    {
        if (!IsSelectedPointValid()) { return; }

        Undo.RecordObject(this, "Delete Boulder Spline Control");
        Transform point = m_ControlPoints[m_SelectedPointIndex];
        m_ControlPoints.RemoveAt(m_SelectedPointIndex);
        if (point != null)
        {
            Undo.DestroyObjectImmediate(point.gameObject);
        }

        if (m_ControlPoints.Count == 0)
        {
            m_SelectedPointIndex = -1;
        }
        else
        {
            m_SelectedPointIndex = Mathf.Min(m_SelectedPointIndex, m_ControlPoints.Count - 1);
            Selection.activeTransform = m_ControlPoints[m_SelectedPointIndex];
        }

        RenameControlPointsSequentially();

        Repaint();
        SceneView.RepaintAll();
    }

    private void MoveControlPoint(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= m_ControlPoints.Count) { return; }
        if (toIndex < 0 || toIndex >= m_ControlPoints.Count) { return; }

        Undo.RecordObject(this, "Reorder Boulder Spline Controls");
        Transform point = m_ControlPoints[fromIndex];
        m_ControlPoints.RemoveAt(fromIndex);
        m_ControlPoints.Insert(toIndex, point);
        m_SelectedPointIndex = toIndex;
        RenameControlPointsSequentially();
        Repaint();
        SceneView.RepaintAll();
    }

    private void ProjectSelectedControlPoint()
    {
        if (!IsSelectedPointValid()) { return; }
        ProjectControlPoint(m_ControlPoints[m_SelectedPointIndex]);
    }

    private void ProjectAllControlPoints()
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Project Boulder Spline Controls");

        for (int index = 0; index < m_ControlPoints.Count; index++)
        {
            ProjectControlPoint(m_ControlPoints[index]);
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }

    private void ProjectControlPoint(Transform point)
    {
        if (point == null) { return; }

        Vector3 projectedPosition;
        Vector3 projectedNormal;
        if (!TryProjectPoint(point.position, out projectedPosition, out projectedNormal))
        {
            return;
        }

        Undo.RecordObject(point, "Project Boulder Spline Control");
        point.position = projectedPosition;
        EditorUtility.SetDirty(point);
    }

    private void LoadControlPointsFromParent()
    {
        if (m_ControlPointParent == null) { return; }

        Undo.RecordObject(this, "Load Boulder Spline Controls");
        m_ControlPoints.Clear();
        for (int index = 0; index < m_ControlPointParent.childCount; index++)
        {
            Transform child = m_ControlPointParent.GetChild(index);
            if (child != null && child.name.StartsWith(ControlPointPrefix))
            {
                m_ControlPoints.Add(child);
            }
        }

        m_SelectedPointIndex = m_ControlPoints.Count > 0 ? 0 : -1;
        RenameControlPointsSequentially();
        Repaint();
        SceneView.RepaintAll();
    }

    private void CreateControlsFromCurrentPath(Transform[] sourcePoints)
    {
        if (sourcePoints == null || sourcePoints.Length == 0) { return; }

        Transform parent = GetOrCreateControlPointParent();
        if (parent == null) { return; }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Spline Controls From Boulder Path");
        Undo.RecordObject(this, "Create Spline Controls From Boulder Path");
        m_ControlPoints.Clear();

        for (int index = 0; index < sourcePoints.Length; index++)
        {
            Transform source = sourcePoints[index];
            if (source == null) { continue; }

            GameObject pointObject = new GameObject(ControlPointPrefix + (index + 1));
            Undo.RegisterCreatedObjectUndo(
                pointObject, "Create Spline Controls From Boulder Path");
            pointObject.transform.SetParent(parent, true);
            pointObject.transform.position = source.position;
            pointObject.transform.rotation = source.rotation;
            pointObject.transform.localScale = source.localScale;
            m_ControlPoints.Add(pointObject.transform);
        }

        m_InterpolateControlMetadata = true;
        m_SelectedPointIndex = m_ControlPoints.Count > 0 ? 0 : -1;
        RenameControlPointsSequentially();
        Undo.CollapseUndoOperations(undoGroup);
        Repaint();
        SceneView.RepaintAll();
    }

    private void BakeWaypoints()
    {
        RemoveNullControlReferences();
        if (m_ControlPoints.Count < 2 || m_PathParent == null) { return; }

        float spacing = m_BakeSpacing;
        if (!IsFinite(spacing) || spacing <= 0f)
        {
            EditorUtility.DisplayDialog(
                "Boulder Spline Path", "Waypoint Spacing must be greater than zero.", "OK");
            return;
        }

        List<PathSample> denseSamples = BuildDenseSamples();
        if (denseSamples.Count < 2 || denseSamples[denseSamples.Count - 1].distance <= 0.0001f)
        {
            EditorUtility.DisplayDialog(
                "Boulder Spline Path", "The spline has no measurable length.", "OK");
            return;
        }

        List<Vector3> bakedPositions = new List<Vector3>();
        List<Vector3> bakedNormals = new List<Vector3>();
        List<Quaternion> bakedRotations = new List<Quaternion>();
        List<Vector3> bakedScales = new List<Vector3>();
        BuildEvenlySpacedSamples(
            denseSamples,
            spacing,
            bakedPositions,
            bakedNormals,
            bakedRotations,
            bakedScales);

        if (bakedPositions.Count < 2)
        {
            EditorUtility.DisplayDialog(
                "Boulder Spline Path", "Baking produced fewer than two waypoints.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Bake Boulder Spline Waypoints");
        RecordRuntimePathTargetsForUndo("Bake Boulder Spline Waypoints");

        if (m_ReplaceExistingBakedPoints)
        {
            DestroyBakedPointObjects();
        }

        List<Transform> combinedPath = new List<Transform>();
        if (!m_ReplaceExistingBakedPoints)
        {
            AddExistingRuntimePath(combinedPath, GetCurrentRuntimePath());
        }

        Quaternion waypointLocalRotation = Quaternion.Euler(m_BakedWaypointEulerPerTick);
        Vector3 waypointScale = IsFinite(m_BakedWaypointScale)
            ? m_BakedWaypointScale
            : Vector3.one;

        for (int index = 0; index < bakedPositions.Count; index++)
        {
            GameObject pointObject = new GameObject(
                BakedPointPrefix + (index + 1).ToString("D3"));
            Undo.RegisterCreatedObjectUndo(pointObject, "Bake Boulder Spline Waypoints");
            pointObject.transform.SetParent(m_PathParent, true);
            pointObject.transform.position = bakedPositions[index];
            if (m_InterpolateControlMetadata)
            {
                pointObject.transform.rotation = bakedRotations[index];
                pointObject.transform.localScale = bakedScales[index];
            }
            else
            {
                pointObject.transform.localRotation = waypointLocalRotation;
                pointObject.transform.localScale = waypointScale;
            }
            combinedPath.Add(pointObject.transform);
        }

        Transform[] bakedTransforms = combinedPath.ToArray();
        AssignRuntimePaths(bakedTransforms);
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeTransform = m_PathParent;
        EditorUtility.DisplayDialog(
            "Boulder Spline Path",
            "Baked " + bakedTransforms.Length + " evenly spaced waypoints.",
            "OK");
    }

    private List<PathSample> BuildDenseSamples()
    {
        int subdivisions = Mathf.Clamp(m_BakeSubdivisionsPerSegment, 4, 200);
        int capacity = (m_ControlPoints.Count - 1) * subdivisions + 1;
        List<PathSample> samples = new List<PathSample>(capacity);

        Vector3 previousPosition = Vector3.zero;
        float cumulativeDistance = 0f;
        for (int segmentIndex = 0; segmentIndex < m_ControlPoints.Count - 1; segmentIndex++)
        {
            int firstStep = segmentIndex == 0 ? 0 : 1;
            for (int step = firstStep; step <= subdivisions; step++)
            {
                float t = (float)step / subdivisions;
                Vector3 position = EvaluateSplineSegment(segmentIndex, t);
                Vector3 normal = Vector3.up;
                Quaternion rotation = Quaternion.Slerp(
                    m_ControlPoints[segmentIndex].rotation,
                    m_ControlPoints[segmentIndex + 1].rotation,
                    t);
                Vector3 scale = Vector3.Lerp(
                    m_ControlPoints[segmentIndex].localScale,
                    m_ControlPoints[segmentIndex + 1].localScale,
                    t);

                Vector3 projectedPosition;
                Vector3 projectedNormal;
                if (TryProjectPoint(position, out projectedPosition, out projectedNormal))
                {
                    position = projectedPosition;
                    normal = projectedNormal;
                }

                if (samples.Count > 0)
                {
                    cumulativeDistance += Vector3.Distance(previousPosition, position);
                }

                samples.Add(new PathSample(
                    position,
                    normal,
                    rotation,
                    scale,
                    cumulativeDistance));
                previousPosition = position;
            }
        }

        return samples;
    }

    private static void BuildEvenlySpacedSamples(
        List<PathSample> denseSamples,
        float spacing,
        List<Vector3> positions,
        List<Vector3> normals,
        List<Quaternion> rotations,
        List<Vector3> scales)
    {
        float totalDistance = denseSamples[denseSamples.Count - 1].distance;
        float targetDistance = 0f;
        int upperSampleIndex = 1;

        while (targetDistance < totalDistance)
        {
            while (upperSampleIndex < denseSamples.Count - 1 &&
                   denseSamples[upperSampleIndex].distance < targetDistance)
            {
                upperSampleIndex++;
            }

            PathSample lower = denseSamples[upperSampleIndex - 1];
            PathSample upper = denseSamples[upperSampleIndex];
            float segmentLength = upper.distance - lower.distance;
            float interpolation = segmentLength > 0.000001f
                ? (targetDistance - lower.distance) / segmentLength
                : 0f;

            positions.Add(Vector3.Lerp(lower.position, upper.position, interpolation));
            Vector3 normal = Vector3.Lerp(lower.normal, upper.normal, interpolation);
            normals.Add(normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up);
            rotations.Add(Quaternion.Slerp(lower.rotation, upper.rotation, interpolation));
            scales.Add(Vector3.Lerp(lower.scale, upper.scale, interpolation));
            targetDistance += spacing;
        }

        PathSample last = denseSamples[denseSamples.Count - 1];
        if (positions.Count == 0 ||
            Vector3.Distance(positions[positions.Count - 1], last.position) > 0.0001f)
        {
            positions.Add(last.position);
            normals.Add(last.normal);
            rotations.Add(last.rotation);
            scales.Add(last.scale);
        }
    }

    private void ClearBakedWaypoints()
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear Baked Boulder Waypoints");
        RecordRuntimePathTargetsForUndo("Clear Baked Boulder Waypoints");
        DestroyBakedPointObjects();
        AssignRuntimePaths(new Transform[0]);
        Undo.CollapseUndoOperations(undoGroup);
    }

    private void DestroyBakedPointObjects()
    {
        if (m_PathParent == null) { return; }

        List<GameObject> objectsToDestroy = new List<GameObject>();
        AddRuntimePathObjectsToDestroy(
            objectsToDestroy,
            m_Boulder != null ? m_Boulder.m_PathPoints : null);
        AddRuntimePathObjectsToDestroy(
            objectsToDestroy,
            m_LocalBoulder != null ? m_LocalBoulder.m_PathPoints : null);
        AddNetworkedRuntimePathObjectsToDestroy(objectsToDestroy);

        for (int childIndex = m_PathParent.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = m_PathParent.GetChild(childIndex);
            if (child != null && child.name.StartsWith(BakedPointPrefix))
            {
                if (!objectsToDestroy.Contains(child.gameObject))
                {
                    objectsToDestroy.Add(child.gameObject);
                }
            }
        }

        for (int index = 0; index < objectsToDestroy.Count; index++)
        {
            if (objectsToDestroy[index] != null)
            {
                Undo.DestroyObjectImmediate(objectsToDestroy[index]);
            }
        }
    }

    private void AddRuntimePathObjectsToDestroy(
        List<GameObject> objectsToDestroy,
        Transform[] pathPoints)
    {
        if (pathPoints == null) { return; }

        for (int index = 0; index < pathPoints.Length; index++)
        {
            Transform point = pathPoints[index];
            if (point == null || point.parent != m_PathParent) { continue; }
            if (point.name.StartsWith(ControlPointPrefix)) { continue; }
            if (!objectsToDestroy.Contains(point.gameObject))
            {
                objectsToDestroy.Add(point.gameObject);
            }
        }
    }

    private static void AddExistingRuntimePath(
        List<Transform> destination,
        Transform[] pathPoints)
    {
        if (pathPoints == null) { return; }

        for (int index = 0; index < pathPoints.Length; index++)
        {
            Transform point = pathPoints[index];
            if (point != null && !destination.Contains(point))
            {
                destination.Add(point);
            }
        }
    }

    private void AddNetworkedRuntimePathObjectsToDestroy(
        List<GameObject> objectsToDestroy)
    {
        if (m_NetworkedSpawner == null || m_NetworkedSpawner.m_BoulderPool == null)
        {
            return;
        }

        NetworkedChaseBoulder[] pool = m_NetworkedSpawner.m_BoulderPool;
        for (int index = 0; index < pool.Length; index++)
        {
            NetworkedChaseBoulder boulder = pool[index];
            if (boulder != null)
            {
                AddRuntimePathObjectsToDestroy(objectsToDestroy, boulder.m_PathPoints);
            }
        }
    }

    private void AssignRuntimePaths(Transform[] pathPoints)
    {
        if (m_NetworkedSpawner != null && m_NetworkedSpawner.m_BoulderPool != null)
        {
            NetworkedChaseBoulder[] pool = m_NetworkedSpawner.m_BoulderPool;
            for (int index = 0; index < pool.Length; index++)
            {
                NetworkedChaseBoulder boulder = pool[index];
                if (boulder == null) { continue; }

                Undo.RecordObject(boulder, "Assign Networked Chase Boulder Path");
                boulder.m_PathPoints = pathPoints;
                EditorUtility.SetDirty(boulder);
                PrefabUtility.RecordPrefabInstancePropertyModifications(boulder);
            }
        }

        if (m_Boulder != null)
        {
            Undo.RecordObject(m_Boulder, "Assign Boulder Path");
            m_Boulder.m_PathPoints = pathPoints;
            EditorUtility.SetDirty(m_Boulder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(m_Boulder);
        }

        if (m_LocalBoulder != null)
        {
            Undo.RecordObject(m_LocalBoulder, "Assign Local Boulder Path");
            m_LocalBoulder.m_PathPoints = pathPoints;
            EditorUtility.SetDirty(m_LocalBoulder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(m_LocalBoulder);
        }
    }

    private void RecordRuntimePathTargetsForUndo(string operationName)
    {
        if (m_NetworkedSpawner != null && m_NetworkedSpawner.m_BoulderPool != null)
        {
            NetworkedChaseBoulder[] pool = m_NetworkedSpawner.m_BoulderPool;
            for (int index = 0; index < pool.Length; index++)
            {
                if (pool[index] != null)
                {
                    Undo.RecordObject(pool[index], operationName);
                }
            }
        }

        if (m_Boulder != null)
        {
            Undo.RecordObject(m_Boulder, operationName);
        }

        if (m_LocalBoulder != null)
        {
            Undo.RecordObject(m_LocalBoulder, operationName);
        }
    }

    private void RenameControlPointsSequentially()
    {
        for (int index = 0; index < m_ControlPoints.Count; index++)
        {
            Transform point = m_ControlPoints[index];
            if (point == null) { continue; }

            string desiredName = ControlPointPrefix + (index + 1);
            if (point.name == desiredName) { continue; }

            Undo.RecordObject(point.gameObject, "Rename Boulder Spline Controls");
            point.name = desiredName;

            if (m_ControlPointParent != null && point.parent == m_ControlPointParent)
            {
                Undo.RecordObject(point, "Reorder Boulder Spline Controls");
                point.SetSiblingIndex(index);
            }
        }
    }

    private Transform[] GetCurrentRuntimePath()
    {
        if (m_NetworkedSpawner != null && m_NetworkedSpawner.m_BoulderPool != null)
        {
            NetworkedChaseBoulder[] pool = m_NetworkedSpawner.m_BoulderPool;
            for (int index = 0; index < pool.Length; index++)
            {
                NetworkedChaseBoulder boulder = pool[index];
                if (boulder != null &&
                    boulder.m_PathPoints != null &&
                    boulder.m_PathPoints.Length > 0)
                {
                    return boulder.m_PathPoints;
                }
            }
        }

        if (m_LocalBoulder != null &&
            m_LocalBoulder.m_PathPoints != null &&
            m_LocalBoulder.m_PathPoints.Length > 0)
        {
            return m_LocalBoulder.m_PathPoints;
        }

        return m_Boulder != null ? m_Boulder.m_PathPoints : null;
    }

    private bool HasRuntimeTarget()
    {
        if (m_Boulder != null || m_LocalBoulder != null) { return true; }
        if (m_NetworkedSpawner == null || m_NetworkedSpawner.m_BoulderPool == null)
        {
            return false;
        }

        NetworkedChaseBoulder[] pool = m_NetworkedSpawner.m_BoulderPool;
        for (int index = 0; index < pool.Length; index++)
        {
            if (pool[index] != null) { return true; }
        }

        return false;
    }

    private int CountValidControlPoints()
    {
        int count = 0;
        for (int index = 0; index < m_ControlPoints.Count; index++)
        {
            if (m_ControlPoints[index] != null) { count++; }
        }
        return count;
    }

    private bool IsSelectedPointValid()
    {
        return m_SelectedPointIndex >= 0 &&
               m_SelectedPointIndex < m_ControlPoints.Count &&
               m_ControlPoints[m_SelectedPointIndex] != null;
    }

    private void RemoveNullControlReferences()
    {
        for (int index = m_ControlPoints.Count - 1; index >= 0; index--)
        {
            if (m_ControlPoints[index] == null)
            {
                m_ControlPoints.RemoveAt(index);
                if (m_SelectedPointIndex >= index) { m_SelectedPointIndex--; }
            }
        }

        if (m_SelectedPointIndex >= m_ControlPoints.Count)
        {
            m_SelectedPointIndex = m_ControlPoints.Count - 1;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private struct PathSample
    {
        public readonly Vector3 position;
        public readonly Vector3 normal;
        public readonly Quaternion rotation;
        public readonly Vector3 scale;
        public readonly float distance;

        public PathSample(
            Vector3 position,
            Vector3 normal,
            Quaternion rotation,
            Vector3 scale,
            float distance)
        {
            this.position = position;
            this.normal = normal;
            this.rotation = rotation;
            this.scale = scale;
            this.distance = distance;
        }
    }
}