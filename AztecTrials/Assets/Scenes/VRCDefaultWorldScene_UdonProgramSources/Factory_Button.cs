
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using Miner28.UdonUtils.Network;
using SaccFlightAndVehicles;

public class Factory_Button : NetworkInterface
{
    // For local collision press state
    public bool m_ButtonPressed = false;

    // For global networked state
    public bool m_ButtonTriggered = false;

    [System.NonSerialized]
    public Vector3 m_ButtonStartPosition;
    public Vector3 m_ButtonPressedPositionOffset = new Vector3(0f, -0.134f, 0f);

    [System.NonSerialized]
    private bool _buttonStartPositionInitialized;

    private void _EnsureStartPositionCached()
    {
        if (_buttonStartPositionInitialized) { return; }
        this.m_ButtonStartPosition = this.transform.localPosition;
        _buttonStartPositionInitialized = true;
    }

    private void Awake()
    {
        _EnsureStartPositionCached();
    }

    void Start()
    {
        _EnsureStartPositionCached();
    }

    void OnEnable()
    {
        _EnsureStartPositionCached();
        this.m_ButtonPressed = false;
        this.m_ButtonTriggered = false;
        this.transform.localPosition = this.m_ButtonStartPosition;
    }

    private void MoveButtonToPressedPosition()
    {
        this.transform.localPosition = this.m_ButtonStartPosition + this.m_ButtonPressedPositionOffset;
    }

    [NetworkedMethod]
    public void On_AnnounceButtonTriggered()
    {
        if (!this.gameObject.activeInHierarchy) return;
        if (this.m_ButtonTriggered) { return; }

        this.m_ButtonTriggered = true;

        this.MoveButtonToPressedPosition();
    }

    private void _OnLocalPlayerTriggeredButton()
    {
        if (this.m_ButtonTriggered) { return; }

        SendMethodNetworked(
            nameof(this.On_AnnounceButtonTriggered),
            SyncTarget.All
        );
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!this.gameObject.activeInHierarchy) return;
        if (this.m_ButtonPressed) { return; }
        if (this.m_ButtonTriggered) { return; }
        if (collision == null || collision.collider == null) { return; }

        SaccEntity entity = GetSaccEntity(collision.collider.transform);
        if (entity == null) { return; }

        // Only the player driving the vehicle should trigger the button behaviour
        if(!entity.Occupied) { return; }
        if(!entity.Using) { return; }

        this.m_ButtonPressed = true;
        entity.EntityRespawn();
        this._OnLocalPlayerTriggeredButton();
    }

    private SaccEntity GetSaccEntity(Transform collisionTransform)
    {
        if (collisionTransform == null) { return null; }
        Transform directParent = collisionTransform.parent;
        if (directParent == null) { return null; }
        return directParent.GetComponent<SaccEntity>();
    }
}
