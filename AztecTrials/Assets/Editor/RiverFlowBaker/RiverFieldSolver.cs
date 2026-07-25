using System.Collections.Generic;
using UnityEngine;

namespace RiverFlowBaker
{
    public sealed class RiverFieldResult
    {
        public int Resolution;
        public Bounds WorldBounds;
        public Vector4 MapOriginSize;
        public float CoveragePercent;
        public float LegacyUvCoveragePercent;
        public Vector2 UvMin;
        public Vector2 UvMax;
        public int CoveredTexelCount;
        public int LegacyCoveredTexelCount;
        public int WrittenTexelCount;

        public bool[] Coverage;
        public bool[] LegacyUvCoverage;
        public bool[] WrittenTexels;
        public Vector3[] WorldPositions;
        public Vector2[] RiverUvs;
        public Vector2[] UvWorldDx;
        public Vector2[] UvWorldDz;
        public bool[] UvBasisValid;
        public Vector3[] FlowWorldDirs;
        public Vector2[] FlowMapDirs;
        public Vector2[] FlowUvDirs;
        public float[] Velocity;
        public float[] Foam;
        public Vector3[] FoamMotion;
        public float[] BankDistance;
        public float[] ObstacleProximity;
        public bool[] ObstacleMask;
        public float[] ObstacleDistance;
        public Vector2[] ObstacleGradient;
        public float[] ObstacleWake;
        public float[] Progress;
        public float ProgressLimit;
        public Vector3[] Centerline;
        public bool[] CenterlineValid;
        public float[] Curvature;
        public RiverBakeUtility.ObstacleInfo[] RawObstacles;
        public RiverBakeUtility.ObstacleInfo[] RetainedObstacles;
        public int RawObstacleCount;
        public int RetainedObstacleCount;

        public Color[] BuildFlowColors()
        {
            int count = Resolution * Resolution;
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = FlowMapDirs[i];
                if (dir.sqrMagnitude < 0.000001f)
                {
                    dir = Vector2.up;
                }
                else
                {
                    dir.Normalize();
                }

                colors[i] = new Color(dir.x * 0.5f + 0.5f, dir.y * 0.5f + 0.5f, 0f, Coverage[i] ? 1f : 0f);
            }

            return colors;
        }

        public Color[] BuildFlowUVColors()
        {
            int count = Resolution * Resolution;
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = FlowUvDirs[i];
                if (dir.sqrMagnitude < 0.000001f)
                {
                    colors[i] = new Color(0.5f, 0.5f, 0f, Coverage[i] ? 1f : 0f);
                    continue;
                }

                dir.Normalize();

                colors[i] = new Color(dir.x * 0.5f + 0.5f, dir.y * 0.5f + 0.5f, 0f, Coverage[i] ? 1f : 0f);
            }

