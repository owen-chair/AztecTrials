
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class PyramidRespawnBtn : UdonSharpBehaviour
{
    [Tooltip("Where to teleport the local player when pressed.")]
    public Transform m_Target;

    [Tooltip("Optional: occlusion manager tick before teleport.")]
    public OcclusionManager m_OcclusionManager;

    private VRCPlayerApi _localPlayer;

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;
    }

    public override void Interact()
    {
        if (m_Target == null) return;

        Vector3 targetPos = m_Target.position;
        Quaternion targetRot = m_Target.rotation;

        if (_localPlayer == null)
        {
            _localPlayer = Networking.LocalPlayer;
        }

        if (m_OcclusionManager != null)
        {
            m_OcclusionManager._TickInternal();
        }

        if (_localPlayer == null) return;

        // Unlike CheckpointTeleportBtn, do NOT move/reposition the world/root.
        _localPlayer.TeleportTo(targetPos, targetRot);
    }
}
