using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class NonMobileMaterials : EditorWindow
{
    private struct Hit
    {
        public Renderer Renderer;
        public Material Material;
        public string ShaderName;
        public string Path;
    }

    private readonly List<Hit> _hits = new List<Hit>(512);
    private readonly Dictionary<string, bool> _shaderFoldouts = new Dictionary<string, bool>(64);
    private Shader _standardLiteShader;
    private string _standardLiteShaderResolvedName;
    private Shader _standardLiteCustomShader;
    private string _standardLiteCustomShaderResolvedName;
    private Vector2 _scroll;
    private bool _includeInactive = true;

    [MenuItem("Tools/Non Mobile Materials")]
    public static void ShowWindow()
    {
        GetWindow<NonMobileMaterials>("Non Mobile Materials");
    }

    private void OnGUI()
    {
        bool changedAnyMaterial = false;

        using (new EditorGUILayout.HorizontalScope())
        {
            _includeInactive = GUILayout.Toggle(_includeInactive, "Include Inactive", GUILayout.Width(120));

            if (GUILayout.Button("Scan Scene", GUILayout.Width(100)))
            {
                ScanScene();
            }

            using (new EditorGUI.DisabledScope(_hits.Count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    _hits.Clear();
                }
            }
        }

        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            bool hasStandardLite = _TryResolveStandardLiteShader();
            bool hasStandardLiteCustom = _TryResolveStandardLiteCustomShader();

            using (new EditorGUI.DisabledScope(!hasStandardLite || !hasStandardLiteCustom))
            {
                if (GUILayout.Button("All StandardLite -> Custom", GUILayout.Width(180)))
                {
                    int changed = _SwitchAllMaterials(_standardLiteShader, _standardLiteCustomShader);
                    if (changed > 0)
                    {
                        changedAnyMaterial = true;
                        Debug.Log($"NonMobileMaterials: switched {changed} material(s) Standard Lite -> Custom.");
                    }
                }

                if (GUILayout.Button("All Custom -> StandardLite", GUILayout.Width(180)))
                {
                    int changed = _SwitchAllMaterials(_standardLiteCustomShader, _standardLiteShader);
                    if (changed > 0)
                    {
                        changedAnyMaterial = true;
                        Debug.Log($"NonMobileMaterials: switched {changed} material(s) Custom -> Standard Lite.");
                    }
                }
            }

            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(6);

        if (_hits.Count == 0)
        {
            EditorGUILayout.HelpBox("Click 'Scan Scene' to list renderers using non-mobile shaders. Click an item to select it in the Hierarchy.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"Found {_hits.Count} material slot(s) using non-mobile shaders:", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        string currentShader = null;
        int currentShaderCount = 0;
        bool currentExpanded = true;

        for (int i = 0; i < _hits.Count; i++)
        {
            var h = _hits[i];
            if (!h.Renderer) { continue; }

            bool shaderChanged = currentShader == null || currentShader != h.ShaderName;
            if (shaderChanged)
            {
                currentShader = h.ShaderName;
                currentShaderCount = _CountShaderHits(currentShader);
                if (!_shaderFoldouts.ContainsKey(currentShader))
                {
                    // Default collapsed.
                    _shaderFoldouts[currentShader] = false;
                }

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        currentExpanded = EditorGUILayout.Foldout(_shaderFoldouts[currentShader], $"{currentShader}  ({currentShaderCount})", true);
                        _shaderFoldouts[currentShader] = currentExpanded;

                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Expand", GUILayout.Width(70)))
                        {
                            _shaderFoldouts[currentShader] = true;
                            currentExpanded = true;
                        }
                        if (GUILayout.Button("Collapse", GUILayout.Width(70)))
                        {
                            _shaderFoldouts[currentShader] = false;
                            currentExpanded = false;
                        }
                    }
                }
            }

            if (!currentExpanded) { continue; }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Clickable object selector
                    if (GUILayout.Button(h.Path, GUILayout.ExpandWidth(true)))
                    {
                        Selection.activeGameObject = h.Renderer.gameObject;
                        EditorGUIUtility.PingObject(h.Renderer.gameObject);
                    }

                    // Optional: ping the material asset too
                    if (h.Material && GUILayout.Button("Ping Mat", GUILayout.Width(70)))
                    {
                        Selection.activeObject = h.Material;
                        EditorGUIUtility.PingObject(h.Material);
                    }

                    if (h.Material && GUILayout.Button("To StandardLite", GUILayout.Width(110)))
                    {
                        if (_TrySwitchToStandardLite(h.Material))
                        {
                            changedAnyMaterial = true;
                        }
                    }

                    if (h.Material && GUILayout.Button("To StdLite Custom", GUILayout.Width(130)))
                    {
                        if (_TrySwitchToStandardLiteCustom(h.Material))
                        {
                            changedAnyMaterial = true;
                        }
                    }
                }

                EditorGUILayout.LabelField("Renderer", h.Renderer.GetType().Name);
                if (h.Material) { EditorGUILayout.LabelField("Material", h.Material.name); }
            }
        }
        EditorGUILayout.EndScrollView();

        if (changedAnyMaterial)
        {
            ScanScene();
        }
    }

    private void ScanScene()
    {
        _hits.Clear();

        // Includes inactive + disabled objects; filter out assets/prefabs not in a loaded scene
        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        foreach (var r in renderers)
        {
            if (!r) continue;

            var go = r.gameObject;
            if (go == null) continue;

            // Only objects in loaded scenes (skip prefab assets, preview scenes, etc.)
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

            // Skip hidden editor-only objects
            if ((r.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

            if (!_includeInactive && !go.activeInHierarchy) continue;

            var mats = r.sharedMaterials;
            if (mats == null) continue;

            for (int mi = 0; mi < mats.Length; mi++)
            {
                var m = mats[mi];
                if (!m) continue;

                var s = m.shader;
                if (!s) continue;

                if (IsNonMobileShader(s))
                {
                    _hits.Add(new Hit
                    {
                        Renderer = r,
                        Material = m,
                        ShaderName = s.name,
                        Path = GetHierarchyPath(go)
                    });
                }
            }
        }

        // Sort for easier browsing: group by shader then by hierarchy path.
        _hits.Sort((a, b) =>
        {
            int shaderCmp = string.Compare(a.ShaderName, b.ShaderName, System.StringComparison.OrdinalIgnoreCase);
            if (shaderCmp != 0) { return shaderCmp; }
            return string.Compare(a.Path, b.Path, System.StringComparison.OrdinalIgnoreCase);
        });

        // Prune foldout states for shaders no longer present.
        var present = new HashSet<string>();
        for (int i = 0; i < _hits.Count; i++)
        {
            present.Add(_hits[i].ShaderName);
        }
        var keys = new List<string>(_shaderFoldouts.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            if (!present.Contains(keys[i])) { _shaderFoldouts.Remove(keys[i]); }
        }

        Debug.Log($"NonMobileMaterials: scan complete. Found {_hits.Count} non-mobile material slot(s).");
        Repaint();
    }

    private int _CountShaderHits(string shaderName)
    {
        int count = 0;
        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Renderer && _hits[i].ShaderName == shaderName) { count++; }
        }
        return count;
    }

    private static bool IsNonMobileShader(Shader shader)
    {
        // Treat anything NOT under "VRChat/Mobile/" as non-mobile
        var shaderName = shader.name;
        if (string.IsNullOrEmpty(shaderName)) return true;
        shaderName = shaderName.ToLowerInvariant();
        return !shaderName.StartsWith("vrchat/mobile/");
    }

    private bool _TryResolveStandardLiteShader()
    {
        if (_standardLiteShader)
        {
            return true;
        }

        // Shader names vary slightly between packages/versions.
        // Try a few common variants.
        string[] names =
        {
            "VRChat/Mobile/Standard Lite",
            "VRChat/Mobile/StandardLite",
            "VRChat/Mobile/StandardLite (Lite)",
        };

        for (int i = 0; i < names.Length; i++)
        {
            var s = Shader.Find(names[i]);
            if (s != null)
            {
                _standardLiteShader = s;
                _standardLiteShaderResolvedName = names[i];
                return true;
            }
        }

        Debug.LogError("NonMobileMaterials: Could not find shader 'VRChat/Mobile/Standard Lite'. Make sure the VRChat Mobile shaders are imported.");
        return false;
    }

    private bool _TryResolveStandardLiteCustomShader()
    {
        if (_standardLiteCustomShader)
        {
            return true;
        }

        // Our copied shader (see Assets/Materials/Shader/StanardLiteCustom/...)
        // Shader name can be edited by the user, so keep a small set of common variants.
        string[] names =
        {
            "VRChat/Mobile/Standard Lite Custom",
            "VRChat/Mobile/StandardLite Custom",
            "VRChat/Mobile/Standard Lite (Custom)",
        };

        for (int i = 0; i < names.Length; i++)
        {
            var s = Shader.Find(names[i]);
            if (s != null)
            {
                _standardLiteCustomShader = s;
                _standardLiteCustomShaderResolvedName = names[i];
                return true;
            }
        }

        Debug.LogError("NonMobileMaterials: Could not find shader 'VRChat/Mobile/Standard Lite Custom'. Make sure your custom shader is imported.");
        return false;
    }

    private bool _TrySwitchToStandardLite(Material material)
    {
        if (!material) { return false; }
        if (!_TryResolveStandardLiteShader()) { return false; }
        if (material.shader == _standardLiteShader) { return false; }

        Undo.RecordObject(material, "Switch to Standard Lite");
        material.shader = _standardLiteShader;
        EditorUtility.SetDirty(material);

        Debug.Log($"NonMobileMaterials: set '{material.name}' shader -> '{_standardLiteShaderResolvedName}'.");
        return true;
    }

    private bool _TrySwitchToStandardLiteCustom(Material material)
    {
        if (!material) { return false; }
        if (!_TryResolveStandardLiteCustomShader()) { return false; }
        if (material.shader == _standardLiteCustomShader) { return false; }

        Undo.RecordObject(material, "Switch to Standard Lite Custom");
        material.shader = _standardLiteCustomShader;
        EditorUtility.SetDirty(material);

        Debug.Log($"NonMobileMaterials: set '{material.name}' shader -> '{_standardLiteCustomShaderResolvedName}'.");
        return true;
    }

    private int _SwitchAllMaterials(Shader from, Shader to)
    {
        if (!from || !to) { return 0; }

        int changed = 0;
        var seen = new HashSet<Material>();

        // Includes inactive + disabled objects; filter out assets/prefabs not in a loaded scene
        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        foreach (var r in renderers)
        {
            if (!r) continue;

            var go = r.gameObject;
            if (go == null) continue;

            // Only objects in loaded scenes (skip prefab assets, preview scenes, etc.)
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;

            // Skip hidden editor-only objects
            if ((r.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

            if (!_includeInactive && !go.activeInHierarchy) continue;

            var mats = r.sharedMaterials;
            if (mats == null) continue;

            for (int mi = 0; mi < mats.Length; mi++)
            {
                var m = mats[mi];
                if (!m) continue;
                if (!seen.Add(m)) continue;

                if (m.shader == from)
                {
                    Undo.RecordObject(m, "Switch Shader");
                    m.shader = to;
                    EditorUtility.SetDirty(m);
                    changed++;
                }
            }
        }

        return changed;
    }

    private static string GetHierarchyPath(GameObject go)
    {
        if (!go) return "<null>";
        var path = go.name;
        var t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}