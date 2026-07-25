
using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;
using VRC.SDKBase;
using VRC.Udon;

public class invisible_wall : UdonSharpBehaviour
{
    [Header("Refs")]
    public GameObject m_ToggleObject;

    [Tooltip("Only toggles the object if ALL of these checkpoints are unlocked.")]
    public CheckpointUnlockTrigger[] m_Checkpoints;

    [Tooltip("Only these vehicles will trigger the toggle.")]
    public SaccEntity[] m_SaccEntities;

    private VRCPlayerApi _localPlayer;

    private void OnTriggerEnter(Collider other)
    {
        if (m_ToggleObject == null) return;
        if (other == null) return;

        if (_localPlayer == null) _localPlayer = Networking.LocalPlayer;
        if (_localPlayer == null) return;

        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) return;

        if (!_IsAllowedEntity(entity)) return;

        if (!_AreAllCheckpointsUnlocked()) return;

        // Only toggle if the local player is the driver or passenger of THIS vehicle.
        // These SAV flags are local-only.
        if (!entity.InVehicle && !entity.Using && !entity.Passenger && !entity.Piloting)
        {
            return;
        }

        if (!m_ToggleObject.activeSelf)
        {
            m_ToggleObject.SetActive(true);
        }
    }

    private bool _AreAllCheckpointsUnlocked()
    {
        if (m_Checkpoints == null || m_Checkpoints.Length == 0) return false;

        int len = m_Checkpoints.Length;
        for (int i = 0; i < len; i++)
        {
            CheckpointUnlockTrigger cp = m_Checkpoints[i];
            if (cp == null) return false;
            if (!cp._unlocked) return false;
        }
        return true;
    }

    private bool _IsAllowedEntity(SaccEntity entity)
    {
        if (entity == null) return false;
        if (m_SaccEntities == null || m_SaccEntities.Length == 0) return false;

        int len = m_SaccEntities.Length;
        for (int i = 0; i < len; i++)
        {
            SaccEntity e = m_SaccEntities[i];
            if (e == null) continue;
            if (e == entity) return true;
        }
        return false;
    }
}
