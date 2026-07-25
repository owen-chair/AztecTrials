using UnityEditor;
using UnityEngine;

public sealed class StandardLiteCustomShaderGUI : ShaderGUI
{
    private static bool HasTexture(Material mat, string propertyName)
    {
        return mat.HasProperty(propertyName) && mat.GetTexture(propertyName) != null;
    }

    private static bool HasPositiveFloat(Material mat, string propertyName)
    {
        return mat.HasProperty(propertyName) && mat.GetFloat(propertyName) > 0.0001f;
    }

    private static bool SetKeyword(Material mat, string keyword, bool enabled)
    {
        bool currentlyEnabled = mat.IsKeywordEnabled(keyword);
        if (currentlyEnabled == enabled)
            return false;

        if (enabled) mat.EnableKeyword(keyword);
        else mat.DisableKeyword(keyword);

        return true;
    }

    private static bool UpdateKeywords(Material material)
    {
        bool changed = false;

        changed |= SetKeyword(material, "_METALLICGLOSSMAP", HasTexture(material, "_MetallicGlossMap") && HasPositiveFloat(material, "_Metallic"));
        changed |= SetKeyword(material, "_NORMALMAP", HasTexture(material, "_BumpMap") && HasPositiveFloat(material, "_BumpScale"));
        changed |= SetKeyword(material, "_OCCLUSIONMAP", HasTexture(material, "_OcclusionMap") && HasPositiveFloat(material, "_OcclusionStrength"));

        bool emissionEnabled = HasTexture(material, "_EmissionMap");
        if (material.HasProperty("_EmissionColor"))
        {
            var c = material.GetColor("_EmissionColor");
            emissionEnabled |= (c.maxColorComponent > 0.0001f);
        }
        changed |= SetKeyword(material, "_EMISSION", emissionEnabled);

        if (material.HasProperty("_LightmapType"))
        {
            int lt = Mathf.RoundToInt(material.GetFloat("_LightmapType"));
            changed |= SetKeyword(material, "_MONOSH", lt != 0);
        }
        else
        {
            changed |= SetKeyword(material, "_MONOSH", false);
        }

        changed |= SetKeyword(material, "_SPECULARHIGHLIGHTS_OFF", false);
        changed |= SetKeyword(material, "_GLOSSYREFLECTIONS_OFF", false);
        changed |= SetKeyword(material, "_ENABLE_GEOMETRIC_SPECULAR_AA", false);
        changed |= SetKeyword(material, "_MONOSH_SPECULAR", false);
        changed |= SetKeyword(material, "_MONOSH_NOSPECULAR", false);

        return changed;
    }

    public override void ValidateMaterial(Material material)
    {
        if (material != null && UpdateKeywords(material))
            EditorUtility.SetDirty(material);
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        var material = materialEditor.target as Material;
        if (material == null)
            return;

        MaterialProperty mainTex = FindProperty("_MainTex", props, false);
        MaterialProperty color = FindProperty("_Color", props, false);

        MaterialProperty metallicGlossMap = FindProperty("_MetallicGlossMap", props, false);
        MaterialProperty metallic = FindProperty("_Metallic", props, false);

        MaterialProperty bumpMap = FindProperty("_BumpMap", props, false);
        MaterialProperty bumpScale = FindProperty("_BumpScale", props, false);

        MaterialProperty occlusionMap = FindProperty("_OcclusionMap", props, false);
        MaterialProperty occlusionStrength = FindProperty("_OcclusionStrength", props, false);

        MaterialProperty emissionMap = FindProperty("_EmissionMap", props, false);
        MaterialProperty emissionColor = FindProperty("_EmissionColor", props, false);

        MaterialProperty lightmapType = FindProperty("_LightmapType", props, false);

        // --- Main
        EditorGUILayout.LabelField("Main", EditorStyles.boldLabel);
        if (mainTex != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("Albedo"), mainTex, color);
        else if (color != null)
            materialEditor.ShaderProperty(color, "Color");

        // --- PBR
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Metallic / Smoothness", EditorStyles.boldLabel);
        if (metallicGlossMap != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("Metallic (R)"), metallicGlossMap);
        if (metallic != null)
            materialEditor.ShaderProperty(metallic, metallic.displayName);

        // --- Normal
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Normal", EditorStyles.boldLabel);
        if (bumpMap != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map"), bumpMap, bumpScale);
        else if (bumpScale != null)
            materialEditor.ShaderProperty(bumpScale, bumpScale.displayName);

        // --- Occlusion
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Occlusion", EditorStyles.boldLabel);
        if (occlusionMap != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion (G)"), occlusionMap, occlusionStrength);
        else if (occlusionStrength != null)
            materialEditor.ShaderProperty(occlusionStrength, occlusionStrength.displayName);

        // --- Emission
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Emission", EditorStyles.boldLabel);
        if (emissionMap != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("Emission"), emissionMap, emissionColor);
        else if (emissionColor != null)
            materialEditor.ShaderProperty(emissionColor, emissionColor.displayName);

        // --- Options
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        if (lightmapType != null)
            materialEditor.ShaderProperty(lightmapType, lightmapType.displayName);

        bool keywordsChanged = UpdateKeywords(material);

        // Ensure changes persist.
        if (GUI.changed || keywordsChanged)
            EditorUtility.SetDirty(material);
    }
}
