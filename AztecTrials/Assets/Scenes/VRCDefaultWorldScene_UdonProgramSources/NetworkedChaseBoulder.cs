using SaccFlightAndVehicles;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class NetworkedChaseBoulder : UdonSharpBehaviour
{
    [Header("Physics")]
    [Tooltip("Optional kinematic Rigidbody used for collision-friendly movement.")]
    public Rigidbody m_Rigidbody;

    [Tooltip("Renderers enabled only while the boulder is chasing.")]
    public Renderer[] m_Renderers;

    [Tooltip("Colliders enabled only while the boulder is chasing.")]
    public Collider[] m_Colliders;

    [Header("Baked Path")]
    [Tooltip("Open, ordered waypoint path baked by BoulderPathFromSurfaceTool.")]
    public Transform[] m_PathPoints;

    [Min(1)]
    [Tooltip("Maximum neighboring path segments examined per simulation tick.")]
    public int m_MaxTargetProjectionStepsPerTick = 8;

    [Min(0f)]
    [Tooltip("A target movement larger than this performs one full path recovery search. Set to zero to disable recovery searches.")]
    public float m_TargetTeleportRecoveryDistance = 20f;

    [Header("Chase")]
    [Min(0.01f)]
    [Tooltip("Maximum chase speed. A faster vehicle can outrun the boulder.")]
    public float m_ChaseSpeed = 30f;

    [Min(0.01f)]
    [Tooltip("The boulder never moves slower than this speed while active.")]
    public float m_MinimumSpeed = 8f;

    [Min(0f)]
    [Tooltip("Distance along the baked path that the boulder attempts to remain behind the target.")]
    public float m_DesiredFollowDistance = 12f;

    [Min(0f)]
    [Tooltip("Speed correction applied per world unit of follow-distance error.")]
    public float m_DistanceCorrectionGain = 0.75f;

    [Min(0f)]
    [Tooltip("World units per second squared. Set to zero for immediate speed changes.")]
    public float m_Acceleration = 20f;

    [Min(0f)]
    [Tooltip("How quickly visual rolling direction follows bends and reversals in the path.")]
    public float m_TurnResponsiveness = 8f;

    [Min(0.01f)]
    public float m_MaximumLifetime = 30f;

    [Tooltip("End the chase if its assigned scene vehicle is destroyed.")]
    public bool m_DespawnIfTargetMissing = true;

    [Header("Deterministic Simulation")]
    [Min(1f)]
    [Tooltip("Server-time simulation ticks per second.")]
    public float m_SimulationTicksPerSecond = 30f;

    [Min(1)]
    [Tooltip("Limits catch-up work after a delayed frame. Normal frames process one or two steps.")]
    public int m_MaxCatchUpStepsPerFrame = 512;

    [Tooltip("Visual spin around the movement axis in degrees per second.")]
    public float m_SpinDegreesPerSecond = 360f;

    [Header("Audio")]
    public AudioSource m_RollingAudio;

    [Header("Collision Damage")]
    public bool m_DamageSaccEntities = true;

    [Tooltip("Only the assigned chase target can be damaged by this boulder.")]
    public bool m_DamageOnlyAssignedTarget = true;

    [Tooltip("Only the collided vehicle owner sends its Sacc damage event.")]
    public bool m_OnlyVehicleOwnerSendsDamage = true;

    public float m_CollisionDamage = 999999f;
    public byte m_CollisionWeaponType = 1;
    public float m_CollisionDamageCooldownSeconds = 0.25f;

    [Tooltip("End this chase after colliding with its assigned target.")]
    public bool m_DespawnOnTargetCollision;

    private bool _cached;
    private bool _scheduled;
    private bool _chasing;
    private long _spawnIdentifier = -1;
    private double _spawnServerTime;
    private SaccEntity _targetEntity;
    private Rigidbody _targetRigidbody;
    private Quaternion _spawnRotation;
    private Vector3 _simulatedPosition;
    private Quaternion _simulatedRotation;
    private Vector3 _movementDirection;
    private float _currentSpeed;
    private long _lastSimulationStep;
    private float _tickDuration;
    private float _cachedMinimumSpeed;
    private float _cachedMaximumSpeed;
    private float _cachedLifetime;
    private double _lastCollisionDamageTime = -999d;
    private SaccEntity _lastDamagedEntity;

    private bool _pathCacheAttempted;
    private bool _pathValid;
    private Vector3[] _pathPositions;
    private Vector3[] _pathSegmentDirections;
    private float[] _pathSegmentLengths;
    private float[] _pathCumulativeDistances;
    private int _pathPointCount;
    private int _pathSegmentCount;
    private float _pathLength;

    private bool _hasTargetProjection;
    private int _targetSegmentIndex;
    private float _targetPathDistance;
    private float _targetTravelDirection = 1f;
    private Vector3 _lastTargetPosition;
    private int _boulderSegmentIndex;
    private float _boulderPathDistance;

    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private bool _hasOriginalTransform;

    private void Start()
    {
        if (!_hasOriginalTransform)
        {
            _originalPosition = transform.position;
            _originalRotation = transform.rotation;
            _hasOriginalTransform = true;
        }
    }

    private void Awake()
    {
        EnsureCached();
        EnsurePathCached();
        SetPresentationActive(false);
    }

    private void OnEnable()
    {
        EnsureCached();
        if (!_scheduled && !_chasing)
        {
            SetPresentationActive(false);
        }
    }

    private void OnDisable()
    {
        DeactivateChase();
    }

    private void EnsureCached()
    {
        if (_cached) { return; }

        if (m_Rigidbody == null)
        {
            m_Rigidbody = GetComponent<Rigidbody>();
        }

        if (m_Rigidbody != null)
        {
            m_Rigidbody.isKinematic = true;
            m_Rigidbody.interpolation = RigidbodyInterpolation.None;
        }

        if (m_Renderers == null || m_Renderers.Length == 0)
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
        }

        if (m_Colliders == null || m_Colliders.Length == 0)
        {
            m_Colliders = GetComponentsInChildren<Collider>(true);
        }

        _cached = true;
    }

    private bool EnsurePathCached()
    {
        if (_pathCacheAttempted) { return _pathValid; }
        _pathCacheAttempted = true;

        _pathPointCount = m_PathPoints != null ? m_PathPoints.Length : 0;
        if (_pathPointCount < 2)
        {
            Debug.LogWarning(
                "[NetworkedChaseBoulder] At least two baked path points are required on " +
                gameObject.name + ".");
            return false;
        }

        _pathSegmentCount = _pathPointCount - 1;
        _pathPositions = new Vector3[_pathPointCount];
        _pathSegmentDirections = new Vector3[_pathSegmentCount];
        _pathSegmentLengths = new float[_pathSegmentCount];
        _pathCumulativeDistances = new float[_pathPointCount];

        for (int pointIndex = 0; pointIndex < _pathPointCount; pointIndex++)
        {
            Transform point = m_PathPoints[pointIndex];
            if (point == null || !IsFinite(point.position))
            {
                Debug.LogWarning(
                    "[NetworkedChaseBoulder] The baked path contains a missing or invalid point on " +
                    gameObject.name + ".");
                return false;
            }

            _pathPositions[pointIndex] = point.position;
        }

        float cumulativeDistance = 0f;
        _pathCumulativeDistances[0] = 0f;
        Vector3 lastValidDirection = Vector3.forward;
        for (int segmentIndex = 0; segmentIndex < _pathSegmentCount; segmentIndex++)
        {
            Vector3 segment =
                _pathPositions[segmentIndex + 1] - _pathPositions[segmentIndex];
            float segmentLength = segment.magnitude;
            if (!IsFinite(segmentLength)) { segmentLength = 0f; }

            _pathSegmentLengths[segmentIndex] = segmentLength;
            if (segmentLength > 0.000001f)
            {
                lastValidDirection = segment / segmentLength;
            }
            _pathSegmentDirections[segmentIndex] = lastValidDirection;

            cumulativeDistance += segmentLength;
            _pathCumulativeDistances[segmentIndex + 1] = cumulativeDistance;
        }

        if (!IsFinite(cumulativeDistance) || cumulativeDistance <= 0.0001f)
        {
            Debug.LogWarning(
                "[NetworkedChaseBoulder] The baked path has no measurable length on " +
                gameObject.name + ".");
            return false;
        }

        for (int segmentIndex = _pathSegmentCount - 2; segmentIndex >= 0; segmentIndex--)
        {
            if (_pathSegmentLengths[segmentIndex] <= 0.000001f)
            {
                _pathSegmentDirections[segmentIndex] =
                    _pathSegmentDirections[segmentIndex + 1];
            }
        }

        _pathLength = cumulativeDistance;
        _pathValid = true;
        return true;
    }

    public void ScheduleChase(
        double spawnServerTime,
        long spawnIdentifier,
        SaccEntity targetEntity,
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        if (!IsFinite(spawnServerTime)) { return; }
        if (spawnIdentifier < 0L) { return; }
        if (targetEntity == null) { return; }

        if ((_scheduled || _chasing) && _spawnIdentifier == spawnIdentifier)
        {
            return;
        }

        EnsureCached();
        if (!EnsurePathCached()) { return; }

        _spawnServerTime = spawnServerTime;
        _spawnIdentifier = spawnIdentifier;
        _targetEntity = targetEntity;
        _targetRigidbody = targetEntity.VehicleRigidbody;
        if (_targetRigidbody == null)
        {
            _targetRigidbody = targetEntity.GetComponent<Rigidbody>();
        }

        _spawnRotation = Quaternion.Normalize(spawnRotation);
        _simulatedRotation = _spawnRotation;

        _boulderSegmentIndex = FindNearestSegmentFull(spawnPosition);
        _boulderPathDistance = GetProjectedPathDistance(
            spawnPosition,
            _boulderSegmentIndex);
        _boulderSegmentIndex = FindSegmentForPathDistance(
            _boulderPathDistance,
            _boulderSegmentIndex);
        _simulatedPosition = GetPathPosition(
            _boulderPathDistance,
            _boulderSegmentIndex);
        _movementDirection = _pathSegmentDirections[_boulderSegmentIndex];
        if (_movementDirection.sqrMagnitude < 0.000001f)
        {
            _movementDirection = spawnRotation * Vector3.forward;
        }
        _movementDirection.Normalize();

        Vector3 targetPosition = targetEntity.transform.position;
        _targetSegmentIndex = FindNearestSegmentFull(targetPosition);
        _targetPathDistance = GetProjectedPathDistance(
            targetPosition,
            _targetSegmentIndex);
        _targetTravelDirection = 1f;
        _lastTargetPosition = targetPosition;
        _hasTargetProjection = true;

        float ticksPerSecond = SanitizePositive(m_SimulationTicksPerSecond, 30f);
        _tickDuration = 1f / ticksPerSecond;
        _cachedMinimumSpeed = SanitizePositive(m_MinimumSpeed, 0.01f);
        _cachedMaximumSpeed = SanitizePositive(m_ChaseSpeed, _cachedMinimumSpeed);
        if (_cachedMaximumSpeed < _cachedMinimumSpeed)
        {
            _cachedMaximumSpeed = _cachedMinimumSpeed;
        }
        _cachedLifetime = SanitizePositive(m_MaximumLifetime, 0.01f);
        _currentSpeed = _cachedMinimumSpeed;
        _lastSimulationStep = 0L;
        _lastCollisionDamageTime = -999d;
        _lastDamagedEntity = null;
        _scheduled = true;
        _chasing = false;

        SetPresentationActive(false);
        ApplyPose(_simulatedPosition, _spawnRotation, true);
    }

    public void CancelScheduledChase(long spawnIdentifier)
    {
        if (_spawnIdentifier != spawnIdentifier) { return; }
        DeactivateChase();
    }

    public bool IsBusy()
    {
        return _scheduled || _chasing;
    }

    public long GetSpawnIdentifier()
    {
        return _spawnIdentifier;
    }

    public SaccEntity GetTargetEntity()
    {
        return _targetEntity;
    }

    public bool IsPathReady()
    {
        return EnsurePathCached();
    }

    private void FixedUpdate()
    {
        if (!_scheduled && !_chasing) { return; }

        double serverTime = Networking.GetServerTimeInSeconds();
        if (!IsFinite(serverTime)) { return; }
        if (serverTime < _spawnServerTime) { return; }
        if (m_DespawnIfTargetMissing && _targetEntity == null)
        {
            DeactivateChase();
            return;
        }

        double elapsed = serverTime - _spawnServerTime;
        if (!IsFinite(elapsed) || elapsed < 0d) { return; }

        if (!_chasing)
        {
            BeginChase();
        }

        if ((float)elapsed >= _cachedLifetime)
        {
            DeactivateChase();
            return;
        }

        double simulationStepExact = elapsed / (double)_tickDuration;
        if (!IsFinite(simulationStepExact)) { return; }
        long targetStep = FloorToLong(simulationStepExact);
        if (targetStep <= _lastSimulationStep) { return; }

        UpdateTargetProjection(_targetEntity.transform.position);

        int maxSteps = m_MaxCatchUpStepsPerFrame;
        if (maxSteps < 1) { maxSteps = 1; }

        int processedSteps = 0;
        while (_lastSimulationStep < targetStep && processedSteps < maxSteps)
        {
            SimulateStep(_tickDuration);
            _lastSimulationStep++;
            processedSteps++;
        }

        if (!_chasing) { return; }
        ApplyPose(_simulatedPosition, _simulatedRotation, false);
    }

    private void BeginChase()
    {
        _scheduled = false;
        _chasing = true;
        _simulatedRotation = Quaternion.Normalize(_spawnRotation);
        SetPresentationActive(true);
        ApplyPose(_simulatedPosition, _simulatedRotation, true);

        if (m_RollingAudio != null &&
            m_RollingAudio.enabled &&
            m_RollingAudio.gameObject.activeInHierarchy &&
            !m_RollingAudio.isPlaying)
        {
            m_RollingAudio.Play();
        }
    }

    private void SimulateStep(float deltaTime)
    {
        if (_targetEntity == null || !_pathValid) { return; }

        Vector3 vehicleVelocity = Vector3.zero;
        if (_targetRigidbody != null)
        {
            vehicleVelocity = _targetRigidbody.velocity;
        }

       float vehicleSpeed = vehicleVelocity.magnitude;

        float pathDistanceError = _targetPathDistance - _boulderPathDistance;

        float correctionGain = SanitizeNonNegative(m_DistanceCorrectionGain, 0f);

        // Positive follow distance means "allow the player this much head start"
        // Once the player is inside that distance, the boulder keeps trying to overtake.
        float catchUpError = pathDistanceError - Mathf.Max(0f, m_DesiredFollowDistance);

        float targetSpeed = vehicleSpeed;

        if (catchUpError > 0f)
        {
            targetSpeed += catchUpError * correctionGain;
        }

        targetSpeed = Mathf.Clamp(
        targetSpeed,
        _cachedMinimumSpeed,
        _cachedMaximumSpeed);

        float acceleration = SanitizeNonNegative(m_Acceleration, 0f);
        if (acceleration > 0f)
        {
            _currentSpeed = Mathf.MoveTowards(
                _currentSpeed,
                targetSpeed,
                acceleration * deltaTime);
        }
        else
        {
            _currentSpeed = targetSpeed;
        }

        _currentSpeed = Mathf.Clamp(
            _currentSpeed,
            _cachedMinimumSpeed,
            _cachedMaximumSpeed);

        float travelDirection = 1f;

        float previousPathDistance = _boulderPathDistance;
        float newPathDistance = _boulderPathDistance + travelDirection * _currentSpeed * deltaTime;

        if (newPathDistance <= 0f || newPathDistance >= _pathLength)
        {
            DeactivateChase();
            return;
        }

        _boulderPathDistance = newPathDistance;
        _boulderSegmentIndex = FindSegmentForPathDistance(
            _boulderPathDistance,
            _boulderSegmentIndex);
        _simulatedPosition = GetPathPosition(
            _boulderPathDistance,
            _boulderSegmentIndex);

        float actualTravel = _boulderPathDistance - previousPathDistance;
        if (Mathf.Abs(actualTravel) > 0.000001f)
        {
            Vector3 pathDirection = _pathSegmentDirections[_boulderSegmentIndex];
            if (actualTravel < 0f) { pathDirection = -pathDirection; }

            float turnResponse = SanitizeNonNegative(m_TurnResponsiveness, 0f);
            float turnBlend = Mathf.Clamp01(turnResponse * deltaTime);
            Vector3 blendedDirection = Vector3.Lerp(
                _movementDirection,
                pathDirection,
                turnBlend);
            if (blendedDirection.sqrMagnitude > 0.000001f)
            {
                _movementDirection = blendedDirection.normalized;
            }
            else
            {
                _movementDirection = pathDirection;
            }
        }

        float spinSpeed = IsFinite(m_SpinDegreesPerSecond)
            ? m_SpinDegreesPerSecond
            : 0f;
        Vector3 spinAxis = Vector3.Cross(Vector3.up, _movementDirection);
        if (spinAxis.sqrMagnitude < 0.000001f)
        {
            spinAxis = Vector3.right;
        }
        spinAxis.Normalize();

        _simulatedRotation = Quaternion.Normalize(
            Quaternion.AngleAxis(
                spinSpeed * deltaTime, spinAxis
            ) * _simulatedRotation
        );
    }

    private void UpdateTargetProjection(Vector3 targetPosition)
    {
        if (!_hasTargetProjection)
        {
            _targetSegmentIndex = FindNearestSegmentFull(targetPosition);
        }
        else
        {
            float recoveryDistance = SanitizeNonNegative(
                m_TargetTeleportRecoveryDistance,
                0f);
            bool targetTeleported =
                recoveryDistance > 0f &&
                (targetPosition - _lastTargetPosition).sqrMagnitude >
                recoveryDistance * recoveryDistance;

            if (targetTeleported)
            {
                _targetSegmentIndex = FindNearestSegmentFull(targetPosition);
            }
            else
            {
                _targetSegmentIndex = FindNearestSegmentIncremental(
                    targetPosition,
                    _targetSegmentIndex);
            }
        }

        float previousTargetPathDistance = _targetPathDistance;
        _targetPathDistance = GetProjectedPathDistance(
            targetPosition,
            _targetSegmentIndex);
        float targetTravel = _targetPathDistance - previousTargetPathDistance;
        if (targetTravel > 0.0001f)
        {
            _targetTravelDirection = 1f;
        }
        else if (targetTravel < -0.0001f)
        {
            _targetTravelDirection = -1f;
        }

        _lastTargetPosition = targetPosition;
        _hasTargetProjection = true;
    }

    private int FindNearestSegmentIncremental(Vector3 targetPosition, int startSegment)
    {
        int segmentIndex = Mathf.Clamp(startSegment, 0, _pathSegmentCount - 1);
        int maxSteps = m_MaxTargetProjectionStepsPerTick;
        if (maxSteps < 1) { maxSteps = 1; }

        for (int step = 0; step < maxSteps; step++)
        {
            int bestSegment = segmentIndex;
            float bestDistance = GetSegmentDistanceSquared(targetPosition, segmentIndex);

            if (segmentIndex > 0)
            {
                float previousDistance = GetSegmentDistanceSquared(
                    targetPosition,
                    segmentIndex - 1);
                if (previousDistance + 0.000001f < bestDistance)
                {
                    bestDistance = previousDistance;
                    bestSegment = segmentIndex - 1;
                }
            }

            if (segmentIndex < _pathSegmentCount - 1)
            {
                float nextDistance = GetSegmentDistanceSquared(
                    targetPosition,
                    segmentIndex + 1);
                if (nextDistance + 0.000001f < bestDistance)
                {
                    bestSegment = segmentIndex + 1;
                }
            }

            if (bestSegment == segmentIndex) { break; }
            segmentIndex = bestSegment;
        }

        return segmentIndex;
    }

    private int FindNearestSegmentFull(Vector3 targetPosition)
    {
        int bestSegment = 0;
        float bestDistance = GetSegmentDistanceSquared(targetPosition, 0);
        for (int segmentIndex = 1; segmentIndex < _pathSegmentCount; segmentIndex++)
        {
            float distance = GetSegmentDistanceSquared(targetPosition, segmentIndex);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSegment = segmentIndex;
            }
        }

        return bestSegment;
    }

    private float GetSegmentDistanceSquared(Vector3 targetPosition, int segmentIndex)
    {
        Vector3 segmentStart = _pathPositions[segmentIndex];
        Vector3 segment =
            _pathPositions[segmentIndex + 1] - segmentStart;
        float segmentLengthSquared = segment.sqrMagnitude;
        float interpolation = segmentLengthSquared > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(targetPosition - segmentStart, segment) /
                            segmentLengthSquared)
            : 0f;
        Vector3 projectedPosition = segmentStart + segment * interpolation;
        return (targetPosition - projectedPosition).sqrMagnitude;
    }

    private float GetProjectedPathDistance(Vector3 targetPosition, int segmentIndex)
    {
        Vector3 segmentStart = _pathPositions[segmentIndex];
        Vector3 segment =
            _pathPositions[segmentIndex + 1] - segmentStart;
        float segmentLengthSquared = segment.sqrMagnitude;
        float interpolation = segmentLengthSquared > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(targetPosition - segmentStart, segment) /
                            segmentLengthSquared)
            : 0f;
        return _pathCumulativeDistances[segmentIndex] +
               _pathSegmentLengths[segmentIndex] * interpolation;
    }

    private int FindSegmentForPathDistance(float pathDistance, int startSegment)
    {
        int segmentIndex = Mathf.Clamp(startSegment, 0, _pathSegmentCount - 1);
        while (segmentIndex > 0 &&
               pathDistance < _pathCumulativeDistances[segmentIndex])
        {
            segmentIndex--;
        }

        while (segmentIndex < _pathSegmentCount - 1 &&
               pathDistance >= _pathCumulativeDistances[segmentIndex + 1])
        {
            segmentIndex++;
        }

        return segmentIndex;
    }

    private Vector3 GetPathPosition(float pathDistance, int segmentIndex)
    {
        float segmentLength = _pathSegmentLengths[segmentIndex];
        float distanceInSegment =
            pathDistance - _pathCumulativeDistances[segmentIndex];
        float interpolation = segmentLength > 0.000001f
            ? Mathf.Clamp01(distanceInSegment / segmentLength)
            : 0f;
        return Vector3.Lerp(
            _pathPositions[segmentIndex],
            _pathPositions[segmentIndex + 1],
            interpolation);
    }

    private void ApplyPose(Vector3 worldPosition, Quaternion worldRotation, bool teleport)
    {
        if (m_Rigidbody != null)
        {
            if (teleport)
            {
                RigidbodyInterpolation previousInterpolation = m_Rigidbody.interpolation;
                m_Rigidbody.interpolation = RigidbodyInterpolation.None;
                m_Rigidbody.position = worldPosition;
                m_Rigidbody.rotation = worldRotation;
                transform.position = worldPosition;
                transform.rotation = worldRotation;
                Physics.SyncTransforms();
                m_Rigidbody.interpolation = previousInterpolation;
                return;
            }

            m_Rigidbody.MovePosition(worldPosition);
            m_Rigidbody.MoveRotation(worldRotation);
            return;
        }

        transform.position = worldPosition;
        transform.rotation = worldRotation;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_chasing || collision == null) { return; }

        SaccEntity collidedEntity = collision.collider != null
            ? collision.collider.GetComponentInParent<SaccEntity>()
            : null;
        if (collidedEntity == null) { return; }

        TryDamageSaccEntity(collidedEntity);

        if (m_DespawnOnTargetCollision && collidedEntity == _targetEntity)
        {
            DeactivateChase();
        }
    }

    private void TryDamageSaccEntity(SaccEntity entity)
    {
        if (!m_DamageSaccEntities || entity == null) { return; }
        if (m_DamageOnlyAssignedTarget && entity != _targetEntity) { return; }
        if (m_OnlyVehicleOwnerSendsDamage && !Networking.IsOwner(entity.gameObject)) { return; }

        float cooldown = SanitizeNonNegative(m_CollisionDamageCooldownSeconds, 0.25f);
        double now = Networking.GetServerTimeInSeconds();
        if (!IsFinite(now)) { return; }
        if (entity == _lastDamagedEntity && now - _lastCollisionDamageTime < (double)cooldown)
        {
            return;
        }

        float damage = IsFinite(m_CollisionDamage) ? m_CollisionDamage : 999999f;
        if (damage <= 0f) { return; }

        entity.SendCustomNetworkEvent(
            VRC.Udon.Common.Interfaces.NetworkEventTarget.All,
            nameof(SaccEntity.SendDamageEvent),
            damage,
            m_CollisionWeaponType);

        _lastDamagedEntity = entity;
        _lastCollisionDamageTime = now;
    }

    public void DeactivateChase()
    {
        _scheduled = false;
        _chasing = false;
        _targetEntity = null;
        _targetRigidbody = null;
        _hasTargetProjection = false;
        SetPresentationActive(false);

        if (m_RollingAudio != null && m_RollingAudio.isPlaying)
        {
            m_RollingAudio.Stop();
        }

        if (_hasOriginalTransform)
        {
            ApplyPose(_originalPosition, _originalRotation, true);
        }
    }

    private void SetPresentationActive(bool active)
    {
        if (m_Renderers != null)
        {
            for (int index = 0; index < m_Renderers.Length; index++)
            {
                Renderer renderer = m_Renderers[index];
                if (renderer != null)
                {
                    renderer.enabled = active;
                }
            }
        }

        if (m_Colliders != null)
        {
            for (int index = 0; index < m_Colliders.Length; index++)
            {
                Collider collider = m_Colliders[index];
                if (collider != null)
                {
                    collider.enabled = active;
                }
            }
        }
    }

    private static float SanitizePositive(float value, float fallback)
    {
        if (!IsFinite(value) || value <= 0f) { return fallback; }
        return value;
    }

    private static float SanitizeNonNegative(float value, float fallback)
    {
        if (!IsFinite(value) || value < 0f) { return fallback; }
        return value;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
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