using UnityEngine;

namespace RiverFlowBaker
{
    /// <summary>
    /// RiverFlowBakerComponent: MonoBehaviour attached to river objects.
    /// Stores configuration and generated textures for river flow baking.
    /// </summary>
    [ExecuteAlways]
    public class RiverFlowBakerComponent : MonoBehaviour
    {
        [Header("River Mesh")]
        [SerializeField] private Mesh riverMesh;

        [Header("Flow Source")]
        [SerializeField] private FlowSourceMode sourceMode = FlowSourceMode.TransformForward;
        [SerializeField] private Vector3 manualSourceDirection = Vector3.forward;
        [SerializeField] private bool useManualEndpoints;
        [SerializeField] private Vector3 manualStartPointLocal;
        [SerializeField] private Vector3 manualEndPointLocal = Vector3.forward;

        [Header("Bake Settings")]
        [SerializeField] private TextureResolution resolution = TextureResolution.Res512;
        [SerializeField] private float flowStrength = 1.0f;
        [SerializeField] private float curvatureInfluence = 0.5f;
        [SerializeField] private float velocitySmoothing = 0.8f;
        [SerializeField] private int relaxationPasses = 3;

        [Header("Obstacles")]
        [SerializeField] private LayerMask obstacleLayerMask = 0;
        [SerializeField] private float obstacleInfluenceRadius = 5.0f;
        [SerializeField] private float obstacleDeflectionStrength = 1.0f;
        [SerializeField] private float bankInfluenceStrength = 0.8f;
        [SerializeField] private float rockTurbulenceStrength = 0.6f;

        [Header("Foam Baking")]
        [SerializeField, Range(0f, 3f)] private float obstacleFoamStrength = 1.45f;
        [SerializeField, Range(0f, 3f)] private float wakeFoamStrength = 1.35f;
        [SerializeField, Range(0f, 2f)] private float openWaterFoamStrength = 0.55f;

        [Header("Generated Textures")]
        [SerializeField] private RenderTexture flowMapRT;
        [SerializeField] private RenderTexture flowUVMapRT;
        [SerializeField] private RenderTexture velocityMapRT;
        [SerializeField] private RenderTexture foamMaskRT;
        [SerializeField] private RenderTexture foamMotionMapRT;

        [Header("Export")]
        [SerializeField] private string exportPath = "Assets/Textures/River/";

        [SerializeField, HideInInspector] private Vector4 mapWorldMin = Vector4.zero;
        [SerializeField, HideInInspector] private Vector4 mapWorldSize = new Vector4(1f, 1f, 1f, 1f);

        public enum FlowSourceMode
        {
            TransformForward,
            SplineDriven,
            ManualSourcePoints
        }

        public enum TextureResolution
        {
            Res256 = 256,
            Res512 = 512,
            Res1024 = 1024,
            Res2048 = 2048,
            Res4096 = 4096
        }

        /// <summary>
        /// Create or retrieve render textures at the specified resolution.
        /// </summary>
        public void CreateRenderTextures()
        {
            int res = (int)resolution;

            EnsureRenderTexture(ref flowMapRT, res, "FlowMap");
            EnsureRenderTexture(ref flowUVMapRT, res, "FlowUVMap");
            EnsureRenderTexture(ref velocityMapRT, res, "VelocityMap");
            EnsureRenderTexture(ref foamMaskRT, res, "FoamMask");
            EnsureRenderTexture(ref foamMotionMapRT, res, "FoamMotionMap");
        }

        /// <summary>
        /// Placeholder for baking. Called by the editor window.
        /// The actual baking logic is in the editor-only classes.
        /// </summary>
        public void BakeAll()
        {
            if (riverMesh == null)
            {
                Debug.LogError("River mesh is not assigned!");
                return;
            }

            CreateRenderTextures();
            Debug.Log($"[RiverFlowBaker] Render textures created at resolution {(int)resolution}");
        }

        public void OnDisable()
        {
            // Note: Don't release render textures here in case they're being used
        }

        public void OnDestroy()
        {
            ReleaseRenderTexture(ref flowMapRT);
            ReleaseRenderTexture(ref flowUVMapRT);
            ReleaseRenderTexture(ref velocityMapRT);
            ReleaseRenderTexture(ref foamMaskRT);
            ReleaseRenderTexture(ref foamMotionMapRT);
        }

