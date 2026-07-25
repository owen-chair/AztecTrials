
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Miner28.UdonUtils.Network;

public class TempleDoorBtn : NetworkInterface
{
    [SerializeField] private Transform m_DoorTransform;
    [SerializeField] private Transform m_DoorTransformClosed;
    [SerializeField] private Transform m_DoorTransformOpen;
    [SerializeField] private float m_OpenDuration = 2f;
    [SerializeField] private Transform m_ButtonTransformPressed;
    [SerializeField] private Transform m_ButtonTransformUnpressed;
    [SerializeField] private AudioSource m_ButtonPressAudioSource;
    [SerializeField] private AudioSource m_DoorOpeningAudioSource;
    [SerializeField] private AudioSource m_DoorFinishedSound;
    [SerializeField] private doorspillcontroller m_DoorFakeLightingController;

    private InsideTempleTrigger m_InsideTempleTrigger;
    private bool m_DoorOpened;
    private float m_MoveStartTime;
    private float m_MoveDuration;
    private Vector3 m_StartPosition;
    private Vector3 m_TargetPosition;
    private bool m_IsAnimating;
    private bool m_IsClosing;

    private void OnEnable()
    {
        this.m_DoorOpened = false;
        this.m_IsAnimating = false;
        this.m_IsClosing = false;
        this.m_MoveStartTime = 0f;
        this.m_MoveDuration = 0f;
        this.m_StartPosition = Vector3.zero;
        this.m_TargetPosition = Vector3.zero;

        this._MoveButtonToUnpressedPosition();

        if (this.m_DoorTransform == null) { return; }
        if (this.m_DoorTransformClosed == null) { return; }

        this.m_DoorTransform.position = this.m_DoorTransformClosed.position;
        this._ResetDoorFakeLighting();
    }

    public override void Interact()
    {
        this.OpenDoor();
    }

    public void OpenDoor()
    {
        if (!this.gameObject.activeInHierarchy) { return; }
        if (this.m_IsClosing) { return; }
        if (this.m_DoorOpened) { return; }

        SendMethodNetworked(
            nameof(this.On_AnnounceDoorOpened),
            SyncTarget.All
        );
    }

    public void CloseDoor()
    {
        if (!this.gameObject.activeInHierarchy) { return; }
        if (this.m_IsClosing) { return; }
        if (!this.m_DoorOpened) { return; }

        SendMethodNetworked(
            nameof(this.On_AnnounceDoorClosed),
            SyncTarget.All
        );
    }

    public void SetPlayerInsideTemple()
    {
        if (this.m_DoorFakeLightingController == null) { return; }

        this.m_DoorFakeLightingController.SetPlayerInsideTemple();
    }

    public void ResetPlayerInsideTemple()
    {
        if (this.m_DoorFakeLightingController == null) { return; }

        this.m_DoorFakeLightingController.ResetPlayerInsideTemple();
    }

    public void SetInsideTempleTrigger(InsideTempleTrigger insideTempleTrigger)
    {
        this.m_InsideTempleTrigger = insideTempleTrigger;
    }

    [NetworkedMethod]
    public void On_AnnounceDoorOpened()
    {
        if (!this.gameObject.activeInHierarchy) { return; }
        if (this.m_IsClosing) { return; }
        if (this.m_DoorOpened) { return; }
        if (!this._AreDoorTransformsAssigned()) { return; }

        this.m_DoorOpened = true;
        this._MoveButtonToPressedPosition();
        this._PlayAudioSource(this.m_ButtonPressAudioSource);
        this._PlayAudioSource(this.m_DoorOpeningAudioSource);
        this._BeginDoorFakeLighting();
        this._StartDoorMovement(this.m_DoorTransformOpen.position);
    }

    [NetworkedMethod]
    public void On_AnnounceDoorClosed()
    {
        if (!this.gameObject.activeInHierarchy) { return; }
        if (this.m_IsClosing) { return; }
        if (!this.m_DoorOpened) { return; }
        if (!this._AreDoorTransformsAssigned()) { return; }

        this.m_IsClosing = true;
        this.m_DoorOpened = false;
        this._MoveButtonToUnpressedPosition();
        this._PlayAudioSource(this.m_DoorOpeningAudioSource);
        this._StartDoorMovement(this.m_DoorTransformClosed.position);
    }

