using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using SaccFlightAndVehicles;

public class FailureTrigger : UdonSharpBehaviour
{
    [Header("Respawn Points")]
    [Tooltip("4 respawn points (or more). A random one will be chosen each time.")]
    public Transform[] m_RespawnPoints;

    [Header("Behavior")]
    [Tooltip("If true, only the current owner of the vehicle will perform the teleport. Prevents non-owners from fighting the owner's simulation.")]
    public bool m_OnlyOwnerTeleports = true;

    [Tooltip("Seconds to ignore repeated triggers (per trigger volume).")]
    public float m_CooldownSeconds = 0.5f;

    [Tooltip("How close another SaccEntity must be to count a respawn point as occupied.")]
    public float m_OccupiedCheckRadius = 5f;

    [Tooltip("Layers to consider when checking whether a respawn point is occupied.")]
    public LayerMask m_OccupiedCheckLayers = ~0;

    private float _lastTriggerTime;

    public void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (m_RespawnPoints == null || m_RespawnPoints.Length == 0) return;
        if (Time.time - _lastTriggerTime < m_CooldownSeconds) return;

        // Per your setup: colliders belong to the vehicle, and the parent/root has SaccEntity.
        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) return;

        if (m_OnlyOwnerTeleports && !Networking.IsOwner(entity.gameObject)) return;

        Transform chosen = _PickRespawnPoint(entity);
        if (chosen == null) return;

        _lastTriggerTime = Time.time;

        // Feed the vehicle's existing respawn system by setting RespawnPoint.
        entity.RespawnPoint = chosen;

        // Prefer vehicle-specific MoveToSpawn/SetRespawnPos to keep wheel/physics state sane.
        // This should also keep seated players in-place (the station/seat moves with the vehicle).
        SaccGroundVehicle sgv = entity.GetComponent<SaccGroundVehicle>();
        if (sgv != null)
        {
            // Use the built-in respawn button path.
            // It resets Fuel/Health, teleports via SetRespawnPos(), and forces serialization for sync.
            if (Networking.IsOwner(entity.gameObject) && !sgv.IsOwner)
            {
                // Ownership can change without this script knowing; ensure SGV enters owner sim mode.
                sgv.SFEXT_O_TakeOwnership();
            }
            sgv.SFEXT_G_RespawnButton();
            return;
        }

        // Fallback: direct teleport + velocity reset.
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
    }

    private Transform _PickRespawnPoint(SaccEntity ignoreEntity)
    {
        // Try to find a non-null, unoccupied point. If all are occupied, fall back to the first non-null.
        int len = m_RespawnPoints != null ? m_RespawnPoints.Length : 0;
        if (len <= 0) return null;

        bool[] tried = new bool[len];
        int triedCount = 0;
        Transform firstNonNull = null;

        while (triedCount < len)
        {
            int i = Random.Range(0, len);
            if (tried[i]) continue;
            tried[i] = true;
            triedCount++;

            Transform p = m_RespawnPoints[i];
            if (p == null) continue;
            if (firstNonNull == null) firstNonNull = p;

            if (!_IsRespawnPointOccupied(p, ignoreEntity))
                return p;
        }

        return firstNonNull;
    }

    private bool _IsRespawnPointOccupied(Transform point, SaccEntity ignoreEntity)
    {
        if (point == null) return true;
        float r = Mathf.Max(0.01f, m_OccupiedCheckRadius);

        Collider[] hits = Physics.OverlapSphere(point.position, r, m_OccupiedCheckLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null) continue;

            SaccEntity e = c.GetComponentInParent<SaccEntity>();
            if (e != null && e != ignoreEntity)
                return true;
        }

        return false;
    }
}