        public Bounds GetWorldBounds()
        {
            if (riverMesh == null)
            {
                return new Bounds(transform.position, Vector3.one);
            }

            Bounds localBounds = riverMesh.bounds;
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

            Bounds worldBounds = new Bounds(transform.TransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                worldBounds.Encapsulate(transform.TransformPoint(corners[i]));
            }

            return worldBounds;
        }

        public void SetMapOriginSize(Vector4 originSize)
        {
            mapWorldMin = new Vector4(originSize.x, 0f, originSize.y, 0f);
            mapWorldSize = new Vector4(Mathf.Max(0.001f, originSize.z), 1f, Mathf.Max(0.001f, originSize.w), 0f);
        }

        // Getters for editor window and shader access
        public Mesh RiverMesh => riverMesh;
        public RenderTexture FlowMapRT => flowMapRT;
        public RenderTexture FlowUVMapRT => flowUVMapRT;
        public RenderTexture VelocityMapRT => velocityMapRT;
        public RenderTexture FoamMaskRT => foamMaskRT;
        public RenderTexture FoamMotionMapRT => foamMotionMapRT;
        public int Resolution => (int)resolution;
        public string ExportPath => exportPath;
        public Transform RiverTransform => transform;
        
        public FlowSourceMode SourceMode => sourceMode;
        public Vector3 ManualSourceDirection => manualSourceDirection;
        public bool UseManualEndpoints => useManualEndpoints;
        public Vector3 ManualStartPointWorld => transform.TransformPoint(manualStartPointLocal);
        public Vector3 ManualEndPointWorld => transform.TransformPoint(manualEndPointLocal);
        public float FlowStrength => flowStrength;
        public float CurvatureInfluence => curvatureInfluence;
        public float VelocitySmoothing => velocitySmoothing;
        public int RelaxationPasses => relaxationPasses;
        public LayerMask ObstacleLayerMask => obstacleLayerMask;
        public float ObstacleInfluenceRadius => obstacleInfluenceRadius;
        public float ObstacleDeflectionStrength => obstacleDeflectionStrength;
        public float BankInfluenceStrength => bankInfluenceStrength;
        public float RockTurbulenceStrength => rockTurbulenceStrength;
        public float ObstacleFoamStrength => obstacleFoamStrength;
        public float WakeFoamStrength => wakeFoamStrength;
        public float OpenWaterFoamStrength => openWaterFoamStrength;
        public Vector4 MapWorldMin => mapWorldMin;
        public Vector4 MapWorldSize => mapWorldSize;

        // Setters for editor window
        public void SetFlowMapRT(RenderTexture rt) => flowMapRT = rt;
        public void SetFlowUVMapRT(RenderTexture rt) => flowUVMapRT = rt;
        public void SetVelocityMapRT(RenderTexture rt) => velocityMapRT = rt;
        public void SetFoamMaskRT(RenderTexture rt) => foamMaskRT = rt;
        public void SetFoamMotionMapRT(RenderTexture rt) => foamMotionMapRT = rt;

        public void SetManualEndpointWorldPositions(Vector3 start, Vector3 end)
        {
            manualStartPointLocal = transform.InverseTransformPoint(start);
            manualEndPointLocal = transform.InverseTransformPoint(end);
        }

        private static void EnsureRenderTexture(ref RenderTexture renderTexture, int resolutionValue, string textureName)
        {
            if (renderTexture != null && renderTexture.width == resolutionValue && renderTexture.height == resolutionValue)
            {
                if (!renderTexture.IsCreated())
                {
                    renderTexture.Create();
                }

                return;
            }

            ReleaseRenderTexture(ref renderTexture);

            renderTexture = new RenderTexture(resolutionValue, resolutionValue, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            renderTexture.Create();
        }

        private static void ReleaseRenderTexture(ref RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                return;
            }

            if (renderTexture.IsCreated())
            {
                renderTexture.Release();
            }

            if (Application.isPlaying)
            {
                Destroy(renderTexture);
            }
            else
            {
                DestroyImmediate(renderTexture);
            }

            renderTexture = null;
        }
    }
}

