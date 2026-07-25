using UnityEngine;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RiverFlowBaker
{
    /// <summary>
    /// FlowMapExporter: Handles exporting render textures to PNG/EXR files
    /// and assigning generated textures to river materials.
    /// </summary>
    public class FlowMapExporter
    {
        /// <summary>
        /// Export all generated maps to PNG files.
        /// </summary>
        public static void ExportAllMaps(RiverFlowBakerComponent component)
        {
            if (component == null) return;

            string basePath = NormalizeDirectoryPath(component.ExportPath);
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            if (component.FlowMapRT != null)
                ExportTexture(component.FlowMapRT, Path.Combine(basePath, "River_FlowMap.png"));

            if (component.FlowUVMapRT != null)
                ExportTexture(component.FlowUVMapRT, Path.Combine(basePath, "River_FlowUVMap.png"));

            if (component.VelocityMapRT != null)
                ExportTexture(component.VelocityMapRT, Path.Combine(basePath, "River_VelocityMap.png"));

            if (component.FoamMaskRT != null)
                ExportTexture(component.FoamMaskRT, Path.Combine(basePath, "River_FoamMask.png"));

            if (component.FoamMotionMapRT != null)
                ExportTexture(component.FoamMotionMapRT, Path.Combine(basePath, "River_FoamMotionMap.png"));

#if UNITY_EDITOR
            AssetDatabase.Refresh();
            ConfigureImporter(Path.Combine(basePath, "River_FlowMap.png"), true);
            ConfigureImporter(Path.Combine(basePath, "River_FlowUVMap.png"), true);
            ConfigureImporter(Path.Combine(basePath, "River_VelocityMap.png"), true);
            ConfigureImporter(Path.Combine(basePath, "River_FoamMask.png"), true);
            ConfigureImporter(Path.Combine(basePath, "River_FoamMotionMap.png"), true);
            Debug.Log($"[RiverFlowBaker] Exported textures to {basePath}");
#endif
        }

        /// <summary>
        /// Export a single render texture to PNG file.
        /// </summary>
        private static void ExportTexture(RenderTexture rt, string filePath)
        {
            if (rt == null) return;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();

            RenderTexture.active = prev;

            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);

            Object.DestroyImmediate(texture);

            Debug.Log($"[RiverFlowBaker] Exported: {filePath}");
        }

        /// <summary>
        /// Assign exported textures to all river materials.
        /// </summary>
        public static void AssignTexturesToMaterial(RiverFlowBakerComponent component, Material targetMaterial)
        {
            if (component == null || targetMaterial == null) return;

            string basePath = NormalizeDirectoryPath(component.ExportPath);

            // Try to load and assign exported textures
            var flowMapPath = Path.Combine(basePath, "River_FlowMap.png");
            var flowUVMapPath = Path.Combine(basePath, "River_FlowUVMap.png");
            var velocityMapPath = Path.Combine(basePath, "River_VelocityMap.png");
            var foamMaskPath = Path.Combine(basePath, "River_FoamMask.png");
            var foamMotionMapPath = Path.Combine(basePath, "River_FoamMotionMap.png");

#if UNITY_EDITOR
            if (File.Exists(flowMapPath))
            {
                var flowMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(flowMapPath));
                if (flowMap != null)
                    targetMaterial.SetTexture("_FlowMap", flowMap);
            }

            if (File.Exists(flowUVMapPath))
            {
                var flowUVMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(flowUVMapPath));
                if (flowUVMap != null)
                    targetMaterial.SetTexture("_FlowUVMap", flowUVMap);
            }

            if (File.Exists(velocityMapPath))
            {
                var velocityMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(velocityMapPath));
                if (velocityMap != null)
                    targetMaterial.SetTexture("_VelocityMap", velocityMap);
            }

            if (File.Exists(foamMaskPath))
            {
                var foamMask = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(foamMaskPath));
                if (foamMask != null)
                    targetMaterial.SetTexture("_FoamMask", foamMask);
            }

            if (File.Exists(foamMotionMapPath))
            {
                var foamMotionMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ToAssetPath(foamMotionMapPath));
                if (foamMotionMap != null)
                    targetMaterial.SetTexture("_FoamMotionMap", foamMotionMap);
            }

            if (targetMaterial.HasProperty("_RiverBoundsMin"))
                targetMaterial.SetVector("_RiverBoundsMin", component.MapWorldMin);

            if (targetMaterial.HasProperty("_RiverBoundsSize"))
                targetMaterial.SetVector("_RiverBoundsSize", component.MapWorldSize);

            Debug.Log("[RiverFlowBaker] Assigned textures to material");
#endif
        }

        /// <summary>
        /// Convert render texture to readable texture for processing.
        /// </summary>
        public static Texture2D RenderTextureToTexture2D(RenderTexture rt)
        {
            if (rt == null) return null;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();

            RenderTexture.active = prev;

            return texture;
        }

#if UNITY_EDITOR
        private static void ConfigureImporter(string absolutePath, bool isLinearData)
        {
            string assetPath = ToAssetPath(absolutePath);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !isLinearData;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
#endif

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Path.Combine(Application.dataPath, "Textures", "River");
            }

            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string assetsRoot = Application.dataPath.Replace('\\', '/');
            if (!normalized.StartsWith(assetsRoot))
            {
                return null;
            }

            return "Assets" + normalized.Substring(assetsRoot.Length);
        }
    }
}
