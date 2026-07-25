using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.UdonNetworkCalling;
using SaccFlightAndVehicles;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BreakableEntity : UdonSharpBehaviour
{
    [Header("Health")]
    public float MaxHealth = 100f;
    public float StartHealth = 100f;

    [Tooltip("Ignore particle weapon hits")]
    public bool DisableBulletHitEvent = false;

    [Header("Destruction Callbacks")]
    [Tooltip("Optional: UdonBehaviour to notify when this object is destroyed")]
    public UdonBehaviour OnDestroyedHandler;

    [Tooltip("Method name to call on OnDestroyedHandler")]
    public string OnDestroyedMethodName = "OnBreakableDestroyed";

    [Header("HUD")]
    [Tooltip("Optional: Renderer using HUDHealthBar.shader. BreakableEntity will set material _Percent (0-100) when health changes.")]
    public Renderer HealthBarRenderer;

    [Tooltip("Optional: Root object to show for RED team (attackers). If null, will auto-find child named 'AttackerHUDElement'.")]
    public GameObject AttackerHUDElement;

    [Tooltip("Optional: Root object to show for BLUE team (defenders). If null, will auto-find child named 'DefenderHUDElement'.")]
    public GameObject DefenderHUDElement;

    [UdonSynced(UdonSyncMode.None)] private bool _initialized;
    [UdonSynced(UdonSyncMode.None)] private float _health;
    [UdonSynced(UdonSyncMode.None)] private bool _destroyed;

    [System.NonSerialized] public VRCPlayerApi LastHitByPlayer;
    [System.NonSerialized] public float LastHitDamage;
    [System.NonSerialized] public byte LastHitWeaponType;
    [System.NonSerialized] public float LastDamageEventTime;

    private VRCPlayerApi _localPlayer;
    private Renderer[] _renderers;
    private Collider[] _colliders;
    private bool _localDestroyedApplied;

    private float _localHealthPercentApplied = -999f;
    private MaterialPropertyBlock _healthBarMpb;
    private const string _HealthBarPercentPropName = "_Percent";

    private int _localHudModeApplied = -999;

    private const float _DamageSendInterval = 0.10f;
    private float _lastDamageSentTime;
    private float _queuedDamage;
    private byte _queuedWeaponType;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);
    }

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;

        if (Networking.IsOwner(gameObject) && !_initialized)
        {
            _initialized = true;
            _destroyed = false;
            _health = _ClampHealth(StartHealth);
            RequestSerialization();
        }

        _ApplyStateLocal(force: true);
    }

    private void OnEnable()
    {
        if (_renderers == null || _colliders == null)
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        // Reset on enable ONLY for the current owner.
        // This avoids late joiners triggering resets (they are not the owner).
        if (Networking.IsOwner(gameObject))
        {
            NetReset();
            return;
        }

        _ApplyStateLocal(force: true);
    }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!this.gameObject.activeInHierarchy) return;
        if (player == null) return;
        if (!player.IsValid()) return;
        if (!player.isLocal) return;

        // Re-apply local-only HUD visibility.
        _ApplyHudVisibilityLocal(force: true);
    }

    public override void OnDeserialization()
    {
        _ApplyStateLocal(force: false);
    }

    public float GetHealth() => _health;
    public bool GetDestroyed() => _destroyed;

    public void NetReset()
    {
        if (!Networking.IsOwner(gameObject)) { return; }
        _initialized = true;
        _destroyed = false;
        _health = _ClampHealth(StartHealth);
        RequestSerialization();
        _ApplyStateLocal(force: true);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // Ownership transfer is common when a player leaves.
        // Ensure the new owner has a consistent authoritative state and re-serializes it so late joiners
        // don't get stuck with default values until the next damage tick.
        if (player != null && player.IsValid() && player.isLocal)
        {
            _localPlayer = Networking.LocalPlayer;

            if (!_initialized)
            {
                _initialized = true;
                _destroyed = false;
                _health = _ClampHealth(StartHealth);
            }

            // Clear any queued non-owner sends from before we became owner.
            _queuedDamage = 0f;
            _lastDamageSentTime = 0f;

            RequestSerialization();
        }

        _ApplyStateLocal(force: true);
    }

    private void OnParticleCollision(GameObject other)
    {
        if (!this.gameObject.activeInHierarchy) return;
        if (!other || _destroyed || DisableBulletHitEvent) { return; }

        byte weaponType = 1;
        float damage = 10f;

        foreach (Transform child in other.transform)
        {
            string pname = child.name;
            if (pname.StartsWith("d:"))
            {
                if (float.TryParse(pname.Substring(2), out float dmg)) { damage = dmg; }
            }
            else if (pname.StartsWith("t:"))
            {
                if (byte.TryParse(pname.Substring(2), out byte wt)) { weaponType = wt; }
            }
        }

        if (damage <= 0f) { return; }
        if (!_CanObjectDamageUs(other)) { return; }

        _HandleIncomingDamage(damage, weaponType);
    }

    private void _HandleIncomingDamage(float damage, byte weaponType)
    {
        if (_destroyed) { return; }

        // Owner is authoritative.
        if (Networking.IsOwner(gameObject))
        {
            _ApplyDamageAsOwner(damage, weaponType, _localPlayer);
        }
        else
        {
            // Notify the current owner via the established Sacc-style network-call pattern.
            _QueueDamageToOwner(damage, weaponType);
        }
    }

    private void _ApplyDamageAsOwner(float damage, byte weaponType, VRCPlayerApi attacker)
    {
        if (!Networking.IsOwner(gameObject)) { return; }
        if (_destroyed) { return; }

        LastHitByPlayer = attacker;
        LastHitDamage = damage;
        LastHitWeaponType = weaponType;
        LastDamageEventTime = Time.time;

        _health = _ClampHealth(_health - damage);
        if (_health <= 0f)
        {
            _health = 0f;
            _destroyed = true;
        }

        RequestSerialization();
        _ApplyStateLocal(force: false);
    }

    private void _QueueDamageToOwner(float dmg, byte weaponType)
    {
        _queuedWeaponType = weaponType;
        _queuedDamage += dmg;

        float now = Time.time;
        if (now - _lastDamageSentTime > _DamageSendInterval)
        {
            _lastDamageSentTime = now;
            // Send directly to the current owner (authoritative) rather than broadcasting to all others.
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(SendDamageEvent), _queuedDamage, _queuedWeaponType);
            _queuedDamage = 0f;
        }
        else
        {
            SendCustomEventDelayedSeconds(nameof(_SendQueuedDamage), _DamageSendInterval);
        }
    }

    public void _SendQueuedDamage()
    {
        float now = Time.time;
        if (now - _lastDamageSentTime <= _DamageSendInterval)
        {
            SendCustomEventDelayedSeconds(nameof(_SendQueuedDamage), _DamageSendInterval);
            return;
        }

        if (_queuedDamage > 0f)
        {
            _lastDamageSentTime = now;
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(SendDamageEvent), _queuedDamage, _queuedWeaponType);
            _queuedDamage = 0f;
        }
    }

    [NetworkCallable]
    public void SendDamageEvent(float dmg, byte weaponType)
    {
        if (_destroyed) { return; }
        if (!Networking.IsOwner(gameObject)) { return; }

        VRCPlayerApi attacker = NetworkCalling.CallingPlayer;

        _ApplyDamageAsOwner(dmg, weaponType, attacker);
    }

    private float _ClampHealth(float value)
    {
        float mh = (MaxHealth > 0f) ? MaxHealth : 0.000001f;
        return Mathf.Clamp(value, 0f, mh);
    }

    private void _ApplyStateLocal(bool force)
    {
        bool wantDestroyed = _destroyed;

        float wantHealthPercent = 0f;
        if (MaxHealth > 0f)
        {
            wantHealthPercent = Mathf.Clamp((_health / MaxHealth) * 100f, 0f, 100f);
        }

        int wantHudMode = _ComputeHudModeLocal(wantDestroyed);

        if (!force
            && wantDestroyed == _localDestroyedApplied
            && Mathf.Abs(wantHealthPercent - _localHealthPercentApplied) < 0.001f
            && wantHudMode == _localHudModeApplied)
        {
            return;
        }

        if (_renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i]) { _renderers[i].enabled = !wantDestroyed; }
            }
        }

        if (_colliders != null)
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i]) { _colliders[i].enabled = !wantDestroyed; }
            }
        }

        // Fire destroyed callback only on a local transition to destroyed.
        // Do NOT fire due to OnEnable/OnDeserialization simply re-applying state.
        if (wantDestroyed && !_localDestroyedApplied)
        {
            if (OnDestroyedHandler != null)
            {
                OnDestroyedHandler.SendCustomEvent(OnDestroyedMethodName);
            }
        }

        _ApplyHealthBarLocal(wantHealthPercent);

        _ApplyHudVisibilityLocal(wantHudMode);

        _localDestroyedApplied = wantDestroyed;
        _localHealthPercentApplied = wantHealthPercent;
        _localHudModeApplied = wantHudMode;
    }

    private int _ComputeHudModeLocal(bool wantDestroyed)
    {
        if (wantDestroyed) return 0;

        VRCPlayerApi lp = _localPlayer;
        if (lp == null) { lp = Networking.LocalPlayer; _localPlayer = lp; }
        if (lp == null || !lp.IsValid()) return 0;

        // No teams: show whichever HUD elements exist.
        bool hasAttacker = AttackerHUDElement != null;
        bool hasDefender = DefenderHUDElement != null;
        if (hasAttacker && !hasDefender) return 1;
        if (!hasAttacker && hasDefender) return 2;
        if (hasAttacker && hasDefender) return 3;
        return 0;
    }

    private void _ApplyHudVisibilityLocal(bool force)
    {
        int wantHudMode = _ComputeHudModeLocal(_destroyed);
        _ApplyHudVisibilityLocal(wantHudMode);
        _localHudModeApplied = wantHudMode;
    }

    private void _ApplyHudVisibilityLocal(int hudMode)
    {
        // hudMode: 0 = none, 1 = attacker, 2 = defender, 3 = both
        if (AttackerHUDElement != null)
        {
            AttackerHUDElement.SetActive(hudMode == 1 || hudMode == 3);
        }
        if (DefenderHUDElement != null)
        {
            DefenderHUDElement.SetActive(hudMode == 2 || hudMode == 3);
        }
    }

    private void _ApplyHealthBarLocal(float percent)
    {
        if (HealthBarRenderer == null) { return; }

        if (_healthBarMpb == null)
        {
            _healthBarMpb = new MaterialPropertyBlock();
        }

        HealthBarRenderer.GetPropertyBlock(_healthBarMpb);
        _healthBarMpb.SetFloat(_HealthBarPercentPropName, percent);
        HealthBarRenderer.SetPropertyBlock(_healthBarMpb);
    }

    private bool _CanObjectDamageUs(GameObject damagingObject)
    {
        return true;
    }

    private static SaccEntity _FindEnemyEntityControl(GameObject damagingObject)
    {
        if (!damagingObject) { return null; }

        GameObject enemyObj = damagingObject;
        SaccEntity enemy = enemyObj.GetComponent<SaccEntity>();
        while (!enemy && enemyObj.transform.parent)
        {
            enemyObj = enemyObj.transform.parent.gameObject;
            enemy = enemyObj.GetComponent<SaccEntity>();
        }
        if (enemy) { return enemy; }

        enemyObj = damagingObject;
        UdonBehaviour enemyUdon = (UdonBehaviour)enemyObj.GetComponent(typeof(UdonBehaviour));
        while (!enemyUdon && enemyObj.transform.parent)
        {
            enemyObj = enemyObj.transform.parent.gameObject;
            enemyUdon = (UdonBehaviour)enemyObj.GetComponent(typeof(UdonBehaviour));
        }
        if (enemyUdon)
        {
            object ec = enemyUdon.GetProgramVariable("EntityControl");
            if (ec != null) { return (SaccEntity)ec; }
        }
        return null;
    }

}
