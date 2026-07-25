
using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;
using VRC.SDKBase;
using VRC.Udon;

public class LocalBoulderRunOnce : UdonSharpBehaviour
{
    [Header("Physics")]
    [Tooltip("Optional. If null, will try GetComponent<Rigidbody>(). For best physics on vehicles, add a Rigidbody and keep it kinematic.")]
    public Rigidbody m_Rigidbody;

    [Header("Path")]
    [Tooltip("Waypoints (in order). Movement runs from point 0 to the last point once, then stops.")]
    public Transform[] m_PathPoints;

    [Min(0.01f)]
    [Tooltip("Movement speed in world units/second (constant along the path).")]
    public float m_SpeedUnitsPerSecond = 2f;

    [Header("Speed Modifiers")]
    [Tooltip("If true, each waypoint's localScale.x is used as a speed multiplier. The multiplier lerps from point A to B during travel.")]
    public bool m_UsePointScaleXSpeedMultiplier = false;

    [Tooltip("Minimum allowed speed multiplier (prevents divide-by-zero if scale.x is 0).")]
    [Min(0.0001f)]
    public float m_MinSpeedMultiplier = 0.01f;

    [Min(0.01f)]
    [Tooltip("Deprecated/ignored: movement uses m_SpeedUnitsPerSecond.")]
    public float m_TravelTimeSeconds = 5f;

    [Min(0f)]
    public float m_WaitTimeSeconds = 0f;

    [Tooltip("Optional local offset (seconds). Applied to Time.time.")]
    public float m_TimeOffsetSeconds = 0f;

    [Tooltip("Deprecated/ignored: boulder movement is always constant-speed (linear).")]
    public bool m_SmoothTravel = false;

    [Header("Rotation")]
    [Tooltip("Ticks per second used for deterministic spin.")]
    [Min(1f)]
    public float m_TicksPerSecond = 30f;

    [Tooltip("If true, uses Rigidbody.MoveRotation when a rigidbody is available.")]
    public bool m_UseRigidbodyForRotation = true;

    [Header("Audio")]
    [Tooltip("Optional rolling sound. Waypoint scale.y commands: 2 = start, -2 = stop.")]
    public AudioSource m_RollingAudio;

    [Header("Collision Damage")]
    [Tooltip("If true, colliding with a SaccEntity will deal damage (sync'd) and typically explode SAV vehicles.")]
    public bool m_DamageSaccEntities = true;

    [Tooltip("If true, only the collided vehicle's owner will send the damage event (prevents multiple clients from spamming damage).")]
    public bool m_OnlyVehicleOwnerSendsDamage = true;

    [HideInInspector]
    public bool m_OnlyMasterSendsDamage = false;

    [Tooltip("Damage amount sent to SaccEntity.SendDamageEvent. Large values will instantly explode most vehicles.")]
    public float m_CollisionDamage = 999999f;

    [Tooltip("Weapon type byte passed to SendDamageEvent (SAV default weapon type is typically 1).")]
    public byte m_CollisionWeaponType = 1;

    [Tooltip("Seconds to ignore repeated collisions with the same SaccEntity.")]
    public float m_CollisionDamageCooldownSeconds = 0.25f;

    [System.NonSerialized] private bool _cached;
    [System.NonSerialized] private Vector3[] _pointLocalPositions;
    [System.NonSerialized] private Vector3[] _pointEulerPerTick;
    [System.NonSerialized] private float[] _pointSpeedMult;
    [System.NonSerialized] private AudioSource[] _pointChildAudio;
    [System.NonSerialized] private bool[] _pointChildAudioTriedResolve;
    [System.NonSerialized] private GameObject[] _pointChildToggle;
    [System.NonSerialized] private bool[] _pointChildToggleTriedResolve;
    [System.NonSerialized] private Quaternion _startLocalRotation;

    [System.NonSerialized] private double[] _segmentDurations;
    [System.NonSerialized] private double[] _segmentStartOffsets;
    [System.NonSerialized] private double[] _segmentEndOffsets;
    [System.NonSerialized] private double _totalDuration;

