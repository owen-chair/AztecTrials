
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class TimeSyncRotatingPivot : UdonSharpBehaviour
{
    [Header("Refs")]
    [Tooltip("Transform to rotate. If null, uses this transform.")]
    public Transform m_Target;

    [Tooltip("Optional. If set (or found on the target), rotation will be applied via Rigidbody.MoveRotation in FixedUpdate for physics safety.")]
    public Rigidbody m_Rigidbody;

    [Header("Tick")]
    [Tooltip("Ticks per second (e.g. 30 for time-locked 30fps motion).")]
    [Min(1f)]
    public float m_TicksPerSecond = 30f;

    [Tooltip("Optional global offset. Use this to align multiple pivots.")]
    public float m_TimeOffsetSeconds = 0f;

    [Header("Rotation")]
    [Tooltip("Local Euler degrees to apply per tick. Example: (1,0,0) rotates 1 degree around X per tick.")]
    public Vector3 m_RotationPerTickEuler = new Vector3(0f, 1f, 0f);

    [System.NonSerialized]
    private bool _startRotationInitialized;

    [System.NonSerialized]
    private Quaternion _startLocalRotation;

    [System.NonSerialized]
    private Quaternion _cachedDesiredLocalRotation;

    [System.NonSerialized]
    private bool _hasCachedDesiredRotation;

    [System.NonSerialized]
    private float _nextComputeTime;

    private void _EnsureStartRotationCached()
    {
        if (_startRotationInitialized) return;
        Transform t = (this.m_Target != null) ? this.m_Target : this.transform;
        this._startLocalRotation = t.localRotation;
        _startRotationInitialized = true;
    }

    private Rigidbody _GetRigidbody()
    {
        if (this.m_Rigidbody != null) return this.m_Rigidbody;
        Transform t = (this.m_Target != null) ? this.m_Target : this.transform;
        if (t == null) return null;
        this.m_Rigidbody = t.GetComponent<Rigidbody>();
        return this.m_Rigidbody;
    }

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
        // Keep angles bounded to avoid float precision loss note: fmod via floor.
        if (!IsFinite(degrees)) return 0f;
        double turns = System.Math.Floor(degrees / 360.0);
        double wrapped = degrees - (turns * 360.0);
        // Map into [-180, 180) for nicer interpolation/inspector sanity (not required).
        if (wrapped >= 180.0) wrapped -= 360.0;
        return (float)wrapped;
    }

    private void Awake()
    {
        _EnsureStartRotationCached();
        _GetRigidbody();
    }

    private void OnEnable()
    {
        // Do NOT re-cache baseline here. This object may be enabled at different times per client
        // (e.g., distance-based culling), and re-caching would bake in a client-specific offset.
        _EnsureStartRotationCached();
        _GetRigidbody();

        _hasCachedDesiredRotation = false;
        _nextComputeTime = 0f;
    }

    private void OnDisable()
    {
        // Keep cached baseline stable across disable/enable.
    }

    private void Start()
    {
        _EnsureStartRotationCached();
        _GetRigidbody();
    }

    private void Update()
    {
        if (!this.gameObject.activeInHierarchy) return;

        float now = Time.time;
        if (now < _nextComputeTime) return;

        float ticksPerSecondRaw = this.m_TicksPerSecond;
        if (!IsFinite(ticksPerSecondRaw)) ticksPerSecondRaw = 30f;
        ticksPerSecondRaw = Mathf.Max(1f, ticksPerSecondRaw);

        float interval = 1f / ticksPerSecondRaw;
        _nextComputeTime = now + interval;

        _EnsureStartRotationCached();

        Transform t = (this.m_Target != null) ? this.m_Target : this.transform;
        if (t == null) return;

        float offsetRaw = this.m_TimeOffsetSeconds;
        Vector3 perTick = this.m_RotationPerTickEuler;

        if (!IsFinite(offsetRaw)) offsetRaw = 0f;
        if (!IsFinite(perTick.x)) perTick.x = 0f;
        if (!IsFinite(perTick.y)) perTick.y = 0f;
        if (!IsFinite(perTick.z)) perTick.z = 0f;

        double ticksPerSecond = (double)ticksPerSecondRaw;

        double baseTime = Networking.GetServerTimeInSeconds();
        if (!IsFinite(baseTime)) return;

        double time = baseTime + (double)offsetRaw;
        if (!IsFinite(time)) return;

        // Continuous server-time.
        double tickExact = time * ticksPerSecond;
        if (!IsFinite(tickExact)) return;

        double x = tickExact * (double)perTick.x;
        double y = tickExact * (double)perTick.y;
        double z = tickExact * (double)perTick.z;

        float wx = WrapDegrees(x);
        float wy = WrapDegrees(y);
        float wz = WrapDegrees(z);

        Quaternion delta = Quaternion.Euler(wx, wy, wz);
        this._cachedDesiredLocalRotation = this._startLocalRotation * delta;
        _hasCachedDesiredRotation = true;

        // If no rigidbody, apply directly here (no need to do anything in FixedUpdate).
        Rigidbody rb = _GetRigidbody();
        if (rb == null)
        {
            t.localRotation = this._cachedDesiredLocalRotation;
        }
    }

    private void FixedUpdate()
    {
        // Physics-safe application when using a Rigidbody.
        if (!_hasCachedDesiredRotation) return;

        Rigidbody rb = _GetRigidbody();
        if (rb == null) return;

        Transform t = (this.m_Target != null) ? this.m_Target : this.transform;
        if (t == null) return;

        Transform parent = t.parent;
        Quaternion desiredWorldRotation = (parent != null)
            ? parent.rotation * this._cachedDesiredLocalRotation
            : this._cachedDesiredLocalRotation;

        rb.MoveRotation(desiredWorldRotation);
    }
}
