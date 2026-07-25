using UnityEditor;
using UnityEngine;

public class DropTransformToGroundTool : EditorWindow
{
    [SerializeField] private Transform m_Target;
    [SerializeField] private float m_HeightAboveHit = 0f;

    [MenuItem("Tools/Level/Drop Transform To Ground")]
    private static void Open()
    {
        GetWindow<DropTransformToGroundTool>("Drop To Ground");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Drop Transform To Ground", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        m_Target = (Transform)EditorGUILayout.ObjectField("Target", m_Target, typeof(Transform), true);
        m_HeightAboveHit = EditorGUILayout.FloatField("Height Above Hit", m_HeightAboveHit);

        using (new EditorGUI.DisabledScope(m_Target == null))
        {
            if (GUILayout.Button("Raycast Down & Place"))
            {
                Place();
            }
        }

        if (m_Target == null)
        {
            EditorGUILayout.HelpBox("Assign a Transform from the Hierarchy, then click the button.", MessageType.Info);
        }
    }

    private void Place()
    {
        if (m_Target == null) return;

        Vector3 origin = m_Target.position;
        const float maxDistance = 100000f;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            EditorUtility.DisplayDialog("Drop To Ground", "No collider hit when raycasting down.", "OK");
            return;
        }

        Undo.RecordObject(m_Target, "Drop Transform To Ground");
        m_Target.position = hit.point + (Vector3.up * m_HeightAboveHit);
        EditorUtility.SetDirty(m_Target);
    }
}
