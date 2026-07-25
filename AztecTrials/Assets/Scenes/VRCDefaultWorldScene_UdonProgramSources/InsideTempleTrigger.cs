
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

public class InsideTempleTrigger : UdonSharpBehaviour
{
    [SerializeField] private TempleDoorBtn m_TempleDoorButton;

    [Header("Interior Detection")]
    [Tooltip("Assign a trigger collider covering the temple interior. The local camera position is tested against this volume, including while seated in a vehicle.")]
    [SerializeField] private Collider m_TempleInteriorVolume;

    [Header("Post-Close Teleport")]
    [Min(0f)]
    [SerializeField] private float m_DelayedSeconds = 1f;
    [Tooltip("Reference frame used to measure the local player's position inside the room.")]
    [SerializeField] private Transform m_RoomReference;
    [Tooltip("Destination frame that receives the same relative player position and orientation.")]
    [SerializeField] private Transform m_TeleportTargetReference;

    [SerializeField] public OcclusionManager m_OcclusionManager;

    private VRCPlayerApi m_LocalPlayer;
    private SaccEntity m_LocalVehicle;
    private bool m_PlayerInsideTemple;
    private bool m_InsideStateInitialized;
    private bool m_TeleportPending;
    private float m_TeleportTime;

    private void Start()
    {
        this.m_LocalPlayer = Networking.LocalPlayer;
        this._EnsureDoorButtonAssigned();
        this._UpdateLocalPlayerInsideTemple();
    }

    private void Update()
    {
        this._UpdateLocalPlayerInsideTemple();
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.IsValid()) return;
        if (!player.isLocal) return;

