
using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;
using VRC.Udon;

public class AnubisStatue : UdonSharpBehaviour
{
    [Header("Refs")]
    [Tooltip("Statue transform to rotate. If null, uses this transform.")]
    public Transform m_Statue;

    [Tooltip("Plate center transform (empty transform is fine).")]
    public Transform m_Plate;

    [Header("Puzzle")]
    [Tooltip("Eye GameObject (used to lock the statue once the puzzle completes).")]
    public GameObject m_Eye;

    [Tooltip("Bloom GameObject (used to lock the statue once the puzzle completes).")]
    public GameObject m_Bloom;

    [Header("Plate")]
    [Min(0.01f)]
    public float m_PlateRadius = 1f;

    [Header("Rotation")]
    [Tooltip("Target rotation amount around local Y (degrees).")]
    public float m_RotationAmountYDegrees = 90f;

    [Min(0.01f)]
    [Tooltip("Rotation speed when a vehicle is on the plate (degrees/second).")]
    public float m_DegreesPerSecondOnPlate = 90f;

    [Min(0.01f)]
    [Tooltip("Rotation speed when no vehicle is on the plate (degrees/second).")]
    public float m_DegreesPerSecondOffPlate = 90f;

    [Header("Looking At Eye")]
    [Tooltip("Reference to the eyeofhorusd controller that computes which statue is aligned.")]
    public eyeofhorusd m_EyeOfHorus;

    [Tooltip("If true, uses eyeofhorusd.m_IsStatue1LookingAtEye; otherwise uses m_IsStatue2LookingAtEye.")]
    public bool m_UseStatue1LookingAtEye = true;

    [Min(0.01f)]
    [Tooltip("Rotation speed (degrees/second) while the statue is looking at the eye (within tolerance).")]
    public float m_DegreesPerSecondLookingAtEye = 30f;

    [Header("Vehicles")]
    [Tooltip("Vehicles to check. If any vehicle is within radius of the plate, the statue rotates to the target.")]
    public SaccEntity[] m_Vehicles;

    [Header("FX")]
    [Tooltip("Optional. Disabled by default. Enabled while the statue is moving back to origin (off plate, not yet at 0).")]
    public GameObject m_LaserEyes;

    [Tooltip("Optional. Looping sound that plays while the statue is rotating. Will be stopped and disabled when not rotating or when the puzzle completes.")]
    public AudioSource m_StoneGrindingSound;

    [Min(0f)]
    [Tooltip("While returning to origin, laser turns off when within this many degrees of the origin.")]
    public float m_LaserOffWithinDegrees = 5f;

    [System.NonSerialized] private bool _baselineCached;
    [System.NonSerialized] private Quaternion _baselineLocalRotation;
    [System.NonSerialized] private Vector3 _baselineLocalEuler;
    [System.NonSerialized] private long _lastTick;
    [System.NonSerialized] private bool _hasLastTick;
    [System.NonSerialized] private float _currentY;
    [System.NonSerialized] private bool _laserStateCached;
    [System.NonSerialized] private bool _laserActive;

    [System.NonSerialized] private bool _stoneSoundPlaying;
    [System.NonSerialized] private bool _wasRotating;

    [System.NonSerialized] private bool _prevEyeBloomBothActive;
    [System.NonSerialized] private bool m_StatuePuzzleComplete;

    // Keep this aligned with typical Udon time-locked motion.
    private const float kTicksPerSecond = 30f;

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

    private static float WrapDegrees(float degrees)
    {
        // Normalize into [-180, 180] so 270 becomes -90 (Unity-style).
        if (!IsFinite(degrees)) return 0f;
        if (degrees > 180f) degrees -= 360f;
        else if (degrees < -180f) degrees += 360f;
        // If someone enters e.g. 900, the single subtraction isn't enough; loop cheaply.
        while (degrees > 180f) degrees -= 360f;
        while (degrees < -180f) degrees += 360f;
        return degrees;
    }

    private Transform _GetStatue()
    {
        return (m_Statue != null) ? m_Statue : transform;
    }

    private void _EnsureBaselineCached()
    {
        if (_baselineCached) return;
        Transform statue = _GetStatue();
        _baselineLocalRotation = (statue != null) ? statue.localRotation : Quaternion.identity;
        _baselineLocalEuler = _baselineLocalRotation.eulerAngles;
        _baselineCached = true;
    }

    private bool _IsAnyVehicleOnPlate()
    {
        if (m_Plate == null) return false;
        if (m_Vehicles == null || m_Vehicles.Length == 0) return false;

        float r = m_PlateRadius;
        if (!IsFinite(r) || r <= 0f) return false;

        Vector3 platePos = m_Plate.position;
        float r2 = r * r;

        int len = m_Vehicles.Length;
        for (int i = 0; i < len; i++)
        {
            SaccEntity ent = m_Vehicles[i];
            if (ent == null) continue;

            Vector3 d = ent.transform.position - platePos;
            float d2 = (d.x * d.x) + (d.y * d.y) + (d.z * d.z);
            if (d2 < r2) return true;
        }

        return false;
    }

    private void Awake()
    {
        _EnsureBaselineCached();
        _hasLastTick = false;
        _currentY = 0f;

        _prevEyeBloomBothActive = false;
        m_StatuePuzzleComplete = false;

        _laserStateCached = false;
        _SetLaserActive(false);

        _stoneSoundPlaying = false;
        _wasRotating = false;
        _SetStoneGrindingPlaying(false);
    }

    private void OnEnable()
    {
        // Keep baseline stable across enable/disable.
        _EnsureBaselineCached();
        _hasLastTick = false;

        _prevEyeBloomBothActive = false;
        m_StatuePuzzleComplete = false;

        _laserStateCached = false;
        _SetLaserActive(false);

        _stoneSoundPlaying = false;
        _wasRotating = false;
        _SetStoneGrindingPlaying(false);
    }

