#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ConvertMeshCollidersToBoxColliders
{
    [MenuItem("Tools/Colliders/Selection: MeshCollider -> BoxCollider")]
    public static void ConvertSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.Log("No GameObjects selected.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        int converted = 0;

        foreach (GameObject go in selected)
        {
            if (go == null) continue;

            MeshCollider[] meshColliders = go.GetComponents<MeshCollider>();
            if (meshColliders == null || meshColliders.Length == 0) continue;

            foreach (MeshCollider mc in meshColliders)
            {
                if (mc == null) continue;

                BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
                if (bc == null) continue;

                // Preserve common collider settings.
                bc.isTrigger = mc.isTrigger;
                bc.sharedMaterial = mc.sharedMaterial;
                bc.enabled = mc.enabled;

                // Approximate the box using the mesh bounds (local space).
                if (mc.sharedMesh != null)
                {
                    Bounds b = mc.sharedMesh.bounds;
                    bc.center = b.center;
                    bc.size = b.size;
                }
                else
                {
                    // Fallback: use world AABB and transform into local space (approximate).
                    Bounds wb = mc.bounds;
                    Transform t = go.transform;
                    bc.center = t.InverseTransformPoint(wb.center);

                    Vector3 worldSize = wb.size;
                    // Convert world size to local size (best-effort under rotation/non-uniform scale).
                    Vector3 localSize = t.InverseTransformVector(worldSize);
                    bc.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
                }

                Undo.DestroyObjectImmediate(mc);
                converted++;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"Converted {converted} MeshCollider(s) to BoxCollider(s) on selection.");
    }

    [MenuItem("Tools/Colliders/Selection: MeshCollider -> BoxCollider", true)]
    private static bool ValidateConvertSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0) return false;

        foreach (GameObject go in selected)
        {
            if (go == null) continue;
            if (go.GetComponent<MeshCollider>() != null) return true;
        }

        return false;
    }
}
#endif