    // Perf: precomputed params for speed-multiplier mapping (per segment i -> i+1).
    [System.NonSerialized] private bool[] _segSpeedUse;
    [System.NonSerialized] private float[] _segSpeedMa;
    [System.NonSerialized] private float[] _segSpeedLnRatio;
    [System.NonSerialized] private float[] _segSpeedInvK;

    // Rotation perf: incremental tick accumulation.
    [System.NonSerialized] private bool _rotInit;
    [System.NonSerialized] private double _rotTicksPerSecond;
    [System.NonSerialized] private long _rotStartTick;
    [System.NonSerialized] private long _rotLastTickProcessed;
    [System.NonSerialized] private long[] _rotSegEndTicks;
    [System.NonSerialized] private int _rotSegIndex;
    [System.NonSerialized] private double _rotAccumX;
    [System.NonSerialized] private double _rotAccumY;
    [System.NonSerialized] private double _rotAccumZ;

    [System.NonSerialized] private float _lastCollisionDamageTime;
    [System.NonSerialized] private SaccEntity _lastDamagedEntity;

    [System.NonSerialized] private bool _hasPoseTick;
    [System.NonSerialized] private long _lastPoseTick;
    [System.NonSerialized] private Vector3 _cachedLocalPos;
    [System.NonSerialized] private Quaternion _cachedLocalRot;
    [System.NonSerialized] private bool _hasAppliedPoseTick;
    [System.NonSerialized] private long _lastAppliedPoseTick;

    [System.NonSerialized] private int _lastEventPointIndex = -1;

    [System.NonSerialized] private bool _runComplete;
    [System.NonSerialized] private double _runStartLocalTime;
    [System.NonSerialized] private long _lastProcessedTick;

    // When using Rigidbody, apply waypoint events after pose is applied (FixedUpdate) to avoid being 1 frame early.
    [System.NonSerialized] private bool _hasPendingWaypointEvent;
    [System.NonSerialized] private int _pendingWaypointIndex;
    [System.NonSerialized] private long _pendingWaypointPoseTick;

    private static bool IsFinite(float v)
    {
        return !(float.IsNaN(v) || float.IsInfinity(v));
    }

    private static bool IsFinite(double v)
    {
        return !(double.IsNaN(v) || double.IsInfinity(v));
    }

    private static long FloorToLong(double v)
    {
        long i = (long)v; // truncates toward zero
        if (v < 0.0 && (double)i != v) i--;
        return i;
    }

    private static float WrapDegrees(double degrees)
    {
        if (!IsFinite(degrees)) return 0f;
        long turns = FloorToLong(degrees / 360.0);
        double wrapped = degrees - (turns * 360.0);
        if (wrapped >= 180.0) wrapped -= 360.0;
        return (float)wrapped;
    }

