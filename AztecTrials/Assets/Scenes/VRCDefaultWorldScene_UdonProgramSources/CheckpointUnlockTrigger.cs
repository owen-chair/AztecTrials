
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using SaccFlightAndVehicles;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

public class CheckpointUnlockTrigger : UdonSharpBehaviour
{
    [Header("World Reposition")]
    public Transform m_VRCWorld;
    public Transform m_PlayerRepsawnPoint;

    [Header("Vehicles")]
    [Tooltip("Vehicles that can unlock this checkpoint. If empty, any SaccEntity can unlock it.")]
    public SaccEntity[] m_Vehicles;

    [Header("Dependency")]
    [Tooltip("If set, this checkpoint can only unlock once the previous checkpoint's _unlocked is true.")]
    public CheckpointUnlockTrigger m_PreviousCheckpoint;

    [Header("Unlock")]
    [Tooltip("Enabled when this checkpoint is unlocked.")]
    public GameObject m_CheckpointBtn;

    [Tooltip("Enabled when this checkpoint is unlocked (optional).")]
    public GameObject m_NextVehicleRespawnBtn;

    [Header("Server Metrics (optional)")]
    [Tooltip("Full checkpointUnlock URL (set per-checkpoint in inspector), e.g. https://host/metrics/checkpointUnlock/SpinningRoomCheckpointUnlocked/{base64_json}")]
    [SerializeField]
    private VRCUrl m_CheckpointUnlockUrl;

    public bool _unlocked;
    private VRCPlayerApi _localPlayer;

    private bool _metricRequestScheduled;

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;
        // Don't force-enable/disable m_CheckpointBtn here; leave initial state up to the scene.
    }

    public void OnTriggerEnter(Collider other)
    {
        if (_unlocked) { return; }
        if (other == null) { return; }

        if (!_IsPreviousCheckpointUnlocked()) { return; }

        if (_localPlayer == null) { _localPlayer = Networking.LocalPlayer; }
        if (_localPlayer == null) { return; }

        // Colliders belong to the vehicle; the parent/root has SaccEntity.
        SaccEntity entity = other.GetComponentInParent<SaccEntity>();
        if (entity == null) { return; }

        if (!_IsVehicleAllowed(entity)) { return; }

        // Only unlock if the local player is currently in THIS vehicle (pilot or passenger).
        // These flags are local-only per SAV.
        if (!entity.InVehicle && !entity.Using && !entity.Passenger && !entity.Piloting)
        {
            return;
        }

        _unlocked = true;

        if (m_VRCWorld != null && m_PlayerRepsawnPoint != null)
        {
            m_VRCWorld.position = m_PlayerRepsawnPoint.position;
            m_VRCWorld.rotation = m_PlayerRepsawnPoint.rotation;
        }

        if (m_CheckpointBtn != null)
        {
            m_CheckpointBtn.SetActive(true);
        }

        if (m_NextVehicleRespawnBtn != null)
        {
            m_NextVehicleRespawnBtn.SetActive(true);
        }

        // Disable this trigger after successful unlock.
        Collider triggerCol = (Collider)GetComponent(typeof(Collider));
        if (triggerCol != null)
        {
            triggerCol.enabled = false;
        }
        enabled = false;

        // Fire-and-forget metrics request AFTER this trigger finishes.
        // Must be best-effort and must not affect unlock behavior.
        if (!_metricRequestScheduled)
        {
            _metricRequestScheduled = true;
            SendCustomEventDelayedFrames(nameof(_SendCheckpointUnlockMetric), 1);
        }
    }

    public void _SendCheckpointUnlockMetric()
    {
        // Fail silently: no logs, no UI, no errors.
        string urlString = m_CheckpointUnlockUrl.Get();
        if (string.IsNullOrEmpty(urlString)) return;
        string lower = urlString.ToLower();
        if (!lower.StartsWith("https://")) return;

        VRCStringDownloader.LoadUrl(m_CheckpointUnlockUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result) {}
    public override void OnStringLoadError(IVRCStringDownload result) {}

    private bool _IsPreviousCheckpointUnlocked()
    {
        if (m_PreviousCheckpoint == null) { return true; }

        return m_PreviousCheckpoint._unlocked;
    }

    private bool _IsVehicleAllowed(SaccEntity entity)
    {
        if (entity == null) { return false; }
        if (m_Vehicles == null || m_Vehicles.Length == 0) { return true; }

        for (int i = 0; i < m_Vehicles.Length; i++)
        {
            if (m_Vehicles[i] == entity) { return true; }
        }
        return false;
    }
}
