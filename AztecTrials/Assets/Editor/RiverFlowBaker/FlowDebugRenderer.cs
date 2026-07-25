using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RiverFlowBaker
{
    public static class FlowDebugRenderer
    {
        public enum DebugMode
        {
            None,
            Coverage,
            LegacyUvOccupancy,
            WrittenTexels,
            FlowArrows,
            VelocityHeatmap,
            FoamPreview,
            BankDistance,
            ObstacleProximity,
            Curvature
        }

        public static void Draw(RiverFieldResult result, DebugMode mode)
        {
            if (result == null || mode == DebugMode.None)
            {
                return;
            }

            switch (mode)
            {
                case DebugMode.Coverage:
                    DrawMask(result, result.Coverage, new Color(0.1f, 0.85f, 1f, 0.85f), true);
                    break;
                case DebugMode.LegacyUvOccupancy:
                    DrawMask(result, result.LegacyUvCoverage, new Color(1f, 0.45f, 0.05f, 0.85f), false);
                    break;
                case DebugMode.WrittenTexels:
                    DrawMask(result, result.WrittenTexels, new Color(0.2f, 1f, 0.25f, 0.85f), false);
                    break;
                case DebugMode.FlowArrows:
                    DrawFlowArrows(result);
                    break;
                case DebugMode.VelocityHeatmap:
                    DrawScalarField(result, result.Velocity, Color.cyan, Color.red, true);
                    break;
                case DebugMode.FoamPreview:
                    DrawScalarField(result, result.Foam, new Color(0.05f, 0.1f, 0.16f, 0.25f), Color.white, true);
                    break;
                case DebugMode.BankDistance:
                    DrawScalarField(result, result.BankDistance, new Color(1f, 0.2f, 0.05f, 0.9f), new Color(0.1f, 0.65f, 1f, 0.9f), true);
                    break;
                case DebugMode.ObstacleProximity:
                    DrawObstacleProximity(result);
                    break;
                case DebugMode.Curvature:
                    DrawCurvature(result);
                    break;
            }
        }

        private static void DrawFlowArrows(RiverFieldResult result, int density = 48)
        {
            if (result.WorldPositions == null || result.FlowWorldDirs == null)
            {
                return;
            }

            int res = result.Resolution;
            int step = Mathf.Max(1, res / Mathf.Max(1, density));
            float worldScale = EstimateArrowScale(result, step);

#if UNITY_EDITOR
            for (int y = 0; y < res; y += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    int index = x + y * res;
                    if (!result.Coverage[index])
                    {
                        continue;
                    }

                    Vector3 dir = result.FlowWorldDirs[index];
                    if (dir.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    dir.Normalize();
                    Vector3 origin = result.WorldPositions[index] + Vector3.up * 0.05f;
                    float velocity = Mathf.Clamp01(result.Velocity[index]);
                    Handles.color = Color.Lerp(new Color(0.2f, 0.5f, 1f), new Color(1f, 0.25f, 0.1f), velocity);

                    Vector3 tip = origin + dir * worldScale * (0.45f + velocity);
                    Handles.DrawLine(origin, tip);

                    Vector3 side = Vector3.Cross(dir, Vector3.up);
                    if (side.sqrMagnitude < 1e-6f)
                    {
                        side = Vector3.Cross(dir, Vector3.forward);
                    }

                    side = side.normalized * worldScale * 0.18f;
                    Vector3 back = dir * worldScale * 0.24f;
                    Handles.DrawLine(tip, tip - back + side);
                    Handles.DrawLine(tip, tip - back - side);
                }
            }
#endif
        }

        private static void DrawMask(RiverFieldResult result, bool[] mask, Color color, bool useHitPosition)
        {
            if (mask == null)
            {
                return;
            }

            int res = result.Resolution;
            int step = Mathf.Max(1, res / 96);
            float radius = EstimatePointRadius(result, step);

#if UNITY_EDITOR
            Handles.color = color;
            for (int y = 0; y < res; y += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    int index = x + y * res;
                    if (!mask[index])
                    {
                        continue;
                    }

                    Vector3 position = useHitPosition && result.Coverage[index]
                        ? result.WorldPositions[index]
                        : GridPosition(result, x, y);
                    Handles.DrawSolidDisc(position + Vector3.up * 0.04f, Vector3.up, radius);
                }
            }
#endif
        }

        private static void DrawScalarField(RiverFieldResult result, float[] values, Color lowColor, Color highColor, bool coveredOnly)
        {
            if (values == null)
            {
                return;
            }

            int res = result.Resolution;
            int step = Mathf.Max(1, res / 96);
            float radius = EstimatePointRadius(result, step);
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < values.Length; i++)
            {
                if (coveredOnly && !result.Coverage[i])
                {
                    continue;
                }

                min = Mathf.Min(min, values[i]);
                max = Mathf.Max(max, values[i]);
            }

            if (max <= min + 0.0001f)
            {
                max = min + 1f;
            }

#if UNITY_EDITOR
            for (int y = 0; y < res; y += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    int index = x + y * res;
                    if (coveredOnly && !result.Coverage[index])
                    {
                        continue;
                    }

                    float t = Mathf.InverseLerp(min, max, values[index]);
                    Handles.color = Color.Lerp(lowColor, highColor, t);
                    Vector3 position = result.Coverage[index] ? result.WorldPositions[index] : GridPosition(result, x, y);
                    Handles.DrawSolidDisc(position + Vector3.up * 0.05f, Vector3.up, radius);
                }
            }
