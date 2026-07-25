using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

[CustomEditor(typeof(GenericMetric))]
public class GenericMetricEditor : Editor
{
    private const string PrefServerBase = "BuggyPyramid.GenericMetric.ServerBase";
    private const string PrefClientKey = "BuggyPyramid.GenericMetric.ClientKey";
    private const string PrefEvent = "BuggyPyramid.GenericMetric.Event";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Metrics URL Generator", EditorStyles.boldLabel);

        string serverBase = EditorPrefs.GetString(PrefServerBase, "http://localhost:8080");
        string clientKey = EditorPrefs.GetString(PrefClientKey, "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000");
        string ev = EditorPrefs.GetString(PrefEvent, "test_generic");

        serverBase = EditorGUILayout.TextField("Server Base", serverBase);
        clientKey = EditorGUILayout.TextField("Client Key", clientKey);
        ev = EditorGUILayout.TextField("Event", ev);

        EditorPrefs.SetString(PrefServerBase, serverBase);
        EditorPrefs.SetString(PrefClientKey, clientKey);
        EditorPrefs.SetString(PrefEvent, ev);

        bool disabled = string.IsNullOrWhiteSpace(serverBase) || string.IsNullOrWhiteSpace(clientKey) || string.IsNullOrWhiteSpace(ev);
        using (new EditorGUI.DisabledScope(disabled))
        {
            if (GUILayout.Button("Generate & Set GenericMetric URL"))
            {
                GenerateAndSetUrl((GenericMetric)target, serverBase, clientKey, ev);
            }
        }

        EditorGUILayout.HelpBox(
            "Generates: {Server Base}/metrics/genericMetric/{base64_json} where base64_json = base64({\"clientkey\":\"...\",\"event\":\"...\"}) with a required 'a' inserted at index 15.\n" +
            "Reminder: VRChat requires https:// at runtime.",
            MessageType.Info);
    }

    private static void GenerateAndSetUrl(GenericMetric metric, string serverBase, string clientKey, string ev)
    {
        if (metric == null) return;

        string baseTrimmed = (serverBase ?? string.Empty).Trim();
        if (baseTrimmed.EndsWith("/")) baseTrimmed = baseTrimmed.TrimEnd('/');

        string json = "{\"clientkey\":\"" + EscapeJson(clientKey) + "\",\"event\":\"" + EscapeJson(ev) + "\"}";
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // Required marker: insert an 'a' at index 15 (same rule as submit time).
        if (b64.Length <= 15)
        {
            Debug.LogError("[GenericMetricEditor] Payload base64 unexpectedly short; cannot insert marker.");
            return;
        }
        b64 = b64.Substring(0, 15) + "a" + b64.Substring(15);

        string fullUrl = baseTrimmed + "/metrics/genericMetric/" + b64;

        Undo.RecordObject(metric, "Generate GenericMetric URL");

        // m_GenericMetricUrl is [SerializeField] private in GenericMetric.
        SerializedObject so = new SerializedObject(metric);
        SerializedProperty p = so.FindProperty("m_GenericMetricUrl");
        if (p == null)
        {
            Debug.LogError("[GenericMetricEditor] Missing serialized field: m_GenericMetricUrl");
            return;
        }

        p.boxedValue = new VRCUrl(fullUrl);
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(metric);
        Debug.Log("[GenericMetricEditor] Set generic metric URL on " + metric.name + ": " + fullUrl);
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
