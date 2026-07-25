
using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public class Boulder : UdonSharpBehaviour
{
    [Header("Physics")]
    [Tooltip("Optional. If null, will try GetComponent<Rigidbody>(). For best physics on vehicles, add a Rigidbody and keep it kinematic.")]
    public Rigidbody m_Rigidbody;

    [Header("Path")]
    [Tooltip("Waypoints (in order). Movement loops from last point back to point 0.")]
    public Transform[] m_PathPoints;

    [Tooltip("If true, when reaching the last point the boulder will teleport to point 0 to restart, instead of moving along the last->0 segment.")]
    public bool m_TeleportToPoint0OnLoopEnd = false;

    [Min(0.01f)]
    [Tooltip("Movement speed in world units/second (constant along the path).")]
    public float m_SpeedUnitsPerSecond = 2f;

    [Header("Speed Modifiers")]
    [Tooltip("If true, each waypoint's localScale.x is used as a speed multiplier. The multiplier lerps from point A to B during travel, and then continues from B on the next segment (resets on loop).")]
    public bool m_UsePointScaleXSpeedMultiplier = false;

    [Tooltip("Minimum allowed speed multiplier (prevents divide-by-zero if scale.x is 0).")]
    [Min(0.0001f)]
    public float m_MinSpeedMultiplier = 0.01f;

    [Min(0.01f)]
    [Tooltip("Deprecated/ignored: movement uses m_SpeedUnitsPerSecond.")]
    public float m_TravelTimeSeconds = 5f;

    [Min(0f)]
    public float m_WaitTimeSeconds = 0f;

    [Tooltip("Optional global offset. Use this to align multiple boulders.")]
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
    [System.NonSerialized] private Quaternion _startLocalRotation;
    [System.NonSerialized] private double[] _segmentDurations;
    [System.NonSerialized] private double[] _segmentStartOffsets;
    [System.NonSerialized] private double[] _segmentEndOffsets;

    // Perf: precomputed params for speed-multiplier mapping (per segment i -> i+1).
    [System.NonSerialized] private bool[] _segSpeedUse;
    [System.NonSerialized] private float[] _segSpeedMa;
    [System.NonSerialized] private float[] _segSpeedLnRatio;
    [System.NonSerialized] private float[] _segSpeedInvK;
    [System.NonSerialized] private double _cycleDuration;
    [System.NonSerialized] private bool _lastCycleValid;
    [System.NonSerialized] private long _lastCycleGlobal;
    [System.NonSerialized] private int _lastEventPointIndex = -1;

    // Rotation perf: incremental tick accumulation instead of summing all segments each frame.
    [System.NonSerialized] private bool _rotInit;
    [System.NonSerialized] private double _rotTicksPerSecond;
    [System.NonSerialized] private long _rotCycleGlobal;
    [System.NonSerialized] private long _rotCycleStartTick;
    [System.NonSerialized] private long _rotLastTickProcessed;
    [System.NonSerialized] private long[] _rotSegEndTicks;
    [System.NonSerialized] private int _rotSegIndex;
    [System.NonSerialized] private double _rotAccumX;
    [System.NonSerialized] private double _rotAccumY;
    [System.NonSerialized] private double _rotAccumZ;

    [System.NonSerialized] private float _lastCollisionDamageTime;
    [System.NonSerialized] private SaccEntity _lastDamagedEntity;

    // Perf: only recompute pose when the server tick changes.
    [System.NonSerialized] private bool _hasPoseTick;
    [System.NonSerialized] private long _lastPoseTick;

    [System.NonSerialized] private Vector3 _cachedLocalPos;
    [System.NonSerialized] private Quaternion _cachedLocalRot;
    [System.NonSerialized] private bool _cachedTeleport;

    [System.NonSerialized] private bool _tickLoopActive;
    [System.NonSerialized] private bool _hasScheduledTick;
    [System.NonSerialized] private long _scheduledTick;
    [System.NonSerialized] private long _lastProcessedTick;
    [System.NonSerialized] private int _lastScheduleFrame;
    [System.NonSerialized] private bool _hasAppliedPoseTick;
    [System.NonSerialized] private long _lastAppliedPoseTick;

    private static bool IsFinite(float v)
    {
        return !(float.IsNaN(v) || float.IsInfinity(v));
    }

    private static bool IsFinite(double v)
    {
        return !(double.IsNaN(v) || double.IsInfinity(v));
    }

    private static float WrapDegrees(double degrees)
    {
        if (!IsFinite(degrees)) return 0f;
        long turns = FloorToLong(degrees / 360.0);
        double wrapped = degrees - (turns * 360.0);
        if (wrapped >= 180.0) wrapped -= 360.0;
        return (float)wrapped;
    }

    private static long FloorToLong(double v)
    {
        // Faster than System.Math.Floor for the common case.
        long i = (long)v; // truncates toward zero
        if (v < 0.0 && (double)i != v) i--;
        return i;
    }

    private static double FloorToDouble(double v)
    {
        return (double)FloorToLong(v);
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

    private static int ModNonNegative(long value, int mod)
    {
        // Udon does not expose Int64 modulo (%). Implement remainder via floor-division.
        if (mod <= 0) return 0;
        double div = (double)value / (double)mod;
        if (!IsFinite(div)) return 0;
        long flo = (long)System.Math.Floor(div);
        long rem = value - ((long)mod * flo);
        int r = (int)rem;
        if (r < 0) r += mod;
        return r;
    }

    private void _EnsureCached()
    {
        if (_cached) return;

        if (m_Rigidbody == null) m_Rigidbody = GetComponent<Rigidbody>();
        if (m_Rigidbody != null)
        {
            // Recommended defaults for moving props.
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
        EnsureDoubleArray(ref _segmentDurations, count);
        EnsureDoubleArray(ref _segmentStartOffsets, count);
        EnsureDoubleArray(ref _segmentEndOffsets, count);

        EnsureBoolArray(ref _segSpeedUse, count);
        EnsureFloatArray(ref _segSpeedMa, count);
        EnsureFloatArray(ref _segSpeedLnRatio, count);
        EnsureFloatArray(ref _segSpeedInvK, count);
        _cycleDuration = 0.0;

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

            // Optional per-waypoint sound: if the waypoint has an AudioSource on a direct child,
            // play it once when the boulder reaches this waypoint.
            _pointChildAudio[i] = _FindDirectChildAudioSource(p);
            if (_pointChildAudioTriedResolve != null && _pointChildAudioTriedResolve.Length == count)
            {
                // If it was null here, allow exactly one later resolve attempt on first waypoint hit.
                _pointChildAudioTriedResolve[i] = (_pointChildAudio[i] != null);
            }

            _pointLocalPositions[i] = (parent != null)
                ? parent.InverseTransformPoint(p.position)
                : p.position;

            // Requirement #3:
            // The *rotation per tick* is derived from the previous point's rotation (starting at point 0).
            // Interpret the point's local rotation euler angles as degrees-per-tick.
            Quaternion localRot = (parent != null)
                ? Quaternion.Inverse(parent.rotation) * p.rotation
                : p.rotation;

            Vector3 e = localRot.eulerAngles;
            // Convert to signed degrees so negative spin is possible.
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

        // Precompute per-segment speed-multiplier mapping parameters.
        // Runtime mapping uses Exp(tau*lnRatio) instead of recalculating Log every tick.
        for (int i = 0; i < count; i++)
        {
            _segSpeedUse[i] = false;
            _segSpeedMa[i] = 1f;
            _segSpeedLnRatio[i] = 0f;
            _segSpeedInvK[i] = 0f;

            if (!m_UsePointScaleXSpeedMultiplier) continue;

            int next = (i + 1) % count;
            float ma = _pointSpeedMult[i];
            float mb = _pointSpeedMult[next];
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

        // Build per-segment timing so speed is constant regardless of point spacing.
        float speedRaw = m_SpeedUnitsPerSecond;
        if (!IsFinite(speedRaw)) speedRaw = 2f;
        float speed = Mathf.Max(0.01f, speedRaw);

        float waitRaw = m_WaitTimeSeconds;
        if (!IsFinite(waitRaw)) waitRaw = 0f;
        double wait = (double)Mathf.Max(0f, waitRaw);

        for (int i = 0; i < count; i++)
        {
            _segmentStartOffsets[i] = _cycleDuration;

            int next = (i + 1) % count;
            double travel = 0.0;
            bool skipLoopSegment = m_TeleportToPoint0OnLoopEnd && (i == count - 1);
            if (!skipLoopSegment && m_PathPoints != null && i < m_PathPoints.Length && next < m_PathPoints.Length)
            {
                Transform a = m_PathPoints[i];
                Transform b = m_PathPoints[next];
                if (a != null && b != null)
                {
                    float dist = Vector3.Distance(a.position, b.position);
                    if (IsFinite(dist) && dist > 0f)
                    {
                        if (m_UsePointScaleXSpeedMultiplier && _pointSpeedMult != null && _pointSpeedMult.Length == count)
                        {
                            float ma = _pointSpeedMult[i];
                            float mb = _pointSpeedMult[next];
                            ma = Mathf.Max(m_MinSpeedMultiplier, IsFinite(ma) ? ma : 1f);
                            mb = Mathf.Max(m_MinSpeedMultiplier, IsFinite(mb) ? mb : 1f);

                            float k = mb - ma;
                            if (Mathf.Abs(k) < 1e-6f)
                            {
                                travel = (double)dist / ((double)speed * (double)ma);
                            }
                            else
                            {
                                // dt = (dist/speed) * du/(ma + k*u)
                                // T = (dist/speed) * (ln(mb/ma)/k)
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
            }

            double segDur = wait + travel;
            if (!IsFinite(segDur) || segDur < 0.0) segDur = 0.0;
            _segmentDurations[i] = segDur;
            _cycleDuration += segDur;
            _segmentEndOffsets[i] = _cycleDuration;
        }

        _cached = true;
        _rotInit = false;
    }

    private void Awake()
    {
        _EnsureCached();
        _SnapToCurrentPose();
        _lastCycleValid = false;
        _lastEventPointIndex = -1;
        _lastCollisionDamageTime = -999f;
        _lastDamagedEntity = null;
        _rotInit = false;
        _hasPoseTick = false;

        // Published instances can drop/throttle delayed custom events under load.
        // Drive motion from FixedUpdate instead of an event-scheduled tick loop.
        _tickLoopActive = false;
        _hasScheduledTick = false;
        _lastScheduleFrame = -1;
        _lastProcessedTick = -9223372036854775807L;
        _hasAppliedPoseTick = false;
    }

    private void OnEnable()
    {
        _cached = false;
        _EnsureCached();
        _SnapToCurrentPose();
        _lastCycleValid = false;
        _lastEventPointIndex = -1;
        _lastCollisionDamageTime = -999f;
        _lastDamagedEntity = null;
        _rotInit = false;
        _hasPoseTick = false;

        // FixedUpdate-driven motion; do not start an event-scheduled tick loop.
        _tickLoopActive = false;
        _hasScheduledTick = false;
        _lastScheduleFrame = -1;
        _lastProcessedTick = -9223372036854775807L;
        _hasAppliedPoseTick = false;
    }

    private void OnDisable()
    {
        _tickLoopActive = false;
        _hasScheduledTick = false;
    }

    private void Start()
    {
        _EnsureCached();
        _SnapToCurrentPose();
        _lastCycleValid = false;
        _lastEventPointIndex = -1;
        _lastCollisionDamageTime = -999f;
        _lastDamagedEntity = null;
        _rotInit = false;
        _hasPoseTick = false;

        // FixedUpdate-driven motion; nothing to schedule here.
        _tickLoopActive = false;
    }

    private void _SnapToCurrentPose()
    {
        // Hard-teleport once on startup to avoid a one-frame overlap at the prefab's authoring position
        // (often world origin) while still placing the boulder at its correct server-time position.
        _EnsureCached();

        int count = (_pointLocalPositions != null) ? _pointLocalPositions.Length : 0;
        if (count < 2)
        {
            if (_pointLocalPositions != null && _pointLocalPositions.Length > 0)
            {
                _ApplyPose(_pointLocalPositions[0], _startLocalRotation, true);
            }
            return;
        }

        double cycleDuration = _cycleDuration;
        if (!IsFinite(cycleDuration) || cycleDuration <= 0.0)
        {
            _ApplyPose(_pointLocalPositions[0], _startLocalRotation, true);
            return;
        }

        float offsetRaw = m_TimeOffsetSeconds;
        float ticksRaw = m_TicksPerSecond;
        if (!IsFinite(offsetRaw)) offsetRaw = 0f;
        if (!IsFinite(ticksRaw)) ticksRaw = 30f;
        double ticksPerSecond = (double)Mathf.Max(1f, ticksRaw);

        double baseTime = Networking.GetServerTimeInSeconds();
        if (!IsFinite(baseTime))
        {
            _ApplyPose(_pointLocalPositions[0], _startLocalRotation, true);
            return;
        }

        double time = baseTime + (double)offsetRaw;
        if (!IsFinite(time))
        {
            _ApplyPose(_pointLocalPositions[0], _startLocalRotation, true);
            return;
        }

        double cyclesExact = time / cycleDuration;
        if (!IsFinite(cyclesExact))
        {
            _ApplyPose(_pointLocalPositions[0], _startLocalRotation, true);
            return;
        }

        long cycleGlobal = FloorToLong(cyclesExact);
        double cycleStartTime = (double)cycleGlobal * cycleDuration;
        double tInCycle = time - cycleStartTime;
        if (tInCycle < 0.0)
        {
            double div = FloorToDouble(time / cycleDuration);
            cycleStartTime = div * cycleDuration;
            tInCycle = time - cycleStartTime;
            cycleGlobal = (long)div;
        }

        int segInCycle = _FindSegmentIndex(tInCycle, count);
        double tInSegment = tInCycle - _segmentStartOffsets[segInCycle];
        if (!IsFinite(tInSegment) || tInSegment < 0.0) tInSegment = 0.0;

        int prevIndex = segInCycle;
        int nextIndex = (segInCycle + 1) % count;

        float waitRaw = m_WaitTimeSeconds;
        if (!IsFinite(waitRaw)) waitRaw = 0f;
        double wait = (double)Mathf.Max(0f, waitRaw);
        double segTotalDur = _segmentDurations[segInCycle];
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

        double tickExact = time * ticksPerSecond;
        if (!IsFinite(tickExact))
        {
            _ApplyPose(targetLocalPos, _startLocalRotation, true);
            return;
        }
        long tickNow = FloorToLong(tickExact);

        _InitRotationCycle(cycleStartTime, ticksPerSecond, cycleGlobal, count);
        _AdvanceRotationToTick(tickNow, count);

        Quaternion spinDelta = Quaternion.Euler(
            WrapDegrees(_rotAccumX),
            WrapDegrees(_rotAccumY),
            WrapDegrees(_rotAccumZ)
        );
        Quaternion targetLocalRot = _startLocalRotation * spinDelta;

        _ApplyPose(targetLocalPos, targetLocalRot, true);

        _cachedLocalPos = targetLocalPos;
        _cachedLocalRot = targetLocalRot;
        _cachedTeleport = true;
        _hasPoseTick = true;
        _lastPoseTick = tickNow;

        // Avoid one-shot waypoint sounds on join; but do apply rolling audio state.
        _lastEventPointIndex = prevIndex;
        _ApplyAudioCommandFromPoint(prevIndex);

        // Seed cycle tracking to avoid wrap detection on the first FixedUpdate.
        _lastCycleValid = true;
        _lastCycleGlobal = cycleGlobal;
    }

    public void _TickLoop()
    {
        if (!_tickLoopActive) return;

        _EnsureCached();

        int count = (_pointLocalPositions != null) ? _pointLocalPositions.Length : 0;
        if (count < 2) return;

        float offsetRaw = m_TimeOffsetSeconds;
        float ticksRaw = m_TicksPerSecond;
        if (!IsFinite(offsetRaw)) offsetRaw = 0f;
        if (!IsFinite(ticksRaw)) ticksRaw = 30f;
        double ticksPerSecond = (double)Mathf.Max(1f, ticksRaw);

        double baseTime = Networking.GetServerTimeInSeconds();
        if (!IsFinite(baseTime)) return;
        double serverTime = baseTime + (double)offsetRaw;
        if (!IsFinite(serverTime)) return;

        double tickExact = serverTime * ticksPerSecond;
        if (!IsFinite(tickExact)) return;
        long tickNow = FloorToLong(tickExact);

        // Initialize the first scheduled tick if needed.
        if (!_hasScheduledTick)
        {
            _scheduledTick = tickNow + 1;
            _hasScheduledTick = true;
        }

        // Do work only once per tick.
        if (tickNow >= _scheduledTick)
        {
            if (tickNow > _lastProcessedTick)
            {
                _ComputeAndCachePoseForTick(tickNow, ticksPerSecond, count);
                _lastProcessedTick = tickNow;
            }

            // Advance schedule to the next tick boundary.
            _scheduledTick = tickNow + 1;
        }

        // Schedule next wake-up relative to server-time tick boundary.
        double nextTickTime = (double)_scheduledTick / ticksPerSecond;
        double delayD = nextTickTime - serverTime;
        if (!IsFinite(delayD)) delayD = 0.05;
        float delay = (float)delayD;
        if (!IsFinite(delay) || delay < 0.001f) delay = 0.001f;
        if (delay > 1f) delay = 1f;

        // Guard against duplicate queued calls scheduling multiple future wakes in the same frame.
        int frame = Time.frameCount;
        if (frame != _lastScheduleFrame)
        {
            _lastScheduleFrame = frame;
            SendCustomEventDelayedSeconds("_TickLoop", delay);
        }
    }

    private void _ComputeAndCachePoseForTick(long poseTick, double ticksPerSecond, int count)
    {
        double cycleDuration = _cycleDuration;
        if (!IsFinite(cycleDuration) || cycleDuration <= 0.0) return;

        // Use quantized time for determinism.
        double time = (double)poseTick / ticksPerSecond;

        // Movement: locate segment by cumulative durations.
        double cyclesExact = time / cycleDuration;
        if (!IsFinite(cyclesExact)) return;

        long cycleGlobal = FloorToLong(cyclesExact);
        double cycleStartTime = (double)cycleGlobal * cycleDuration;
        double tInCycle = time - cycleStartTime;
        if (tInCycle < 0.0)
        {
            double div = FloorToDouble(time / cycleDuration);
            cycleStartTime = div * cycleDuration;
            tInCycle = time - cycleStartTime;
            cycleGlobal = (long)div;
        }

        bool wrappedCycle = false;
        if (_lastCycleValid)
        {
            wrappedCycle = (cycleGlobal != _lastCycleGlobal);
        }
        _lastCycleValid = true;
        _lastCycleGlobal = cycleGlobal;

        int segInCycle = _FindSegmentIndex(tInCycle, count);
        double tInSegment = tInCycle - _segmentStartOffsets[segInCycle];
        if (!IsFinite(tInSegment) || tInSegment < 0.0) tInSegment = 0.0;

        int prevIndex = segInCycle;
        int nextIndex = (segInCycle + 1) % count;

        // Waypoint events: rolling audio commands + per-waypoint AudioSource.
        _TriggerWaypointEvents(prevIndex, wrappedCycle, count);

        float waitRaw = m_WaitTimeSeconds;
        if (!IsFinite(waitRaw)) waitRaw = 0f;
        double wait = (double)Mathf.Max(0f, waitRaw);
        double segTotalDur = _segmentDurations[segInCycle];
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

        // Rotation: integrate tick-based spin within the current cycle for continuity.
        long tickNow = poseTick;
        double dTicks = _rotTicksPerSecond - ticksPerSecond;
        if (dTicks < 0.0) dTicks = -dTicks;
        if (!_rotInit || _rotCycleGlobal != cycleGlobal || dTicks > 0.0001 || tickNow < _rotLastTickProcessed)
        {
            _InitRotationCycle(cycleStartTime, ticksPerSecond, cycleGlobal, count);
        }

        _AdvanceRotationToTick(tickNow, count);

        Quaternion spinDelta = Quaternion.Euler(
            WrapDegrees(_rotAccumX),
            WrapDegrees(_rotAccumY),
            WrapDegrees(_rotAccumZ)
        );

        Quaternion targetLocalRot = _startLocalRotation * spinDelta;

        bool forceTeleport = m_TeleportToPoint0OnLoopEnd && wrappedCycle;

        _cachedLocalPos = targetLocalPos;
        _cachedLocalRot = targetLocalRot;
        _cachedTeleport = forceTeleport;
        _hasPoseTick = true;
        _lastPoseTick = poseTick;

        // If there's no rigidbody, apply directly now.
        if (m_Rigidbody == null)
        {
            _ApplyPose(targetLocalPos, targetLocalRot, forceTeleport);
        }
    }

    private int _FindSegmentIndex(double tInCycle, int count)
    {
        // Find first segment where tInCycle < endOffset.
        if (_segmentEndOffsets == null || _segmentEndOffsets.Length != count) return 0;
        if (tInCycle <= 0.0) return 0;
        if (tInCycle >= _segmentEndOffsets[count - 1]) return count - 1;

        int lo = 0;
        int hi = count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (tInCycle < _segmentEndOffsets[mid]) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    private void _InitRotationCycle(double cycleStartTime, double ticksPerSecond, long cycleGlobal, int count)
    {
        if (count <= 0) return;
        EnsureLongArray(ref _rotSegEndTicks, count);

        _rotTicksPerSecond = ticksPerSecond;
        _rotCycleGlobal = cycleGlobal;
        _rotCycleStartTick = FloorToLong(cycleStartTime * ticksPerSecond);
        _rotLastTickProcessed = _rotCycleStartTick;
        _rotSegIndex = 0;
        _rotAccumX = 0.0;
        _rotAccumY = 0.0;
        _rotAccumZ = 0.0;

        // Compute absolute tick end boundaries for this cycle only (floor-based; depends on cycleStartTime).
        for (int s = 0; s < count; s++)
        {
            double segEndTime = cycleStartTime + _segmentStartOffsets[s] + _segmentDurations[s];
            long segEndTick = FloorToLong(segEndTime * ticksPerSecond);
            _rotSegEndTicks[s] = segEndTick;
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

            // Move to next segment if we reached this segment's end.
            if (_rotLastTickProcessed >= segEnd)
            {
                if (_rotSegIndex < count - 1)
                {
                    _rotSegIndex++;
                }
                else
                {
                    // End of cycle.
                    break;
                }
            }
            else
            {
                break;
            }
        }
    }

    private void _TryDamageSaccEntityFromCollider(Collider other)
    {
        if (!m_DamageSaccEntities) return;
        if (other == null) return;

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) return;

        if (m_OnlyVehicleOwnerSendsDamage && !Networking.IsOwner(entity.gameObject)) return;

        // Simple cooldown (mostly to prevent spam while scraping along a vehicle).
        float now = Time.time;
        float cd = m_CollisionDamageCooldownSeconds;
        if (!IsFinite(cd)) cd = 0.25f;
        cd = Mathf.Max(0f, cd);

        if (entity == _lastDamagedEntity && (now - _lastCollisionDamageTime) < cd) return;

        float dmg = m_CollisionDamage;
        if (!IsFinite(dmg)) dmg = 999999f;
        if (dmg <= 0f) return;

        // This is the same networked entry point SAV uses internally.
        // On the vehicle owner it will reduce Health and call NetworkExplode().
        entity.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SaccEntity.SendDamageEvent), dmg, m_CollisionWeaponType);

        _lastDamagedEntity = entity;
        _lastCollisionDamageTime = now;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;
        _TryDamageSaccEntityFromCollider(collision.collider);
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

        // Encoding: +2 starts audio, -2 stops audio.
        if (cmd >= 2f)
        {
            if (!m_RollingAudio.isPlaying) m_RollingAudio.Play();
        }
        else if (cmd <= -2f)
        {
            if (m_RollingAudio.isPlaying) m_RollingAudio.Stop();
        }
    }

    private static AudioSource _FindDirectChildAudioSource(Transform root)
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

        // Lazy resolve in case caching ran before children/components were ready (or Udon cached null).
        if (src == null)
        {
            bool tried = (_pointChildAudioTriedResolve != null && pointIndex >= 0 && pointIndex < _pointChildAudioTriedResolve.Length)
                ? _pointChildAudioTriedResolve[pointIndex]
                : true;

            if (tried) return;

            src = _FindDirectChildAudioSource(p);

            if (_pointChildAudio != null && pointIndex >= 0 && pointIndex < _pointChildAudio.Length)
            {
                _pointChildAudio[pointIndex] = src;
            }
            if (_pointChildAudioTriedResolve != null && pointIndex >= 0 && pointIndex < _pointChildAudioTriedResolve.Length)
            {
                _pointChildAudioTriedResolve[pointIndex] = true;
            }
        }

        if (src == null) return;

        // Force a retrigger even if it was already playing.
        src.Stop();
        src.Play();
    }

    private void _TriggerWaypointEvents(int currentIndex, bool wrappedCycle, int count)
    {
        if (count <= 0) return;
        if (currentIndex < 0) currentIndex = 0;
        if (currentIndex >= count) currentIndex = count - 1;

        // First tick: just fire the current index.
        if (_lastEventPointIndex < 0 || _lastEventPointIndex >= count)
        {
            _lastEventPointIndex = currentIndex;
            _ApplyAudioCommandFromPoint(currentIndex);
            _PlayWaypointAudio(currentIndex);
            return;
        }

        if (_lastEventPointIndex == currentIndex) return;

        // If we wrapped the cycle (or time jumped), keep it simple and fire only the current index.
        if (wrappedCycle)
        {
            _lastEventPointIndex = currentIndex;
            _ApplyAudioCommandFromPoint(currentIndex);
            _PlayWaypointAudio(currentIndex);
            return;
        }

        // Normal forward progression within a cycle: fire every crossed waypoint index.
        int start = _lastEventPointIndex;
        int end = currentIndex;
        if (end < start)
        {
            // Unexpected backwards step; just fire current.
            _lastEventPointIndex = currentIndex;
            _ApplyAudioCommandFromPoint(currentIndex);
            _PlayWaypointAudio(currentIndex);
            return;
        }

        for (int i = start + 1; i <= end; i++)
        {
            _ApplyAudioCommandFromPoint(i);
            _PlayWaypointAudio(i);
        }

        _lastEventPointIndex = currentIndex;
    }

    private void FixedUpdate()
    {
        if (!gameObject.activeInHierarchy) return;

        _EnsureCached();

        int count = (_pointLocalPositions != null) ? _pointLocalPositions.Length : 0;
        if (count < 2) return;

        float offsetRaw = m_TimeOffsetSeconds;
        float ticksRaw = m_TicksPerSecond;
        if (!IsFinite(offsetRaw)) offsetRaw = 0f;
        if (!IsFinite(ticksRaw)) ticksRaw = 30f;
        double ticksPerSecond = (double)Mathf.Max(1f, ticksRaw);

        double baseTime = Networking.GetServerTimeInSeconds();
        if (!IsFinite(baseTime)) return;
        double serverTime = baseTime + (double)offsetRaw;
        if (!IsFinite(serverTime)) return;

        double tickExact = serverTime * ticksPerSecond;
        if (!IsFinite(tickExact)) return;
        long tickNow = FloorToLong(tickExact);

        // Only do work when the deterministic server tick changes.
        if (_hasPoseTick && tickNow == _lastPoseTick) return;

        _ComputeAndCachePoseForTick(tickNow, ticksPerSecond, count);

        // If we have a Rigidbody, apply the cached pose through physics.
        // If we don't, _ComputeAndCachePoseForTick applies the pose directly.
        if (m_Rigidbody != null)
        {
            _ApplyPose(_cachedLocalPos, _cachedLocalRot, _cachedTeleport);
            _hasAppliedPoseTick = true;
            _lastAppliedPoseTick = _lastPoseTick;
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
                // Hard-set pose to avoid render interpolation across a large delta.
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
}
