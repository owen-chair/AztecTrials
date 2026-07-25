#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LightmapSnapshot", menuName = "Lighting/Lightmap Snapshot", order = 0)]
public class GroupedLightmapSnapshot : ScriptableObject
{
    [Serializable]
    public struct LightmapEntry
    {
        public Texture2D color;
        public Texture2D dir;
        public Texture2D shadowMask;
    }

    [Serializable]
    public struct ObjectAssignment
    {
        public string globalObjectId;
        public int lightmapIndex;
        public Vector4 scaleOffset;
    }

    public LightmapsMode lightmapsMode = LightmapsMode.NonDirectional;
    public List<LightmapEntry> lightmaps = new List<LightmapEntry>();

    public List<ObjectAssignment> rendererAssignments = new List<ObjectAssignment>();
    public List<ObjectAssignment> terrainAssignments = new List<ObjectAssignment>();
}
#endif
