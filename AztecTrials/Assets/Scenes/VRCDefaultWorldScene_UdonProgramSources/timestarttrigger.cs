
using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;
using VRC.SDKBase;
using VRC.Udon;

public class timestarttrigger : UdonSharpBehaviour
{
    [Header("Output")]
    [Tooltip("Latched start time (Networking.GetServerTimeInSeconds) when the local user (pilot or passenger) enters with a ground vehicle.")]
    public float m_StartTime = -1f;

    [Tooltip("True once a valid start time has been latched. This avoids relying on the sign of server time.")]
    public bool m_HasStarted;

    [Header("Debug")]
    [Tooltip("If true, writes Debug.Log messages explaining why the trigger did/didn't latch.")]
    public bool m_DebugLogs;

    [System.NonSerialized] private bool _triggered;
    [System.NonSerialized] private Collider _collider;

    private void Awake()
    {
        // Do NOT reset here: OcclusionManager may enable/disable this object.
        // Only cache refs and restore latched state.
        _collider = (Collider)GetComponent<Collider>();
        _triggered = m_HasStarted;

        if (m_DebugLogs)
        {
            Debug.Log("[timestarttrigger] Awake. HasStarted=" + m_HasStarted + " StartTime=" + m_StartTime);
        }
    }

    private void OnEnable()
    {
        // Do NOT reset when re-enabled (OcclusionManager may toggle this object).
        if (_collider == null) _collider = (Collider)GetComponent<Collider>();
        if (!_triggered && m_HasStarted) _triggered = true;

        // If we already triggered, keep the collider disabled even if something re-enabled it.
        if (_triggered && _collider != null) _collider.enabled = false;

        if (m_DebugLogs)
        {
            Debug.Log("[timestarttrigger] OnEnable. Triggered=" + _triggered + " ColliderEnabled=" + (_collider != null && _collider.enabled));
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other == null) return;

        if (m_DebugLogs)
        {
            Debug.Log("[timestarttrigger] OnTriggerEnter hit: " + other.gameObject.name);
        }

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null)
        {
            if (m_DebugLogs) Debug.Log("[timestarttrigger] No SaccEntity found in parent chain.");
            return;
        }

        // Requirement: must be a SaccEntity ground vehicle.
        SaccGroundVehicle sgv = entity.GetComponent<SaccGroundVehicle>();
        if (sgv == null) sgv = entity.GetComponentInChildren<SaccGroundVehicle>();
        if (sgv == null)
        {
            if (m_DebugLogs) Debug.Log("[timestarttrigger] Not a SaccGroundVehicle.");
            return;
        }

        // Requirement: local player is driver or passenger.
        if (!(entity.Piloting || entity.Passenger))
        {
            if (m_DebugLogs) Debug.Log("[timestarttrigger] Vehicle found but local player is not Piloting/Passenger.");
            return;
        }

        // Use server time so all clients measure consistently.
        m_StartTime = (float)Networking.GetServerTimeInSeconds();
        m_HasStarted = true;
        _triggered = true;

        if (m_DebugLogs)
        {
            Debug.Log("[timestarttrigger] START latched. ServerTime=" + m_StartTime);
        }

        // Disable the (box) collider so no more trigger events happen.
        if (_collider != null) _collider.enabled = false;
    }
}
