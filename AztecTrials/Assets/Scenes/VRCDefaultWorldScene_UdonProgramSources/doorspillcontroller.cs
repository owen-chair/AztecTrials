using UdonSharp;
using UnityEngine;

[ExecuteInEditMode]
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class doorspillcontroller : UdonSharpBehaviour
{
    private const string DoorPlaneOriginPropertyName = "_UdonTempleDoorPlaneOrigin";
    private const string DoorPlaneNormalPropertyName = "_UdonTempleDoorPlaneNormal";
    private const string DoorInteriorVisibilityPropertyName = "_UdonTempleDoorInteriorVisibility";
    
    [Tooltip("initial plane offset")]
    [SerializeField] private float ClosedPlaneLift = 0.5f;
    [Tooltip("initial plane interp percent")]
    [SerializeField] private float PlaneTransitionEndFraction = 0.1f;

    [Header("Door References")]
    [SerializeField] private Transform m_DoorTransform;
    [SerializeField] private Transform m_DoorTransformClosed;
    [SerializeField] private Transform m_DoorTransformOpen;
    [Tooltip("Assign the baked Directional Light's Transform. Sunlight travels along its forward axis.")]
    [SerializeField] private Transform m_BakedLightTransform;

    [Header("Baked Light Reveal")]
    [Min(0f)]
    [SerializeField] private float m_RevealFadeWidth = 0.75f;
    [SerializeField] private bool m_DebugReveal;

    [Header("Affected Room Renderers")]
    [SerializeField] private Renderer[] m_AffectedRenderers;

    [Header("Door Interior Lighting")]
    [Range(0f, 1f)]
    [SerializeField] private float m_DoorInteriorOpenBrightness = 0.1f;
    [SerializeField] private Renderer[] m_DoorRenderers;

    private MaterialPropertyBlock m_PropertyBlock;
    private MeshFilter m_DoorMeshFilter;
    private Vector3 m_DoorTopLocalPosition;
    private Vector3 m_DoorTopEdgeLocalDirection;
    private bool m_DoorTopPositionCached;
    private bool m_PlayerInsideTemple;
    private Vector4 m_DoorPlaneOrigin;
    private Vector4 m_DoorPlaneNormal;

    private void OnEnable()
    {
        this.m_PlayerInsideTemple = false;
        this.ResetDoorLighting();
    }

    public void ResetDoorLighting()
    {
        this._UpdateLightingState();
    }

    public void BeginDoorOpening()
    {
        this._UpdateLightingState();
    }

    public void UpdateDoorLighting()
    {
        this._UpdateLightingState();
    }

    public void SetPlayerInsideTemple()
    {
        this.m_PlayerInsideTemple = true;
        this._UpdateLightingState();
    }

    public void ResetPlayerInsideTemple()
    {
        this.m_PlayerInsideTemple = false;
        this._UpdateLightingState();
    }

    private void _UpdateLightingState()
    {
        this._EnsurePropertyBlockInitialized();
        this._EnsureDoorTopPositionCached();
        if (!this._AreDoorTransformsAssigned()) { return; }

        float doorOpenFraction = this._CalculateDoorOpenFraction();
        this._ApplyDoorInteriorVisibility(doorOpenFraction);

        if (!this._AreReferencesAssigned()) { return; }

        this._UpdateRevealPlane(doorOpenFraction);
    }

    private void LateUpdate()
    {
        if (!this.m_DebugReveal) { return; }

        this.UpdateDoorLighting();
    }

    private void _UpdateRevealPlane(float doorOpenFraction)
    {
        float inverseRevealFadeWidth = 1f / Mathf.Max(this.m_RevealFadeWidth, 0.001f);
        Vector3 incomingLightDirection = this.m_BakedLightTransform.forward;
        float transitionProgress = this._CalculatePlaneTransitionProgress(doorOpenFraction);
        Vector3 planeNormal = this._CalculatePlaneNormal(
            incomingLightDirection,
            transitionProgress
        );
        Vector3 planeOrigin = this._CalculatePlaneOrigin(transitionProgress);

        this.m_DoorPlaneOrigin = new Vector4(
            planeOrigin.x,
            planeOrigin.y,
            planeOrigin.z,
            0f
        );

        this.m_DoorPlaneNormal = new Vector4(
            planeNormal.x,
            planeNormal.y,
            planeNormal.z,
            inverseRevealFadeWidth
        );

        this._ApplyRevealPlaneToRenderers();
    }

    private void _ApplyDoorInteriorVisibility(float doorOpenFraction)
    {
        if (this.m_DoorRenderers == null) { return; }

        float doorVisibility = 1f;
        if (this.m_PlayerInsideTemple)
        {
            doorVisibility = Mathf.Lerp(
                0f,
                Mathf.Clamp01(this.m_DoorInteriorOpenBrightness),
                doorOpenFraction
            );
        }

        for (int rendererIndex = 0; rendererIndex < this.m_DoorRenderers.Length; rendererIndex++)
        {
            Renderer doorRenderer = this.m_DoorRenderers[rendererIndex];
            if (doorRenderer == null) { continue; }

            doorRenderer.GetPropertyBlock(this.m_PropertyBlock);
            this.m_PropertyBlock.SetFloat(
                DoorInteriorVisibilityPropertyName,
                doorVisibility
            );
            doorRenderer.SetPropertyBlock(this.m_PropertyBlock);
        }
    }

    private void _ApplyRevealPlaneToRenderers()
    {
        if (this.m_AffectedRenderers == null) { return; }

        for (int rendererIndex = 0; rendererIndex < this.m_AffectedRenderers.Length; rendererIndex++)
        {
            Renderer affectedRenderer = this.m_AffectedRenderers[rendererIndex];
            if (affectedRenderer == null) { continue; }

            affectedRenderer.GetPropertyBlock(this.m_PropertyBlock);
            this.m_PropertyBlock.SetVector(DoorPlaneOriginPropertyName, this.m_DoorPlaneOrigin);
            this.m_PropertyBlock.SetVector(DoorPlaneNormalPropertyName, this.m_DoorPlaneNormal);
            affectedRenderer.SetPropertyBlock(this.m_PropertyBlock);
        }
    }

    private Vector3 _CalculatePlaneOrigin(float transitionProgress)
    {
        Vector3 doorTop = this.m_DoorMeshFilter.transform.TransformPoint(
            this.m_DoorTopLocalPosition
        );
        Vector3 openingDirection = this._CalculateOpeningDirection();

        return doorTop
            + openingDirection * ClosedPlaneLift * (1f - transitionProgress);
    }

    private Vector3 _CalculatePlaneNormal(
        Vector3 incomingLightDirection,
        float transitionProgress
    )
    {
        Vector3 topEdgeDirection = this.m_DoorMeshFilter.transform.TransformDirection(
            this.m_DoorTopEdgeLocalDirection
        ).normalized;

        // The optical boundary contains both the slit edge and every incoming sunlight ray.
        Vector3 opticalPlaneNormal = Vector3.Cross(topEdgeDirection, incomingLightDirection);
        Vector3 openingDirection = this._CalculateOpeningDirection();

        if (opticalPlaneNormal.sqrMagnitude <= 0.000001f)
        {
            opticalPlaneNormal = openingDirection
                - incomingLightDirection
                * Vector3.Dot(openingDirection, incomingLightDirection);
        }

        if (opticalPlaneNormal.sqrMagnitude <= 0.000001f)
        {
            opticalPlaneNormal = -openingDirection;
        }

        opticalPlaneNormal.Normalize();
        if (Vector3.Dot(opticalPlaneNormal, openingDirection) > 0f)
        {
            opticalPlaneNormal = -opticalPlaneNormal;
        }

        return Vector3.Slerp(
            -openingDirection,
            opticalPlaneNormal,
            transitionProgress
        ).normalized;
    }

    private Vector3 _CalculateOpeningDirection()
    {
        Vector3 openingDirection = this.m_DoorTransformClosed.position
            - this.m_DoorTransformOpen.position;

        if (openingDirection.sqrMagnitude <= 0.000001f) { return Vector3.up; }

        return openingDirection.normalized;
    }

    private float _CalculateDoorOpenFraction()
    {
        Vector3 doorTravel = this.m_DoorTransformOpen.position
            - this.m_DoorTransformClosed.position;
        float doorTravelDistanceSquared = doorTravel.sqrMagnitude;

        if (doorTravelDistanceSquared <= 0.000001f) { return 0f; }

        Vector3 currentDoorTravel = this.m_DoorTransform.position
            - this.m_DoorTransformClosed.position;
        return Mathf.Clamp01(
            Vector3.Dot(currentDoorTravel, doorTravel) / doorTravelDistanceSquared
        );
    }

    private float _CalculatePlaneTransitionProgress(float doorOpenFraction)
    {
        return Mathf.Clamp01(
            doorOpenFraction / Mathf.Max(PlaneTransitionEndFraction, 0.0001f)
        );
    }

    private void _EnsureDoorTopPositionCached()
    {
        if (this.m_DoorTopPositionCached) { return; }
        if (this.m_DoorTransform == null) { return; }

        this.m_DoorMeshFilter = (MeshFilter)this.m_DoorTransform.GetComponentInChildren(
            typeof(MeshFilter)
        );
        if (this.m_DoorMeshFilter == null) { return; }
        if (this.m_DoorMeshFilter.sharedMesh == null) { return; }

        Bounds localBounds = this.m_DoorMeshFilter.sharedMesh.bounds;
        this.m_DoorTopLocalPosition = localBounds.center;
        this.m_DoorTopLocalPosition.y = localBounds.max.y;
        this.m_DoorTopEdgeLocalDirection = localBounds.size.x >= localBounds.size.z
            ? Vector3.right
            : Vector3.forward;
        this.m_DoorTopPositionCached = true;
    }

    private void _EnsurePropertyBlockInitialized()
    {
        if (this.m_PropertyBlock != null) { return; }

        this.m_PropertyBlock = new MaterialPropertyBlock();
    }

    private bool _AreReferencesAssigned()
    {
        if (!this._AreDoorTransformsAssigned()) { return false; }
        if (this.m_BakedLightTransform == null) { return false; }
        if (this.m_DoorMeshFilter == null) { return false; }
        if (!this.m_DoorTopPositionCached) { return false; }

        return true;
    }

    private bool _AreDoorTransformsAssigned()
    {
        if (this.m_DoorTransform == null) { return false; }
        if (this.m_DoorTransformClosed == null) { return false; }
        if (this.m_DoorTransformOpen == null) { return false; }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!this.m_DebugReveal) { return; }

        this._EnsureDoorTopPositionCached();
        if (!this._AreReferencesAssigned()) { return; }

        Vector3 incomingLightDirection = this.m_BakedLightTransform.forward;
        float transitionProgress = this._CalculatePlaneTransitionProgress(
            this._CalculateDoorOpenFraction()
        );
        Vector3 planeNormal = this._CalculatePlaneNormal(
            incomingLightDirection,
            transitionProgress
        );
        Vector3 planeOrigin = this._CalculatePlaneOrigin(transitionProgress);
        Vector3 topEdgeDirection = this.m_DoorMeshFilter.transform.TransformDirection(
            this.m_DoorTopEdgeLocalDirection
        ).normalized;
        float planeSize = 2f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(planeOrigin, 0.1f);
        Gizmos.DrawLine(
            planeOrigin - topEdgeDirection * planeSize,
            planeOrigin + topEdgeDirection * planeSize
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(planeOrigin, incomingLightDirection * planeSize);

        Gizmos.color = Color.red;
        Gizmos.DrawRay(planeOrigin, planeNormal * planeSize);
    }
}