    private void Update()
    {
        if (!this.m_IsAnimating) { return; }

        if (this.m_DoorTransform == null)
        {
            this.m_IsAnimating = false;
            return;
        }

        float interpolation = (Time.time - this.m_MoveStartTime) / this.m_MoveDuration;
        interpolation = Mathf.Clamp01(interpolation);

        this.m_DoorTransform.position = Vector3.Lerp(
            this.m_StartPosition,
            this.m_TargetPosition,
            interpolation
        );
        this._UpdateDoorFakeLighting();

        if (interpolation < 1f) { return; }

        this.m_DoorTransform.position = this.m_TargetPosition;
        this.m_IsAnimating = false;
        if (!this.m_DoorOpened)
        {
            this.m_IsClosing = false;
            this._ResetDoorFakeLighting();
            this._NotifyDoorClosed();
        }
        this._PlayAudioSource(this.m_DoorFinishedSound);
    }

    private void _StartDoorMovement(Vector3 targetPosition)
    {
        this.m_StartPosition = this.m_DoorTransform.position;
        this.m_TargetPosition = targetPosition;

        float fullTravelDistance = Vector3.Distance(
            this.m_DoorTransformClosed.position,
            this.m_DoorTransformOpen.position
        );
        float remainingDistance = Vector3.Distance(
            this.m_StartPosition,
            this.m_TargetPosition
        );

        if (this.m_OpenDuration <= 0f || fullTravelDistance <= 0.0001f || remainingDistance <= 0.0001f)
        {
            this.m_DoorTransform.position = this.m_TargetPosition;
            this.m_IsAnimating = false;
            this._UpdateDoorFakeLighting();
            if (!this.m_DoorOpened)
            {
                this.m_IsClosing = false;
                this._ResetDoorFakeLighting();
                this._NotifyDoorClosed();
            }
            this._PlayAudioSource(this.m_DoorFinishedSound);
            return;
        }

        this.m_MoveDuration = this.m_OpenDuration * remainingDistance / fullTravelDistance;
        this.m_MoveStartTime = Time.time;
        this.m_IsAnimating = true;
    }

    private void _MoveButtonToPressedPosition()
    {
        if (this.m_ButtonTransformPressed == null) { return; }

        this.transform.position = this.m_ButtonTransformPressed.position;
    }

    private void _MoveButtonToUnpressedPosition()
    {
        if (this.m_ButtonTransformUnpressed == null) { return; }

        this.transform.position = this.m_ButtonTransformUnpressed.position;
    }

    private void _PlayAudioSource(AudioSource audioSource)
    {
        if (audioSource == null) { return; }
        if (!audioSource.gameObject.activeInHierarchy) { return; }
        if (audioSource.isPlaying) { return; }

        audioSource.Play();
    }

    private void _ResetDoorFakeLighting()
    {
        if (this.m_DoorFakeLightingController == null) { return; }

        this.m_DoorFakeLightingController.ResetDoorLighting();
    }

    private void _BeginDoorFakeLighting()
    {
        if (this.m_DoorFakeLightingController == null) { return; }

        this.m_DoorFakeLightingController.BeginDoorOpening();
    }

    private void _UpdateDoorFakeLighting()
    {
        if (this.m_DoorFakeLightingController == null) { return; }

        this.m_DoorFakeLightingController.UpdateDoorLighting();
    }

    private void _NotifyDoorClosed()
    {
        if (this.m_InsideTempleTrigger == null) { return; }

        this.m_InsideTempleTrigger.OnDoorClosed();
    }

    private bool _AreDoorTransformsAssigned()
    {
        if (this.m_DoorTransform == null) { return false; }
        if (this.m_DoorTransformClosed == null) { return false; }
        if (this.m_DoorTransformOpen == null) { return false; }

        return true;
    }
}