        this._TriggerForLocalPlayer();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) { return; }

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) { return; }
        if (!entity.Piloting && !entity.Passenger) { return; }

        this.m_LocalVehicle = entity;
        this._TriggerForLocalPlayer();
    }

    private void _TriggerForLocalPlayer()
    {
        this._EnsureDoorButtonAssigned();
        if (this.m_TempleDoorButton == null) { return; }

        if (this.m_TempleInteriorVolume == null)
        {
            this._SetPlayerInsideTemple(true);
        }
        else
        {
            this._UpdateLocalPlayerInsideTemple();
        }
        this.m_TempleDoorButton.CloseDoor();
    }

    public void OnDoorClosed()
    {
        this.m_TeleportPending = true;
        float delay = Mathf.Max(this.m_DelayedSeconds, 0f);
        this.m_TeleportTime = Time.time + delay;

        if (delay <= 0f)
        {
            this._TeleportAfterDoorClosed();
            return;
        }

        this.SendCustomEventDelayedSeconds(
            nameof(this._TeleportAfterDoorClosed),
            delay
        );
    }

    public void _TeleportAfterDoorClosed()
    {
        if (!this.m_TeleportPending) { return; }

        float remainingDelay = this.m_TeleportTime - Time.time;
        if (remainingDelay > 0.001f)
        {
            this.SendCustomEventDelayedSeconds(
                nameof(this._TeleportAfterDoorClosed),
                remainingDelay
            );
            return;
        }

        this.m_TeleportPending = false;
        if (this.m_TempleInteriorVolume == null) { return; }

        this._UpdateLocalPlayerInsideTemple();

        if (!this.m_PlayerInsideTemple) { return; }
        if (this.m_RoomReference == null) { return; }
        if (this.m_TeleportTargetReference == null) { return; }

        this._EnsureLocalPlayerAssigned();
        if (this.m_LocalPlayer == null) { return; }
        if (!this.m_LocalPlayer.IsValid()) { return; }

        if (this.m_LocalVehicle != null)
        {
            if (this.m_LocalVehicle.Passenger) { return; }
            if (this.m_LocalVehicle.Piloting)
            {
                this._TeleportLocalVehicle(this.m_LocalVehicle);
                return;
            }

            this.m_LocalVehicle = null;
        }

        if (this.m_LocalPlayer.GetPlayerTag("SF_LocalInVehicle") == "T") { return; }

        this._TeleportLocalPlayer();
    }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.IsValid()) return;
        if (!player.isLocal) return;

        this.m_TeleportPending = false;
        this.m_LocalVehicle = null;
        this._EnsureDoorButtonAssigned();
        if (this.m_TempleDoorButton == null) { return; }

        this._SetPlayerInsideTemple(false);
    }

    private void _UpdateLocalPlayerInsideTemple()
    {
        if (this.m_TempleInteriorVolume == null) { return; }

        this._EnsureLocalPlayerAssigned();
        if (this.m_LocalPlayer == null) { return; }
        if (!this.m_LocalPlayer.IsValid()) { return; }

        Vector3 cameraPosition = this.m_LocalPlayer.GetTrackingData(
            VRCPlayerApi.TrackingDataType.Head
        ).position;
        Vector3 closestPoint = this.m_TempleInteriorVolume.ClosestPoint(cameraPosition);
        bool playerInsideTemple = (closestPoint - cameraPosition).sqrMagnitude <= 0.0001f;

        if (this.m_InsideStateInitialized
            && playerInsideTemple == this.m_PlayerInsideTemple)
        {
            return;
        }

        this._SetPlayerInsideTemple(playerInsideTemple);
    }

    private void _SetPlayerInsideTemple(bool playerInsideTemple)
    {
        this._EnsureDoorButtonAssigned();
        if (this.m_TempleDoorButton == null)
        {
            this.m_InsideStateInitialized = false;
            return;
        }

        this.m_PlayerInsideTemple = playerInsideTemple;
        this.m_InsideStateInitialized = true;

        if (playerInsideTemple)
        {
            this.m_TempleDoorButton.SetPlayerInsideTemple();
        }
        else
        {
            this.m_TempleDoorButton.ResetPlayerInsideTemple();
        }
    }

    private void _TeleportLocalPlayer()
    {
        Vector3 playerPosition = this.m_LocalPlayer.GetPosition();
        Quaternion frameRotation = this.m_TeleportTargetReference.rotation
            * Quaternion.Inverse(this.m_RoomReference.rotation);
        Vector3 targetPosition = this.m_TeleportTargetReference.TransformPoint(
            this.m_RoomReference.InverseTransformPoint(playerPosition)
        );
        Quaternion targetRotation = frameRotation * this.m_LocalPlayer.GetRotation();

        this.m_LocalPlayer.TeleportTo(targetPosition, targetRotation);
        if (this.m_OcclusionManager != null)
        {
            this.m_OcclusionManager._TickInternal();
        }
    }

    private void _TeleportLocalVehicle(SaccEntity entity)
    {
        if (entity == null) { return; }

        Transform vehicleTransform = entity.transform;
        Vector3 playerPosition = this.m_LocalPlayer.GetPosition();
        Quaternion frameRotation = this.m_TeleportTargetReference.rotation
            * Quaternion.Inverse(this.m_RoomReference.rotation);
        Vector3 targetPlayerPosition = this.m_TeleportTargetReference.TransformPoint(
            this.m_RoomReference.InverseTransformPoint(playerPosition)
        );
        Vector3 targetVehiclePosition = targetPlayerPosition
            + frameRotation * (vehicleTransform.position - playerPosition);
        Quaternion targetVehicleRotation = frameRotation * vehicleTransform.rotation;

        if (!Networking.IsOwner(entity.gameObject))
        {
            Networking.SetOwner(this.m_LocalPlayer, entity.gameObject);
        }

        Rigidbody vehicleRigidbody = entity.GetComponent<Rigidbody>();
        Vector3 targetVelocity = Vector3.zero;
        Vector3 targetAngularVelocity = Vector3.zero;
        if (vehicleRigidbody != null && !vehicleRigidbody.isKinematic)
        {
            targetVelocity = frameRotation * vehicleRigidbody.velocity;
            targetAngularVelocity = frameRotation * vehicleRigidbody.angularVelocity;
        }

        vehicleTransform.position = targetVehiclePosition;
        vehicleTransform.rotation = targetVehicleRotation;

        if (vehicleRigidbody != null)
        {
            vehicleRigidbody.position = targetVehiclePosition;
            vehicleRigidbody.rotation = targetVehicleRotation;
            if (!vehicleRigidbody.isKinematic)
            {
                vehicleRigidbody.velocity = targetVelocity;
                vehicleRigidbody.angularVelocity = targetAngularVelocity;
            }
            else
            {
                vehicleRigidbody.Sleep();
            }
        }

        if (this.m_OcclusionManager != null)
        {
            this.m_OcclusionManager._TickInternal();
        }
        this._ResetGroundVehicleWheels(entity);
    }

    private void _ResetGroundVehicleWheels(SaccEntity entity)
    {
        SaccGroundVehicle groundVehicle = entity.GetComponent<SaccGroundVehicle>();
        if (groundVehicle == null)
        {
            groundVehicle = entity.GetComponentInChildren<SaccGroundVehicle>();
        }
        if (groundVehicle == null) { return; }

        this._ResetWheelArray(groundVehicle.DriveWheels);
        this._ResetWheelArray(groundVehicle.SteerWheels);
        this._ResetWheelArray(groundVehicle.OtherWheels);
    }

    private void _ResetWheelArray(UdonSharpBehaviour[] wheels)
    {
        if (wheels == null) { return; }

        for (int wheelIndex = 0; wheelIndex < wheels.Length; wheelIndex++)
        {
            UdonSharpBehaviour wheel = wheels[wheelIndex];
            if (wheel == null) { continue; }

            wheel.SendCustomEvent("ResetAfterTeleport");
        }
    }

    private void _EnsureLocalPlayerAssigned()
    {
        if (this.m_LocalPlayer != null) { return; }

        this.m_LocalPlayer = Networking.LocalPlayer;
    }

    private void _EnsureDoorButtonAssigned()
    {
        if (this.m_TempleDoorButton != null)
        {
            this.m_TempleDoorButton.SetInsideTempleTrigger(this);
            return;
        }

        GameObject doorButtonObject = GameObject.Find("TempleDoorBtn");
        if (doorButtonObject == null) { return; }

        this.m_TempleDoorButton = doorButtonObject.GetComponent<TempleDoorBtn>();
        if (this.m_TempleDoorButton == null) { return; }

        this.m_TempleDoorButton.SetInsideTempleTrigger(this);
    }
}
