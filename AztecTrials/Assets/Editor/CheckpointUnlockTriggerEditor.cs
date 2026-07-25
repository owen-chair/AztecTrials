using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

[CustomEditor(typeof(CheckpointUnlockTrigger))]
public class CheckpointUnlockTriggerEditor : Editor
{
    private const string PrefServerBase = "BuggyPyramid.CheckpointUnlock.ServerBase";
    private const string PrefClientKey = "BuggyPyramid.CheckpointUnlock.ClientKey";
    private const string PrefCheckpointName = "BuggyPyramid.CheckpointUnlock.CheckpointName";

    private static readonly string[] CheckpointNames =
    {
        "ZiplineCheckpointUnlocked",
        "BoulderTunnelCheckpointUnlocked",
        "crushingWallsCheckpointUnlocked",
        "jumpRoomCheckpointUnlocked"
    };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Metrics URL Generator", EditorStyles.boldLabel);

        string serverBase = EditorPrefs.GetString(PrefServerBase, "http://localhost:8080");
        string clientKey = EditorPrefs.GetString(PrefClientKey, "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000");
        string checkpointName = EditorPrefs.GetString(PrefCheckpointName, CheckpointNames[0]);

        serverBase = EditorGUILayout.TextField("Server Base", serverBase);
        clientKey = EditorGUILayout.TextField("Client Key", clientKey);

        int selectedIndex = Array.IndexOf(CheckpointNames, checkpointName);
        if (selectedIndex < 0) selectedIndex = 0;
        selectedIndex = EditorGUILayout.Popup("Checkpoint", selectedIndex, CheckpointNames);
        checkpointName = CheckpointNames[selectedIndex];

        EditorPrefs.SetString(PrefServerBase, serverBase);
        EditorPrefs.SetString(PrefClientKey, clientKey);
        EditorPrefs.SetString(PrefCheckpointName, checkpointName);

        bool disabled = string.IsNullOrWhiteSpace(serverBase) || string.IsNullOrWhiteSpace(clientKey) || string.IsNullOrWhiteSpace(checkpointName);
        using (new EditorGUI.DisabledScope(disabled))
        {
            if (GUILayout.Button("Generate & Set Checkpoint URL"))
            {
                GenerateAndSetUrl((CheckpointUnlockTrigger)target, serverBase, clientKey, checkpointName);
            }
        }

        EditorGUILayout.HelpBox(
            "Generates: {Server Base}/metrics/checkpointUnlock/{Checkpoint}/{base64_json} where base64_json = {\"clientkey\":\"...\"}.\n" +
            "Reminder: VRChat requires https:// at runtime.",
            MessageType.Info);
    }

    private static void GenerateAndSetUrl(CheckpointUnlockTrigger trigger, string serverBase, string clientKey, string checkpointName)
    {
        if (trigger == null) return;

        string baseTrimmed = (serverBase ?? string.Empty).Trim();
        if (baseTrimmed.EndsWith("/")) baseTrimmed = baseTrimmed.TrimEnd('/');

        string json = "{\"clientkey\":\"" + EscapeJson(clientKey) + "\"}";
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        string fullUrl = baseTrimmed + "/metrics/checkpointUnlock/" + checkpointName + "/" + b64;

        Undo.RecordObject(trigger, "Generate Checkpoint Unlock URL");

        // m_CheckpointUnlockUrl is [SerializeField] private in CheckpointUnlockTrigger.
        SerializedObject so = new SerializedObject(trigger);
        SerializedProperty p = so.FindProperty("m_CheckpointUnlockUrl");
        if (p == null)
        {
            Debug.LogError("[CheckpointUnlockTriggerEditor] Missing serialized field: m_CheckpointUnlockUrl");
            return;
        }

        p.boxedValue = new VRCUrl(fullUrl);
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(trigger);
        Debug.Log("[CheckpointUnlockTriggerEditor] Set checkpoint unlock URL on " + trigger.name + ": " + fullUrl);
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