    private void _SetStoneGrindingPlaying(bool shouldPlay)
    {
        if (m_StoneGrindingSound == null) return;
        if (_stoneSoundPlaying == shouldPlay) return;
        _stoneSoundPlaying = shouldPlay;

        if (shouldPlay)
        {
            // Looping clip: only start once when motion begins.
            if (!m_StoneGrindingSound.isPlaying) m_StoneGrindingSound.Play();
        }
        else
        {
            // Stop once when motion ends.
            if (m_StoneGrindingSound.isPlaying) m_StoneGrindingSound.Stop();
        }
    }

    private void _DisableStoneGrindingOnPuzzleComplete()
    {
        if (m_StoneGrindingSound == null) return;
        if (m_StoneGrindingSound.isPlaying) m_StoneGrindingSound.Stop();
        GameObject go = m_StoneGrindingSound.gameObject;
        if (go != null && go.activeSelf) go.SetActive(false);
    }

    private void _SetLaserActive(bool active)
    {
        if (m_LaserEyes == null) return;
        if (_laserStateCached && _laserActive == active) return;
        _laserStateCached = true;
        _laserActive = active;
        m_LaserEyes.SetActive(active);
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy) return;

        // If Eye + Bloom become active together, latch puzzle completion.
        bool eyeActive = (m_Eye != null) && m_Eye.activeInHierarchy;
        bool bloomActive = (m_Bloom != null) && m_Bloom.activeInHierarchy;
        bool bothActiveNow = eyeActive && bloomActive;
        if (!m_StatuePuzzleComplete && bothActiveNow && !_prevEyeBloomBothActive)
        {
            m_StatuePuzzleComplete = true;
            _SetLaserActive(false);
            _DisableStoneGrindingOnPuzzleComplete();
        }
        _prevEyeBloomBothActive = bothActiveNow;

        // Once complete, stop rotating forever.
        if (m_StatuePuzzleComplete)
        {
            _DisableStoneGrindingOnPuzzleComplete();
            return;
        }

        _EnsureBaselineCached();

        Transform statue = _GetStatue();
        if (statue == null) return;
        if (m_Plate == null) return;

        // Local time is sufficient here because the "on plate" condition is derived
        // from local observations of vehicle positions (not an authoritative networked flag).
        double localTime = (double)Time.time;
        if (!IsFinite(localTime)) return;

        double tickExact = localTime * (double)kTicksPerSecond;
        if (!IsFinite(tickExact)) return;

        long tickNow = FloorToLong(tickExact);
        if (_hasLastTick && tickNow == _lastTick) return;

        long deltaTicks = _hasLastTick ? (tickNow - _lastTick) : 1;
        if (deltaTicks < 1) deltaTicks = 1;
        if (deltaTicks > 300) deltaTicks = 300; // safety clamp for long stalls

        _hasLastTick = true;
        _lastTick = tickNow;

        bool onPlate = _IsAnyVehicleOnPlate();

        float rotAmount = WrapDegrees(m_RotationAmountYDegrees);

        // Support negative rotation amounts (rotate left) by clamping within the signed range.
        float minY = Mathf.Min(0f, rotAmount);
        float maxY = Mathf.Max(0f, rotAmount);

        float target = onPlate ? rotAmount : 0f;
        target = Mathf.Clamp(target, minY, maxY);

        // Keep current within range in case values changed in the inspector.
        _currentY = Mathf.Clamp(_currentY, minY, maxY);

        // Third speed mode: when eyeofhorusd reports this statue is aligned.
        bool lookingAtEye = false;
        if (m_EyeOfHorus != null)
        {
            lookingAtEye = m_UseStatue1LookingAtEye ? m_EyeOfHorus.m_IsStatue1LookingAtEye : m_EyeOfHorus.m_IsStatue2LookingAtEye;
        }

        float speedDegPerSec = onPlate ? m_DegreesPerSecondOnPlate : m_DegreesPerSecondOffPlate;
        if (!onPlate && lookingAtEye)
        {
            float eyeSpeed = m_DegreesPerSecondLookingAtEye;
            if (IsFinite(eyeSpeed) && eyeSpeed > 0f) speedDegPerSec = eyeSpeed;
        }
        if (!IsFinite(speedDegPerSec) || speedDegPerSec <= 0f) speedDegPerSec = 90f;

        float dt = (float)((double)deltaTicks / (double)kTicksPerSecond);
        float maxStep = speedDegPerSec * dt;
        if (!IsFinite(maxStep) || maxStep < 0f) maxStep = 0f;

        float newY = Mathf.MoveTowards(_currentY, target, maxStep);

        float offWithin = m_LaserOffWithinDegrees;
        if (!IsFinite(offWithin) || offWithin < 0f) offWithin = 0f;

        // Laser eyes ON only while returning AND not yet close enough to origin.
        // Use post-step value (newY) so the laser turns off promptly as we approach origin.
        bool laserOn = (!onPlate) && (Mathf.Abs(newY) > offWithin);
        _SetLaserActive(laserOn);

        bool isRotating = newY != _currentY;
        if (isRotating != _wasRotating)
        {
            _wasRotating = isRotating;
            _SetStoneGrindingPlaying(isRotating);
        }

        if (!isRotating) return;

        _currentY = newY;
        // Apply as additive yaw over the cached baseline euler, preserving baseline X/Z.
        statue.localRotation = Quaternion.Euler(_baselineLocalEuler.x, _baselineLocalEuler.y + _currentY, _baselineLocalEuler.z);
    }
}
