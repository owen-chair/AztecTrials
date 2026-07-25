using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

[CustomEditor(typeof(PaginatedRanksHandler))]
public class PaginatedRanksHandlerEditor : Editor
{
    private const string PrefServerBase = "BuggyPyramid.PaginatedRanks.ServerBase";
    private const string PrefClientKey = "BuggyPyramid.PaginatedRanks.ClientKey";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("URL Generator", EditorStyles.boldLabel);

        string serverBase = EditorPrefs.GetString(PrefServerBase, "http://localhost:8080");
        string clientKey = EditorPrefs.GetString(PrefClientKey, "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000");

        serverBase = EditorGUILayout.TextField("Server Base", serverBase);
        clientKey = EditorGUILayout.TextField("Client Key", clientKey);

        EditorPrefs.SetString(PrefServerBase, serverBase);
        EditorPrefs.SetString(PrefClientKey, clientKey);

        bool disabled = string.IsNullOrWhiteSpace(serverBase) || string.IsNullOrWhiteSpace(clientKey);
        using (new EditorGUI.DisabledScope(disabled))
        {
            if (GUILayout.Button("Generate 100 Page URLs"))
            {
                GenerateUrls((PaginatedRanksHandler)target, serverBase, clientKey);
            }
        }
    }

    private static void GenerateUrls(PaginatedRanksHandler handler, string serverBase, string clientKey)
    {
        if (handler == null) return;

        string baseTrimmed = (serverBase ?? string.Empty).Trim();
        if (baseTrimmed.EndsWith("/")) baseTrimmed = baseTrimmed.TrimEnd('/');

        Undo.RecordObject(handler, "Generate Page URLs");

        Type handlerType = typeof(PaginatedRanksHandler);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        for (int page = 0; page < 100; page++)
        {
            string json = "{\"clientkey\":\"" + EscapeJson(clientKey) + "\",\"page\":" + page + "}";
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            string fullUrl = baseTrimmed + "/data/page/" + b64;

            FieldInfo field = handlerType.GetField("m_PageUrl" + page, flags);
            if (field == null)
            {
                Debug.LogError("[PaginatedRanksHandlerEditor] Missing field: m_PageUrl" + page);
                continue;
            }

            field.SetValue(handler, new VRCUrl(fullUrl));
        }

        EditorUtility.SetDirty(handler);
        Debug.Log("[PaginatedRanksHandlerEditor] Generated 100 page URLs on " + handler.name);
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
