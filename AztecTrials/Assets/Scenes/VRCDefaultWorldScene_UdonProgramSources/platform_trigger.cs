
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Miner28.UdonUtils.Network;
using SaccFlightAndVehicles;

public class platform_trigger : NetworkInterface
{
    // For local trigger press state
    public bool m_TriggeredLocally = false;

    // For global networked state
    public bool m_TriggeredGlobally = false;

    [Header("Visuals")]
    [Tooltip("Renderers whose materials will have emissive color set. If empty, will use this GameObject's Renderer.")]
    public Renderer[] m_EmissiveRenderers;

    private void Awake()
    {
        _EnsureRenderersCached();
    }

    void Start()
    {
        _EnsureRenderersCached();
    }

    void OnEnable()
    {
        _EnsureRenderersCached();
        this.m_TriggeredLocally = false;
        this.m_TriggeredGlobally = false;
        _SetEmissiveColor(new Color(1f, 0f, 0f));
    }

    private void _EnsureRenderersCached()
    {
        if (this.m_EmissiveRenderers != null && this.m_EmissiveRenderers.Length > 0) return;
        Renderer r = this.GetComponent<Renderer>();
        if (r != null)
        {
            this.m_EmissiveRenderers = new Renderer[] { r };
        }
    }

    public void EnableHUDElements(byte team)
    {
    }

    public void DisableHUDElements()
    {
    }

    private void _SetEmissiveColor(Color c)
    {
        if (this.m_EmissiveRenderers == null) return;

        for (int i = 0; i < this.m_EmissiveRenderers.Length; i++)
        {
            Renderer r = this.m_EmissiveRenderers[i];
            if (r == null) continue;

            Material[] mats = r.materials;
            if (mats == null) continue;

            for (int j = 0; j < mats.Length; j++)
            {
                Material m = mats[j];
                if (m == null) continue;
                if (!m.HasProperty("_EmissionColor")) continue;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c);
            }
        }
    }

    [NetworkedMethod]
    public void On_AnnounceTriggered()
    {
        if (this.m_TriggeredGlobally) { return; }

        this.m_TriggeredGlobally = true;
        _SetEmissiveColor(new Color(0f, 1f, 0f));
    }

    private void _OnLocalPlayerTriggered()
    {
        if (this.m_TriggeredGlobally) { return; }

        SendMethodNetworked(
            nameof(this.On_AnnounceTriggered),
            SyncTarget.All
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        if (this.m_TriggeredLocally) { return; }
        if (this.m_TriggeredGlobally) { return; }
        if (other == null) { return; }

        SaccEntity entity = GetSaccEntity(other.transform);
        if (entity == null) { return; }

        // Only the player driving the vehicle should trigger the behaviour.
        if (!entity.Occupied) { return; }
        if (!entity.Using) { return; }

        this.m_TriggeredLocally = true;
        entity.EntityRespawn();
        this._OnLocalPlayerTriggered();
    }

    private SaccEntity GetSaccEntity(Transform collisionTransform)
    {
        if (collisionTransform == null) { return null; }
        Transform directParent = collisionTransform.parent;
        if (directParent == null) { return null; }
        return directParent.GetComponent<SaccEntity>();
    }
}
