
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using SaccFlightAndVehicles;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

public class GenericMetric : UdonSharpBehaviour
{
    [Header("Server Metrics (optional)")]
    [Tooltip("Full genericMetric URL (set in inspector), e.g. https://host/metrics/genericMetric/{base64_json} (with required 15th-char 'a' marker)")]
    [SerializeField]
    private VRCUrl m_GenericMetricUrl;

    [Header("Trigger")]
    [Tooltip("If true, sends only once per instance (per client)")]
    public bool m_SendOnlyOnce = true;

    [Tooltip("If OnTriggerExit never fires (vehicle despawn/respawn), re-arm after this many seconds since the last trigger event. Set <= 0 to disable.")]
    public float m_StuckInsideResetSeconds = 10f;

    private bool _metricRequestScheduled;
    private bool _metricSent;
    private bool _sentThisEntry;
    private int _insideCount;
    private float _lastTriggerEventTime;

    private VRCPlayerApi _localPlayer;

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;
    }

    private void OnDisable()
    {
        // If the object/trigger is disabled while "inside", make sure we don't get wedged.
        _insideCount = 0;
        _sentThisEntry = false;
        _metricRequestScheduled = false;
        _lastTriggerEventTime = 0f;
    }

    public void OnTriggerEnter(Collider other)
    {
        if (m_SendOnlyOnce && _metricSent) return;
        if (other == null) return;

        float now = Time.time;
        if (m_StuckInsideResetSeconds > 0f && _insideCount > 0 && _lastTriggerEventTime > 0f)
        {
            if ((now - _lastTriggerEventTime) > m_StuckInsideResetSeconds)
            {
                // Recover from missing exits (e.g., vehicle respawn/despawn inside trigger).
                _insideCount = 0;
                _sentThisEntry = false;
                _metricRequestScheduled = false;
            }
        }

        // Match CheckpointUnlockTrigger behavior: only trigger when the local player is
        // currently in the vehicle (pilot or passenger) that entered this trigger.
        if (_localPlayer == null) _localPlayer = Networking.LocalPlayer;
        if (_localPlayer == null) return;

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) return;

        // These flags are local-only per SAV.
        if (!entity.InVehicle && !entity.Using && !entity.Passenger && !entity.Piloting)
        {
            return;
        }

        _lastTriggerEventTime = now;

        _insideCount++;
        if (_insideCount < 1) _insideCount = 1;

        // Only send once per "entry"; re-arm once the player fully exits.
        if (_insideCount != 1) return;
        if (_sentThisEntry) return;
        if (_metricRequestScheduled) return;

        // Fire-and-forget metrics request AFTER this trigger finishes.
        // Must be best-effort and must not affect gameplay.
        _metricRequestScheduled = true;
        SendCustomEventDelayedFrames(nameof(_SendGenericMetric), 1);
    }

    public void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        // Only count exits for colliders that belong to a SAV vehicle.
        // This keeps the re-arm behavior aligned with the same vehicle-gated entry rule.
        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) return;

        _lastTriggerEventTime = Time.time;

        _insideCount--;
        if (_insideCount <= 0)
        {
            _insideCount = 0;
            _sentThisEntry = false;
        }
    }

    public void _SendGenericMetric()
    {
        _metricRequestScheduled = false;
        if (m_SendOnlyOnce && _metricSent) return;

        // If we've exited before the delayed send runs, skip.
        if (_insideCount <= 0) return;
        if (_sentThisEntry) return;

        // Fail silently: no logs, no UI, no errors.
        string urlString = m_GenericMetricUrl.Get();
        if (string.IsNullOrEmpty(urlString)) return;
        string lower = urlString.ToLower();
        if (!lower.StartsWith("https://")) return;

        // Match CheckpointUnlockTrigger behavior: request the inspector-provided URL as-is.
        VRCStringDownloader.LoadUrl(m_GenericMetricUrl, (IUdonEventReceiver)this);
        _metricSent = true;
        _sentThisEntry = true;
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result) { }
    public override void OnStringLoadError(IVRCStringDownload result) { }
}
