
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using SaccFlightAndVehicles;

public class VehicleRespawnBtn : UdonSharpBehaviour
{
    [Header("Respawn Points")]
    [Tooltip("A random unoccupied point will be chosen per vehicle.")]
    public Transform[] m_RespawnPoints;

    [Header("Vehicles")]
    [Tooltip("Vehicles that this button respawns.")]
    public SaccEntity[] m_Vehicles;

    [Header("Occupancy Check")]
    [Tooltip("How close another SaccEntity must be to count a respawn point as occupied.")]
    public float m_OccupiedCheckRadius = 5f;

    [Tooltip("Layers to consider when checking whether a respawn point is occupied.")]
    public LayerMask m_OccupiedCheckLayers = ~0;

    [Header("Behavior")]
    [Tooltip("Seconds to ignore repeated presses.")]
    public float m_ButtonCooldownSeconds = 0.5f;

    private float _lastPressTime;
    private VRCPlayerApi _localPlayer;

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;
    }

    public override void Interact()
    {
        if (_localPlayer == null) { _localPlayer = Networking.LocalPlayer; }
        if (_localPlayer == null) { return; }

        if (Time.time - _lastPressTime < m_ButtonCooldownSeconds) { return; }
        _lastPressTime = Time.time;

        if (m_RespawnPoints == null || m_RespawnPoints.Length == 0) { return; }
        if (m_Vehicles == null || m_Vehicles.Length == 0) { return; }

        for (int v = 0; v < m_Vehicles.Length; v++)
        {
            SaccEntity entity = m_Vehicles[v];
            if (entity == null) { continue; }

            // Match SAV's safety: don't respawn a vehicle that currently has a pilot.
            // Also avoids trying to take ownership from an active driver.
            if (entity.Occupied) { continue; }

            Transform chosen = _PickRespawnPoint(entity);
            if (chosen == null) { continue; }

            entity.RespawnPoint = chosen;

            // Ensure we own the vehicle before moving it.
            if (!Networking.IsOwner(entity.gameObject))
            {
                Networking.SetOwner(_localPlayer, entity.gameObject);
            }

            // Prefer SAV's ground vehicle respawn path when available.
            SaccGroundVehicle sgv = entity.GetComponent<SaccGroundVehicle>();
            if (sgv != null)
            {
                if (Networking.IsOwner(entity.gameObject) && !sgv.IsOwner)
                {
                    sgv.SFEXT_O_TakeOwnership();
                }
                sgv.SFEXT_G_RespawnButton();
                return;
            }

            // Generic fallback: teleport rigidbody + trigger extensions' respawn logic.
            Transform t = entity.transform;
            t.position = chosen.position;
            t.rotation = chosen.rotation;

            Rigidbody rb = entity.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    rb.Sleep();
                }

                rb.position = t.position;
                rb.rotation = t.rotation;
            }

            // Mirror SaccEntity.EntityRespawn(): tell extensions the respawn button was used.
            entity.SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SaccEntity.SendRespawn));

            // Successfully respawned one vehicle; stop.
            return;
        }
    }

    private Transform _PickRespawnPoint(SaccEntity ignoreEntity)
    {
        int len = m_RespawnPoints != null ? m_RespawnPoints.Length : 0;
        if (len <= 0) { return null; }

        bool[] tried = new bool[len];
        int triedCount = 0;
        Transform firstNonNull = null;

        while (triedCount < len)
        {
            int i = Random.Range(0, len);
            if (tried[i]) { continue; }
            tried[i] = true;
            triedCount++;

            Transform p = m_RespawnPoints[i];
            if (p == null) { continue; }
            if (firstNonNull == null) { firstNonNull = p; }

            if (_IsRespawnPointOccupied(p, ignoreEntity)) { continue; }
            return p;
        }

        // Fallback: first non-null point even if occupied.
        return firstNonNull;
    }

    private bool _IsRespawnPointOccupied(Transform point, SaccEntity ignoreEntity)
    {
        if (point == null) { return true; }
        float r = Mathf.Max(0.01f, m_OccupiedCheckRadius);

        Collider[] hits = Physics.OverlapSphere(point.position, r, m_OccupiedCheckLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) { return false; }

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null) { continue; }

            SaccEntity e = c.GetComponentInParent<SaccEntity>();
            if (e != null && e != ignoreEntity)
            {
                return true;
            }
        }

        return false;
    }
}
