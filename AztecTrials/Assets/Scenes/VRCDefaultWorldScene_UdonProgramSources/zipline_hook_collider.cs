using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using SaccFlightAndVehicles;

public class zipline_hook_collider : GrappleHookAttachmentScript
{
    public Vector3 m_ZiplineEndPosition;
    public Vector3 m_ZiplineStartPosition;

    public GameObject m_ParentObject;
    public GameObject m_ZiplineEndPositionObject;

    public AudioSource m_ZiplineAudioSource;

    public bool m_IsZiplineMoving = false;

    [SerializeField]
    public float m_ZiplineSpeed = 5f; // Speed at which the zipline moves

    public void Start()
    {
        this.m_ParentObject = this.transform.parent.gameObject;
        this.m_ZiplineStartPosition = this.m_ParentObject.transform.position;

        if (this.m_ZiplineEndPositionObject != null)
        {
            this.m_ZiplineEndPosition = this.m_ZiplineEndPositionObject.transform.position;
        }
    }

    public override void OnAttach()
    {
        // Code to execute when the grapple is attached to this hook
        Debug.Log("zipline_hook_collider: Grapple attached to hook: " + this.gameObject.name);

        this.m_IsZiplineMoving = true;
        this.PlayZiplineSound();
    }

    public override void OnDetach()
    {
        // Code to execute when the grapple is detached from this hook
        Debug.Log("zipline_hook_collider: Grapple detached from hook: " + this.gameObject.name);
        this.m_IsZiplineMoving = true;
        this.PlayZiplineSound();
    }

    public override void OnGrappleAddedToCooldown(DFUNC_Grapple grapple)
    {
        if (this.m_ParentObject)
        {
            this.m_ParentObject.transform.position = this.m_ZiplineStartPosition;
        }
    }

    public void PlayZiplineSound()
    {
        if (this.m_ZiplineAudioSource != null && !this.m_ZiplineAudioSource.isPlaying)
        {
            this.m_ZiplineAudioSource.Play();
        }
    }

    public void StopZiplineSound()
    {
        if (this.m_ZiplineAudioSource != null && this.m_ZiplineAudioSource.isPlaying)
        {
            this.m_ZiplineAudioSource.Stop();
        }
    }

    public void OnReachedZiplineEnd()
    {
        this.m_IsZiplineMoving = false;
        StopZiplineSound();
    }

    public void OnReachedZiplineStart()
    {
        this.m_IsZiplineMoving = false;
        StopZiplineSound();
    }

    public void FixedUpdate()
    {
        if(this.gameObject == null) return;
        if(this.gameObject.activeInHierarchy == false) return;
        if(!this.m_IsZiplineMoving) return;
        
        if(!this.m_IsGrappleAttached)
        {
            // Move towards start if not already there
            if (Vector3.Distance(this.m_ParentObject.transform.position, this.m_ZiplineStartPosition) < 0.1f)
            {
                this.OnReachedZiplineEnd();
                return;
            }
        
            Vector3 direction = (this.m_ZiplineStartPosition - this.m_ParentObject.transform.position).normalized;
            this.m_ParentObject.transform.position += direction * this.m_ZiplineSpeed * Time.fixedDeltaTime;
        }
        else
        {
            if (Vector3.Distance(this.m_ParentObject.transform.position, this.m_ZiplineEndPosition) < 0.1f)
            {
                this.OnReachedZiplineStart();
                return;
            }

            Vector3 direction = (this.m_ZiplineEndPosition - this.m_ParentObject.transform.position).normalized;
            this.m_ParentObject.transform.position += direction * this.m_ZiplineSpeed * Time.fixedDeltaTime;
        }
    }
}