#endif
        }

        private static void DrawObstacleProximity(RiverFieldResult result)
        {
            if (result.ObstacleProximity == null && result.ObstacleMask == null)
            {
                return;
            }

            int res = result.Resolution;
            int step = Mathf.Max(1, res / 48);
            float radius = EstimatePointRadius(result, step);

#if UNITY_EDITOR
            float max = 0f;
            if (result.ObstacleProximity != null)
            {
                for (int i = 0; i < result.ObstacleProximity.Length; i++)
                {
                    if (result.Coverage[i])
                    {
                        max = Mathf.Max(max, result.ObstacleProximity[i]);
                    }
                }
            }

            for (int y = 0; y < res; y += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    int index = x + y * res;
                    if (result.ObstacleMask != null && result.ObstacleMask[index])
                    {
                        Handles.color = new Color(1f, 0.72f, 0.05f, 0.95f);
                        Vector3 maskPosition = result.Coverage[index] ? result.WorldPositions[index] : GridPosition(result, x, y);
                        Handles.DrawSolidDisc(maskPosition + Vector3.up * 0.09f, Vector3.up, radius * 1.1f);
                        continue;
                    }

                    if (!result.Coverage[index] || result.ObstacleProximity == null)
                    {
                        continue;
                    }

                    float value = result.ObstacleProximity[index];
                    if (value <= 0.02f)
                    {
                        continue;
                    }

                    float t = max > 0.0001f ? value / max : value;
                    Handles.color = Color.Lerp(new Color(0.15f, 0.35f, 1f, 0.35f), new Color(1f, 0.85f, 0.05f, 0.9f), Mathf.Clamp01(t));
                    Handles.DrawSolidDisc(result.WorldPositions[index] + Vector3.up * 0.07f, Vector3.up, radius);
                }
            }

            if (result.RetainedObstacles != null)
            {
                Handles.color = new Color(1f, 0.9f, 0.05f, 0.95f);
                int labelCount = Mathf.Min(result.RetainedObstacles.Length, 24);
                for (int i = 0; i < result.RetainedObstacles.Length; i++)
                {
                    RiverBakeUtility.ObstacleInfo obstacle = result.RetainedObstacles[i];
                    Vector3 position = obstacle.Bounds.center + Vector3.up * 0.12f;
                    Handles.DrawWireCube(obstacle.Bounds.center, obstacle.Bounds.size);
                    if (i < labelCount)
                    {
                        string label = string.IsNullOrEmpty(obstacle.Name) ? $"Obstacle {i + 1}" : $"{i + 1}: {obstacle.Name}";
                        Handles.Label(position, label);
                    }
                }
            }

            Vector3 labelPosition = result.WorldBounds.min + new Vector3(0f, result.WorldBounds.size.y + 0.5f, 0f);
            int retained = result.RetainedObstacles != null ? result.RetainedObstacles.Length : 0;
            Handles.color = Color.white;
                Handles.Label(labelPosition, $"Obstacle SDF: rasterized {retained}/{result.RawObstacleCount} mesh candidates. Yellow dots are obstacle mask texels; colored dots are water texels inside SDF influence.");
#endif
        }

        private static void DrawCurvature(RiverFieldResult result)
        {
            if (result.Centerline == null || result.CenterlineValid == null || result.Curvature == null)
            {
                return;
            }

            int res = result.Resolution;
            int step = Mathf.Max(1, res / 64);
            float baseRadius = EstimatePointRadius(result, step) * 2f;

#if UNITY_EDITOR
            Vector3 previous = Vector3.zero;
            bool hasPrevious = false;
            Handles.color = new Color(0.1f, 1f, 0.6f, 0.9f);
            for (int i = 0; i < res; i++)
            {
                if (!result.CenterlineValid[i])
                {
                    continue;
                }

                Vector3 center = result.Centerline[i] + Vector3.up * 0.08f;
                if (hasPrevious)
                {
                    Handles.DrawLine(previous, center);
                }

                previous = center;
                hasPrevious = true;
            }

            for (int i = 0; i < res; i += step)
            {
                if (!result.CenterlineValid[i])
                {
                    continue;
                }

                float curvature = Mathf.Clamp01(result.Curvature[i]);
                if (curvature < 0.01f)
                {
                    continue;
                }

                Handles.color = Color.Lerp(new Color(1f, 1f, 0.2f, 0.8f), new Color(1f, 0.2f, 0.1f, 0.95f), curvature);
                Handles.DrawWireDisc(result.Centerline[i] + Vector3.up * 0.08f, Vector3.up, baseRadius + curvature * baseRadius * 5f);
            }
#endif
        }

        private static Vector3 GridPosition(RiverFieldResult result, int x, int y)
        {
            float u = (x + 0.5f) / result.Resolution;
            float v = (y + 0.5f) / result.Resolution;
            Bounds bounds = result.WorldBounds;
            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, u),
                bounds.center.y,
                Mathf.Lerp(bounds.min.z, bounds.max.z, v));
        }

        private static float EstimateArrowScale(RiverFieldResult result, int step)
        {
            float diagonal = Mathf.Max(result.WorldBounds.size.x, result.WorldBounds.size.z);
            int samplesAcross = Mathf.Max(1, result.Resolution / Mathf.Max(1, step));
            return Mathf.Max(0.02f, diagonal / samplesAcross * 0.9f);
        }

        private static float EstimatePointRadius(RiverFieldResult result, int step)
        {
            float texelWorld = Mathf.Max(result.WorldBounds.size.x, result.WorldBounds.size.z) / Mathf.Max(1, result.Resolution);
            return Mathf.Max(0.01f, texelWorld * Mathf.Max(1, step) * 0.35f);
        }
    }
}
