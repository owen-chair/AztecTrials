
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class CheckpointTeleportBtn : UdonSharpBehaviour
{
    [Tooltip("Where to teleport the local player when pressed.")]
    public Transform m_Target;

    [Tooltip("Optional: world/root transform to reposition to the checkpoint target.")]
    public Transform m_VRCWorld;

    public OcclusionManager m_OcclusionManager;
    private VRCPlayerApi _localPlayer;

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;
    }

    public override void Interact()
    {
        if (m_Target == null) { return; }

        Vector3 targetPos = m_Target.position;
        Quaternion targetRot = m_Target.rotation;

        if (_localPlayer == null)
        {
            _localPlayer = Networking.LocalPlayer;
        }

        if (this.m_OcclusionManager != null)
        {
            this.m_OcclusionManager._TickInternal();
        }

        if (_localPlayer == null) { return; }

        if (m_VRCWorld != null)
        {
            m_VRCWorld.position = targetPos;
            m_VRCWorld.rotation = targetRot;
        }

        _localPlayer.TeleportTo(targetPos, targetRot);
    }
}
