using Miner28.UdonUtils.Network;
using SaccFlightAndVehicles;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class NetworkedBoulderSpawner : NetworkInterface
{
    [Header("Trigger")]
    [Tooltip("Trigger collider that detects the locally piloted Sacc vehicle.")]
    public Collider m_TriggerCollider;

    [Header("Scheduling")]
    [Min(0f)]
    [Tooltip("Network-time delay between the pilot entering and the boulder spawning.")]
    public float m_SpawnDelay = 3f;

    [Min(0f)]
    [Tooltip("Seconds from trigger activation before another activation is allowed.")]
    public float m_Cooldown = 10f;

    [Header("Spawn")]
    [Tooltip("World-space spawn pose. Uses this component's transform when unassigned.")]
    public Transform m_SpawnPoint;

    [Tooltip("Preallocated boulder pool. Every client must have the same ordering.")]
    public NetworkedChaseBoulder[] m_BoulderPool;

    [Tooltip("Scene vehicle registry. Every client must have the same ordering.")]
    public SaccEntity[] m_TargetVehicles;

    private bool _hasPendingSchedule;
    private double _scheduledSpawnTime;
    private long _scheduledSpawnIdentifier = -1L;
    private int _scheduledPlayerId = -1;
    private int _scheduledTargetIndex = -1;
    private int _scheduledPoolSlot = -1;
    private Vector3 _scheduledSpawnPosition;
    private Quaternion _scheduledSpawnRotation;
    private double _scheduledCooldownEndTime;

    private double _lastStartedSpawnTime;
    private long _lastSpawnedIdentifier = -1L;
    private double _lastCooldownEndTime;
    private bool _hasStartedSpawn;

    private SaccEntity _localPilotVehicleInside;
    private int _localPilotColliderCount;
    private bool _localEntryHandled;

    private void Start()
    {
        if (m_TriggerCollider == null)
        {
            m_TriggerCollider = GetComponent<Collider>();
        }

        if (m_TriggerCollider == null)
        {
            Debug.LogWarning(
                "[NetworkedBoulderSpawner] No trigger collider is assigned on " + gameObject.name + ".");
        }
        else if (!m_TriggerCollider.isTrigger)
        {
            Debug.LogWarning(
                "[NetworkedBoulderSpawner] The assigned collider is not a trigger on " + gameObject.name + ".");
        }

        if (m_BoulderPool == null || m_BoulderPool.Length == 0)
        {
            Debug.LogWarning(
                "[NetworkedBoulderSpawner] The boulder pool is empty on " + gameObject.name + ".");
        }

        if (m_TargetVehicles == null || m_TargetVehicles.Length == 0)
        {
            Debug.LogWarning(
                "[NetworkedBoulderSpawner] The target vehicle registry is empty on " + gameObject.name + ".");
        }
    }

    private void OnEnable()
    {
        _localPilotVehicleInside = null;
        _localPilotColliderCount = 0;
        _localEntryHandled = false;
    }

    private void Update()
    {
        if (!_hasPendingSchedule) { return; }

        double serverTime = Networking.GetServerTimeInSeconds();
        if (!IsValidServerTime(serverTime)) { return; }
        if (serverTime < _scheduledSpawnTime) { return; }

        SpawnPendingBoulder();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) { return; }
        if (m_TriggerCollider != null && !m_TriggerCollider.enabled) { return; }

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) { return; }

        // SaccEntity.Piloting is local-only, so exactly the pilot client proposes the spawn.
        if (!entity.Piloting) { return; }

        if (entity == _localPilotVehicleInside)
        {
            _localPilotColliderCount++;
            return;
        }

        _localPilotVehicleInside = entity;
        _localPilotColliderCount = 1;
        TryProposeSpawn(entity);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) { return; }

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null || entity != _localPilotVehicleInside) { return; }

        _localPilotColliderCount--;
        if (_localPilotColliderCount > 0) { return; }

        _localPilotColliderCount = 0;
        _localPilotVehicleInside = null;
        _localEntryHandled = false;
    }
    
    private int _nextPoolSearchIndex;

    private int FindFreePoolSlot()
    {
        int count = m_BoulderPool.Length;

        for (int offset = 0; offset < count; offset++)
        {
            int index = (_nextPoolSearchIndex + offset) % count;

            if (!m_BoulderPool[index].IsBusy())
            {
                _nextPoolSearchIndex = (index + 1) % count;
                return index;
            }
        }

        return -1;
    }

    private void TryProposeSpawn(SaccEntity targetEntity)
    {
        if (_localEntryHandled) { return; }
        _localEntryHandled = true;

        if (targetEntity == null || !targetEntity.Piloting) { return; }
        if (m_BoulderPool == null || m_BoulderPool.Length == 0) { return; }

        int targetIndex = FindTargetVehicleIndex(targetEntity);
        if (targetIndex < 0)
        {
            Debug.LogWarning(
                "[NetworkedBoulderSpawner] The entering SaccEntity is not registered on " +
                gameObject.name + ".");
            return;
        }

        double serverTime = Networking.GetServerTimeInSeconds();
        if (!IsValidServerTime(serverTime)) { return; }

        RecordStartedScheduleIfNeeded(serverTime);
        if (_hasPendingSchedule) { return; }
        if (_hasStartedSpawn && serverTime < _lastCooldownEndTime) { return; }

        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null || !localPlayer.IsValid()) { return; }

        float spawnDelay = GetSpawnDelay();
        double proposedSpawnTime = serverTime + (double)spawnDelay;
        if (!IsValidServerTime(proposedSpawnTime)) { return; }

        long spawnIdentifier = CreateSpawnIdentifier(
            proposedSpawnTime,
            localPlayer.playerId);
        int poolSlot = FindFreePoolSlot();

        if (poolSlot < 0)
        {
            Debug.LogWarning("No free boulders in pool.");
            return;
        }

        Transform spawnPoint = m_SpawnPoint != null ? m_SpawnPoint : transform;

        SendMethodNetworked(
            nameof(OnApplySpawnSchedule),
            SyncTarget.All,
            new DataToken(proposedSpawnTime),
            new DataToken(spawnIdentifier),
            new DataToken(localPlayer.playerId),
            new DataToken(targetIndex),
            new DataToken(poolSlot),
            new DataToken(spawnPoint.position),
            new DataToken(spawnPoint.rotation)
        );
    }

    [NetworkedMethod]
    public void OnApplySpawnSchedule(
        double scheduledSpawnTime,
        long spawnIdentifier,
        int schedulePlayerId,
        int targetVehicleIndex,
        int poolSlot,
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        if (!IsValidServerTime(scheduledSpawnTime)) { return; }
        if (spawnIdentifier < 0L || schedulePlayerId < 0) { return; }
        if (!IsTargetIndexValid(targetVehicleIndex)) { return; }
        if (!IsPoolSlotValid(poolSlot)) { return; }
        if (spawnIdentifier == _lastSpawnedIdentifier) { return; }

        double serverTime = Networking.GetServerTimeInSeconds();
        if (IsValidServerTime(serverTime))
        {
            RecordStartedScheduleIfNeeded(serverTime);
        }

        double triggerTime = scheduledSpawnTime - (double)GetSpawnDelay();
        if (_hasStartedSpawn && scheduledSpawnTime <= _lastStartedSpawnTime) { return; }
        if (_hasStartedSpawn && triggerTime < _lastCooldownEndTime) { return; }

        if (!_hasPendingSchedule)
        {
            ApplySchedule(
                scheduledSpawnTime,
                spawnIdentifier,
                schedulePlayerId,
                targetVehicleIndex,
                poolSlot,
                spawnPosition,
                spawnRotation);
            return;
        }

        if (IsCurrentSchedule(scheduledSpawnTime, spawnIdentifier, schedulePlayerId))
        {
            return;
        }

        bool currentSchedulePending =
            !IsValidServerTime(serverTime) || serverTime < _scheduledSpawnTime;
        if (currentSchedulePending &&
            IncomingScheduleWins(
                scheduledSpawnTime,
                spawnIdentifier,
                schedulePlayerId))
        {
            ApplySchedule(
                scheduledSpawnTime,
                spawnIdentifier,
                schedulePlayerId,
                targetVehicleIndex,
                poolSlot,
                spawnPosition,
                spawnRotation);
            return;
        }

        ReplyWithCurrentSchedule();
    }

    private void ApplySchedule(
        double scheduledSpawnTime,
        long spawnIdentifier,
        int schedulePlayerId,
        int targetVehicleIndex,
        int poolSlot,
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        _hasPendingSchedule = true;
        _scheduledSpawnTime = scheduledSpawnTime;
        _scheduledSpawnIdentifier = spawnIdentifier;
        _scheduledPlayerId = schedulePlayerId;
        _scheduledTargetIndex = targetVehicleIndex;
        _scheduledPoolSlot = poolSlot;
        _scheduledSpawnPosition = spawnPosition;
        _scheduledSpawnRotation = spawnRotation;

        double triggerTime = scheduledSpawnTime - (double)GetSpawnDelay();
        _scheduledCooldownEndTime = triggerTime + (double)GetCooldown();
    }

    private void SpawnPendingBoulder()
    {
        if (!_hasPendingSchedule) { return; }
        
        double spawnTime = _scheduledSpawnTime;
        long spawnIdentifier = _scheduledSpawnIdentifier;
        int targetIndex = _scheduledTargetIndex;
        int poolSlot = _scheduledPoolSlot;
        Vector3 spawnPosition = _scheduledSpawnPosition;
        Quaternion spawnRotation = _scheduledSpawnRotation;
        double cooldownEndTime = _scheduledCooldownEndTime;

        _hasPendingSchedule = false;
        _lastStartedSpawnTime = spawnTime;
        _lastSpawnedIdentifier = spawnIdentifier;
        _lastCooldownEndTime = cooldownEndTime;
        _hasStartedSpawn = true;

        if (!IsTargetIndexValid(targetIndex) || !IsPoolSlotValid(poolSlot)) { return; }

        NetworkedChaseBoulder boulder = m_BoulderPool[poolSlot];
        SaccEntity targetEntity = m_TargetVehicles[targetIndex];
        if (boulder == null || targetEntity == null) { return; }

        if (!boulder.gameObject.activeSelf)
        {
            boulder.gameObject.SetActive(true);
        }

        if (boulder.IsBusy() && boulder.GetSpawnIdentifier() != spawnIdentifier)
        {
            Debug.LogWarning(
                "[NetworkedBoulderSpawner] Pool slot " + poolSlot +
                " recycled its active boulder on " + gameObject.name +
                ". Increase the pool size to avoid early despawns.");
            boulder.DeactivateChase();
        }

        boulder.ScheduleChase(
            spawnTime,
            spawnIdentifier,
            targetEntity,
            spawnPosition,
            spawnRotation);
    }

    private void RecordStartedScheduleIfNeeded(double serverTime)
    {
        if (!_hasPendingSchedule) { return; }
        if (serverTime < _scheduledSpawnTime) { return; }

        SpawnPendingBoulder();
    }

    private bool IsCurrentSchedule(
        double scheduledSpawnTime,
        long spawnIdentifier,
        int schedulePlayerId)
    {
        if (!_hasPendingSchedule) { return false; }
        if (_scheduledSpawnIdentifier != spawnIdentifier) { return false; }
        if (_scheduledPlayerId != schedulePlayerId) { return false; }

        double difference = _scheduledSpawnTime - scheduledSpawnTime;
        return difference > -0.0001d && difference < 0.0001d;
    }

    private bool IncomingScheduleWins(
        double scheduledSpawnTime,
        long spawnIdentifier,
        int schedulePlayerId)
    {
        if (scheduledSpawnTime < _scheduledSpawnTime) { return true; }
        if (scheduledSpawnTime > _scheduledSpawnTime) { return false; }

        if (schedulePlayerId < _scheduledPlayerId) { return true; }
        if (schedulePlayerId > _scheduledPlayerId) { return false; }

        return spawnIdentifier < _scheduledSpawnIdentifier;
    }

    private void ReplyWithCurrentSchedule()
    {
        if (!_hasPendingSchedule) { return; }

        VRCPlayerApi requester = eventSenderPlayer;
        if (requester == null || !requester.IsValid() || requester.isLocal) { return; }

        VRCPlayerApi scheduler = VRCPlayerApi.GetPlayerById(_scheduledPlayerId);
        if (scheduler != null && scheduler.IsValid() && !scheduler.isLocal) { return; }

        SendMethodNetworked(
            nameof(OnApplySpawnSchedule),
            requester,
            new DataToken(_scheduledSpawnTime),
            new DataToken(_scheduledSpawnIdentifier),
            new DataToken(_scheduledPlayerId),
            new DataToken(_scheduledTargetIndex),
            new DataToken(_scheduledPoolSlot),
            new DataToken(_scheduledSpawnPosition),
            new DataToken(_scheduledSpawnRotation)
        );
    }

    private int FindTargetVehicleIndex(SaccEntity targetEntity)
    {
        if (targetEntity == null || m_TargetVehicles == null) { return -1; }

        for (int index = 0; index < m_TargetVehicles.Length; index++)
        {
            if (m_TargetVehicles[index] == targetEntity)
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsTargetIndexValid(int targetIndex)
    {
        return m_TargetVehicles != null &&
               targetIndex >= 0 &&
               targetIndex < m_TargetVehicles.Length &&
               m_TargetVehicles[targetIndex] != null;
    }

    private bool IsPoolSlotValid(int poolSlot)
    {
        return m_BoulderPool != null &&
               poolSlot >= 0 &&
               poolSlot < m_BoulderPool.Length &&
               m_BoulderPool[poolSlot] != null;
    }
    private static long CreateSpawnIdentifier(double spawnTime, int playerId)
    {
        long milliseconds = FloorToLong(spawnTime * 1000d);
        long timeBits = milliseconds & 0x007FFFFFFFFFFFFFL;
        return (timeBits << 8) | (long)(playerId & 0xFF);
    }
    private static long CreateSpawnIdentifierX(double spawnTime, int playerId)
    {
        long milliseconds = FloorToLong(spawnTime * 1000d);
        return milliseconds * 100000L + (long)Mathf.Clamp(playerId, 0, 99999);
    }

    private static int GetPoolSlot(long spawnIdentifier, int poolLength)
    {
        if (spawnIdentifier < 0L || poolLength <= 0) { return -1; }

        long divisor = (long)poolLength;
        long quotient = spawnIdentifier / divisor;
        long remainder = spawnIdentifier - quotient * divisor;
        return (int)remainder;
    }

    private float GetSpawnDelay()
    {
        if (!IsFinite(m_SpawnDelay)) { return 0f; }
        return Mathf.Max(0f, m_SpawnDelay);
    }

    private float GetCooldown()
    {
        if (!IsFinite(m_Cooldown)) { return 0f; }
        return Mathf.Max(0f, m_Cooldown);
    }

    private static bool IsValidServerTime(double serverTime)
    {
        return IsFinite(serverTime);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static long FloorToLong(double value)
    {
        long integer = (long)value;
        if (value < 0d && (double)integer != value)
        {
            integer--;
        }
        return integer;
    }
}