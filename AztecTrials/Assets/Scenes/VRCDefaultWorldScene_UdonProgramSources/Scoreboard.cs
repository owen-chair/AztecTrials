using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Miner28.UdonUtils.Network;

public class Scoreboard : NetworkInterface
{
    [Header("Refs")]
    [Tooltip("Maximum players to scan when building the list.")]
    public int m_MaxPlayersToList = 80;

    public TMPro.TMP_Text m_PlayerListText;
    private string m_PlayerListTextString = "";

    [Header("Visibility")]
    [Tooltip("Object to hide/show (defaults to m_PlayerListText GameObject if null).")]
    public GameObject m_RootToHide;

    [Tooltip("Only show this scoreboard when the local player is within this many meters. Set to 0 to disable distance culling.")]
    public float m_MaxDistance = 0f;

    [Header("Update")]
    [Tooltip("Seconds between visibility checks.")]
    public float m_UpdateIntervalSeconds = 0.25f;

    private bool m_IsHidden;

    private string m_LastPlayerListSignature;
    private bool m_ForceRedraw;

    void OnEnable()
    {
        if (this.m_RootToHide == null)
        {
            // Default: hide a child object (keeps this behaviour enabled so it can re-show).
            if (this.m_PlayerListText != null) this.m_RootToHide = this.m_PlayerListText.gameObject;
            else this.m_RootToHide = this.gameObject;
        }

        GameObject hideObj;
        if (this.m_RootToHide != null && this.m_RootToHide != this.gameObject) hideObj = this.m_RootToHide;
        else if (this.m_PlayerListText != null) hideObj = this.m_PlayerListText.gameObject;
        else hideObj = null;

        this.m_IsHidden = (hideObj != null) && !hideObj.activeSelf;

        this.m_ForceRedraw = true;
        this.m_LastPlayerListSignature = null;

        float dt = this.m_UpdateIntervalSeconds;
        if (dt <= 0f) dt = 0.25f;
        SendCustomEventDelayedSeconds(nameof(this._Tick), dt);
    }

    public void _Tick()
    {
        if (!this.gameObject.activeInHierarchy) return;
        float dt = this.m_UpdateIntervalSeconds;
        if (dt <= 0f) dt = 0.25f;
        SendCustomEventDelayedSeconds(nameof(this._Tick), dt);

        var localPlayer = Networking.LocalPlayer;
        if (localPlayer == null) return;
        if (!localPlayer.IsValid()) return;
        if (localPlayer.isSuspended) return;

        // Distance-gate visibility.
        bool withinDistance = true;
        if (this.m_MaxDistance > 0f)
        {
            Vector3 a = localPlayer.GetPosition();
            withinDistance = Vector3.Distance(a, this.transform.position) <= this.m_MaxDistance;
        }

        // Visibility: distance gate only.
        bool shouldShow = withinDistance;

        GameObject hideObj;
        if (this.m_RootToHide != null && this.m_RootToHide != this.gameObject) hideObj = this.m_RootToHide;
        else if (this.m_PlayerListText != null) hideObj = this.m_PlayerListText.gameObject;
        else hideObj = null;

        if (hideObj != null)
        {
            if (shouldShow)
            {
                if (!hideObj.activeSelf)
                {
                    hideObj.SetActive(true);
                    this.m_IsHidden = false;
                    this.m_ForceRedraw = true;
                }
                else
                {
                    this.m_IsHidden = false;
                }
            }
            else
            {
                if (hideObj.activeSelf)
                {
                    hideObj.SetActive(false);
                }
                this.m_IsHidden = true;
                return;
            }
        }
        else
        {
            if (!shouldShow) return;
        }

        // Only rebuild UI text when player list actually changed (or when forced).
        this._RefreshIfPlayersChanged();
    }

    private void Redraw()
    {
        if (this.m_PlayerListText == null) return;

        this.m_PlayerListTextString = "";

        VRCPlayerApi[] players = this._GetPlayersSnapshot();
        this._AppendPlayersFromArray(players);

        this.m_PlayerListText.text = this.m_PlayerListTextString;
    }

    private void _RefreshIfPlayersChanged()
    {
        if (this.m_PlayerListText == null) return;

        string signature = this._ComputePlayerListSignature();
        if (!this.m_ForceRedraw && signature == this.m_LastPlayerListSignature) return;

        this.m_ForceRedraw = false;
        this.m_LastPlayerListSignature = signature;
        this.Redraw();
    }

    private string _ComputePlayerListSignature()
    {
        VRCPlayerApi[] players = this._GetPlayersSnapshot();
        string sig = "P:";
        for (int i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (player == null) continue;
            if (!player.IsValid()) continue;
            if (player.isSuspended) continue;
            if (string.IsNullOrEmpty(player.displayName)) continue;
            sig += this._PlayerToUniqueKey(player) + "|";
        }
        return sig;
    }

    private void _AppendPlayersFromArray(VRCPlayerApi[] players)
    {
        if (players == null) return;

        for (int i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (player == null) continue;
            if (!player.IsValid()) continue;
            if (player.isSuspended) continue;
            if (string.IsNullOrEmpty(player.displayName)) continue;

            this.m_PlayerListTextString += player.displayName + "\n";
        }
    }

    private VRCPlayerApi[] _GetPlayersSnapshot()
    {
        int max = this.m_MaxPlayersToList;
        if (max <= 0) max = 80;
        if (max > 100) max = 100;

        VRCPlayerApi[] players = new VRCPlayerApi[max];
        VRCPlayerApi.GetPlayers(players);
        return players;
    }

    private string _PlayerToUniqueKey(VRCPlayerApi player)
    {
        if (player == null) return null;
        if (string.IsNullOrEmpty(player.displayName)) return null;
        return player.displayName + "#" + player.playerId.ToString();
    }
}
