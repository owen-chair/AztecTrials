using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BreakableEntity_Lite))]
public sealed class BreakableEntityLiteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BreakableEntity_Lite breakable = (BreakableEntity_Lite)target;

        EditorGUILayout.Space(8f);
        if (breakable.BreakableID < 0)
        {
            EditorGUILayout.HelpBox(
                "BreakableID is unset. Network backup events will not be sent until a unique ID is assigned.",
                MessageType.Warning);
        }

        if (HasDuplicateId(breakable))
        {
            EditorGUILayout.HelpBox(
                "Another loaded BreakableEntity_Lite uses this BreakableID. Generate a new unique ID.",
                MessageType.Error);
        }

        if (GUILayout.Button("Generate Unique Breakable ID"))
        {
            AssignUniqueId(breakable);
        }
    }

    private static void AssignUniqueId(BreakableEntity_Lite breakable)
    {
        BreakableEntity_Lite[] breakables =
            Resources.FindObjectsOfTypeAll<BreakableEntity_Lite>();
        HashSet<int> usedIds = new HashSet<int>();

        for (int index = 0; index < breakables.Length; index++)
        {
            BreakableEntity_Lite other = breakables[index];
            if (other == null || other == breakable || EditorUtility.IsPersistent(other))
            {
                continue;
            }

            if (other.BreakableID >= 0)
            {
                usedIds.Add(other.BreakableID);
            }
        }

        int uniqueId = 0;
        while (usedIds.Contains(uniqueId))
        {
            uniqueId++;
        }

        Undo.RecordObject(breakable, "Generate Unique Breakable ID");
        breakable.BreakableID = uniqueId;
        EditorUtility.SetDirty(breakable);

        if (breakable.gameObject.scene.IsValid())
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                breakable.gameObject.scene);
        }
    }

    private static bool HasDuplicateId(BreakableEntity_Lite breakable)
    {
        if (breakable.BreakableID < 0)
        {
            return false;
        }

        BreakableEntity_Lite[] breakables =
            Resources.FindObjectsOfTypeAll<BreakableEntity_Lite>();
        for (int index = 0; index < breakables.Length; index++)
        {
            BreakableEntity_Lite other = breakables[index];
            if (other == null || other == breakable || EditorUtility.IsPersistent(other))
            {
                continue;
            }

            if (other.BreakableID == breakable.BreakableID)
            {
                return true;
            }
        }

        return false;
    }
}