    private static void EnsureVector3Array(ref Vector3[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new Vector3[len];
    }

    private static void EnsureFloatArray(ref float[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new float[len];
    }

    private static void EnsureAudioSourceArray(ref AudioSource[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new AudioSource[len];
    }

    private static void EnsureGameObjectArray(ref GameObject[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new GameObject[len];
    }

    private static void EnsureDoubleArray(ref double[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new double[len];
    }

    private static void EnsureLongArray(ref long[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new long[len];
    }

    private static void EnsureBoolArray(ref bool[] arr, int len)
    {
        if (arr == null || arr.Length != len) arr = new bool[len];
    }

    private static AudioSource FindDirectChildAudioSource(Transform root)
    {
        if (root == null) return null;
        int c = root.childCount;
        for (int i = 0; i < c; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null) continue;
            AudioSource src = child.GetComponent<AudioSource>();
            if (src != null) return src;
        }
        return null;
    }

    private static GameObject FindDirectChildNonAudioObject(Transform root)
    {
        if (root == null) return null;
        int c = root.childCount;
        for (int i = 0; i < c; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null) continue;

            // Requirement: "isnt an audiosource" => no AudioSource component on that child.
            if (child.GetComponent<AudioSource>() != null) continue;

            return child.gameObject;
        }
        return null;
    }

    private void EnsureCached()
    {
        if (_cached) return;

        if (m_Rigidbody == null) m_Rigidbody = GetComponent<Rigidbody>();
        if (m_Rigidbody != null)
        {
            m_Rigidbody.isKinematic = true;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        _startLocalRotation = transform.localRotation;

        int count = (m_PathPoints != null) ? m_PathPoints.Length : 0;
        EnsureVector3Array(ref _pointLocalPositions, count);
        EnsureVector3Array(ref _pointEulerPerTick, count);
        EnsureFloatArray(ref _pointSpeedMult, count);
        EnsureAudioSourceArray(ref _pointChildAudio, count);
        EnsureBoolArray(ref _pointChildAudioTriedResolve, count);
        EnsureGameObjectArray(ref _pointChildToggle, count);
        EnsureBoolArray(ref _pointChildToggleTriedResolve, count);
        EnsureDoubleArray(ref _segmentDurations, count);
        EnsureDoubleArray(ref _segmentStartOffsets, count);
        EnsureDoubleArray(ref _segmentEndOffsets, count);

        EnsureBoolArray(ref _segSpeedUse, count);
        EnsureFloatArray(ref _segSpeedMa, count);
        EnsureFloatArray(ref _segSpeedLnRatio, count);
        EnsureFloatArray(ref _segSpeedInvK, count);

        _totalDuration = 0.0;

        Transform parent = transform.parent;
        for (int i = 0; i < count; i++)
        {
            Transform p = m_PathPoints[i];
            if (p == null)
            {
                _pointLocalPositions[i] = transform.localPosition;
                _pointEulerPerTick[i] = Vector3.zero;
                _pointSpeedMult[i] = 1f;
                _pointChildAudio[i] = null;
                if (_pointChildAudioTriedResolve != null && _pointChildAudioTriedResolve.Length == count)
                {
                    _pointChildAudioTriedResolve[i] = true;
                }
                continue;
            }

            _pointChildAudio[i] = FindDirectChildAudioSource(p);
            if (_pointChildAudioTriedResolve != null && _pointChildAudioTriedResolve.Length == count)
            {
                _pointChildAudioTriedResolve[i] = (_pointChildAudio[i] != null);
            }

            _pointChildToggle[i] = FindDirectChildNonAudioObject(p);
            if (_pointChildToggleTriedResolve != null && _pointChildToggleTriedResolve.Length == count)
            {
                _pointChildToggleTriedResolve[i] = (_pointChildToggle[i] != null);
            }

            _pointLocalPositions[i] = (parent != null)
                ? parent.InverseTransformPoint(p.position)
                : p.position;

            Quaternion localRot = (parent != null)
                ? Quaternion.Inverse(parent.rotation) * p.rotation
                : p.rotation;

            Vector3 e = localRot.eulerAngles;
            e.x = WrapDegrees(e.x);
            e.y = WrapDegrees(e.y);
            e.z = WrapDegrees(e.z);
            _pointEulerPerTick[i] = e;

            float mult = 1f;
            if (m_UsePointScaleXSpeedMultiplier)
            {
                mult = p.localScale.x;
                if (!IsFinite(mult)) mult = 1f;
                mult = Mathf.Max(m_MinSpeedMultiplier, mult);
            }
            _pointSpeedMult[i] = mult;
        }

        for (int i = 0; i < count; i++)
        {
            _segSpeedUse[i] = false;
            _segSpeedMa[i] = 1f;
            _segSpeedLnRatio[i] = 0f;
            _segSpeedInvK[i] = 0f;

            if (!m_UsePointScaleXSpeedMultiplier) continue;
            if (i >= count - 1) continue;

            float ma = _pointSpeedMult[i];
            float mb = _pointSpeedMult[i + 1];
            ma = Mathf.Max(m_MinSpeedMultiplier, IsFinite(ma) ? ma : 1f);
            mb = Mathf.Max(m_MinSpeedMultiplier, IsFinite(mb) ? mb : 1f);

            float k = mb - ma;
            if (Mathf.Abs(k) < 1e-6f) continue;

            float lnRatio = Mathf.Log(mb / ma);
            if (!IsFinite(lnRatio)) continue;

            _segSpeedUse[i] = true;
            _segSpeedMa[i] = ma;
            _segSpeedLnRatio[i] = lnRatio;
            _segSpeedInvK[i] = 1f / k;
        }

        float speedRaw = m_SpeedUnitsPerSecond;
        if (!IsFinite(speedRaw)) speedRaw = 2f;
        float speed = Mathf.Max(0.01f, speedRaw);

        float waitRaw = m_WaitTimeSeconds;
        if (!IsFinite(waitRaw)) waitRaw = 0f;
        double wait = (double)Mathf.Max(0f, waitRaw);

        _totalDuration = 0.0;
        for (int i = 0; i < count; i++)
        {
            _segmentStartOffsets[i] = _totalDuration;

            double segDur = 0.0;
            if (i < count - 1 && m_PathPoints != null && i < m_PathPoints.Length && (i + 1) < m_PathPoints.Length)
            {
                Transform a = m_PathPoints[i];
                Transform b = m_PathPoints[i + 1];
                double travel = 0.0;
                if (a != null && b != null)
                {
                    float dist = Vector3.Distance(a.position, b.position);
                    if (IsFinite(dist) && dist > 0f)
                    {
                        if (m_UsePointScaleXSpeedMultiplier && _pointSpeedMult != null && _pointSpeedMult.Length == count)
                        {
                            float ma = _pointSpeedMult[i];
                            float mb = _pointSpeedMult[i + 1];
                            ma = Mathf.Max(m_MinSpeedMultiplier, IsFinite(ma) ? ma : 1f);
                            mb = Mathf.Max(m_MinSpeedMultiplier, IsFinite(mb) ? mb : 1f);

                            float k = mb - ma;
                            if (Mathf.Abs(k) < 1e-6f)
                            {
                                travel = (double)dist / ((double)speed * (double)ma);
                            }
                            else
                            {
                                float ln = (_segSpeedUse != null && _segSpeedUse.Length == count && _segSpeedUse[i])
                                    ? _segSpeedLnRatio[i]
                                    : Mathf.Log(mb / ma);
                                travel = (double)dist / (double)speed * (double)(ln / k);
                            }
                        }
                        else
                        {
                            travel = (double)dist / (double)speed;
                        }
                    }
                }

                segDur = wait + travel;
                if (!IsFinite(segDur) || segDur < 0.0) segDur = 0.0;
            }

            _segmentDurations[i] = segDur;
            _totalDuration += segDur;
            _segmentEndOffsets[i] = _totalDuration;
        }

        _cached = true;
        _rotInit = false;
    }

    private void Awake()
    {
        EnsureCached();
        _lastCollisionDamageTime = -999f;
        _lastDamagedEntity = null;
        _hasPoseTick = false;
        _hasAppliedPoseTick = false;
        _lastAppliedPoseTick = -9223372036854775807L;
        _lastEventPointIndex = -1;
        _runComplete = false;
        _lastProcessedTick = -9223372036854775807L;

        _hasPendingWaypointEvent = false;
        _pendingWaypointIndex = 0;
        _pendingWaypointPoseTick = 0;
    }

    private void OnEnable()
    {
        _cached = false;
        EnsureCached();
        _hasPoseTick = false;
        _hasAppliedPoseTick = false;
        _lastAppliedPoseTick = -9223372036854775807L;
        _lastEventPointIndex = -1;
        _runComplete = false;
        _lastProcessedTick = -9223372036854775807L;

        _hasPendingWaypointEvent = false;

        // Start from point 0 on enable.
        _runStartLocalTime = (double)Time.time;
        if (!IsFinite(_runStartLocalTime)) _runStartLocalTime = 0.0;
        _SnapToPoint0();
    }

    private void _SnapToPoint0()
    {
        EnsureCached();
        int count = (_pointLocalPositions != null) ? _pointLocalPositions.Length : 0;
        if (count <= 0)
        {
            _ApplyPose(transform.localPosition, _startLocalRotation, true);
            return;
        }

        Vector3 p0 = _pointLocalPositions[0];
        Quaternion r0 = _startLocalRotation;
        _ApplyPose(p0, r0, true);
        _cachedLocalPos = p0;
        _cachedLocalRot = r0;
        _hasPoseTick = true;
        _lastPoseTick = 0;
        _hasAppliedPoseTick = false;

        // We are at waypoint 0 immediately.
        _TriggerWaypointEvents(0);
    }

    private void Update()
    {
        if (_runComplete) return;
        if (!gameObject.activeInHierarchy) return;

        EnsureCached();

        int count = (_pointLocalPositions != null) ? _pointLocalPositions.Length : 0;
        if (count < 2) return;

        float ticksRaw = m_TicksPerSecond;
        if (!IsFinite(ticksRaw)) ticksRaw = 30f;
        double ticksPerSecond = (double)Mathf.Max(1f, ticksRaw);

        double now = (double)Time.time + (double)m_TimeOffsetSeconds;
        if (!IsFinite(now)) return;
        double localElapsed = now - _runStartLocalTime;
        if (!IsFinite(localElapsed)) return;
        if (localElapsed < 0.0) localElapsed = 0.0;

        double tickExact = localElapsed * ticksPerSecond;
        if (!IsFinite(tickExact)) return;
        long tickNow = FloorToLong(tickExact);
        if (tickNow == _lastProcessedTick) return;
        _lastProcessedTick = tickNow;

        _ComputeAndCachePoseForTick(tickNow, ticksPerSecond, count);
    }

    private void _ComputeAndCachePoseForTick(long poseTick, double ticksPerSecond, int count)
    {
        if (_runComplete) return;

        double totalDuration = _totalDuration;
        if (!IsFinite(totalDuration) || totalDuration <= 0.0)
        {
            // No movement; snap to last point immediately.
            int last = count - 1;
            _cachedLocalPos = _pointLocalPositions[last];
            _cachedLocalRot = _startLocalRotation;
            _hasPoseTick = true;
            _lastPoseTick = poseTick;
            if (m_Rigidbody == null) _ApplyPose(_cachedLocalPos, _cachedLocalRot, true);
            _runComplete = true;
            return;
        }

        double time = (double)poseTick / ticksPerSecond;
        if (!IsFinite(time) || time < 0.0) time = 0.0;

        if (time >= totalDuration)
        {
            // Reached the final point: latch done and stop updating.
            int last = count - 1;
            _cachedLocalPos = _pointLocalPositions[last];
            _cachedLocalRot = _startLocalRotation * _GetSpinDeltaAtEnd(count);
            _hasPoseTick = true;
            _lastPoseTick = poseTick;

            if (m_Rigidbody == null)
            {
                _ApplyPose(_cachedLocalPos, _cachedLocalRot, true);
                _TriggerWaypointEvents(last);
            }
            else
            {
                _QueueWaypointEventsAfterPose(last, poseTick);
            }

            _runComplete = true;
            return;
        }

        int seg = _FindSegmentIndex(time, count);
        if (seg < 0) seg = 0;
        if (seg > count - 2) seg = count - 2;

        double tInSegment = time - _segmentStartOffsets[seg];
        if (!IsFinite(tInSegment) || tInSegment < 0.0) tInSegment = 0.0;

        int prevIndex = seg;
        int nextIndex = seg + 1;

        if (m_Rigidbody != null)
        {
            _QueueWaypointEventsAfterPose(prevIndex, poseTick);
        }

        float waitRaw = m_WaitTimeSeconds;
        if (!IsFinite(waitRaw)) waitRaw = 0f;
        double wait = (double)Mathf.Max(0f, waitRaw);
        double segTotalDur = _segmentDurations[seg];
        double travelDur = segTotalDur - wait;
        if (!IsFinite(travelDur) || travelDur < 0.0) travelDur = 0.0;

        Vector3 prevPos = _pointLocalPositions[prevIndex];
        Vector3 nextPos = _pointLocalPositions[nextIndex];

        Vector3 targetLocalPos;
        if (tInSegment < wait || travelDur <= 0.0)
        {
            targetLocalPos = prevPos;
        }
        else
        {
            float tau = (float)((tInSegment - wait) / travelDur);
            if (tau < 0f) tau = 0f;
            if (tau > 1f) tau = 1f;

            float u = tau;
            if (m_UsePointScaleXSpeedMultiplier && _segSpeedUse != null && _segSpeedUse.Length == count && _segSpeedUse[prevIndex])
            {
                float exp = Mathf.Exp(tau * _segSpeedLnRatio[prevIndex]);
                u = _segSpeedMa[prevIndex] * (exp - 1f) * _segSpeedInvK[prevIndex];
            }

            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            targetLocalPos = Vector3.Lerp(prevPos, nextPos, u);
        }

        _InitRotation(ticksPerSecond, count);
        _AdvanceRotationToTick(poseTick, count);

        Quaternion spinDelta = Quaternion.Euler(
            WrapDegrees(_rotAccumX),
            WrapDegrees(_rotAccumY),
            WrapDegrees(_rotAccumZ)
        );

        Quaternion targetLocalRot = _startLocalRotation * spinDelta;

        _cachedLocalPos = targetLocalPos;
        _cachedLocalRot = targetLocalRot;
        _hasPoseTick = true;
        _lastPoseTick = poseTick;

        if (m_Rigidbody == null)
        {
            _ApplyPose(targetLocalPos, targetLocalRot, false);
            _TriggerWaypointEvents(prevIndex);
        }
    }

    private void _QueueWaypointEventsAfterPose(int waypointIndex, long poseTick)
    {
        _hasPendingWaypointEvent = true;
        _pendingWaypointIndex = waypointIndex;
        _pendingWaypointPoseTick = poseTick;
    }

    private int _FindSegmentIndex(double t, int count)
    {
        // Find first segment where t < endOffset, clamped to [0, count-2].
        if (_segmentEndOffsets == null || _segmentEndOffsets.Length != count) return 0;
        if (t <= 0.0) return 0;

        int lastSeg = count - 2;
        if (lastSeg <= 0) return 0;
        if (t >= _segmentEndOffsets[lastSeg]) return lastSeg;

        int lo = 0;
        int hi = lastSeg;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (t < _segmentEndOffsets[mid]) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    private void _InitRotation(double ticksPerSecond, int count)
    {
        if (_rotInit && System.Math.Abs(_rotTicksPerSecond - ticksPerSecond) < 0.0001) return;
        if (count <= 0) return;
        EnsureLongArray(ref _rotSegEndTicks, count);

        _rotTicksPerSecond = ticksPerSecond;
        _rotStartTick = 0;
        _rotLastTickProcessed = 0;
        _rotSegIndex = 0;
        _rotAccumX = 0.0;
        _rotAccumY = 0.0;
        _rotAccumZ = 0.0;

        // Precompute tick boundaries per segment end.
        for (int s = 0; s < count; s++)
        {
            long endTick = FloorToLong(_segmentEndOffsets[s] * ticksPerSecond);
            _rotSegEndTicks[s] = endTick;
        }

        _rotInit = true;
    }

    private void _AdvanceRotationToTick(long tickNow, int count)
    {
        if (!_rotInit) return;
        if (count <= 0) return;
        if (_rotSegEndTicks == null || _rotSegEndTicks.Length != count) return;
        if (tickNow <= _rotLastTickProcessed) return;

        while (_rotLastTickProcessed < tickNow)
        {
            int seg = _rotSegIndex;
            if (seg < 0) seg = 0;
            if (seg >= count) seg = count - 1;

            long segEnd = _rotSegEndTicks[seg];
            long endTick = (tickNow < segEnd) ? tickNow : segEnd;

            long used = endTick - _rotLastTickProcessed;
            if (used > 0)
            {
                Vector3 perTick = _pointEulerPerTick[seg];
                _rotAccumX += (double)perTick.x * (double)used;
                _rotAccumY += (double)perTick.y * (double)used;
                _rotAccumZ += (double)perTick.z * (double)used;
                _rotLastTickProcessed = endTick;
            }

            if (_rotLastTickProcessed >= segEnd)
            {
                if (_rotSegIndex < count - 2)
                {
                    _rotSegIndex++;
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }
    }

    private Quaternion _GetSpinDeltaAtEnd(int count)
    {
        // Use the accumulated rotation as of the last processed tick.
        return Quaternion.Euler(
            WrapDegrees(_rotAccumX),
            WrapDegrees(_rotAccumY),
            WrapDegrees(_rotAccumZ)
        );
    }

    private void _ApplyAudioCommandFromPoint(int pointIndex)
    {
        if (m_RollingAudio == null) return;
        if (m_PathPoints == null) return;
        if (pointIndex < 0 || pointIndex >= m_PathPoints.Length) return;

        Transform p = m_PathPoints[pointIndex];
        if (p == null) return;

        float cmd = p.localScale.y;
        if (!IsFinite(cmd)) return;

        if (cmd >= 2f)
        {
            if (!m_RollingAudio.isPlaying) m_RollingAudio.Play();
        }
        else if (cmd <= -2f)
        {
            if (m_RollingAudio.isPlaying) m_RollingAudio.Stop();
        }
    }

    private void _PlayWaypointAudio(int pointIndex)
    {
        if (m_PathPoints == null) return;
        if (pointIndex < 0 || pointIndex >= m_PathPoints.Length) return;

        Transform p = m_PathPoints[pointIndex];
        if (p == null) return;

        AudioSource src = null;
        if (_pointChildAudio != null && pointIndex >= 0 && pointIndex < _pointChildAudio.Length)
        {
            src = _pointChildAudio[pointIndex];
        }

        if (src == null)
        {
            bool tried = (_pointChildAudioTriedResolve != null && pointIndex >= 0 && pointIndex < _pointChildAudioTriedResolve.Length)
                ? _pointChildAudioTriedResolve[pointIndex]
                : true;

            if (!tried)
            {
                src = FindDirectChildAudioSource(p);
                if (_pointChildAudio != null && pointIndex >= 0 && pointIndex < _pointChildAudio.Length)
                {
                    _pointChildAudio[pointIndex] = src;
                }
                if (_pointChildAudioTriedResolve != null && pointIndex >= 0 && pointIndex < _pointChildAudioTriedResolve.Length)
                {
                    _pointChildAudioTriedResolve[pointIndex] = true;
                }
            }
        }

        if (src == null) return;
        src.Stop();
        src.Play();
    }

    private void _EnableWaypointChildObject(int pointIndex)
    {
        if (m_PathPoints == null) return;
        if (pointIndex < 0 || pointIndex >= m_PathPoints.Length) return;

        Transform p = m_PathPoints[pointIndex];
        if (p == null) return;

        GameObject go = null;
        if (_pointChildToggle != null && pointIndex >= 0 && pointIndex < _pointChildToggle.Length)
        {
            go = _pointChildToggle[pointIndex];
        }

        // Lazy resolve once if it was null.
        if (go == null)
        {
            bool tried = (_pointChildToggleTriedResolve != null && pointIndex >= 0 && pointIndex < _pointChildToggleTriedResolve.Length)
                ? _pointChildToggleTriedResolve[pointIndex]
                : true;

            if (!tried)
            {
                go = FindDirectChildNonAudioObject(p);
                if (_pointChildToggle != null && pointIndex >= 0 && pointIndex < _pointChildToggle.Length)
                {
                    _pointChildToggle[pointIndex] = go;
                }
                if (_pointChildToggleTriedResolve != null && pointIndex >= 0 && pointIndex < _pointChildToggleTriedResolve.Length)
                {
                    _pointChildToggleTriedResolve[pointIndex] = true;
                }
            }
        }

        if (go == null) return;
        if (!go.activeSelf) go.SetActive(true);
    }

    private void _TriggerWaypointEvents(int currentIndex)
    {
        if (currentIndex < 0) currentIndex = 0;
        int count = (m_PathPoints != null) ? m_PathPoints.Length : 0;
        if (count <= 0) return;
        if (currentIndex >= count) currentIndex = count - 1;

        if (_lastEventPointIndex < 0)
        {
            _lastEventPointIndex = currentIndex;
            _ApplyAudioCommandFromPoint(currentIndex);
            _PlayWaypointAudio(currentIndex);
            _EnableWaypointChildObject(currentIndex);
            return;
        }

        if (currentIndex == _lastEventPointIndex) return;

        // Monotonic forward progression: fire all crossed indices.
        int start = _lastEventPointIndex;
        int end = currentIndex;
        if (end < start)
        {
            _lastEventPointIndex = currentIndex;
            _ApplyAudioCommandFromPoint(currentIndex);
            _PlayWaypointAudio(currentIndex);
            _EnableWaypointChildObject(currentIndex);
            return;
        }

        for (int i = start + 1; i <= end; i++)
        {
            _ApplyAudioCommandFromPoint(i);
            _PlayWaypointAudio(i);
            _EnableWaypointChildObject(i);
        }

        _lastEventPointIndex = currentIndex;
    }

    private void FixedUpdate()
    {
        if (!gameObject.activeInHierarchy) return;
        if (m_Rigidbody == null) return;
        if (!_hasPoseTick) return;
        if (_hasAppliedPoseTick && _lastAppliedPoseTick == _lastPoseTick) return;

        _ApplyPose(_cachedLocalPos, _cachedLocalRot, false);
        _hasAppliedPoseTick = true;
        _lastAppliedPoseTick = _lastPoseTick;

        // Fire waypoint events only after pose is applied (prevents 1-frame early triggers).
        if (_hasPendingWaypointEvent && _pendingWaypointPoseTick == _lastPoseTick)
        {
            _hasPendingWaypointEvent = false;
            _TriggerWaypointEvents(_pendingWaypointIndex);
        }
    }

    private void _ApplyPose(Vector3 targetLocalPosition, Quaternion targetLocalRotation, bool teleport)
    {
        Transform parent = transform.parent;

        Vector3 targetWorldPos = (parent != null)
            ? parent.TransformPoint(targetLocalPosition)
            : targetLocalPosition;

        Quaternion targetWorldRot = (parent != null)
            ? parent.rotation * targetLocalRotation
            : targetLocalRotation;

        if (m_Rigidbody != null)
        {
            if (teleport)
            {
                var prevInterp = m_Rigidbody.interpolation;
                m_Rigidbody.interpolation = RigidbodyInterpolation.None;

                if (!m_Rigidbody.isKinematic)
                {
                    m_Rigidbody.velocity = Vector3.zero;
                    m_Rigidbody.angularVelocity = Vector3.zero;
                }

                m_Rigidbody.position = targetWorldPos;
                m_Rigidbody.rotation = targetWorldRot;
                transform.position = targetWorldPos;
                transform.rotation = targetWorldRot;
                Physics.SyncTransforms();
                m_Rigidbody.interpolation = prevInterp;
                return;
            }

            m_Rigidbody.MovePosition(targetWorldPos);
            if (m_UseRigidbodyForRotation) { m_Rigidbody.MoveRotation(targetWorldRot); }
            else { transform.rotation = targetWorldRot; }
            return;
        }

        if (teleport)
        {
            transform.position = targetWorldPos;
            transform.rotation = targetWorldRot;
        }
        else
        {
            transform.localPosition = targetLocalPosition;
            transform.localRotation = targetLocalRotation;
        }
    }

    private void _TryDamageSaccEntityFromCollider(Collider other)
    {
        if (!m_DamageSaccEntities) return;
        if (other == null) return;

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) return;

        if (m_OnlyVehicleOwnerSendsDamage && !Networking.IsOwner(entity.gameObject)) return;

        float now = Time.time;
        float cd = m_CollisionDamageCooldownSeconds;
        if (!IsFinite(cd)) cd = 0.25f;
        cd = Mathf.Max(0f, cd);

        if (entity == _lastDamagedEntity && (now - _lastCollisionDamageTime) < cd) return;

        float dmg = m_CollisionDamage;
        if (!IsFinite(dmg)) dmg = 999999f;
        if (dmg <= 0f) return;

        entity.SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SaccEntity.SendDamageEvent), dmg, m_CollisionWeaponType);

        _lastDamagedEntity = entity;
        _lastCollisionDamageTime = now;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;
        _TryDamageSaccEntityFromCollider(collision.collider);
    }
}
