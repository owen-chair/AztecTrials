using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RiverFlowBaker
{
    public static class RiverBakeUtility
    {
        public struct SurfaceData
        {
            public int Resolution;
            public Bounds WorldBounds;
            public bool[] Coverage;
            public Vector3[] WorldPositions;
            public Vector3[] WorldNormals;
            public Vector3[] WorldTangents;
            public Vector3[] WorldBitangents;

            public bool HasCoverage()
            {
                if (Coverage == null)
                {
                    return false;
                }

                for (int i = 0; i < Coverage.Length; i++)
                {
                    if (Coverage[i])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public struct ObstacleInfo
        {
            public MeshFilter MeshFilter;
            public Mesh Mesh;
            public Matrix4x4 LocalToWorld;
            public Bounds Bounds;
            public string Name;
            public int SourceId;
        }

        public static SurfaceData BuildSurfaceData(Mesh mesh, Transform riverTransform, int resolution)
        {
            SurfaceData data = new SurfaceData
            {
                Resolution = resolution,
                WorldBounds = new Bounds(riverTransform != null ? riverTransform.position : Vector3.zero, Vector3.one),
                Coverage = new bool[Mathf.Max(1, resolution * resolution)],
                WorldPositions = new Vector3[Mathf.Max(1, resolution * resolution)],
                WorldNormals = new Vector3[Mathf.Max(1, resolution * resolution)],
                WorldTangents = new Vector3[Mathf.Max(1, resolution * resolution)],
                WorldBitangents = new Vector3[Mathf.Max(1, resolution * resolution)]
            };

            if (mesh == null || riverTransform == null || resolution <= 0)
            {
                return data;
            }

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            if (vertices == null || uvs == null || triangles == null || vertices.Length == 0 || uvs.Length != vertices.Length || triangles.Length < 3)
            {
                return data;
            }

            bool useMeshNormals = normals != null && normals.Length == vertices.Length;
            Matrix4x4 localToWorld = riverTransform.localToWorldMatrix;
            data.WorldBounds = CalculateWorldBounds(mesh, riverTransform);

            for (int tri = 0; tri < triangles.Length; tri += 3)
            {
                int i0 = triangles[tri];
                int i1 = triangles[tri + 1];
                int i2 = triangles[tri + 2];

                Vector2 uv0 = uvs[i0];
                Vector2 uv1 = uvs[i1];
                Vector2 uv2 = uvs[i2];

                Vector3 wp0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
                Vector3 wp1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
                Vector3 wp2 = localToWorld.MultiplyPoint3x4(vertices[i2]);

                Vector3 wn0 = useMeshNormals ? localToWorld.MultiplyVector(normals[i0]).normalized : riverTransform.up;
                Vector3 wn1 = useMeshNormals ? localToWorld.MultiplyVector(normals[i1]).normalized : riverTransform.up;
                Vector3 wn2 = useMeshNormals ? localToWorld.MultiplyVector(normals[i2]).normalized : riverTransform.up;

                Vector3 tangent;
                Vector3 bitangent;
                BuildTriangleBasis(wp0, wp1, wp2, uv0, uv1, uv2, wn0, out tangent, out bitangent);

                RasterizeTriangle(data, wp0, wp1, wp2, wn0, wn1, wn2, tangent, bitangent, uv0, uv1, uv2);
            }

            return data;
        }

        public static List<ObstacleInfo> FindNearbyObstacles(Bounds worldBounds, LayerMask layerMask, float influenceRadius, Transform excludedRoot = null)
        {
            List<ObstacleInfo> obstacles = new List<ObstacleInfo>();
            if (layerMask.value == 0)
            {
                return obstacles;
            }

            Bounds queryBounds = worldBounds;
            float padding = Mathf.Max(0.1f, influenceRadius);
            queryBounds.Expand(new Vector3(padding * 2f, worldBounds.size.y + padding * 2f, padding * 2f));

            MeshFilter[] meshFilters = Object.FindObjectsOfType<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                if (excludedRoot != null && meshFilter.transform.IsChildOf(excludedRoot))
                {
                    continue;
                }

                if (!LayerInHierarchy(meshFilter.transform, layerMask))
                {
                    continue;
                }

                Renderer renderer = meshFilter.GetComponent<Renderer>();
                if (renderer != null && !renderer.enabled)
                {
                    continue;
                }

                Bounds bounds = renderer != null ? renderer.bounds : CalculateWorldBounds(meshFilter.sharedMesh, meshFilter.transform);
                if (bounds.size.sqrMagnitude <= 0.000001f || !IntersectsXZ(bounds, queryBounds))
                {
                    continue;
                }

                obstacles.Add(new ObstacleInfo
                {
                    MeshFilter = meshFilter,
                    Mesh = meshFilter.sharedMesh,
                    LocalToWorld = meshFilter.transform.localToWorldMatrix,
                    Bounds = bounds,
                    Name = meshFilter.name,
                    SourceId = meshFilter.GetInstanceID()
                });
            }

            return obstacles;
        }

        private static bool LayerInHierarchy(Transform transform, LayerMask layerMask)
        {
            Transform current = transform;
            while (current != null)
            {
                if ((layerMask.value & (1 << current.gameObject.layer)) != 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IntersectsXZ(Bounds a, Bounds b)
        {
            return a.min.x <= b.max.x && a.max.x >= b.min.x &&
                a.min.z <= b.max.z && a.max.z >= b.min.z;
        }

        public static Color[] ReadColorsFromRenderTexture(RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                return null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();
            Color[] colors = texture.GetPixels();

            Object.DestroyImmediate(texture);
            RenderTexture.active = previous;

            return colors;
        }

        public static void WriteColorsToRenderTexture(RenderTexture renderTexture, int resolution, Color[] colors)
        {
            if (renderTexture == null || colors == null || colors.Length != resolution * resolution)
            {
                return;
            }

            if (!renderTexture.IsCreated())
            {
                renderTexture.Create();
            }

            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            texture.SetPixels(colors);
            texture.Apply();
            Graphics.Blit(texture, renderTexture);
            Object.DestroyImmediate(texture);
        }

        public static float[] DistanceTransform(bool[] zeroDistanceMask, int resolution)
        {
            int count = resolution * resolution;
            float[] distances = new float[count];
            float maxDistance = resolution * resolution;

            for (int i = 0; i < count; i++)
            {
                distances[i] = zeroDistanceMask[i] ? 0f : maxDistance;
            }

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = x + y * resolution;
                    float best = distances[index];

                    if (x > 0)
                    {
                        best = Mathf.Min(best, distances[index - 1] + 1f);
                    }

                    if (y > 0)
                    {
                        best = Mathf.Min(best, distances[index - resolution] + 1f);

                        if (x > 0)
                        {
                            best = Mathf.Min(best, distances[index - resolution - 1] + 1.41421356f);
                        }

                        if (x < resolution - 1)
                        {
                            best = Mathf.Min(best, distances[index - resolution + 1] + 1.41421356f);
                        }
                    }

                    distances[index] = best;
                }
            }

            for (int y = resolution - 1; y >= 0; y--)
            {
                for (int x = resolution - 1; x >= 0; x--)
                {
                    int index = x + y * resolution;
                    float best = distances[index];

                    if (x < resolution - 1)
                    {
                        best = Mathf.Min(best, distances[index + 1] + 1f);
                    }

                    if (y < resolution - 1)
                    {
                        best = Mathf.Min(best, distances[index + resolution] + 1f);

                        if (x > 0)
                        {
                            best = Mathf.Min(best, distances[index + resolution - 1] + 1.41421356f);
                        }

                        if (x < resolution - 1)
                        {
                            best = Mathf.Min(best, distances[index + resolution + 1] + 1.41421356f);
                        }
                    }

                    distances[index] = best;
                }
            }

            return distances;
        }

        public static void BlurScalar(float[] values, bool[] coverage, int resolution, int passes)
        {
            if (values == null || passes <= 0)
            {
                return;
            }

            float[] source = values;
            float[] target = new float[values.Length];

            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        int index = x + y * resolution;
                        if (coverage != null && !coverage[index])
                        {
                            target[index] = 0f;
                            continue;
                        }

                        float sum = 0f;
                        int samples = 0;

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int ny = y + oy;
                            if (ny < 0 || ny >= resolution)
                            {
                                continue;
                            }

                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int nx = x + ox;
                                if (nx < 0 || nx >= resolution)
                                {
                                    continue;
                                }

                                int neighborIndex = nx + ny * resolution;
                                if (coverage != null && !coverage[neighborIndex])
                                {
                                    continue;
                                }

                                sum += source[neighborIndex];
                                samples++;
                            }
                        }

                        target[index] = samples > 0 ? sum / samples : source[index];
                    }
                }

                float[] temp = source;
                source = target;
                target = temp;
            }

            if (!ReferenceEquals(source, values))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = source[i];
                }
            }
        }

        public static void BlurVectors(Vector2[] values, bool[] coverage, int resolution, int passes)
        {
            if (values == null || passes <= 0)
            {
                return;
            }

            Vector2[] source = values;
            Vector2[] target = new Vector2[values.Length];

            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        int index = x + y * resolution;
                        if (coverage != null && !coverage[index])
                        {
                            target[index] = Vector2.zero;
                            continue;
                        }

                        Vector2 sum = Vector2.zero;
                        int samples = 0;

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int ny = y + oy;
                            if (ny < 0 || ny >= resolution)
                            {
                                continue;
                            }

                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int nx = x + ox;
                                if (nx < 0 || nx >= resolution)
                                {
                                    continue;
                                }

                                int neighborIndex = nx + ny * resolution;
                                if (coverage != null && !coverage[neighborIndex])
                                {
                                    continue;
                                }

                                sum += source[neighborIndex];
                                samples++;
                            }
                        }

                        Vector2 averaged = samples > 0 ? sum / samples : source[index];
                        target[index] = PreserveFlowMagnitude(averaged, source[index]);
                    }
                }

                Vector2[] temp = source;
                source = target;
                target = temp;
            }

            if (!ReferenceEquals(source, values))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = source[i];
                }
            }
        }

        public static Color EncodeFlow(Vector2 flow, bool covered)
        {
            if (!covered)
            {
                return new Color(0.5f, 0.5f, 0f, 0f);
            }

            Vector2 clamped = Vector2.ClampMagnitude(flow, 1f);
            return new Color(clamped.x * 0.5f + 0.5f, clamped.y * 0.5f + 0.5f, 0f, 1f);
        }

        public static Vector2 DecodeFlow(Color pixel)
        {
            return new Vector2(pixel.r * 2f - 1f, pixel.g * 2f - 1f);
        }

        public static Vector2 NormalizeFlow(Vector2 flow, Vector2 fallback)
        {
            if (flow.sqrMagnitude > 0.000001f)
            {
                return flow.normalized;
            }

            if (fallback.sqrMagnitude > 0.000001f)
            {
                return fallback.normalized;
            }

            return Vector2.up;
        }

        public static Vector2 PreserveFlowMagnitude(Vector2 flow, Vector2 fallback)
        {
            float magnitude = Mathf.Clamp01(flow.magnitude);
            if (magnitude > 0.000001f)
            {
                return flow.normalized * magnitude;
            }

            float fallbackMagnitude = Mathf.Clamp01(fallback.magnitude);
            if (fallbackMagnitude > 0.000001f)
            {
                return fallback.normalized * fallbackMagnitude;
            }

            return Vector2.zero;
        }

        public static Bounds CalculateWorldBounds(Mesh mesh, Transform riverTransform)
        {
            if (mesh == null || riverTransform == null)
            {
                return new Bounds(riverTransform != null ? riverTransform.position : Vector3.zero, Vector3.one);
            }

            Bounds localBounds = mesh.bounds;
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z)
            };

            Bounds worldBounds = new Bounds(riverTransform.TransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                worldBounds.Encapsulate(riverTransform.TransformPoint(corners[i]));
            }

            return worldBounds;
        }

        private static void BuildTriangleBasis(
            Vector3 wp0,
            Vector3 wp1,
            Vector3 wp2,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            Vector3 fallbackNormal,
            out Vector3 tangent,
            out Vector3 bitangent)
        {
            Vector3 edge1 = wp1 - wp0;
            Vector3 edge2 = wp2 - wp0;
            Vector2 deltaUv1 = uv1 - uv0;
            Vector2 deltaUv2 = uv2 - uv0;
            float determinant = deltaUv1.x * deltaUv2.y - deltaUv1.y * deltaUv2.x;
            Vector3 normal = Vector3.Cross(edge1, edge2).normalized;
            if (normal.sqrMagnitude < 0.000001f)
            {
                normal = fallbackNormal.sqrMagnitude > 0.000001f ? fallbackNormal.normalized : Vector3.up;
            }

            if (Mathf.Abs(determinant) > 0.000001f)
            {
                tangent = (edge1 * deltaUv2.y - edge2 * deltaUv1.y) / determinant;
                bitangent = (edge2 * deltaUv1.x - edge1 * deltaUv2.x) / determinant;
            }
            else
            {
                tangent = Vector3.ProjectOnPlane(Vector3.right, normal);
                if (tangent.sqrMagnitude < 0.000001f)
                {
                    tangent = Vector3.ProjectOnPlane(Vector3.forward, normal);
                }

                bitangent = Vector3.Cross(normal, tangent);
            }

            tangent = tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.right;
            bitangent = Vector3.ProjectOnPlane(bitangent, normal);
            bitangent = bitangent.sqrMagnitude > 0.000001f ? bitangent.normalized : Vector3.Cross(normal, tangent).normalized;
        }

        private static void RasterizeTriangle(
            SurfaceData data,
            Vector3 wp0,
            Vector3 wp1,
            Vector3 wp2,
            Vector3 wn0,
            Vector3 wn1,
            Vector3 wn2,
            Vector3 tangent,
            Vector3 bitangent,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2)
        {
            int resolution = data.Resolution;
            Vector2 p0 = uv0 * (resolution - 1);
            Vector2 p1 = uv1 * (resolution - 1);
            Vector2 p2 = uv2 * (resolution - 1);
            float area = Edge(p0, p1, p2);
            if (Mathf.Abs(area) < 0.000001f)
            {
                return;
            }

            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, resolution - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, resolution - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, resolution - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(p1, p2, point) / area;
                    float w1 = Edge(p2, p0, point) / area;
                    float w2 = 1f - w0 - w1;

                    if (w0 < -0.0001f || w1 < -0.0001f || w2 < -0.0001f)
                    {
                        continue;
                    }

                    int index = x + y * resolution;
                    Vector3 worldPosition = wp0 * w0 + wp1 * w1 + wp2 * w2;
                    Vector3 worldNormal = (wn0 * w0 + wn1 * w1 + wn2 * w2).normalized;
                    if (worldNormal.sqrMagnitude < 0.000001f)
                    {
                        worldNormal = Vector3.up;
                    }

                    if (data.Coverage[index])
                    {
                        data.WorldPositions[index] = Vector3.Lerp(data.WorldPositions[index], worldPosition, 0.5f);
                        data.WorldNormals[index] = Vector3.Slerp(data.WorldNormals[index], worldNormal, 0.5f).normalized;
                        data.WorldTangents[index] = Vector3.Slerp(data.WorldTangents[index], tangent, 0.5f).normalized;
                        data.WorldBitangents[index] = Vector3.Slerp(data.WorldBitangents[index], bitangent, 0.5f).normalized;
                    }
                    else
                    {
                        data.Coverage[index] = true;
                        data.WorldPositions[index] = worldPosition;
                        data.WorldNormals[index] = worldNormal;
                        data.WorldTangents[index] = tangent;
                        data.WorldBitangents[index] = bitangent;
                    }
                }
            }
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 point)
        {
            return (point.x - a.x) * (b.y - a.y) - (point.y - a.y) * (b.x - a.x);
        }
    }
}