using System;
using System.Linq;
using UnityEditor;
using UnityEditor.ProBuilder;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

public class ProBuilderRoadLoopTool : EditorWindow
{
    [SerializeField] private ProBuilderMesh m_Mesh;

    [Header("Loop Shape")]
    [SerializeField] private float m_Radius = 8f;
    [SerializeField] private int m_Steps = 32;
    [Tooltip("Signed lateral offset at the end of the loop (+right / -left, relative to the end face).")]
    [SerializeField] private float m_EndLateralOffset = -4f;
    [Tooltip("Optional roll (bank) around the tangent direction, in degrees.")]
    [SerializeField] private float m_RollDegrees = 0f;

    [MenuItem("Tools/ProBuilder/Road Loop Builder")]
    public static void Open()
    {
        GetWindow<ProBuilderRoadLoopTool>("Road Loop Builder");
    }

    private void OnSelectionChange()
    {
        if (m_Mesh == null)
        {
            m_Mesh = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<ProBuilderMesh>()
                : null;
        }
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Select a ProBuilder mesh, then select ONE face at the end of the road (Face selection mode).", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(8);

        m_Mesh = (ProBuilderMesh)EditorGUILayout.ObjectField("Mesh", m_Mesh, typeof(ProBuilderMesh), true);

        using (new EditorGUI.DisabledScope(true))
        {
            if (m_Mesh != null)
            {
                var sel = m_Mesh.GetSelectedFaces();
                EditorGUILayout.IntField("Selected Faces", sel != null ? sel.Length : 0);
            }
            else
            {
                EditorGUILayout.IntField("Selected Faces", 0);
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Loop Shape", EditorStyles.boldLabel);
        m_Radius = EditorGUILayout.FloatField("Radius", m_Radius);
        m_Steps = EditorGUILayout.IntField("Steps", m_Steps);
        m_EndLateralOffset = EditorGUILayout.FloatField("End Lateral Offset", m_EndLateralOffset);
        m_RollDegrees = EditorGUILayout.FloatField("Roll Degrees", m_RollDegrees);

        EditorGUILayout.Space(12);

        using (new EditorGUI.DisabledScope(m_Mesh == null))
        {
            if (GUILayout.Button("Build Loop"))
            {
                try
                {
                    BuildLoop();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }

    private void BuildLoop()
    {
        if (m_Mesh == null)
            throw new InvalidOperationException("No ProBuilderMesh assigned.");

        if (m_Radius <= 0.001f)
            throw new InvalidOperationException("Radius must be > 0.");

        int steps = Mathf.Clamp(m_Steps, 4, 512);

        Face[] selectedFaces = m_Mesh.GetSelectedFaces();
        if (selectedFaces == null || selectedFaces.Length != 1)
            throw new InvalidOperationException("Select exactly one ProBuilder face on the end of the road.");

        Face currentFace = selectedFaces[0];

        Undo.RegisterCompleteObjectUndo(m_Mesh, "Build Road Loop");

        Transform meshTransform = m_Mesh.transform;

        Vector3[] positions = m_Mesh.positions.ToArray();

        Vector3 startCenterWorld = AverageFaceCenterWorld(meshTransform, positions, currentFace);

        // Face normal from ProBuilder is in mesh local space.
        Vector3 startNormalWorld = meshTransform.TransformDirection(UnityEngine.ProBuilder.Math.Normal(m_Mesh, currentFace)).normalized;
        if (startNormalWorld.sqrMagnitude < 1e-8f)
            throw new InvalidOperationException("Selected face normal is invalid.");

        Vector3 forward = startNormalWorld;

        // Stable frame: right is defined relative to world-up and forward.
        Vector3 upHint = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(upHint, forward)) > 0.95f)
            upHint = Vector3.forward;

        Vector3 right = Vector3.Cross(upHint, forward).normalized;
        if (right.sqrMagnitude < 1e-8f)
            right = Vector3.right;

        Vector3 up = Vector3.Cross(forward, right).normalized;

        // Path points are expressed in world space.
        Vector3 prevCenter = startCenterWorld;

        // Ensure mesh is in a consistent state before beginning.
        m_Mesh.ToMesh();
        m_Mesh.Refresh();

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / (float)steps;
            float theta = (Mathf.PI * 2f) * t;

            // Base vertical loop around the forward/up plane.
            Vector3 loopDelta = (Mathf.Sin(theta) * forward * m_Radius) + ((1f - Mathf.Cos(theta)) * up * m_Radius);

            // Lateral drift to create a "side-shifted" loop exit.
            float lateral = Mathf.SmoothStep(0f, 1f, t) * m_EndLateralOffset;
            Vector3 driftDelta = right * lateral;

            Vector3 desiredCenter = startCenterWorld + loopDelta + driftDelta;

            Vector3 stepDelta = desiredCenter - prevCenter;
            float extrudeDistance = stepDelta.magnitude;
            if (extrudeDistance < 0.0001f)
            {
                prevCenter = desiredCenter;
                continue;
            }

            Vector3 tangent = stepDelta / extrudeDistance;

            Vector3 desiredUp = Vector3.Cross(right, tangent).normalized;
            if (desiredUp.sqrMagnitude < 1e-8f)
                desiredUp = up;

            Quaternion desiredRotation = Quaternion.LookRotation(tangent, desiredUp);
            if (Mathf.Abs(m_RollDegrees) > 0.001f)
                desiredRotation = Quaternion.AngleAxis(m_RollDegrees, tangent) * desiredRotation;

            // Rotate the current end face into the next step's frame BEFORE extruding.
            // This avoids shearing the side walls by extruding in one direction and then "teleporting"
            // the cap to a different position/orientation.
            positions = m_Mesh.positions.ToArray();
            RotateFaceInPlaceWorld(m_Mesh, meshTransform, ref positions, currentFace, desiredRotation, right, up);
            m_Mesh.positions = positions;
            m_Mesh.ToMesh();
            m_Mesh.Refresh();

            Face[] newFaces = m_Mesh.Extrude(new[] { currentFace }, ExtrudeMethod.FaceNormal, extrudeDistance);
            if (newFaces == null || newFaces.Length == 0)
                throw new InvalidOperationException("Extrude failed. Ensure the selected face is valid and not manifold/locked.");

            Face newEndFace = newFaces[0];

            currentFace = newEndFace;
            prevCenter = desiredCenter;
        }

        m_Mesh.ToMesh();
        m_Mesh.Refresh();
        // NOTE: Avoid Optimize() here. When the loop passes near itself, Optimize can weld coincident vertices
        // and make the geometry look like it "closed" unexpectedly.

        UnityEditor.EditorUtility.SetDirty(m_Mesh);
        Debug.Log("Road loop built.", m_Mesh);
    }

    private static Vector3 AverageFaceCenterWorld(Transform meshTransform, Vector3[] positionsLocal, Face face)
    {
        var idx = face.distinctIndexes;
        if (idx == null || idx.Count == 0)
            return meshTransform.position;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < idx.Count; i++)
        {
            int vi = idx[i];
            if (vi < 0 || vi >= positionsLocal.Length) continue;
            sum += meshTransform.TransformPoint(positionsLocal[vi]);
        }

        return sum / Mathf.Max(1, idx.Count);
    }

    private static void RotateFaceInPlaceWorld(
        ProBuilderMesh mesh,
        Transform meshTransform,
        ref Vector3[] positionsLocal,
        Face face,
        Quaternion desiredRotationWorld,
        Vector3 stableRightWorld,
        Vector3 fallbackUpWorld)
    {
        var idx = face.distinctIndexes;
        if (idx == null || idx.Count == 0)
            return;

        // ProBuilder frequently splits vertices (UV seams, hard edges). Those coincident vertices are tracked in
        // shared vertex groups. If we only move the face's local indexes, adjacent geometry may not follow,
        // producing visible cracks that look like disconnected segments.
        var shared = mesh.sharedVertices;
        int sharedCount = shared != null ? shared.Count : 0;
        bool[] groupDone = sharedCount > 0 ? new bool[sharedCount] : null;
        var localToGroup = sharedCount > 0
            ? new System.Collections.Generic.Dictionary<int, int>(positionsLocal.Length)
            : null;

        if (sharedCount > 0)
        {
            for (int gi = 0; gi < sharedCount; gi++)
            {
                var sv = shared[gi];
                if (sv == null) continue;
                for (int j = 0; j < sv.Count; j++)
                {
                    int vi = sv[j];
                    if (vi < 0 || vi >= positionsLocal.Length) continue;
                    localToGroup[vi] = gi;
                }
            }
        }

        Vector3 currentCenterWorld = AverageFaceCenterWorld(meshTransform, positionsLocal, face);

        Vector3 currentNormalWorld = meshTransform.TransformDirection(UnityEngine.ProBuilder.Math.Normal(mesh, face)).normalized;
        if (currentNormalWorld.sqrMagnitude < 1e-8f)
            currentNormalWorld = desiredRotationWorld * Vector3.forward;

        // Keep twist stable by deriving up from the stable right reference.
        Vector3 currentUpWorld = Vector3.Cross(stableRightWorld, currentNormalWorld).normalized;
        if (currentUpWorld.sqrMagnitude < 1e-8f)
            currentUpWorld = fallbackUpWorld;

        Quaternion currentRotationWorld = Quaternion.LookRotation(currentNormalWorld, currentUpWorld);

        Quaternion deltaRot = desiredRotationWorld * Quaternion.Inverse(currentRotationWorld);

        for (int i = 0; i < idx.Count; i++)
        {
            int vi = idx[i];
            if (vi < 0 || vi >= positionsLocal.Length) continue;

            if (sharedCount > 0 && localToGroup != null && localToGroup.TryGetValue(vi, out int gi))
            {
                if (groupDone[gi]) continue;
                groupDone[gi] = true;

                var sv = shared[gi];
                for (int j = 0; j < sv.Count; j++)
                {
                    int vj = sv[j];
                    if (vj < 0 || vj >= positionsLocal.Length) continue;

                    Vector3 w = meshTransform.TransformPoint(positionsLocal[vj]);
                    Vector3 w2 = currentCenterWorld + (deltaRot * (w - currentCenterWorld));
                    positionsLocal[vj] = meshTransform.InverseTransformPoint(w2);
                }
            }
            else
            {
                Vector3 w = meshTransform.TransformPoint(positionsLocal[vi]);
                Vector3 w2 = currentCenterWorld + (deltaRot * (w - currentCenterWorld));
                positionsLocal[vi] = meshTransform.InverseTransformPoint(w2);
            }
        }
    }
}