            return colors;
        }

        public Color[] BuildVelocityColors()
        {
            int count = Resolution * Resolution;
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                float v = Mathf.Clamp01(Velocity[i]);
                colors[i] = new Color(v, v, v, Coverage[i] ? 1f : 0f);
            }

            return colors;
        }

        public Color[] BuildFoamColors()
        {
            int count = Resolution * Resolution;
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                float f = Coverage[i] ? Mathf.Clamp01(Foam[i]) : 0f;
                colors[i] = new Color(f, f, f, 1f);
            }

            return colors;
        }

        public Color[] BuildFoamMotionColors()
        {
            int count = Resolution * Resolution;
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 motion = Coverage[i] ? FoamMotion[i] : Vector3.zero;
                colors[i] = new Color(Mathf.Clamp01(motion.x), Mathf.Clamp01(motion.y), Mathf.Clamp01(motion.z), Coverage[i] ? 1f : 0f);
            }

            return colors;
        }
    }

    public static class RiverFieldCache
    {
        private static readonly Dictionary<int, RiverFieldResult> Results = new Dictionary<int, RiverFieldResult>();

        public static void Set(int instanceId, RiverFieldResult result)
        {
            Results[instanceId] = result;
        }

        public static RiverFieldResult Get(int instanceId)
        {
            return Results.TryGetValue(instanceId, out RiverFieldResult result) ? result : null;
        }

        public static void Clear(int instanceId)
        {
            Results.Remove(instanceId);
        }
    }

    public static class RiverFieldSolver
    {
        public struct SolverConfig
        {
            public Vector3 sourceDirectionWorld;
            public bool useManualEndpoints;
            public Vector3 manualStartWorld;
            public Vector3 manualEndWorld;
            public float flowStrength;
            public float curvatureInfluence;
            public float velocitySmoothing;
            public int relaxationPasses;
            public float obstacleInfluenceRadius;
            public float obstacleDeflectionStrength;
            public float bankInfluenceStrength;
            public float rockTurbulenceStrength;
            public float obstacleFoamStrength;
            public float wakeFoamStrength;
            public float openWaterFoamStrength;
        }

        private const float Unreached = 1e20f;

        public static RiverFieldResult Solve(Mesh mesh, Transform riverTransform, int resolution, List<RiverBakeUtility.ObstacleInfo> obstacles, SolverConfig config)
        {
            if (mesh == null || riverTransform == null)
            {
                throw new System.ArgumentException("A river mesh and transform are required.");
            }

            int res = Mathf.Max(16, resolution);
            int count = res * res;
            Bounds bounds = RiverBakeUtility.CalculateWorldBounds(mesh, riverTransform);
            bounds.Expand(new Vector3(
                Mathf.Max(0.1f, bounds.size.x * 0.01f),
                Mathf.Max(0.5f, bounds.size.y * 0.25f + 1f),
                Mathf.Max(0.1f, bounds.size.z * 0.01f)));

            RiverFieldResult result = new RiverFieldResult
            {
                Resolution = res,
                WorldBounds = bounds,
                MapOriginSize = new Vector4(bounds.min.x, bounds.min.z, Mathf.Max(0.001f, bounds.size.x), Mathf.Max(0.001f, bounds.size.z)),
                UvMin = Vector2.zero,
                UvMax = Vector2.one,
                Coverage = new bool[count],
                LegacyUvCoverage = new bool[count],
                WrittenTexels = new bool[count],
                WorldPositions = new Vector3[count],
                RiverUvs = new Vector2[count],
                UvWorldDx = new Vector2[count],
                UvWorldDz = new Vector2[count],
                UvBasisValid = new bool[count],
                FlowWorldDirs = new Vector3[count],
                FlowMapDirs = new Vector2[count],
                FlowUvDirs = new Vector2[count],
                Velocity = new float[count],
                Foam = new float[count],
                FoamMotion = new Vector3[count],
                BankDistance = new float[count],
                ObstacleProximity = new float[count],
                ObstacleMask = new bool[count],
                ObstacleDistance = new float[count],
                ObstacleGradient = new Vector2[count],
                ObstacleWake = new float[count],
                Progress = new float[count],
                Centerline = new Vector3[res],
                CenterlineValid = new bool[res],
                Curvature = new float[res]
            };

            BuildLegacyUvOccupancy(mesh, res, result);
            RaycastSurface(mesh, riverTransform, bounds, res, result);

            if (result.CoveredTexelCount == 0)
            {
                throw new System.InvalidOperationException("The world-space raycast baker did not hit the river mesh. Check that the mesh is a top-facing river surface and has readable geometry.");
            }

            Vector2 downstream = ProjectToMap(config.sourceDirectionWorld);
            if (downstream.sqrMagnitude < 0.000001f)
            {
                downstream = ProjectToMap(riverTransform.forward);
            }
            if (downstream.sqrMagnitude < 0.000001f)
            {
                downstream = Vector2.up;
            }
            downstream.Normalize();

            float[] bankDistance = BuildBankDistance(result.Coverage, res);
            for (int i = 0; i < count; i++)
            {
                result.BankDistance[i] = bankDistance[i];
            }

            int rawObstacleCount = obstacles != null ? obstacles.Count : 0;
            result.RawObstacleCount = rawObstacleCount;
            result.RawObstacles = obstacles != null ? obstacles.ToArray() : new RiverBakeUtility.ObstacleInfo[0];
            obstacles = BuildObstacleGeometryFields(result, obstacles, config.obstacleInfluenceRadius);
            result.RetainedObstacleCount = obstacles.Count;
            result.RetainedObstacles = obstacles.ToArray();
            Debug.Log($"[RiverFlowBaker] Rasterized {obstacles.Count}/{rawObstacleCount} obstacle mesh candidates into the obstacle SDF.");
            if (obstacles.Count > 0)
            {
                Debug.Log($"[RiverFlowBaker] Retained obstacles: {DescribeObstacles(obstacles, 16)}");
            }
            if (rawObstacleCount > 0 && obstacles.Count == 0)
            {
                Debug.LogWarning("[RiverFlowBaker] No obstacle mesh triangles rasterized into the river field. Check the obstacle layer mask and MeshFilter/sharedMesh setup.");
            }

            ComputeProgressField(result, downstream, config);
            result.ProgressLimit = ResolveProgressLimit(result, config);
            ComputeCenterlineAndCurvature(result);
            ComputeFlow(result, downstream, config);
            ComputeUvFlow(result);
            ComputeVelocity(result, config);
            ComputeFoam(result, config);
            FillUncoveredTexels(result);

            for (int i = 0; i < result.WrittenTexels.Length; i++)
            {
                result.WrittenTexels[i] = true;
            }
            result.WrittenTexelCount = result.WrittenTexels.Length;

            Debug.Log($"[RiverFlowBaker] Coverage {result.CoveredTexelCount}/{count} ({result.CoveragePercent:P1}). Legacy UV [0,1] occupancy {result.LegacyCoveredTexelCount}/{count} ({result.LegacyUvCoveragePercent:P1}). UV bounds {result.UvMin} .. {result.UvMax}. If legacy occupancy is tiny, the old baker only wrote texels whose raw UVs overlapped [0,1].");
            return result;
        }

        private static List<RiverBakeUtility.ObstacleInfo> BuildObstacleGeometryFields(RiverFieldResult result, List<RiverBakeUtility.ObstacleInfo> obstacles, float influenceRadius)
        {
            List<RiverBakeUtility.ObstacleInfo> retained = new List<RiverBakeUtility.ObstacleInfo>();
            if (obstacles == null || obstacles.Count == 0)
            {
                BuildObstacleDistanceAndGradient(result, influenceRadius);
                return retained;
            }

            for (int i = 0; i < obstacles.Count; i++)
            {
                RiverBakeUtility.ObstacleInfo obstacle = obstacles[i];
                if (RasterizeObstacleMesh(result, obstacle))
                {
                    retained.Add(obstacle);
                }
            }

            BuildObstacleDistanceAndGradient(result, influenceRadius);
            return retained;
        }

        private static string DescribeObstacles(List<RiverBakeUtility.ObstacleInfo> obstacles, int maxCount)
        {
            int count = Mathf.Min(obstacles.Count, maxCount);
            List<string> names = new List<string>(count + 1);
            for (int i = 0; i < count; i++)
            {
                RiverBakeUtility.ObstacleInfo obstacle = obstacles[i];
                string name = string.IsNullOrEmpty(obstacle.Name) ? $"Obstacle {i + 1}" : obstacle.Name;
                Vector3 size = obstacle.Bounds.size;
                names.Add($"{i + 1}:{name} bounds={size.x:0.#}x{size.y:0.#}x{size.z:0.#}");
            }

            if (obstacles.Count > count)
            {
                names.Add($"+{obstacles.Count - count} more");
            }

            return string.Join(", ", names);
        }

        private static bool RasterizeObstacleMesh(RiverFieldResult result, RiverBakeUtility.ObstacleInfo obstacle)
        {
            Mesh mesh = obstacle.Mesh != null ? obstacle.Mesh : obstacle.MeshFilter != null ? obstacle.MeshFilter.sharedMesh : null;
            if (mesh == null)
            {
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length < 3)
            {
                return false;
            }

            bool wrote = false;
            Matrix4x4 localToWorld = obstacle.MeshFilter != null ? obstacle.MeshFilter.transform.localToWorldMatrix : obstacle.LocalToWorld;
            for (int tri = 0; tri < triangles.Length; tri += 3)
            {
                int i0 = triangles[tri];
                int i1 = triangles[tri + 1];
                int i2 = triangles[tri + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                {
                    continue;
                }

                Vector2 p0 = WorldToObstacleTexel(result, localToWorld.MultiplyPoint3x4(vertices[i0]));
                Vector2 p1 = WorldToObstacleTexel(result, localToWorld.MultiplyPoint3x4(vertices[i1]));
                Vector2 p2 = WorldToObstacleTexel(result, localToWorld.MultiplyPoint3x4(vertices[i2]));
                wrote |= RasterizeProjectedTriangle(result, p0, p1, p2);
            }

            return wrote;
        }

        private static Vector2 WorldToObstacleTexel(RiverFieldResult result, Vector3 world)
        {
            Bounds bounds = result.WorldBounds;
            float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, world.x);
            float v = Mathf.InverseLerp(bounds.min.z, bounds.max.z, world.z);
            return new Vector2(u * result.Resolution - 0.5f, v * result.Resolution - 0.5f);
        }

        private static bool RasterizeProjectedTriangle(RiverFieldResult result, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            float area = Edge(p0, p1, p2);
            if (Mathf.Abs(area) < 0.00001f)
            {
                bool edge01 = RasterizeProjectedSegment(result, p0, p1);
                bool edge12 = RasterizeProjectedSegment(result, p1, p2);
                bool edge20 = RasterizeProjectedSegment(result, p2, p0);
                return edge01 || edge12 || edge20;
            }

            int res = result.Resolution;
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, res - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, res - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, res - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, res - 1);
            bool wrote = false;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    float w0 = Edge(p1, p2, point);
                    float w1 = Edge(p2, p0, point);
                    float w2 = Edge(p0, p1, point);
                    bool inside = area > 0f
                        ? w0 >= -0.0001f && w1 >= -0.0001f && w2 >= -0.0001f
                        : w0 <= 0.0001f && w1 <= 0.0001f && w2 <= 0.0001f;

                    if (inside)
                    {
                        result.ObstacleMask[x + y * res] = true;
                        wrote = true;
                    }
                }
            }

            return wrote;
        }

        private static bool RasterizeProjectedSegment(RiverFieldResult result, Vector2 a, Vector2 b)
        {
            int res = result.Resolution;
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - 1f), 0, res - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + 1f), 0, res - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - 1f), 0, res - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + 1f), 0, res - 1);
            bool wrote = false;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (DistanceToSegment(new Vector2(x, y), a, b) <= 0.75f)
                    {
                        result.ObstacleMask[x + y * res] = true;
                        wrote = true;
                    }
                }
            }

            return wrote;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr < 0.000001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSqr);
            return Vector2.Distance(point, a + segment * t);
        }

        private static void BuildObstacleDistanceAndGradient(RiverFieldResult result, float influenceRadius)
        {
            int res = result.Resolution;
            int count = res * res;
            bool hasObstacle = false;
            for (int i = 0; i < count; i++)
            {
                if (result.ObstacleMask[i])
                {
                    hasObstacle = true;
                    break;
                }
            }

            float farDistance = Mathf.Max(result.WorldBounds.size.x, result.WorldBounds.size.z);
            if (!hasObstacle)
            {
                for (int i = 0; i < count; i++)
                {
                    result.ObstacleDistance[i] = farDistance;
                    result.ObstacleGradient[i] = Vector2.zero;
                    result.ObstacleProximity[i] = 0f;
                    result.ObstacleWake[i] = 0f;
                }

                return;
            }

            bool[] openMask = new bool[count];
            for (int i = 0; i < count; i++)
            {
                openMask[i] = !result.ObstacleMask[i];
            }

            float[] outsideDistance = RiverBakeUtility.DistanceTransform(result.ObstacleMask, res);
            float[] insideDistance = RiverBakeUtility.DistanceTransform(openMask, res);
            float texelWorld = EstimateTexelWorld(result);
            for (int i = 0; i < count; i++)
            {
                float distanceTexels = result.ObstacleMask[i] ? -insideDistance[i] : outsideDistance[i];
                result.ObstacleDistance[i] = distanceTexels * texelWorld;
            }

            float dxWorld = Mathf.Max(0.0001f, result.WorldBounds.size.x / Mathf.Max(1, res));
            float dzWorld = Mathf.Max(0.0001f, result.WorldBounds.size.z / Mathf.Max(1, res));
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    float center = result.ObstacleDistance[index];
                    float left = SampleDistance(result, x - 1, y, center);
                    float right = SampleDistance(result, x + 1, y, center);
                    float down = SampleDistance(result, x, y - 1, center);
                    float up = SampleDistance(result, x, y + 1, center);
                    Vector2 gradient = new Vector2((right - left) / (2f * dxWorld), (up - down) / (2f * dzWorld));
                    result.ObstacleGradient[index] = gradient.sqrMagnitude > 0.000001f ? gradient.normalized : Vector2.zero;
                    result.ObstacleProximity[index] = result.Coverage[index] ? ObstacleFalloff(center, influenceRadius) : 0f;
                }
            }
        }

        private static float EstimateTexelWorld(RiverFieldResult result)
        {
            return Mathf.Max(result.WorldBounds.size.x, result.WorldBounds.size.z) / Mathf.Max(1, result.Resolution);
        }

        private static float SampleDistance(RiverFieldResult result, int x, int y, float fallback)
        {
            if (x < 0 || y < 0 || x >= result.Resolution || y >= result.Resolution)
            {
                return fallback;
            }

            return result.ObstacleDistance[x + y * result.Resolution];
        }

        private static void RaycastSurface(Mesh mesh, Transform riverTransform, Bounds bounds, int res, RiverFieldResult result)
        {
            GameObject go = new GameObject("River Flow Baker Raycast Mesh")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            bool previousQueriesHitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;

            try
            {
                go.transform.SetPositionAndRotation(riverTransform.position, riverTransform.rotation);
                go.transform.localScale = riverTransform.lossyScale;

                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                int[] triangles = mesh.triangles;

                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                Physics.SyncTransforms();

                float rayY = bounds.max.y + Mathf.Max(1f, bounds.size.y);
                float maxDistance = Mathf.Max(2f, bounds.size.y * 3f);

                for (int py = 0; py < res; py++)
                {
                    float z = Mathf.Lerp(bounds.min.z, bounds.max.z, (py + 0.5f) / res);
                    for (int px = 0; px < res; px++)
                    {
                        float x = Mathf.Lerp(bounds.min.x, bounds.max.x, (px + 0.5f) / res);
                        int index = px + py * res;
                        Ray ray = new Ray(new Vector3(x, rayY, z), Vector3.down);
                        if (!collider.Raycast(ray, out RaycastHit hit, maxDistance))
                        {
                            continue;
                        }

                        result.Coverage[index] = true;
                        result.WorldPositions[index] = hit.point;
                        result.RiverUvs[index] = hit.textureCoord;
                        if (TryBuildTriangleUvBasis(vertices, uvs, triangles, riverTransform, hit.triangleIndex, out Vector2 dUvDx, out Vector2 dUvDz))
                        {
                            result.UvWorldDx[index] = dUvDx;
                            result.UvWorldDz[index] = dUvDz;
                            result.UvBasisValid[index] = true;
                        }
                        result.CoveredTexelCount++;
                    }
                }

                result.CoveragePercent = result.CoveredTexelCount / (float)(res * res);
            }
            finally
            {
                Physics.queriesHitBackfaces = previousQueriesHitBackfaces;
                Object.DestroyImmediate(go);
            }
        }

        private static void BuildLegacyUvOccupancy(Mesh mesh, int res, RiverFieldResult result)
        {
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            if (uvs == null || uvs.Length == 0 || triangles == null || triangles.Length < 3)
            {
                return;
            }

            Vector2 uvMin = uvs[0];
            Vector2 uvMax = uvs[0];
            for (int i = 1; i < uvs.Length; i++)
            {
                uvMin = Vector2.Min(uvMin, uvs[i]);
                uvMax = Vector2.Max(uvMax, uvs[i]);
            }

            result.UvMin = uvMin;
            result.UvMax = uvMax;

            for (int tri = 0; tri < triangles.Length; tri += 3)
            {
                RasterizeUvTriangle(result.LegacyUvCoverage, res, uvs[triangles[tri]], uvs[triangles[tri + 1]], uvs[triangles[tri + 2]]);
            }

            int covered = 0;
            for (int i = 0; i < result.LegacyUvCoverage.Length; i++)
            {
                if (result.LegacyUvCoverage[i])
                {
                    covered++;
                }
            }

            result.LegacyCoveredTexelCount = covered;
            result.LegacyUvCoveragePercent = covered / (float)(res * res);
        }

        private static void RasterizeUvTriangle(bool[] mask, int res, Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            Vector2 p0 = uv0 * (res - 1);
            Vector2 p1 = uv1 * (res - 1);
            Vector2 p2 = uv2 * (res - 1);
            float area = Edge(p0, p1, p2);
            if (Mathf.Abs(area) < 0.000001f)
            {
                return;
            }

            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, res - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, res - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, res - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, res - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(p1, p2, point) / area;
                    float w1 = Edge(p2, p0, point) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= -0.0001f && w1 >= -0.0001f && w2 >= -0.0001f)
                    {
                        mask[x + y * res] = true;
                    }
                }
            }
        }

        private static bool TryBuildTriangleUvBasis(Vector3[] vertices, Vector2[] uvs, int[] triangles, Transform riverTransform, int triangleIndex, out Vector2 dUvDx, out Vector2 dUvDz)
        {
            dUvDx = Vector2.zero;
            dUvDz = Vector2.zero;
            if (vertices == null || uvs == null || triangles == null || riverTransform == null || uvs.Length == 0)
            {
                return false;
            }

            int triangleOffset = triangleIndex * 3;
            if (triangleOffset < 0 || triangleOffset + 2 >= triangles.Length)
            {
                return false;
            }

            int i0 = triangles[triangleOffset];
            int i1 = triangles[triangleOffset + 1];
            int i2 = triangles[triangleOffset + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length || i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length)
            {
                return false;
            }

            Vector3 p0 = riverTransform.TransformPoint(vertices[i0]);
            Vector3 p1 = riverTransform.TransformPoint(vertices[i1]);
            Vector3 p2 = riverTransform.TransformPoint(vertices[i2]);
            Vector2 worldA = new Vector2(p1.x - p0.x, p1.z - p0.z);
            Vector2 worldB = new Vector2(p2.x - p0.x, p2.z - p0.z);
            Vector2 uvA = uvs[i1] - uvs[i0];
            Vector2 uvB = uvs[i2] - uvs[i0];
            if (uvA.sqrMagnitude < 0.00000001f && uvB.sqrMagnitude < 0.00000001f)
            {
                return false;
            }

            float det = worldA.x * worldB.y - worldA.y * worldB.x;
            if (Mathf.Abs(det) < 0.0000001f)
            {
                return false;
            }

            dUvDx = (uvA * worldB.y - uvB * worldA.y) / det;
            dUvDz = (-uvA * worldB.x + uvB * worldA.x) / det;
            return dUvDx.sqrMagnitude > 0.00000001f || dUvDz.sqrMagnitude > 0.00000001f;
        }

        private static float[] BuildBankDistance(bool[] coverage, int res)
        {
            bool[] bank = new bool[coverage.Length];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!coverage[index])
                    {
                        bank[index] = true;
                        continue;
                    }

                    bool edge = x == 0 || y == 0 || x == res - 1 || y == res - 1;
                    for (int oy = -1; oy <= 1 && !edge; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = x + ox;
                            int ny = y + oy;
                            if (nx < 0 || ny < 0 || nx >= res || ny >= res || !coverage[nx + ny * res])
                            {
                                edge = true;
                                break;
                            }
                        }
                    }

                    bank[index] = edge;
                }
            }

            return RiverBakeUtility.DistanceTransform(bank, res);
        }

        private static void ComputeProgressField(RiverFieldResult result, Vector2 downstream, SolverConfig config)
        {
            int res = result.Resolution;
            int count = res * res;
            for (int i = 0; i < count; i++)
            {
                result.Progress[i] = Unreached;
            }

            float minProjection = float.MaxValue;
            float maxProjection = float.MinValue;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    float projection = Vector2.Dot(new Vector2(x, y), downstream);
                    minProjection = Mathf.Min(minProjection, projection);
                    maxProjection = Mathf.Max(maxProjection, projection);
                }
            }

            float seedBand = Mathf.Max(2f, (maxProjection - minProjection) * 0.025f);
            float centerSeedThreshold = Mathf.Max(0.5f, MaxBankDistanceInProjectionBand(result, downstream, minProjection, seedBand) * 0.35f);
            MinHeap heap = new MinHeap(count * 2);
            bool seeded = config.useManualEndpoints && SeedProgressAtAnchor(result, config.manualStartWorld, heap);
            if (!seeded)
            {
                seeded = SeedProgressBand(result, downstream, minProjection, seedBand, centerSeedThreshold, heap);
            }
            if (!seeded)
            {
                SeedProgressBand(result, downstream, minProjection, seedBand, -1f, heap);
            }

            int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] oy = { 0, 0, 1, -1, 1, -1, 1, -1 };

            while (heap.Count > 0)
            {
                int current = heap.Pop(out float currentCost);
                if (currentCost > result.Progress[current] + 0.0001f)
                {
                    continue;
                }

                int cx = current % res;
                int cy = current / res;
                for (int i = 0; i < ox.Length; i++)
                {
                    int nx = cx + ox[i];
                    int ny = cy + oy[i];
                    if (nx < 0 || ny < 0 || nx >= res || ny >= res)
                    {
                        continue;
                    }

                    int next = nx + ny * res;
                    if (!result.Coverage[next])
                    {
                        continue;
                    }

                    float step = (ox[i] == 0 || oy[i] == 0) ? 1f : 1.41421356f;
                    float centerBias = Mathf.Lerp(2.2f, 0.8f, Mathf.Clamp01(result.BankDistance[next] / Mathf.Max(2f, res * 0.04f)));
                    float cost = currentCost + step * centerBias;
                    if (cost < result.Progress[next])
                    {
                        result.Progress[next] = cost;
                        heap.Push(next, cost);
                    }
                }
            }
        }

        private static bool SeedProgressAtAnchor(RiverFieldResult result, Vector3 anchorWorld, MinHeap heap)
        {
            if (!TryFindNearestCoveredIndex(result, anchorWorld, true, out int seed))
            {
                return false;
            }

            int res = result.Resolution;
            int seedX = seed % res;
            int seedY = seed / res;
            float cellSize = EstimateGridCellSize(result);
            float seedRadius = Mathf.Max(cellSize * 2.25f, 0.001f);
            Vector2 anchor = new Vector2(result.WorldPositions[seed].x, result.WorldPositions[seed].z);
            bool seeded = false;

            int texelRadius = Mathf.Max(1, Mathf.CeilToInt(seedRadius / cellSize));
            for (int y = seedY - texelRadius; y <= seedY + texelRadius; y++)
            {
                for (int x = seedX - texelRadius; x <= seedX + texelRadius; x++)
                {
                    if (!TryGetCoveredIndex(result, x, y, out int index))
                    {
                        continue;
                    }

                    Vector2 point = new Vector2(result.WorldPositions[index].x, result.WorldPositions[index].z);
                    float distance = Vector2.Distance(point, anchor);
                    if (distance > seedRadius)
                    {
                        continue;
                    }

                    float progress = distance / Mathf.Max(cellSize, 0.001f) * 0.05f;
                    if (progress < result.Progress[index])
                    {
                        result.Progress[index] = progress;
                        heap.Push(index, progress);
                        seeded = true;
                    }
                }
            }

            if (!seeded)
            {
                result.Progress[seed] = 0f;
                heap.Push(seed, 0f);
            }

            return true;
        }

        private static bool SeedProgressBand(RiverFieldResult result, Vector2 downstream, float minProjection, float seedBand, float minBankDistance, MinHeap heap)
        {
            bool seeded = false;
            int res = result.Resolution;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index] || result.BankDistance[index] < minBankDistance)
                    {
                        continue;
                    }

                    float projection = Vector2.Dot(new Vector2(x, y), downstream);
                    if (projection <= minProjection + seedBand)
                    {
                        result.Progress[index] = 0f;
                        heap.Push(index, 0f);
                        seeded = true;
                    }
                }
            }

            return seeded;
        }

        private static float MaxBankDistanceInProjectionBand(RiverFieldResult result, Vector2 downstream, float minProjection, float seedBand)
        {
            float maxBankDistance = 0f;
            int res = result.Resolution;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    float projection = Vector2.Dot(new Vector2(x, y), downstream);
                    if (projection <= minProjection + seedBand)
                    {
                        maxBankDistance = Mathf.Max(maxBankDistance, result.BankDistance[index]);
                    }
                }
            }

            return maxBankDistance;
        }

        private static void ComputeFlow(RiverFieldResult result, Vector2 downstream, SolverConfig config)
        {
            int res = result.Resolution;
            float maxProgress = MaxProgress(result);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    Vector2 tangentFlow = CenterlineFlow(result, index, maxProgress, downstream);
                    Vector2 gradientFlow = ProgressGradient(result.Progress, result.Coverage, res, x, y);
                    Vector2 flow = tangentFlow;
                    if (gradientFlow.sqrMagnitude > 0.000001f && result.Progress[index] < Unreached * 0.5f)
                    {
                        gradientFlow.Normalize();
                        if (Vector2.Dot(gradientFlow, tangentFlow) > 0.35f)
                        {
                            flow = (tangentFlow * 0.85f + gradientFlow * 0.15f).normalized;
                        }
                    }

                    Vector2 deflection = Vector2.ClampMagnitude(ObstacleSdfDeflection(result, index, config.obstacleInfluenceRadius, config.obstacleDeflectionStrength), 0.65f);
                    if (deflection.sqrMagnitude > 0.000001f)
                    {
                        Vector2 deflected = flow + deflection;
                        if (deflected.sqrMagnitude > 0.000001f)
                        {
                            deflected.Normalize();
                            if (Vector2.Dot(deflected, tangentFlow) > 0.25f)
                            {
                                flow = deflected;
                            }
                        }
                    }

                    result.FlowMapDirs[index] = flow;
                    result.FlowWorldDirs[index] = new Vector3(flow.x, 0f, flow.y).normalized;
                }
            }

            int passes = Mathf.Clamp(config.relaxationPasses, 0, 12);
            if (passes <= 0)
            {
                return;
            }

            RiverBakeUtility.BlurVectors(result.FlowMapDirs, result.Coverage, res, passes);
            for (int i = 0; i < result.FlowMapDirs.Length; i++)
            {
                if (!result.Coverage[i])
                {
                    continue;
                }

                Vector2 flow = result.FlowMapDirs[i];
                Vector2 tangentFlow = CenterlineFlow(result, i, maxProgress, downstream);
                if (flow.sqrMagnitude < 0.000001f)
                {
                    flow = tangentFlow;
                }
                else
                {
                    flow.Normalize();
                    if (Vector2.Dot(flow, tangentFlow) < 0.25f)
                    {
                        flow = tangentFlow;
                    }
                    else
                    {
                        flow = (flow * 0.88f + tangentFlow * 0.12f).normalized;
                    }
                }

                result.FlowMapDirs[i] = flow;
                result.FlowWorldDirs[i] = new Vector3(flow.x, 0f, flow.y).normalized;
            }
        }

        private static void ComputeUvFlow(RiverFieldResult result)
        {
            int res = result.Resolution;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    result.FlowUvDirs[index] = WorldFlowToUvFlow(result, x, y, result.FlowMapDirs[index]);
                }
            }

            RiverBakeUtility.BlurVectors(result.FlowUvDirs, result.Coverage, res, 1);
            for (int i = 0; i < result.FlowUvDirs.Length; i++)
            {
                if (!result.Coverage[i])
                {
                    continue;
                }

                if (result.FlowUvDirs[i].sqrMagnitude > 0.000001f)
                {
                    result.FlowUvDirs[i].Normalize();
                }
            }
        }

        private static Vector2 WorldFlowToUvFlow(RiverFieldResult result, int x, int y, Vector2 worldFlow)
        {
            if (worldFlow.sqrMagnitude < 0.000001f)
            {
                return Vector2.zero;
            }

            worldFlow.Normalize();
            int index = x + y * result.Resolution;
            if (result.UvBasisValid[index])
            {
                Vector2 triangleUvFlow = result.UvWorldDx[index] * worldFlow.x + result.UvWorldDz[index] * worldFlow.y;
                return triangleUvFlow.sqrMagnitude > 0.000001f ? triangleUvFlow.normalized : Vector2.zero;
            }

            bool hasX = TryUvDerivative(result, x, y, true, out Vector2 dUvDx);
            bool hasZ = TryUvDerivative(result, x, y, false, out Vector2 dUvDz);
            if (!hasX && !hasZ)
            {
                return Vector2.zero;
            }

            Vector2 uvFlow = (hasX ? dUvDx * worldFlow.x : Vector2.zero) + (hasZ ? dUvDz * worldFlow.y : Vector2.zero);
            return uvFlow.sqrMagnitude > 0.000001f ? uvFlow.normalized : Vector2.zero;
        }

        private static bool TryUvDerivative(RiverFieldResult result, int x, int y, bool horizontal, out Vector2 derivative)
        {
            derivative = Vector2.zero;
            int res = result.Resolution;
            int center = x + y * res;
            int negativeX = horizontal ? x - 1 : x;
            int negativeY = horizontal ? y : y - 1;
            int positiveX = horizontal ? x + 1 : x;
            int positiveY = horizontal ? y : y + 1;
            bool hasNegative = TryGetCoveredIndex(result, negativeX, negativeY, out int negative);
            bool hasPositive = TryGetCoveredIndex(result, positiveX, positiveY, out int positive);

            if (hasNegative && hasPositive)
            {
                float worldDelta = horizontal
                    ? result.WorldPositions[positive].x - result.WorldPositions[negative].x
                    : result.WorldPositions[positive].z - result.WorldPositions[negative].z;
                if (Mathf.Abs(worldDelta) < 0.000001f)
                {
                    return false;
                }

                derivative = UvDelta(result.RiverUvs[negative], result.RiverUvs[positive]) / worldDelta;
                return derivative.sqrMagnitude > 0.000001f;
            }

            int neighbor = hasPositive ? positive : hasNegative ? negative : -1;
            if (neighbor < 0)
            {
                return false;
            }

            float oneSidedWorldDelta = horizontal
                ? result.WorldPositions[neighbor].x - result.WorldPositions[center].x
                : result.WorldPositions[neighbor].z - result.WorldPositions[center].z;
            if (Mathf.Abs(oneSidedWorldDelta) < 0.000001f)
            {
                return false;
            }

            derivative = UvDelta(result.RiverUvs[center], result.RiverUvs[neighbor]) / oneSidedWorldDelta;
            return derivative.sqrMagnitude > 0.000001f;
        }

        private static bool TryGetCoveredIndex(RiverFieldResult result, int x, int y, out int index)
        {
            index = -1;
            if (x < 0 || y < 0 || x >= result.Resolution || y >= result.Resolution)
            {
                return false;
            }

            index = x + y * result.Resolution;
            return result.Coverage[index];
        }

        private static Vector2 UvDelta(Vector2 from, Vector2 to)
        {
            return to - from;
        }

        private static Vector2 CenterlineFlow(RiverFieldResult result, int index, float maxProgress, Vector2 fallback)
        {
            if (maxProgress <= 0f || result.Centerline == null || result.CenterlineValid == null || result.Centerline.Length < 2)
            {
                return fallback;
            }

            int res = result.Resolution;
            int station = ProgressStation(result.Progress[index], maxProgress, res);
            int previous = Mathf.Max(0, station - 2);
            int next = Mathf.Min(res - 1, station + 2);

            while (previous > 0 && !result.CenterlineValid[previous])
            {
                previous--;
            }
            while (next < res - 1 && !result.CenterlineValid[next])
            {
                next++;
            }

            Vector3 tangent = result.Centerline[next] - result.Centerline[previous];
            Vector2 flow = new Vector2(tangent.x, tangent.z);
            if (flow.sqrMagnitude < 0.000001f)
            {
                return fallback;
            }

            return flow.normalized;
        }

        private static void ComputeCenterlineAndCurvature(RiverFieldResult result)
        {
            int res = result.Resolution;
            float maxProgress = MaxProgress(result);
            if (maxProgress <= 0f)
            {
                return;
            }

            float[] bestDistance = new float[res];
            Vector3[] weightedCenters = new Vector3[res];
            float[] centerWeights = new float[res];
            for (int i = 0; i < res; i++)
            {
                bestDistance[i] = -1f;
            }

            for (int i = 0; i < result.Coverage.Length; i++)
            {
                if (!result.Coverage[i] || !WithinProgressLimit(result, result.Progress[i]))
                {
                    continue;
                }

                int station = ProgressStation(result.Progress[i], maxProgress, res);
                float weight = result.BankDistance[i] * result.BankDistance[i];
                if (weight > 0.0001f)
                {
                    weightedCenters[station] += result.WorldPositions[i] * weight;
                    centerWeights[station] += weight;
                }

                if (result.BankDistance[i] > bestDistance[station])
                {
                    bestDistance[station] = result.BankDistance[i];
                    result.Centerline[station] = result.WorldPositions[i];
                    result.CenterlineValid[station] = true;
                }
            }

            for (int station = 0; station < res; station++)
            {
                if (centerWeights[station] > 0.0001f)
                {
                    result.Centerline[station] = weightedCenters[station] / centerWeights[station];
                    result.CenterlineValid[station] = true;
                }
            }

            FillCenterline(result.Centerline, result.CenterlineValid);

            for (int s = 1; s < res - 1; s++)
            {
                Vector3 a = result.Centerline[s - 1];
                Vector3 b = result.Centerline[s];
                Vector3 c = result.Centerline[s + 1];
                float length = Mathf.Max(0.001f, (Vector3.Distance(a, b) + Vector3.Distance(b, c)) * 0.5f);
                result.Curvature[s] = (a - 2f * b + c).magnitude / (length * length);
            }

            SmoothStations(result.Curvature, 2);
        }

        private static void ComputeVelocity(RiverFieldResult result, SolverConfig config)
        {
            int res = result.Resolution;
            float maxProgress = MaxProgress(result);
            float[] stationHalfWidth = new float[res];

            for (int i = 0; i < result.Coverage.Length; i++)
            {
                if (!result.Coverage[i])
                {
                    continue;
                }

                int station = ProgressStation(result.Progress[i], maxProgress, res);
                stationHalfWidth[station] = Mathf.Max(stationHalfWidth[station], result.BankDistance[i]);
            }

            FillFloatStations(stationHalfWidth);
            SmoothStations(stationHalfWidth, 4);

            float referenceWidth = Mathf.Max(1f, MedianPositive(stationHalfWidth) * 2f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    int station = ProgressStation(result.Progress[index], maxProgress, res);
                    float width = Mathf.Max(1f, stationHalfWidth[station] * 2f);
                    float constriction = Mathf.Clamp(referenceWidth / width, 0.55f, 2.2f);
                    float bankFriction = 1f - Mathf.Clamp01(result.BankDistance[index] / Mathf.Max(1f, stationHalfWidth[station]));

                    Vector2 flow = result.FlowMapDirs[index];
                    Vector2 forward = SampleFlow(result, x + Mathf.RoundToInt(flow.x * 2f), y + Mathf.RoundToInt(flow.y * 2f));
                    Vector2 backward = SampleFlow(result, x - Mathf.RoundToInt(flow.x * 2f), y - Mathf.RoundToInt(flow.y * 2f));
                    Vector2 bendVector = forward - backward;
                    float bendAmount = bendVector.magnitude;
                    Vector2 outer = bendAmount > 0.0001f ? -bendVector.normalized : Vector2.zero;
                    Vector2 bankNormal = BankGradient(result.BankDistance, result.Coverage, res, x, y);
                    float outerBias = outer.sqrMagnitude > 0f && bankNormal.sqrMagnitude > 0f ? Vector2.Dot(bankNormal.normalized, outer) : 0f;

                    float obstacleNear = ObstacleFalloff(result.ObstacleDistance[index], config.obstacleInfluenceRadius);
                    result.ObstacleProximity[index] = obstacleNear;
                    Vector2 obstacleGradient = result.ObstacleGradient[index];
                    Vector2 normalizedFlow = flow.sqrMagnitude > 0.000001f ? flow.normalized : Vector2.up;
                    Vector2 sideAxis = new Vector2(-normalizedFlow.y, normalizedFlow.x);
                    float obstacleSideRush = obstacleGradient.sqrMagnitude > 0.000001f ? obstacleNear * Mathf.SmoothStep(0.25f, 0.95f, Mathf.Abs(Vector2.Dot(obstacleGradient, sideAxis))) : 0f;
                    float obstacleWake = ObstacleWakeFromSdf(result, x, y, normalizedFlow, config.obstacleInfluenceRadius);
                    float obstacleFront = obstacleGradient.sqrMagnitude > 0.000001f ? obstacleNear * Mathf.SmoothStep(0.05f, 0.85f, -Vector2.Dot(obstacleGradient, normalizedFlow)) : 0f;
                    result.ObstacleWake[index] = obstacleWake;
                    float turbulence = obstacleNear * (Noise(result.WorldPositions[index] * 0.5f) * 2f - 1f) * config.rockTurbulenceStrength;

                    float velocity = 0.42f;
                    velocity *= constriction;
                    velocity += outerBias * bendAmount * config.curvatureInfluence * 0.75f;
                    velocity += obstacleSideRush * 0.34f;
                    velocity += obstacleWake * config.rockTurbulenceStrength * 0.18f;
                    velocity += turbulence * 0.16f;
                    velocity -= bankFriction * config.bankInfluenceStrength * 0.2f;
                    velocity -= obstacleFront * 0.16f;
                    velocity -= obstacleNear * 0.05f;
                    velocity *= Mathf.Lerp(0.75f, 1.35f, Mathf.Clamp01(config.flowStrength));

                    result.Velocity[index] = Mathf.Clamp01(velocity);
                }
            }

            RiverBakeUtility.BlurScalar(result.Velocity, result.Coverage, res, Mathf.Clamp(Mathf.RoundToInt(config.velocitySmoothing * 4f), 1, 8));
            Remap(result.Velocity, result.Coverage, 0.18f, 1f);
        }

        private static void ComputeFoam(RiverFieldResult result, SolverConfig config)
        {
            int res = result.Resolution;
            float maxProgress = MaxProgress(result);
            float[] stationHalfWidth = new float[res];

            for (int i = 0; i < result.Coverage.Length; i++)
            {
                if (!result.Coverage[i])
                {
                    continue;
                }

                int station = ProgressStation(result.Progress[i], maxProgress, res);
                stationHalfWidth[station] = Mathf.Max(stationHalfWidth[station], result.BankDistance[i]);
            }

            FillFloatStations(stationHalfWidth);
            SmoothStations(stationHalfWidth, 3);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    int station = ProgressStation(result.Progress[index], maxProgress, res);
                    float halfWidth = Mathf.Max(1f, stationHalfWidth[station]);
                    float bankWidth = Mathf.Clamp(halfWidth * 0.16f, 1.25f, 5f);
                    float bankProximity = Mathf.Clamp01((bankWidth - result.BankDistance[index]) / bankWidth);
                    bankProximity *= bankProximity;

                    float obstacle = ObstacleFalloff(result.ObstacleDistance[index], config.obstacleInfluenceRadius);
                    float contact = ObstacleContactFoam(result.ObstacleDistance[index], config.obstacleInfluenceRadius);
                    float rapid = Mathf.SmoothStep(0.82f, 1f, result.Velocity[index]);
                    float constriction = Mathf.SmoothStep(0.78f, 1f, result.Velocity[index]);
                    float turbulence = Mathf.SmoothStep(0.45f, 1f, Noise(result.WorldPositions[index] * 0.9f)) * config.rockTurbulenceStrength;
                    float curvature = Mathf.SmoothStep(0.25f, 1f, LocalCurvature(result, x, y) * config.curvatureInfluence);
                    float wake = ObstacleWakeFromSdf(result, x, y, result.FlowMapDirs[index], config.obstacleInfluenceRadius);
                    result.ObstacleWake[index] = wake;

                    float obstacleFoamStrength = Mathf.Max(0f, config.obstacleFoamStrength);
                    float wakeFoamStrength = Mathf.Max(0f, config.wakeFoamStrength);
                    float openWaterFoamStrength = Mathf.Max(0f, config.openWaterFoamStrength);

                    float bankNoise = Mathf.Lerp(0.25f, 1f, Noise(result.WorldPositions[index] * 0.55f));
                    float bankFoam = bankProximity * bankNoise * config.bankInfluenceStrength * 0.42f;
                    float obstacleFoam = Mathf.Max(Mathf.SmoothStep(0.08f, 0.78f, contact) * 1.08f, Mathf.SmoothStep(0.28f, 0.95f, obstacle) * 0.48f) * obstacleFoamStrength;
                    float centerMask = Mathf.Clamp01(result.BankDistance[index] / Mathf.Max(1f, halfWidth * 0.35f));
                    float rapidFoam = rapid * Mathf.Lerp(0.25f, 1f, turbulence) * centerMask * 0.32f * openWaterFoamStrength;
                    float bendFoam = rapid * curvature * centerMask * 0.18f * openWaterFoamStrength;
                    float constrictionFoam = rapid * constriction * centerMask * 0.14f * openWaterFoamStrength;
                    float wakeFoam = wake * 0.45f * wakeFoamStrength;

                    result.Foam[index] = Mathf.Clamp01(bankFoam + obstacleFoam + wakeFoam + rapidFoam + bendFoam + constrictionFoam);
                    result.FoamMotion[index] = new Vector3(
                        Mathf.Clamp01(wake * 1.25f * Mathf.Lerp(0.6f, 1.3f, Mathf.Clamp01(wakeFoamStrength / 2f))),
                        Mathf.Clamp01(obstacle * 0.28f * Mathf.Lerp(0.65f, 1.35f, Mathf.Clamp01(obstacleFoamStrength / 2f)) + turbulence * 0.9f + wake * 0.35f),
                        Mathf.Clamp01((rapid * centerMask + constriction * 0.25f + curvature * rapid * 0.25f) * openWaterFoamStrength * Mathf.Lerp(0.45f, 1.15f, Mathf.Clamp01(config.flowStrength))));
                }
            }

            RiverBakeUtility.BlurScalar(result.Foam, result.Coverage, res, 1);

            for (int i = 0; i < result.Foam.Length; i++)
            {
                if (result.Coverage[i])
                {
                    result.Foam[i] = Mathf.Clamp01(Mathf.Pow(result.Foam[i], 1.35f) * 1.15f);
                    result.FoamMotion[i] = new Vector3(
                        Mathf.Clamp01(result.FoamMotion[i].x),
                        Mathf.Clamp01(result.FoamMotion[i].y),
                        Mathf.Clamp01(result.FoamMotion[i].z));
                }
                else
                {
                    result.Foam[i] = 0f;
                    result.FoamMotion[i] = Vector3.zero;
                }
            }
        }

        private static void FillUncoveredTexels(RiverFieldResult result)
        {
            int[] nearest = BuildNearestCovered(result.Coverage, result.Resolution);
            for (int i = 0; i < result.Coverage.Length; i++)
            {
                if (result.Coverage[i])
                {
                    continue;
                }

                int source = nearest[i];
                result.FlowMapDirs[i] = result.FlowMapDirs[source];
                result.FlowUvDirs[i] = result.FlowUvDirs[source];
                result.FlowWorldDirs[i] = result.FlowWorldDirs[source];
                result.Velocity[i] = result.Velocity[source];
                result.Foam[i] = result.Foam[source];
                result.FoamMotion[i] = result.FoamMotion[source];
                result.BankDistance[i] = result.BankDistance[source];
                result.Progress[i] = result.Progress[source];
                result.ObstacleProximity[i] = result.ObstacleProximity[source];
                result.WorldPositions[i] = result.WorldPositions[source];
                result.RiverUvs[i] = result.RiverUvs[source];
                result.UvWorldDx[i] = result.UvWorldDx[source];
                result.UvWorldDz[i] = result.UvWorldDz[source];
                result.UvBasisValid[i] = result.UvBasisValid[source];
            }
        }

        private static Vector2 ProgressGradient(float[] progress, bool[] coverage, int res, int x, int y)
        {
            float center = progress[x + y * res];
            float left = SampleScalar(progress, coverage, res, x - 1, y, center);
            float right = SampleScalar(progress, coverage, res, x + 1, y, center);
            float down = SampleScalar(progress, coverage, res, x, y - 1, center);
            float up = SampleScalar(progress, coverage, res, x, y + 1, center);
            return new Vector2(right - left, up - down);
        }

        private static Vector2 BankGradient(float[] bankDistance, bool[] coverage, int res, int x, int y)
        {
            float center = bankDistance[x + y * res];
            return new Vector2(
                SampleScalar(bankDistance, coverage, res, x + 1, y, center) - SampleScalar(bankDistance, coverage, res, x - 1, y, center),
                SampleScalar(bankDistance, coverage, res, x, y + 1, center) - SampleScalar(bankDistance, coverage, res, x, y - 1, center));
        }

        private static float SampleScalar(float[] values, bool[] coverage, int res, int x, int y, float fallback)
        {
            if (x < 0 || y < 0 || x >= res || y >= res)
            {
                return fallback;
            }

            int index = x + y * res;
            return coverage[index] ? values[index] : fallback;
        }

        private static Vector2 SampleFlow(RiverFieldResult result, int x, int y)
        {
            if (x < 0 || y < 0 || x >= result.Resolution || y >= result.Resolution)
            {
                return Vector2.zero;
            }

            int index = x + y * result.Resolution;
            return result.Coverage[index] ? result.FlowMapDirs[index] : Vector2.zero;
        }

        private static Vector2 ObstacleSdfDeflection(RiverFieldResult result, int index, float influenceRadius, float strength)
        {
            if (strength <= 0f || result.ObstacleGradient == null || result.ObstacleDistance == null)
            {
                return Vector2.zero;
            }

            float falloff = ObstacleFalloff(result.ObstacleDistance[index], influenceRadius);
            return result.ObstacleGradient[index] * (falloff * falloff * strength);
        }

        private static float ObstacleFalloff(float signedDistance, float influenceRadius)
        {
            if (influenceRadius <= 0.0001f)
            {
                return signedDistance <= 0f ? 1f : 0f;
            }

            return 1f - Mathf.Clamp01(Mathf.Max(0f, signedDistance) / influenceRadius);
        }

        private static float ObstacleContactFoam(float signedDistance, float influenceRadius)
        {
            float contactWidth = Mathf.Max(0.08f, influenceRadius * 0.18f);
            return 1f - Mathf.Clamp01(Mathf.Abs(signedDistance) / contactWidth);
        }

        private static float ObstacleWakeFromSdf(RiverFieldResult result, int x, int y, Vector2 flow, float influenceRadius)
        {
            if (result.ObstacleMask == null || result.ObstacleDistance == null || result.ObstacleGradient == null || flow.sqrMagnitude < 0.000001f)
            {
                return 0f;
            }

            flow.Normalize();
            int res = result.Resolution;
            int index = x + y * res;
            float signedDistance = result.ObstacleDistance[index];
            if (signedDistance < 0f)
            {
                return 0f;
            }

            Vector2 currentPoint = TexelWorldXZ(result, x, y);
            Vector2 nearestPoint = NearestObstaclePointFromSdf(result, x, y);
            Vector2 fromNearestObstacle = currentPoint - nearestPoint;
            float downstreamFromNearest = fromNearestObstacle.sqrMagnitude > 0.000001f ? Mathf.SmoothStep(0.05f, 0.85f, Vector2.Dot(fromNearestObstacle.normalized, flow)) : 0f;
            float nearestWake = downstreamFromNearest * (1f - Mathf.Clamp01(signedDistance / Mathf.Max(0.001f, influenceRadius * 1.5f)));

            float texelWorld = EstimateTexelWorld(result);
            int maxSteps = Mathf.Clamp(Mathf.CeilToInt(influenceRadius * 3f / Mathf.Max(0.001f, texelWorld)), 4, 96);
            float shadowWake = 0f;
            for (int step = 1; step <= maxSteps; step++)
            {
                int sx = x - Mathf.RoundToInt(flow.x * step);
                int sy = y - Mathf.RoundToInt(flow.y * step);
                if (sx < 0 || sy < 0 || sx >= res || sy >= res)
                {
                    break;
                }

                int sample = sx + sy * res;
                if (!result.ObstacleMask[sample] && result.ObstacleDistance[sample] > texelWorld * 0.75f)
                {
                    continue;
                }

                float lengthFalloff = 1f - step / (float)maxSteps;
                float widthFalloff = 1f - Mathf.Clamp01(signedDistance / Mathf.Max(0.001f, influenceRadius * 1.25f));
                shadowWake = lengthFalloff * widthFalloff;
                break;
            }

            return Mathf.Clamp01(Mathf.Max(nearestWake * 0.7f, shadowWake));
        }

        private static Vector2 NearestObstaclePointFromSdf(RiverFieldResult result, int x, int y)
        {
            int index = x + y * result.Resolution;
            Vector2 point = TexelWorldXZ(result, x, y);
            Vector2 gradient = result.ObstacleGradient[index];
            if (gradient.sqrMagnitude < 0.000001f)
            {
                return point;
            }

            return point - gradient.normalized * result.ObstacleDistance[index];
        }

        private static Vector2 TexelWorldXZ(RiverFieldResult result, int x, int y)
        {
            float u = (x + 0.5f) / result.Resolution;
            float v = (y + 0.5f) / result.Resolution;
            Bounds bounds = result.WorldBounds;
            return new Vector2(Mathf.Lerp(bounds.min.x, bounds.max.x, u), Mathf.Lerp(bounds.min.z, bounds.max.z, v));
        }

        private static float LocalCurvature(RiverFieldResult result, int x, int y)
        {
            Vector2 flow = result.FlowMapDirs[x + y * result.Resolution];
            Vector2 forward = SampleFlow(result, x + Mathf.RoundToInt(flow.x * 3f), y + Mathf.RoundToInt(flow.y * 3f));
            Vector2 backward = SampleFlow(result, x - Mathf.RoundToInt(flow.x * 3f), y - Mathf.RoundToInt(flow.y * 3f));
            return Mathf.Clamp01((forward - backward).magnitude);
        }

        private static float MaxProgress(RiverFieldResult result)
        {
            if (result.ProgressLimit > 0.0001f)
            {
                return result.ProgressLimit;
            }

            return MaxCentralProgress(result);
        }

        private static float ResolveProgressLimit(RiverFieldResult result, SolverConfig config)
        {
            if (config.useManualEndpoints && TryFindNearestCoveredIndex(result, config.manualEndWorld, true, out int endIndex))
            {
                float endProgress = result.Progress[endIndex];
                if (endProgress > 0.0001f && endProgress < Unreached * 0.5f)
                {
                    return endProgress;
                }
            }

            return MaxCentralProgress(result);
        }

        private static bool WithinProgressLimit(RiverFieldResult result, float progress)
        {
            if (result.ProgressLimit <= 0.0001f)
            {
                return true;
            }

            float tolerance = Mathf.Max(2f, result.ProgressLimit * 0.02f);
            return progress <= result.ProgressLimit + tolerance;
        }

        private static float MaxCentralProgress(RiverFieldResult result)
        {
            float maxBankDistance = 0f;
            for (int i = 0; i < result.BankDistance.Length; i++)
            {
                if (result.Coverage[i])
                {
                    maxBankDistance = Mathf.Max(maxBankDistance, result.BankDistance[i]);
                }
            }

            float centralThreshold = Mathf.Max(0.5f, maxBankDistance * 0.08f);
            float maxProgress = 0f;
            bool foundCentralProgress = false;
            for (int i = 0; i < result.Progress.Length; i++)
            {
                if (result.Coverage[i] && result.Progress[i] < Unreached * 0.5f && result.BankDistance[i] >= centralThreshold)
                {
                    maxProgress = Mathf.Max(maxProgress, result.Progress[i]);
                    foundCentralProgress = true;
                }
            }

            if (foundCentralProgress && maxProgress > 0f)
            {
                return maxProgress;
            }

            for (int i = 0; i < result.Progress.Length; i++)
            {
                if (result.Coverage[i] && result.Progress[i] < Unreached * 0.5f)
                {
                    maxProgress = Mathf.Max(maxProgress, result.Progress[i]);
                }
            }

            return maxProgress;
        }

        private static bool TryFindNearestCoveredIndex(RiverFieldResult result, Vector3 anchorWorld, bool preferCenter, out int nearestIndex)
        {
            nearestIndex = -1;
            Vector2 anchor = new Vector2(anchorWorld.x, anchorWorld.z);
            float bestScore = float.MaxValue;
            float cellSize = EstimateGridCellSize(result);

            for (int i = 0; i < result.Coverage.Length; i++)
            {
                if (!result.Coverage[i])
                {
                    continue;
                }

                Vector2 point = new Vector2(result.WorldPositions[i].x, result.WorldPositions[i].z);
                float centerWorldDistance = result.BankDistance[i] * cellSize;
                float centerBonus = preferCenter ? centerWorldDistance * centerWorldDistance * 0.12f : 0f;
                float score = (point - anchor).sqrMagnitude - centerBonus;
                if (score < bestScore)
                {
                    bestScore = score;
                    nearestIndex = i;
                }
            }

            return nearestIndex >= 0;
        }

        private static float EstimateGridCellSize(RiverFieldResult result)
        {
            return Mathf.Max(0.001f, Mathf.Max(result.WorldBounds.size.x, result.WorldBounds.size.z) / Mathf.Max(1, result.Resolution));
        }

        private static int ProgressStation(float progress, float maxProgress, int stationCount)
        {
            if (maxProgress <= 0f || progress >= Unreached * 0.5f)
            {
                return 0;
            }

            return Mathf.Clamp(Mathf.RoundToInt((progress / maxProgress) * (stationCount - 1)), 0, stationCount - 1);
        }

        private static void FillCenterline(Vector3[] centers, bool[] valid)
        {
            int first = -1;
            for (int i = 0; i < valid.Length; i++)
            {
                if (valid[i])
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
            {
                return;
            }

            for (int i = 0; i < first; i++)
            {
                centers[i] = centers[first];
                valid[i] = true;
            }

            int last = first;
            for (int i = first + 1; i < valid.Length; i++)
            {
                if (!valid[i])
                {
                    continue;
                }

                for (int k = last + 1; k < i; k++)
                {
                    float t = (k - last) / (float)(i - last);
                    centers[k] = Vector3.Lerp(centers[last], centers[i], t);
                    valid[k] = true;
                }
                last = i;
            }

            for (int i = last + 1; i < valid.Length; i++)
            {
                centers[i] = centers[last];
                valid[i] = true;
            }
        }

        private static void FillFloatStations(float[] values)
        {
            int first = -1;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 0f)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
            {
                return;
            }
            for (int i = 0; i < first; i++)
            {
                values[i] = values[first];
            }
            int last = first;
            for (int i = first + 1; i < values.Length; i++)
            {
                if (values[i] <= 0f)
                {
                    continue;
                }
                for (int k = last + 1; k < i; k++)
                {
                    float t = (k - last) / (float)(i - last);
                    values[k] = Mathf.Lerp(values[last], values[i], t);
                }
                last = i;
            }
            for (int i = last + 1; i < values.Length; i++)
            {
                values[i] = values[last];
            }
        }

        private static void SmoothStations(float[] values, int passes)
        {
            float[] temp = new float[values.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    float prev = values[Mathf.Max(0, i - 1)];
                    float next = values[Mathf.Min(values.Length - 1, i + 1)];
                    temp[i] = (prev + values[i] + next) / 3f;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = temp[i];
                }
            }
        }

        private static void Remap(float[] values, bool[] coverage, float min, float max)
        {
            float lo = float.MaxValue;
            float hi = float.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (!coverage[i])
                {
                    continue;
                }
                lo = Mathf.Min(lo, values[i]);
                hi = Mathf.Max(hi, values[i]);
            }

            if (hi <= lo + 0.0001f)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (coverage[i])
                {
                    values[i] = Mathf.Lerp(min, max, (values[i] - lo) / (hi - lo));
                }
            }
        }

        private static int[] BuildNearestCovered(bool[] coverage, int res)
        {
            int count = coverage.Length;
            int[] nearest = new int[count];
            int[] queue = new int[count];
            int head = 0;
            int tail = 0;
            for (int i = 0; i < count; i++)
            {
                if (coverage[i])
                {
                    nearest[i] = i;
                    queue[tail++] = i;
                }
                else
                {
                    nearest[i] = -1;
                }
            }

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (head < tail)
            {
                int current = queue[head++];
                int x = current % res;
                int y = current / res;
                for (int i = 0; i < dx.Length; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];
                    if (nx < 0 || ny < 0 || nx >= res || ny >= res)
                    {
                        continue;
                    }
                    int next = nx + ny * res;
                    if (nearest[next] == -1)
                    {
                        nearest[next] = nearest[current];
                        queue[tail++] = next;
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (nearest[i] == -1)
                {
                    nearest[i] = 0;
                }
            }
            return nearest;
        }

        private static float MedianPositive(float[] values)
        {
            List<float> positive = new List<float>();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 0f)
                {
                    positive.Add(values[i]);
                }
            }

            if (positive.Count == 0)
            {
                return 0f;
            }

            positive.Sort();
            return positive[positive.Count / 2];
        }

        private static Vector2 ProjectToMap(Vector3 worldDirection)
        {
            return new Vector2(worldDirection.x, worldDirection.z);
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 point)
        {
            return (point.x - a.x) * (b.y - a.y) - (point.y - a.y) * (b.x - a.x);
        }

        private static float Noise(Vector3 p)
        {
            float v = Mathf.Sin(p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private sealed class MinHeap
        {
            private readonly List<int> indices;
            private readonly List<float> costs;

            public int Count => indices.Count;

            public MinHeap(int capacity)
            {
                indices = new List<int>(Mathf.Max(1, capacity));
                costs = new List<float>(Mathf.Max(1, capacity));
            }

            public void Push(int index, float cost)
            {
                indices.Add(index);
                costs.Add(cost);
                int i = indices.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (costs[parent] <= costs[i])
                    {
                        break;
                    }
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop(out float cost)
            {
                int result = indices[0];
                cost = costs[0];
                int last = indices.Count - 1;
                indices[0] = indices[last];
                costs[0] = costs[last];
                indices.RemoveAt(last);
                costs.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    int right = left + 1;
                    if (left >= indices.Count)
                    {
                        break;
                    }
                    int smallest = right < indices.Count && costs[right] < costs[left] ? right : left;
                    if (costs[i] <= costs[smallest])
                    {
                        break;
                    }
                    Swap(i, smallest);
                    i = smallest;
                }

                return result;
            }

            private void Swap(int a, int b)
            {
                int index = indices[a];
                indices[a] = indices[b];
                indices[b] = index;

                float cost = costs[a];
                costs[a] = costs[b];
                costs[b] = cost;
            }
        }
    }
}
