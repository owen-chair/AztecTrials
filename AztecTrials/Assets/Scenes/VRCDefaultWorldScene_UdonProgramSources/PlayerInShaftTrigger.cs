
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using Miner28.UdonUtils.Network;
using SaccFlightAndVehicles;

public class PlayerInShaftTrigger : NetworkInterface
{
    [Header("Movement")]
    [Tooltip("The kinematic Rigidbody on the elevator platform.")]
    [SerializeField] private Rigidbody elevatorBody;
    [Tooltip("The stationary marker for the elevator's starting position.")]
    [SerializeField] private Transform startPosition;
    [Tooltip("The stationary marker for the elevator's destination.")]
    [SerializeField] private Transform endPosition;
    [Min(0f)]
    [SerializeField] private float movementDurationSeconds = 3f;
    [Min(0f)]
    [SerializeField] private float leadTimeSeconds = 1f;

    [Header("Flame Whoosh Audio")]
    [SerializeField] private AudioSource whooshAudio;
    [Header("Drum Beat Audio")]
    [SerializeField] private AudioSource beatAudio;

    private Vector3 m_MovementStartPosition;
    private Vector3 m_MovementEndPosition;
    private bool m_ResetPositionPending;
    private bool m_MovementFinished;

    private bool m_HasSynchronizedSchedule;
    private double m_ScheduledStartTime;
    private int m_SchedulePlayerId = -1;
    private double m_LastStartedScheduleTime;

    private bool m_LocalFallbackActive;
    private float m_LocalFallbackStartTime;

    private bool m_LocalPlayerInsideTrigger;
    private SaccEntity m_LocalVehicleInsideTrigger;
    private int m_LocalVehicleColliderCount;
    private bool m_LocalEntryHandled;

    private void FixedUpdate()
    {
        if (this.m_ResetPositionPending && this.elevatorBody != null)
        {
            this.elevatorBody.MovePosition(this.m_MovementStartPosition);
            this.m_ResetPositionPending = false;
        }

        if (this.m_LocalFallbackActive)
        {
            double localElapsed = (double)Time.time - (double)this.m_LocalFallbackStartTime;
            if (this._UpdateElevatorPosition(localElapsed))
            {
                this.m_LocalFallbackActive = false;
            }
            return;
        }

        if (!this.m_HasSynchronizedSchedule) { return; }

        double serverTime = Networking.GetServerTimeInSeconds();
        if (!this._IsValidServerTime(serverTime)) { return; }

        this._RecordCurrentScheduleStarted(serverTime);
        this._UpdateElevatorPosition(serverTime - this.m_ScheduledStartTime);
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null) { return; }
        if (!player.IsValid()) { return; }
        if (!player.isLocal) { return; }

