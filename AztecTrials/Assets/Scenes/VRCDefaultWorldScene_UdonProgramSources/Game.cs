using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using Miner28.UdonUtils.Network;

public class Game : NetworkInterface
{
    private VRCPlayerApi[] m_PlayerBuffer = new VRCPlayerApi[80];

    void Start()
    {
        // Apply all-talk settings shortly after world start.
        SendCustomEventDelayedSeconds(nameof(this._ApplyAllTalkToAllPlayers), 0.5f);
    }

    public void EnableAllTalkForPlayer(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.IsValid()) return;

        // Large-world settings: keep falloff from starting until extremely far away.
        // (Near should be less than Far; using values consistent with other scripts in the project.)
        const float nearRadius = 999999f;
        const float farRadius = 1000000f;
        const float voiceGain = 15f;
        const float voiceVolumetricRadius = 25f;
        const bool voiceDisableLowpass = false;

        player.SetVoiceDistanceNear(nearRadius);
        player.SetVoiceDistanceFar(farRadius);
        player.SetVoiceGain(voiceGain);
        player.SetVoiceVolumetricRadius(voiceVolumetricRadius);
        player.SetVoiceLowpass(!voiceDisableLowpass);
    }

    public void _ApplyAllTalkToAllPlayers()
    {
        VRCPlayerApi.GetPlayers(m_PlayerBuffer);
        int playerCount = VRCPlayerApi.GetPlayerCount();
        for (int i = 0; i < playerCount; i++)
        {
            var p = m_PlayerBuffer[i];
            if (p == null) continue;
            if (!p.IsValid()) continue;
            this.EnableAllTalkForPlayer(p);
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.IsValid()) return;

        this.EnableAllTalkForPlayer(player);

        base.OnPlayerJoined(player);
    }

    public override void OnPlayerSuspendChanged(VRCPlayerApi player)
    {
        if (player == null) return;
        if (!player.IsValid()) return;
        if (player.isSuspended) return;

        base.OnPlayerSuspendChanged(player);
    }

    public override void OnMasterTransferred(VRCPlayerApi newMaster)
    {
        if (newMaster == null) return;
        if (!newMaster.IsValid()) return;

        base.OnMasterTransferred(newMaster);
    }
}
