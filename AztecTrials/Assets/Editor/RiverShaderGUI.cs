using UnityEditor;
using UnityEngine;

public sealed class RiverShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        Material material = materialEditor.target as Material;
        if (material == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Baked Maps", EditorStyles.boldLabel);
        DrawTextureProperty(materialEditor, props, "_FlowMap", "Flow Map");
        DrawTextureProperty(materialEditor, props, "_FlowUVMap", "Flow UV Map");
        DrawTextureProperty(materialEditor, props, "_VelocityMap", "Velocity Map");
        DrawTextureProperty(materialEditor, props, "_FoamMask", "Foam Mask");
        DrawTextureProperty(materialEditor, props, "_FoamMotionMap", "Foam Motion Map");
        DrawTextureProperty(materialEditor, props, "_FoamTex", "Foam Texture");
        DrawTextureProperty(materialEditor, props, "_NormalA", "Normal A");
        DrawTextureProperty(materialEditor, props, "_NormalB", "Normal B");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
        DrawProperty(materialEditor, props, "_SlowColor", "Slow Color");
        DrawProperty(materialEditor, props, "_FastColor", "Fast Color");
        DrawProperty(materialEditor, props, "_FoamColor", "Foam Color");
        DrawProperty(materialEditor, props, "_HighlightColor", "Highlight Color");
        DrawProperty(materialEditor, props, "_WaterColorVisibility", "Water Color Visibility");
        DrawProperty(materialEditor, props, "_HighlightIntensity", "Highlight Intensity");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Flow", EditorStyles.boldLabel);
        DrawProperty(materialEditor, props, "_FlowStrength", "Flow Strength");
        DrawProperty(materialEditor, props, "_FlowSpeed", "Flow Speed");
        DrawProperty(materialEditor, props, "_VelocityContrast", "Velocity Contrast");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Normals", EditorStyles.boldLabel);
        DrawProperty(materialEditor, props, "_NormalASpeed", "Normal A Speed");
        DrawProperty(materialEditor, props, "_NormalBSpeed", "Normal B Speed");
        DrawProperty(materialEditor, props, "_NormalAScale", "Normal A Scale");
        DrawProperty(materialEditor, props, "_NormalBScale", "Normal B Scale");
        DrawProperty(materialEditor, props, "_NormalStrength", "Normal Strength");
        DrawProperty(materialEditor, props, "_NormalFlowDistortion", "Normal Flow Distortion");
        DrawProperty(materialEditor, props, "_NormalVelocityDistortion", "Normal Velocity Distortion");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Foam", EditorStyles.boldLabel);
        DrawProperty(materialEditor, props, "_FoamIntensity", "Foam Intensity");
        DrawProperty(materialEditor, props, "_FoamThreshold", "Foam Threshold");
        DrawProperty(materialEditor, props, "_FoamScroll", "Foam Scroll");
        DrawProperty(materialEditor, props, "_FoamTexScale", "Foam Texture Scale");
        DrawProperty(materialEditor, props, "_FoamBubbleNoise", "Foam Bubble Noise");
        DrawProperty(materialEditor, props, "_FoamBubbleScale", "Foam Bubble Scale");
        DrawProperty(materialEditor, props, "_FoamDistortion", "Foam Distortion");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
        DrawProperty(materialEditor, props, "_Smoothness", "Smoothness");
        DrawProperty(materialEditor, props, "_TimeScale", "Time Scale");

        if (GUI.changed)
        {
            EditorUtility.SetDirty(material);
        }
    }

    private static void DrawTextureProperty(MaterialEditor materialEditor, MaterialProperty[] props, string propertyName, string label)
    {
        MaterialProperty property = FindProperty(propertyName, props, false);
        if (property != null)
        {
            materialEditor.TexturePropertySingleLine(new GUIContent(label), property);
        }
    }

    private static void DrawProperty(MaterialEditor materialEditor, MaterialProperty[] props, string propertyName, string label)
    {
        MaterialProperty property = FindProperty(propertyName, props, false);
        if (property != null)
        {
            materialEditor.ShaderProperty(property, label);
        }
    }
}