        this.m_LocalPlayerInsideTrigger = true;
        this._TriggerForLocalPlayer();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null) { return; }
        if (!player.IsValid()) { return; }
        if (!player.isLocal) { return; }

        this.m_LocalPlayerInsideTrigger = false;
        this._ResetLocalEntryWhenOutside();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) { return; }

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) { return; }
        if (!entity.Piloting && !entity.Passenger) { return; }

        if (entity == this.m_LocalVehicleInsideTrigger)
        {
            this.m_LocalVehicleColliderCount++;
            return;
        }

        this.m_LocalVehicleInsideTrigger = entity;
        this.m_LocalVehicleColliderCount = 1;
        this._TriggerForLocalPlayer();
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) { return; }

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) { return; }
        if (entity != this.m_LocalVehicleInsideTrigger) { return; }

        this.m_LocalVehicleColliderCount--;
        if (this.m_LocalVehicleColliderCount > 0) { return; }

        this.m_LocalVehicleColliderCount = 0;
        this.m_LocalVehicleInsideTrigger = null;
        this._ResetLocalEntryWhenOutside();
    }

    private void _TriggerForLocalPlayer()
    {
        if (this.m_LocalEntryHandled) { return; }

        this.m_LocalEntryHandled = true;
        this._PlayWhoosh();

        double serverTime = Networking.GetServerTimeInSeconds();
        if (!this._IsValidServerTime(serverTime))
        {
            this._BeginLocalFallback();
            return;
        }

        if (this._HasUnexpiredSynchronizedSchedule(serverTime)) { return; }

        double proposedStartTime = serverTime + (double)this._GetLeadTimeSeconds();
        if (!this._IsValidServerTime(proposedStartTime))
        {
            this._BeginLocalFallback();
            return;
        }

        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null) { return; }
        if (!localPlayer.IsValid()) { return; }

        this.SendMethodNetworked(
            nameof(this.On_ApplyElevatorSchedule),
            SyncTarget.All,
            new DataToken(proposedStartTime),
            new DataToken(localPlayer.playerId)
        );
    }

    [NetworkedMethod]
    public void On_ApplyElevatorSchedule(double scheduledStartTime, int schedulePlayerId)
    {
        if (!this._IsValidServerTime(scheduledStartTime)) { return; }
        if (schedulePlayerId < 0) { return; }
        if (this.m_LocalFallbackActive) { return; }

        double serverTime = Networking.GetServerTimeInSeconds();
        if (this.m_HasSynchronizedSchedule && this._IsValidServerTime(serverTime))
        {
            this._RecordCurrentScheduleStarted(serverTime);
        }

        if (this._IsCurrentSchedule(scheduledStartTime, schedulePlayerId)) { return; }

        double triggerTime = scheduledStartTime - (double)this._GetLeadTimeSeconds();
        if (scheduledStartTime <= this.m_LastStartedScheduleTime) { return; }
        if (triggerTime < this.m_LastStartedScheduleTime) { return; }

        if (!this.m_HasSynchronizedSchedule)
        {
            this._ApplySynchronizedSchedule(scheduledStartTime, schedulePlayerId);
            return;
        }

        if (triggerTime < this.m_ScheduledStartTime)
        {
            bool currentSchedulePending = !this._IsValidServerTime(serverTime)
                || serverTime < this.m_ScheduledStartTime;
            if (currentSchedulePending
                && this._IncomingScheduleWins(scheduledStartTime, schedulePlayerId))
            {
                this._ApplySynchronizedSchedule(scheduledStartTime, schedulePlayerId);
                return;
            }

            this._ReplyWithCurrentSchedule();
            return;
        }

        this._ApplySynchronizedSchedule(scheduledStartTime, schedulePlayerId);
    }

    private void _ApplySynchronizedSchedule(double scheduledStartTime, int schedulePlayerId)
    {
        this.m_HasSynchronizedSchedule = true;
        this.m_ScheduledStartTime = scheduledStartTime;
        this.m_SchedulePlayerId = schedulePlayerId;
        this._PrepareMovement();
    }

    private void _BeginLocalFallback()
    {
        this.m_HasSynchronizedSchedule = false;
        this.m_SchedulePlayerId = -1;
        this.m_LocalFallbackActive = true;
        this.m_LocalFallbackStartTime = Time.time + this._GetLeadTimeSeconds();
        this._PrepareMovement();
    }

    private void _PrepareMovement()
    {
        if (this.elevatorBody == null) { return; }
        if (this.startPosition == null) { return; }
        if (this.endPosition == null) { return; }

        this.m_MovementStartPosition = this.startPosition.position;
        this.m_MovementEndPosition = this.endPosition.position;
        this.m_ResetPositionPending = true;
        this.m_MovementFinished = false;

        this._OnElevatorMovementStarted();
    }

    private void _OnElevatorMovementStarted()
    {
        if (this.beatAudio == null) { return; }
        if (!this.beatAudio.gameObject.activeInHierarchy) { return; }
        if (!this.beatAudio.enabled) { return; }
        if (this.beatAudio.isPlaying) { return; }

        this.beatAudio.Play();
    }

    private bool _UpdateElevatorPosition(double elapsedSeconds)
    {
        if (elapsedSeconds < 0d) { return false; }
        if (this.m_MovementFinished) { return true; }
        if (this.elevatorBody == null) { return false; }
        if (this.startPosition == null) { return false; }
        if (this.endPosition == null) { return false; }

        float duration = this._GetMovementDurationSeconds();
        if (duration <= 0f)
        {
            this.elevatorBody.MovePosition(this.m_MovementEndPosition);
            this.m_MovementFinished = true;
            return true;
        }

        float interpolation = Mathf.Clamp01((float)(elapsedSeconds / (double)duration));
        float smoothedInterpolation = Mathf.SmoothStep(0f, 1f, interpolation);
        Vector3 targetPosition = Vector3.Lerp(
            this.m_MovementStartPosition,
            this.m_MovementEndPosition,
            smoothedInterpolation
        );
        this.elevatorBody.MovePosition(targetPosition);

        if (interpolation < 1f) { return false; }

        this.m_MovementFinished = true;
        return true;
    }

    private bool _HasUnexpiredSynchronizedSchedule(double serverTime)
    {
        if (!this.m_HasSynchronizedSchedule) { return false; }

        return serverTime < this.m_ScheduledStartTime;
    }

    private void _RecordCurrentScheduleStarted(double serverTime)
    {
        if (serverTime < this.m_ScheduledStartTime) { return; }
        if (this.m_ScheduledStartTime <= this.m_LastStartedScheduleTime) { return; }

        this.m_LastStartedScheduleTime = this.m_ScheduledStartTime;
    }

    private bool _IsCurrentSchedule(double scheduledStartTime, int schedulePlayerId)
    {
        if (!this.m_HasSynchronizedSchedule) { return false; }
        if (this.m_SchedulePlayerId != schedulePlayerId) { return false; }

        double difference = this.m_ScheduledStartTime - scheduledStartTime;
        return difference > -0.0001d && difference < 0.0001d;
    }

    private bool _IncomingScheduleWins(double scheduledStartTime, int schedulePlayerId)
    {
        if (scheduledStartTime < this.m_ScheduledStartTime) { return true; }
        if (scheduledStartTime > this.m_ScheduledStartTime) { return false; }

        return schedulePlayerId < this.m_SchedulePlayerId;
    }

    private void _ReplyWithCurrentSchedule()
    {
        VRCPlayerApi requester = this.eventSenderPlayer;
        if (requester == null) { return; }
        if (!requester.IsValid()) { return; }
        if (requester.isLocal) { return; }

        VRCPlayerApi scheduler = VRCPlayerApi.GetPlayerById(this.m_SchedulePlayerId);
        if (scheduler != null && scheduler.IsValid() && !scheduler.isLocal) { return; }

        this.SendMethodNetworked(
            nameof(this.On_ApplyElevatorSchedule),
            requester,
            new DataToken(this.m_ScheduledStartTime),
            new DataToken(this.m_SchedulePlayerId)
        );
    }

    private void _PlayWhoosh()
    {
        if (this.whooshAudio == null) { return; }
        if (!this.whooshAudio.gameObject.activeInHierarchy) { return; }

        this.whooshAudio.Play();
    }

    private void _ResetLocalEntryWhenOutside()
    {
        if (this.m_LocalPlayerInsideTrigger) { return; }
        if (this.m_LocalVehicleColliderCount > 0) { return; }

        this.m_LocalEntryHandled = false;
    }

    private float _GetMovementDurationSeconds()
    {
        if (float.IsNaN(this.movementDurationSeconds)) { return 0f; }
        if (float.IsInfinity(this.movementDurationSeconds)) { return 0f; }

        return Mathf.Max(this.movementDurationSeconds, 0f);
    }

    private float _GetLeadTimeSeconds()
    {
        if (float.IsNaN(this.leadTimeSeconds)) { return 0f; }
        if (float.IsInfinity(this.leadTimeSeconds)) { return 0f; }

        return Mathf.Max(this.leadTimeSeconds, 0f);
    }

    private bool _IsValidServerTime(double serverTime)
    {
        if (double.IsNaN(serverTime)) { return false; }
        if (double.IsInfinity(serverTime)) { return false; }

        return serverTime > 0d;
    }
}
