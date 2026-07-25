using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class MaterialMaxTextureSizeScanner : EditorWindow
{
    private sealed class Entry
    {
        public Material Material;
        public Texture MaxTexture;
        public int MaxSize;
        public string ExamplePath;
        public int UseCount;
    }

    private readonly List<Entry> _entries = new List<Entry>(512);
    private readonly Dictionary<int, List<Entry>> _groups = new Dictionary<int, List<Entry>>(16);
    private readonly Dictionary<int, bool> _sizeFoldouts = new Dictionary<int, bool>(16);

    private Vector2 _scroll;

    [MenuItem("Tools/Material Max Texture Size")]
    public static void ShowWindow()
    {
        GetWindow<MaterialMaxTextureSizeScanner>("Material Max Texture Size");
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Scan Scene", GUILayout.Width(120)))
            {
                ScanScene();
            }

            using (new EditorGUI.DisabledScope(_entries.Count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    _entries.Clear();
                    _groups.Clear();
                    _sizeFoldouts.Clear();
                }
            }

            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(6);

        if (_entries.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Click 'Scan Scene' to list all materials used in loaded scenes (including inactive objects), grouped by their maximum referenced texture size.",
                MessageType.Info
            );
            return;
        }

        int materialCount = _entries.Count;
        EditorGUILayout.LabelField($"Found {materialCount} material(s) used in loaded scenes:", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        List<int> keys = new List<int>(_groups.Keys);
        keys.Sort((a, b) => b.CompareTo(a)); // largest size first; 0 (no textures) last

        for (int k = 0; k < keys.Count; k++)
        {
            int sizeKey = keys[k];
            List<Entry> list = _groups[sizeKey];
            if (list == null) { continue; }

            if (!_sizeFoldouts.ContainsKey(sizeKey))
            {
                _sizeFoldouts[sizeKey] = false;
            }

            string label = sizeKey <= 0 ? $"No textures  ({list.Count})" : $"{sizeKey}  ({list.Count})";

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool expanded = EditorGUILayout.Foldout(_sizeFoldouts[sizeKey], label, true);
                    _sizeFoldouts[sizeKey] = expanded;

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Expand", GUILayout.Width(70)))
                    {
                        _sizeFoldouts[sizeKey] = true;
                        expanded = true;
                    }
                    if (GUILayout.Button("Collapse", GUILayout.Width(70)))
                    {
                        _sizeFoldouts[sizeKey] = false;
                        expanded = false;
                    }
                }
            }

            if (!_sizeFoldouts[sizeKey]) { continue; }

            for (int i = 0; i < list.Count; i++)
            {
                Entry e = list[i];
                if (e == null || !e.Material) { continue; }

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string pathLabel = string.IsNullOrEmpty(e.ExamplePath) ? "<unknown>" : e.ExamplePath;
                        if (GUILayout.Button(pathLabel, GUILayout.ExpandWidth(true)))
                        {
                            // Best-effort: select the first known object using the material.
                            // We only store the path string to keep the data lightweight.
                            // (Unity doesn't provide a fast path->object lookup without searching.)
                        }

                        using (new EditorGUI.DisabledScope(!e.Material))
                        {
                            if (GUILayout.Button("Select Mat", GUILayout.Width(90)))
                            {
                                Selection.activeObject = e.Material;
                                EditorGUIUtility.PingObject(e.Material);
                            }
                        }

                        using (new EditorGUI.DisabledScope(!e.MaxTexture))
                        {
                            if (GUILayout.Button("Select Tex", GUILayout.Width(90)))
                            {
                                Selection.activeObject = e.MaxTexture;
                                EditorGUIUtility.PingObject(e.MaxTexture);
                            }
                        }
                    }

                    EditorGUILayout.LabelField("Material", e.Material.name);
                    EditorGUILayout.LabelField("Uses", e.UseCount.ToString());

                    if (e.MaxTexture)
                    {
                        string texName = e.MaxTexture.name;
                        int w = 0, h = 0;
                        try
                        {
                            w = e.MaxTexture.width;
                            h = e.MaxTexture.height;
                        }
                        catch
                        {
                            // Some texture types may throw; keep UI resilient.
                        }

                        string sizeInfo = (w > 0 && h > 0) ? $"{Mathf.Max(w, h)} ({w}x{h})" : e.MaxSize.ToString();
                        EditorGUILayout.LabelField("Max Texture", $"{texName}  [{sizeInfo}]");
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Max Texture", "<none>");
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void ScanScene()
    {
        _entries.Clear();
        _groups.Clear();

        // Track unique materials and aggregate usage count + an example path.
        Dictionary<Material, Entry> byMaterial = new Dictionary<Material, Entry>();

        // Includes inactive + disabled objects; filter out assets/prefabs not in a loaded scene.
        Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int ri = 0; ri < renderers.Length; ri++)
        {
            Renderer r = renderers[ri];
            if (!r) { continue; }

            GameObject go = r.gameObject;
            if (!go) { continue; }

            // Only objects in loaded scenes (skip prefab assets, preview scenes, etc.)
            if (!go.scene.IsValid() || !go.scene.isLoaded) { continue; }

            // Skip hidden editor-only objects
            if ((r.hideFlags & HideFlags.HideInHierarchy) != 0) { continue; }

            Material[] mats = r.sharedMaterials;
            if (mats == null) { continue; }

            for (int mi = 0; mi < mats.Length; mi++)
            {
                Material m = mats[mi];
                if (!m) { continue; }

                Entry entry;
                if (!byMaterial.TryGetValue(m, out entry) || entry == null)
                {
                    entry = new Entry
                    {
                        Material = m,
                        MaxTexture = null,
                        MaxSize = 0,
                        ExamplePath = GetHierarchyPath(go),
                        UseCount = 0,
                    };
                    byMaterial[m] = entry;
                }

                entry.UseCount += 1;
                if (string.IsNullOrEmpty(entry.ExamplePath))
                {
                    entry.ExamplePath = GetHierarchyPath(go);
                }
            }
        }

        // Compute max texture size per material.
        foreach (var kv in byMaterial)
        {
            Entry e = kv.Value;
            if (e == null || !e.Material) { continue; }

            Texture maxTex;
            int maxSize;
            ComputeMaxTextureSize(e.Material, out maxTex, out maxSize);
            e.MaxTexture = maxTex;
            e.MaxSize = maxSize;

            _entries.Add(e);
        }

        // Group by max size.
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            if (e == null) { continue; }

            int key = e.MaxSize;
            List<Entry> list;
            if (!_groups.TryGetValue(key, out list) || list == null)
            {
                list = new List<Entry>();
                _groups[key] = list;
            }
            list.Add(e);
        }

        // Sort each group by material name.
        foreach (var kv in _groups)
        {
            kv.Value.Sort((a, b) =>
            {
                string an = a != null && a.Material ? a.Material.name : "";
                string bn = b != null && b.Material ? b.Material.name : "";
                return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Prune foldout states for sizes no longer present.
        {
            var present = new HashSet<int>(_groups.Keys);
            var keys = new List<int>(_sizeFoldouts.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!present.Contains(keys[i])) { _sizeFoldouts.Remove(keys[i]); }
            }
        }

        Debug.Log($"MaterialMaxTextureSizeScanner: scan complete. Found {_entries.Count} material(s).");
        Repaint();
    }

    private static void ComputeMaxTextureSize(Material material, out Texture maxTexture, out int maxSize)
    {
        maxTexture = null;
        maxSize = 0;

        if (!material) { return; }

        // Iterate all texture properties the shader exposes.
        string[] texProps;
        try
        {
            texProps = material.GetTexturePropertyNames();
        }
        catch
        {
            texProps = null;
        }

        if (texProps == null || texProps.Length == 0) { return; }

        for (int i = 0; i < texProps.Length; i++)
        {
            string prop = texProps[i];
            if (string.IsNullOrEmpty(prop)) { continue; }

            Texture t;
            try
            {
                t = material.GetTexture(prop);
            }
            catch
            {
                continue;
            }

            if (!t) { continue; }

            int w = 0;
            int h = 0;
            try
            {
                w = t.width;
                h = t.height;
            }
            catch
            {
                // Some texture types may throw; ignore.
            }

            int size = Mathf.Max(w, h);
            if (size > maxSize)
            {
                maxSize = size;
                maxTexture = t;
            }
        }
    }

    private static string GetHierarchyPath(GameObject go)
    {
        if (!go) return "<null>";
        string path = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
