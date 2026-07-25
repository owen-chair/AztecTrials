using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class TimeSyncdMovingPlatform : UdonSharpBehaviour
{
    [Header("Physics")]
    [Tooltip("Optional. If null, will try GetComponent<Rigidbody>(). For best physics on vehicles, add a Rigidbody and keep it kinematic.")]
    public Rigidbody m_Rigidbody;

    [System.NonSerialized]
    public Vector3 m_StartPosition;

    [Header("Path")]
    [Tooltip("Optional. If set, the platform will treat this transform's position as the end target (preferred over m_EndPosition).")]
    public Transform m_EndTarget;

    [Tooltip("Fallback end position in the platform's parent-local space, used when m_EndTarget is null.")]
    public Vector3 m_EndPosition;

    [System.NonSerialized]
    private bool _startPositionInitialized;
    private bool _endPositionInitialized;

    private void _EnsureStartPositionCached()
    {
        if (_startPositionInitialized) { return; }
        // Treat the initial localPosition as the start point.
        this.m_StartPosition = this.transform.localPosition;
        _startPositionInitialized = true;
    }

    private void _EnsureEndPositionCached()
    {
        if (_endPositionInitialized) { return; }
        if (this.m_EndTarget == null) { _endPositionInitialized = true; return; }

        // Convert EndTarget world position into this platform's parent-local space.
        Transform parent = this.transform.parent;
        this.m_EndPosition = (parent != null)
            ? parent.InverseTransformPoint(this.m_EndTarget.position)
            : this.m_EndTarget.position;

        _endPositionInitialized = true;
    }

    private void Awake()
    {
        _EnsureStartPositionCached();
        _EnsureEndPositionCached();

        _lastIsWaitingValid = false;

        if (this.m_Rigidbody == null) this.m_Rigidbody = this.GetComponent<Rigidbody>();
        if (this.m_Rigidbody != null)
        {
            // Recommended defaults for moving platforms.
            this.m_Rigidbody.isKinematic = true;
            this.m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void OnEnable()
    {
        _EnsureStartPositionCached();
        _EnsureEndPositionCached();

        _lastIsWaitingValid = false;

        if (this.m_Rigidbody == null) this.m_Rigidbody = this.GetComponent<Rigidbody>();
    }

    [Min(0.01f)]
    public float m_TravelTimeSeconds = 15f;

    [Min(0f)]
    public float m_WaitTimeSeconds = 2f;

    [Tooltip("Optional global offset for the cycle. Use this to align multiple platforms.")]
    public float m_TimeOffsetSeconds = 0f;

    [Tooltip("If true, uses SmoothStep easing during travel segments.")]
    public bool m_SmoothTravel = true;

    [Header("Audio")]
    [Tooltip("Optional. Played once when the platform starts moving (transition from wait phase into travel phase).")]
    public AudioSource m_MoveAudio;

    [System.NonSerialized] private bool _lastIsWaitingValid;
    [System.NonSerialized] private bool _lastIsWaiting;

    private static bool IsFinite(float v)
    {
        return !(float.IsNaN(v) || float.IsInfinity(v));
    }

    private static bool IsFinite(double v)
    {
        return !(double.IsNaN(v) || double.IsInfinity(v));
    }
    
    void Start()
    {
        _EnsureStartPositionCached();
        _EnsureEndPositionCached();
    }

    private void _TryPlayMoveAudio()
    {
        if (this.m_MoveAudio == null) return;
        if (this.m_MoveAudio.isPlaying) return;
        this.m_MoveAudio.Play();
    }

    private void FixedUpdate()
    {
        if (!this.gameObject.activeInHierarchy) return;
        _EnsureStartPositionCached();
        _EnsureEndPositionCached();

        float travelRaw = m_TravelTimeSeconds;
        float waitRaw = m_WaitTimeSeconds;
        float offsetRaw = m_TimeOffsetSeconds;

        // Defensive: NaN/Infinity can sneak in from serialized data or runtime writes and will break Math.Floor/casts.
        if (!IsFinite(travelRaw)) { travelRaw = 0.01f; }
        if (!IsFinite(waitRaw)) { waitRaw = 0f; }
        if (!IsFinite(offsetRaw)) { offsetRaw = 0f; }

        float travel = Mathf.Max(0.01f, travelRaw);
        float wait = Mathf.Max(0f, waitRaw);

        // We want movement START times to be aligned to global server-time boundaries.
        // Define a repeating segment of length S=(wait+travel).
        // On even segments (0,2,4,...) we do: wait-at-end then travel end->start.
        // On odd segments (1,3,5,...) we do: wait-at-start then travel start->end.
        // This matches the example: with S=20, TO starts at 10,30,50... and BACK starts at 0,20,40...
        double segment = (double)travel + (double)wait;
        if (!IsFinite(segment) || segment <= 0.0) return;

        // Keep server time in double. In the built client this value can be very large;
        // casting to float loses precision and breaks modulo/segment timing.
        // Use server time directly.
        // NOTE: Server time may be negative early on; that's still fine as long as it's finite and monotonic.
        // The real problem was clamping negative time to 0 (freezing the cycle at segment 0).
        double baseTime = Networking.GetServerTimeInSeconds();

        if (!IsFinite(baseTime)) { return; }

        double t = baseTime + (double)offsetRaw;
        if (!IsFinite(t)) { return; }
        // Do NOT clamp negative time; the floor-division remainder math below already yields a valid phase.

        double div = t / segment;
        if (!IsFinite(div)) { return; }
        // Avoid casting NaN/Infinity to long (would throw and potentially stop the behaviour).
        long segmentIndex = (long)System.Math.Floor(div);
        float tInSegment = (float)(t - ((double)segmentIndex * segment));

        bool toEnd = ((segmentIndex & 1L) == 1L);

        bool isWaiting = tInSegment < wait;
        if (_lastIsWaitingValid)
        {
            if (_lastIsWaiting && !isWaiting)
            {
                _TryPlayMoveAudio();
            }
        }
        _lastIsWaitingValid = true;
        _lastIsWaiting = isWaiting;

        // Each segment: first wait, then travel.
        if (isWaiting)
        {
            Vector3 targetLocal = toEnd ? m_StartPosition : m_EndPosition;
            _ApplyTargetLocalPosition(targetLocal);
            return;
        }

        float u = (tInSegment - wait) / travel;
        float eased = m_SmoothTravel ? Mathf.SmoothStep(0f, 1f, u) : u;

        Vector3 target = toEnd
            ? Vector3.Lerp(m_StartPosition, m_EndPosition, eased)
            : Vector3.Lerp(m_EndPosition, m_StartPosition, eased);

        _ApplyTargetLocalPosition(target);
    }

    private void _ApplyTargetLocalPosition(Vector3 targetLocalPosition)
    {
        // If a Rigidbody is present, move through physics for stable contacts.
        if (this.m_Rigidbody != null)
        {
            Transform parent = this.transform.parent;
            Vector3 targetWorld = (parent != null)
                ? parent.TransformPoint(targetLocalPosition)
                : targetLocalPosition;

            this.m_Rigidbody.MovePosition(targetWorld);
            return;
        }

        // Fallback (no Rigidbody): original behaviour.
        this.transform.localPosition = targetLocalPosition;
    }
}
