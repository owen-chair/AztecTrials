#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class TerrainLayerAudit
{
    // Any weight <= this is treated as unused.
    private const float WeightThreshold = 0.001f;

    [MenuItem("Tools/Terrain/Audit Layer Usage (<=4 per tile)")]
    public static void Audit()
    {
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            Debug.LogWarning("TerrainLayerAudit: No active Terrains found in loaded scenes.");
            return;
        }

        try
        {
            for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
            {
                var terrain = terrains[terrainIndex];
                if (terrain == null)
                    continue;

                var terrainData = terrain.terrainData;
                if (terrainData == null)
                {
                    Debug.LogWarning($"TerrainLayerAudit: Terrain '{terrain.name}' has no TerrainData.");
                    continue;
                }

                int alphamapWidth = terrainData.alphamapWidth;
                int alphamapHeight = terrainData.alphamapHeight;
                int alphamapLayers = terrainData.alphamapLayers;
                int terrainLayerCount = terrainData.terrainLayers != null ? terrainData.terrainLayers.Length : 0;

                if (alphamapWidth <= 0 || alphamapHeight <= 0 || alphamapLayers <= 0)
                {
                    Debug.LogWarning($"TerrainLayerAudit: Terrain '{terrain.name}' has invalid alphamap dimensions.");
                    continue;
                }

                EditorUtility.DisplayProgressBar(
                    "Terrain Layer Audit",
                    $"Scanning '{terrain.name}' ({terrainIndex + 1}/{terrains.Length})",
                    terrains.Length == 1 ? 0.5f : (float)terrainIndex / terrains.Length);

                float[,,] alphaMaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);

                int maxNonZeroLayersPerPixel = 0;
                int highestLayerIndexUsed = -1;
                bool anyWeightInLayer4Plus = false;

                // Note: alphaMaps is indexed [y, x, layer]
                int totalPixels = alphamapWidth * alphamapHeight;
                int progressStride = Math.Max(1, totalPixels / 50);
                int pixelCounter = 0;

                for (int y = 0; y < alphamapHeight; y++)
                {
                    for (int x = 0; x < alphamapWidth; x++)
                    {
                        int nonZero = 0;
                        for (int layer = 0; layer < alphamapLayers; layer++)
                        {
                            float w = alphaMaps[y, x, layer];
                            if (w > WeightThreshold)
                            {
                                nonZero++;
                                if (layer > highestLayerIndexUsed)
                                    highestLayerIndexUsed = layer;
                                if (layer >= 4)
                                    anyWeightInLayer4Plus = true;
                            }
                        }

                        if (nonZero > maxNonZeroLayersPerPixel)
                            maxNonZeroLayersPerPixel = nonZero;

                        pixelCounter++;
                        if ((pixelCounter % progressStride) == 0)
                        {
                            float perTerrainProgress = (float)pixelCounter / totalPixels;
                            EditorUtility.DisplayProgressBar(
                                "Terrain Layer Audit",
                                $"Scanning '{terrain.name}'… {Mathf.RoundToInt(perTerrainProgress * 100f)}%",
                                perTerrainProgress);
                        }
                    }
                }

                int highestLayerCountUsed = highestLayerIndexUsed >= 0 ? (highestLayerIndexUsed + 1) : 0;
                int estimatedPasses = highestLayerCountUsed <= 4 ? 1 : 1 + Mathf.CeilToInt((highestLayerCountUsed - 4) / 4f);

                Debug.Log(
                    "TerrainLayerAudit:\n" +
                    $"- Terrain: {terrain.name}\n" +
                    $"- TerrainData layers list: {terrainLayerCount}\n" +
                    $"- Alphamap layers: {alphamapLayers}\n" +
                    $"- Max non-zero layers at any pixel: {maxNonZeroLayersPerPixel}\n" +
                    $"- Highest painted layer index used: {highestLayerIndexUsed} (count {highestLayerCountUsed})\n" +
                    $"- Any paint in layer 4+: {(anyWeightInLayer4Plus ? "YES" : "no")}\n" +
                    $"- Estimated terrain splat passes: {estimatedPasses} (ideal is 1)"
                );
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
#endif
