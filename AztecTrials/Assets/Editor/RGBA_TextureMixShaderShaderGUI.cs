using UnityEditor;
using UnityEngine;

public sealed class RGBA_TextureMixShaderShaderGUI : ShaderGUI
{
    private static void SetKeyword(Material mat, string keyword, bool enabled)
    {
        if (enabled) mat.EnableKeyword(keyword);
        else mat.DisableKeyword(keyword);
    }

    private static void DrawLayer(MaterialEditor materialEditor, string label, MaterialProperty textureProperty, MaterialProperty tilingProperty)
    {
        if (textureProperty != null)
        {
            materialEditor.TexturePropertySingleLine(new GUIContent(label), textureProperty);
            if (tilingProperty != null)
                materialEditor.ShaderProperty(tilingProperty, "Tiling/Offset");
        }
        else if (tilingProperty != null)
        {
            materialEditor.ShaderProperty(tilingProperty, label + " Tiling/Offset");
        }
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        var material = materialEditor.target as Material;
        if (material == null)
            return;

        MaterialProperty color = FindProperty("_Color", props, false);

        MaterialProperty splatMap = FindProperty("_SplatMap", props, false);
        MaterialProperty texR = FindProperty("_TexR", props, false);
        MaterialProperty texG = FindProperty("_TexG", props, false);
        MaterialProperty texB = FindProperty("_TexB", props, false);
        MaterialProperty texR_ST = FindProperty("_TexR_ST", props, false);
        MaterialProperty texG_ST = FindProperty("_TexG_ST", props, false);
        MaterialProperty texB_ST = FindProperty("_TexB_ST", props, false);
        MaterialProperty rootsTex = FindProperty("_RootsTex", props, false);
        MaterialProperty triplanarBlendSharpness = FindProperty("_TriplanarBlendSharpness", props, false);
        MaterialProperty rootsTriplanarTiling = FindProperty("_RootsTriplanarTiling", props, false);
        MaterialProperty rootStartAngle = FindProperty("_RootStartAngle", props, false);
        MaterialProperty rootFullAngle = FindProperty("_RootFullAngle", props, false);

        MaterialProperty lightmapType = FindProperty("_LightmapType", props, false);

        EditorGUILayout.LabelField("Splat Albedo", EditorStyles.boldLabel);
        if (splatMap != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("RGB Control Map"), splatMap);
        if (color != null)
            materialEditor.ShaderProperty(color, "Tint");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
        DrawLayer(materialEditor, "Mud Layer (R)", texR, texR_ST);
        DrawLayer(materialEditor, "Leaf Litter Layer (G)", texG, texG_ST);
        DrawLayer(materialEditor, "Stones Layer (B)", texB, texB_ST);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Automatic Roots", EditorStyles.boldLabel);
        if (rootsTex != null)
            materialEditor.TexturePropertySingleLine(new GUIContent("Roots Layer"), rootsTex);
        if (rootsTriplanarTiling != null)
            materialEditor.ShaderProperty(rootsTriplanarTiling, rootsTriplanarTiling.displayName);
        if (triplanarBlendSharpness != null)
            materialEditor.ShaderProperty(triplanarBlendSharpness, triplanarBlendSharpness.displayName);
        if (rootStartAngle != null)
            materialEditor.ShaderProperty(rootStartAngle, rootStartAngle.displayName);
        if (rootFullAngle != null)
            materialEditor.ShaderProperty(rootFullAngle, rootFullAngle.displayName);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        if (lightmapType != null)
            materialEditor.ShaderProperty(lightmapType, lightmapType.displayName);

        if (lightmapType != null)
        {
            int lt = Mathf.RoundToInt(lightmapType.floatValue);
            SetKeyword(material, "_MONOSH", lt != 0);
        }
        else
        {
            SetKeyword(material, "_MONOSH", false);
        }

        SetKeyword(material, "_SPECULARHIGHLIGHTS_OFF", false);
        SetKeyword(material, "_GLOSSYREFLECTIONS_OFF", false);
        SetKeyword(material, "_ENABLE_GEOMETRIC_SPECULAR_AA", false);
        SetKeyword(material, "_MONOSH_SPECULAR", false);
        SetKeyword(material, "_MONOSH_NOSPECULAR", false);

        if (GUI.changed)
            EditorUtility.SetDirty(material);
    }
}
