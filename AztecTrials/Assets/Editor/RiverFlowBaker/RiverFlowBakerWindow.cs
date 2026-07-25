using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RiverFlowBaker
{
    /// <summary>
    /// Custom editor window for baking world-space river flow maps.
    /// </summary>
    public class RiverFlowBakerWindow : EditorWindow
    {
        private RiverFlowBakerComponent component;
        private SerializedObject serializedComponent;
        private FlowDebugRenderer.DebugMode debugMode = FlowDebugRenderer.DebugMode.None;
        private bool showDebugVisualization;
        private Vector2 scrollPosition;

        private static GUIStyle sectionHeaderStyle;
        private static GUIStyle buttonStyle;

        [MenuItem("Tools/River Flow Baker")]
        public static void ShowWindow()
        {
            GetWindow<RiverFlowBakerWindow>("River Flow Baker");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneViewGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;
        }

        private void OnGUI()
        {
            InitializeStyles();
            EnsureComponentState();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            DrawHeader();

            if (component != null && serializedComponent != null)
            {
                DrawRiverSettings();
                DrawObstacleSettings();
                DrawFoamBakeSettings();
                DrawDebugVisualization();
                DrawOutputSettings();
                DrawActionButtons();
            }

            GUILayout.EndScrollView();
        }

        private void InitializeStyles()
        {
            if (sectionHeaderStyle == null)
            {
                sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 10, 5)
                };
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    padding = new RectOffset(10, 10, 8, 8),
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("River Flow Baker", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("World-space raycast baker. Outputs FlowMap, FlowUVMap, VelocityMap, FoamMask, and FoamMotionMap.", MessageType.Info);
            EditorGUILayout.Space(10);

            RiverFlowBakerComponent newComponent = EditorGUILayout.ObjectField("River Component", component, typeof(RiverFlowBakerComponent), true) as RiverFlowBakerComponent;
            if (newComponent != component)
            {
                component = newComponent;
                serializedComponent = component != null ? new SerializedObject(component) : null;
            }

            if (component == null)
            {
                EditorGUILayout.HelpBox("Select a RiverFlowBakerComponent to begin.", MessageType.Warning);
            }
        }

        private void DrawRiverSettings()
        {
            EditorGUILayout.LabelField("River Settings", sectionHeaderStyle);
            serializedComponent.Update();
            DrawPropertyField("riverMesh", "River Mesh");
            DrawPropertyField("sourceMode", "Flow Source Mode");
            SerializedProperty sourceMode = serializedComponent.FindProperty("sourceMode");
            DrawPropertyField("manualSourceDirection", "Manual Source Direction", "sourceMode");
            DrawManualEndpointSettings(sourceMode);
            DrawPropertyField("resolution", "Texture Resolution");
            DrawPropertyField("flowStrength", "Flow Strength");
            DrawPropertyField("curvatureInfluence", "Curvature Influence");
            DrawPropertyField("velocitySmoothing", "Velocity Smoothing");
            DrawPropertyField("relaxationPasses", "Flow Relaxation Passes");

            if (sourceMode != null && sourceMode.enumValueIndex == (int)RiverFlowBakerComponent.FlowSourceMode.SplineDriven)
            {
                EditorGUILayout.HelpBox("SplineDriven currently falls back to the river transform forward direction because no spline source is serialized.", MessageType.Info);
            }

            serializedComponent.ApplyModifiedProperties();
        }

        private void DrawManualEndpointSettings(SerializedProperty sourceMode)
        {
            if (sourceMode == null || sourceMode.enumValueIndex != (int)RiverFlowBakerComponent.FlowSourceMode.ManualSourcePoints)
            {
                return;
            }

            DrawPropertyField("useManualEndpoints", "Use Manual Start / End");
            SerializedProperty useManualEndpoints = serializedComponent.FindProperty("useManualEndpoints");
            if (useManualEndpoints == null || !useManualEndpoints.boolValue)
            {
                return;
            }

            DrawPropertyField("manualStartPointLocal", "Start Point (Local)");
            DrawPropertyField("manualEndPointLocal", "End Point (Local)");
            EditorGUILayout.HelpBox("Drag the Start and End handles in the Scene view. These anchors define progress stationing and downstream direction for the bake.", MessageType.Info);

            using (new EditorGUI.DisabledScope(component == null || component.RiverMesh == null))
            {
                if (GUILayout.Button("Initialize Endpoints From Current Direction"))
                {
                    InitializeManualEndpointsFromDirection();
                }
            }
        }

        private void DrawObstacleSettings()
        {
            EditorGUILayout.LabelField("Obstacles / Banks", sectionHeaderStyle);
            serializedComponent.Update();
            DrawPropertyField("obstacleLayerMask", "Obstacle Layers");
            DrawPropertyField("obstacleInfluenceRadius", "Influence Radius");
            DrawPropertyField("obstacleDeflectionStrength", "Deflection Strength");
            DrawPropertyField("bankInfluenceStrength", "Bank Influence");
            DrawPropertyField("rockTurbulenceStrength", "Rock Turbulence");
            serializedComponent.ApplyModifiedProperties();
        }

        private void DrawFoamBakeSettings()
        {
            EditorGUILayout.LabelField("Foam Baking", sectionHeaderStyle);
            serializedComponent.Update();
            DrawPropertyField("obstacleFoamStrength", "Obstacle Foam Strength");
            DrawPropertyField("wakeFoamStrength", "Wake Foam Strength");
            DrawPropertyField("openWaterFoamStrength", "Open Water Foam Strength");
            serializedComponent.ApplyModifiedProperties();
        }

        private void DrawDebugVisualization()
        {
            EditorGUILayout.LabelField("Debug Visualization", sectionHeaderStyle);
            showDebugVisualization = EditorGUILayout.Foldout(showDebugVisualization, "Show Debug Visuals");
            if (!showDebugVisualization)
            {
                return;
            }

            debugMode = (FlowDebugRenderer.DebugMode)EditorGUILayout.EnumPopup("Debug Mode", debugMode);
            EditorGUILayout.HelpBox(
                "Coverage: world-space raycast texels hit by the river mesh.\n" +
                "UV Occupancy: texels the old raw [0,1] UV rasterizer would have written.\n" +
                "Written Texels: final map pixels after nearest-hit fill.\n" +
                "Flow/Velocity/Foam/Proximity/Curvature: solved field diagnostics.",
                MessageType.Info);
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("Output", sectionHeaderStyle);
            serializedComponent.Update();
            DrawPropertyField("exportPath", "Export Path");
            serializedComponent.ApplyModifiedProperties();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.LabelField("Actions", sectionHeaderStyle);

            using (new EditorGUI.DisabledScope(component == null || component.RiverMesh == null))
            {
                if (GUILayout.Button("Bake Flow Field", buttonStyle, GUILayout.Height(40)))
                {
                    BakeMaps();
                }
            }

            using (new EditorGUI.DisabledScope(component == null))
            {
                if (GUILayout.Button("Export Textures", buttonStyle, GUILayout.Height(32)))
                {
                    ExportTextures();
                }
            }

            if (GUILayout.Button("Refresh Scene Debug", buttonStyle))
            {
                SceneView.RepaintAll();
            }
        }

        private void ExportTextures()
        {
            try
            {
                EditorUtility.DisplayProgressBar("River Flow Baker", "Exporting textures...", 0.5f);
                FlowMapExporter.ExportAllMaps(component);
                EditorUtility.DisplayDialog("Success", "FlowMap, FlowUVMap, VelocityMap, FoamMask, and FoamMotionMap exported.", "OK");
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Error", $"Export failed: {exception.Message}", "OK");
                Debug.LogError($"[RiverFlowBaker] Export error: {exception}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawPropertyField(string propertyName, string label, string visibilityProperty = null)
        {
            if (!string.IsNullOrEmpty(visibilityProperty))
            {
                SerializedProperty controller = serializedComponent.FindProperty(visibilityProperty);
                if (controller != null && controller.propertyType == SerializedPropertyType.Enum)
                {
                    RiverFlowBakerComponent.FlowSourceMode mode = (RiverFlowBakerComponent.FlowSourceMode)controller.enumValueIndex;
                    if (propertyName == "manualSourceDirection" && mode != RiverFlowBakerComponent.FlowSourceMode.ManualSourcePoints)
                    {
                        return;
                    }
                }
            }

            SerializedProperty property = serializedComponent.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private void OnSceneViewGUI(SceneView sceneView)
        {
            if (component == null)
            {
                return;
            }

            DrawManualEndpointHandles();

            if (showDebugVisualization)
            {
                FlowDebugRenderer.Draw(RiverFieldCache.Get(component.GetInstanceID()), debugMode);
            }
        }

        private void DrawManualEndpointHandles()
        {
            if (serializedComponent == null || component.SourceMode != RiverFlowBakerComponent.FlowSourceMode.ManualSourcePoints || !component.UseManualEndpoints)
            {
                return;
            }

            SerializedProperty startProperty = serializedComponent.FindProperty("manualStartPointLocal");
            SerializedProperty endProperty = serializedComponent.FindProperty("manualEndPointLocal");
            if (startProperty == null || endProperty == null)
            {
                return;
            }

            serializedComponent.Update();
            Vector3 start = component.RiverTransform.TransformPoint(startProperty.vector3Value);
            Vector3 end = component.RiverTransform.TransformPoint(endProperty.vector3Value);

            Handles.color = new Color(0.1f, 1f, 0.45f, 0.95f);
            Handles.Label(start + Vector3.up * 0.35f, "River Start");
            Handles.SphereHandleCap(0, start, Quaternion.identity, HandleUtility.GetHandleSize(start) * 0.12f, EventType.Repaint);
            EditorGUI.BeginChangeCheck();
            start = Handles.PositionHandle(start, Quaternion.identity);
            bool movedStart = EditorGUI.EndChangeCheck();

            Handles.color = new Color(1f, 0.35f, 0.1f, 0.95f);
            Handles.Label(end + Vector3.up * 0.35f, "River End");
            Handles.SphereHandleCap(0, end, Quaternion.identity, HandleUtility.GetHandleSize(end) * 0.12f, EventType.Repaint);
            EditorGUI.BeginChangeCheck();
            end = Handles.PositionHandle(end, Quaternion.identity);
            bool movedEnd = EditorGUI.EndChangeCheck();

            Handles.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Handles.DrawLine(start, end);

            if (!movedStart && !movedEnd)
            {
                return;
            }

            Undo.RecordObject(component, "Move River Flow Endpoint");
            if (movedStart)
            {
                startProperty.vector3Value = component.RiverTransform.InverseTransformPoint(start);
            }
            if (movedEnd)
            {
                endProperty.vector3Value = component.RiverTransform.InverseTransformPoint(end);
            }
            serializedComponent.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            SceneView.RepaintAll();
        }

        private void InitializeManualEndpointsFromDirection()
        {
            Bounds bounds = RiverBakeUtility.CalculateWorldBounds(component.RiverMesh, component.RiverTransform);
            Vector3 direction = ResolveSourceDirection();
            Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);
            if (horizontal.sqrMagnitude < 1e-6f)
            {
                horizontal = component.RiverTransform.forward;
                horizontal.y = 0f;
            }

            horizontal.Normalize();
            float halfLength = Mathf.Max(bounds.extents.x, bounds.extents.z);
            Vector3 center = bounds.center;
            center.y = component.RiverTransform.position.y;

            Undo.RecordObject(component, "Initialize River Flow Endpoints");
            component.SetManualEndpointWorldPositions(center - horizontal * halfLength, center + horizontal * halfLength);
            EditorUtility.SetDirty(component);
            serializedComponent.Update();
            SceneView.RepaintAll();
        }

        private void EnsureComponentState()
        {
            if (component == null && Selection.activeGameObject != null)
            {
                component = Selection.activeGameObject.GetComponent<RiverFlowBakerComponent>();
            }

            if (component == null)
            {
                serializedComponent = null;
                return;
            }

            if (serializedComponent == null || serializedComponent.targetObject != component)
            {
                serializedComponent = new SerializedObject(component);
            }
        }

        private void BakeMaps()
        {
            if (component == null || component.RiverMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "Assign a river mesh before baking.", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("River Flow Baker", "Creating render textures...", 0.1f);
                component.CreateRenderTextures();

                EditorUtility.DisplayProgressBar("River Flow Baker", "Finding obstacle meshes...", 0.2f);
                Bounds worldBounds = RiverBakeUtility.CalculateWorldBounds(component.RiverMesh, component.RiverTransform);
                List<RiverBakeUtility.ObstacleInfo> obstacles = RiverBakeUtility.FindNearbyObstacles(worldBounds, component.ObstacleLayerMask, component.ObstacleInfluenceRadius, component.RiverTransform);
                Debug.Log($"[RiverFlowBaker] Found {obstacles.Count} obstacle mesh candidates from MeshFilters on the obstacle layer mask.");

                EditorUtility.DisplayProgressBar("River Flow Baker", "Raycasting and solving flow field...", 0.45f);
                RiverFieldSolver.SolverConfig config = new RiverFieldSolver.SolverConfig
                {
                    sourceDirectionWorld = ResolveSourceDirection(),
                    useManualEndpoints = component.SourceMode == RiverFlowBakerComponent.FlowSourceMode.ManualSourcePoints && component.UseManualEndpoints,
                    manualStartWorld = component.ManualStartPointWorld,
                    manualEndWorld = component.ManualEndPointWorld,
                    flowStrength = component.FlowStrength,
                    curvatureInfluence = component.CurvatureInfluence,
                    velocitySmoothing = component.VelocitySmoothing,
                    relaxationPasses = component.RelaxationPasses,
                    obstacleInfluenceRadius = component.ObstacleInfluenceRadius,
                    obstacleDeflectionStrength = component.ObstacleDeflectionStrength,
                    bankInfluenceStrength = component.BankInfluenceStrength,
                    rockTurbulenceStrength = component.RockTurbulenceStrength,
                    obstacleFoamStrength = component.ObstacleFoamStrength,
                    wakeFoamStrength = component.WakeFoamStrength,
                    openWaterFoamStrength = component.OpenWaterFoamStrength
                };

                RiverFieldResult result = RiverFieldSolver.Solve(component.RiverMesh, component.RiverTransform, component.Resolution, obstacles, config);

                EditorUtility.DisplayProgressBar("River Flow Baker", "Writing maps...", 0.85f);
                RiverBakeUtility.WriteColorsToRenderTexture(component.FlowMapRT, result.Resolution, result.BuildFlowColors());
                RiverBakeUtility.WriteColorsToRenderTexture(component.FlowUVMapRT, result.Resolution, result.BuildFlowUVColors());
                RiverBakeUtility.WriteColorsToRenderTexture(component.VelocityMapRT, result.Resolution, result.BuildVelocityColors());
                RiverBakeUtility.WriteColorsToRenderTexture(component.FoamMaskRT, result.Resolution, result.BuildFoamColors());
                RiverBakeUtility.WriteColorsToRenderTexture(component.FoamMotionMapRT, result.Resolution, result.BuildFoamMotionColors());

                component.SetMapOriginSize(result.MapOriginSize);
                ApplyResultToMaterials();
                RiverFieldCache.Set(component.GetInstanceID(), result);
                EditorUtility.SetDirty(component);

                int total = result.Resolution * result.Resolution;
                Debug.Log($"[RiverFlowBaker] Final maps write {result.WrittenTexelCount}/{total} texels. Raycast coverage {Percent(result.CoveredTexelCount, total)}; old UV occupancy {Percent(result.LegacyCoveredTexelCount, total)}.");
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Error", $"Bake failed: {exception.Message}", "OK");
                Debug.LogError($"[RiverFlowBaker] Bake error: {exception}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private void ApplyResultToMaterials()
        {
            Renderer renderer = component.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_FlowMap")) material.SetTexture("_FlowMap", component.FlowMapRT);
                if (material.HasProperty("_FlowUVMap")) material.SetTexture("_FlowUVMap", component.FlowUVMapRT);
                if (material.HasProperty("_VelocityMap")) material.SetTexture("_VelocityMap", component.VelocityMapRT);
                if (material.HasProperty("_FoamMask")) material.SetTexture("_FoamMask", component.FoamMaskRT);
                if (material.HasProperty("_FoamMotionMap")) material.SetTexture("_FoamMotionMap", component.FoamMotionMapRT);
                if (material.HasProperty("_RiverBoundsMin")) material.SetVector("_RiverBoundsMin", component.MapWorldMin);
                if (material.HasProperty("_RiverBoundsSize")) material.SetVector("_RiverBoundsSize", component.MapWorldSize);
                EditorUtility.SetDirty(material);
            }
        }

        private Vector3 ResolveSourceDirection()
        {
            Vector3 direction;
            if (component.SourceMode == RiverFlowBakerComponent.FlowSourceMode.ManualSourcePoints && component.UseManualEndpoints)
            {
                direction = component.ManualEndPointWorld - component.ManualStartPointWorld;
            }
            else
            {
                direction = component.SourceMode == RiverFlowBakerComponent.FlowSourceMode.ManualSourcePoints
                    ? component.ManualSourceDirection
                    : component.RiverTransform.forward;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f)
            {
                direction = Vector3.forward;
            }

            return direction.normalized;
        }

        private static string Percent(int value, int total)
        {
            return total > 0 ? (value / (float)total * 100f).ToString("0.0") + "%" : "0.0%";
        }
    }
}
