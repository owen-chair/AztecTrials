
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using SaccFlightAndVehicles;
public class GrappleHookAttachmentScript : UdonSharpBehaviour
{
    public DFUNC_Grapple[] m_GrappleObjects;
    public float m_MaxAttachmentSeconds = 10f;
    public float m_GrappleCooldownSeconds = 5f;

    public bool m_IsGrappleAttached = false;

    private float[] m_GrappleAttachmentTimes;
    private float[] m_GrappleCooldownUntilTimes;
    private bool[] m_GrappleWasAttached;

    public virtual void OnAttach()
    {
        // Stub, override in behaviour   
    }

    public virtual void OnDetach()
    {
        // Stub, override in behaviour   
    }

    public virtual void OnGrappleAddedToCooldown(DFUNC_Grapple grapple)
    {
        // Stub, override in behaviour
    }

    public void DFUNC_Grapple_Attached()
    {
        if(this.gameObject.activeInHierarchy == false) return;
        RefreshGrappleAttachmentState();
    }

    public void DFUNC_Grapple_Detached()
    {
        if(this.gameObject.activeInHierarchy == false) return;
        SendCustomEventDelayedFrames(nameof(RefreshGrappleAttachmentState), 1);
    }

    public void Update()
    {
        if(this.gameObject == null) return;
        if(this.gameObject.activeInHierarchy == false) return;

        RefreshGrappleAttachmentState();
    }

    public void RefreshGrappleAttachmentState()
    {
        EnsureGrappleStateArrays();

        if (this.m_GrappleObjects == null)
        {
            SetGrappleAttachedState(false);
            return;
        }

        bool isAnyGrappleAttached = false;
        float time = Time.time;

        for (int i = 0; i < this.m_GrappleObjects.Length; i++)
        {
            DFUNC_Grapple grapple = this.m_GrappleObjects[i];
            if (!grapple)
            {
                this.m_GrappleWasAttached[i] = false;
                this.m_GrappleAttachmentTimes[i] = 0f;
                continue;
            }

            bool isAttachedToThisHook = grapple.CurrentGrappleHookAttachment == this.gameObject;
            bool isInCooldown = time < this.m_GrappleCooldownUntilTimes[i];

            if (isAttachedToThisHook && isInCooldown)
            {
                ForceDetachGrapple(grapple);
                isAttachedToThisHook = false;
            }

            if (isAttachedToThisHook)
            {
                if (!this.m_GrappleWasAttached[i])
                {
                    this.m_GrappleAttachmentTimes[i] = time;
                    this.m_GrappleWasAttached[i] = true;
                }
                else if (this.m_MaxAttachmentSeconds > 0f && time - this.m_GrappleAttachmentTimes[i] > this.m_MaxAttachmentSeconds)
                {
                    AddGrappleToCooldown(i, grapple, time);
                    isAttachedToThisHook = false;
                }
            }
            else
            {
                this.m_GrappleWasAttached[i] = false;
                this.m_GrappleAttachmentTimes[i] = 0f;
            }

            if (isAttachedToThisHook)
            {
                isAnyGrappleAttached = true;
            }
        }

        SetGrappleAttachedState(isAnyGrappleAttached);
    }

    private void EnsureGrappleStateArrays()
    {
        int grappleCount = this.m_GrappleObjects == null ? 0 : this.m_GrappleObjects.Length;
        if (this.m_GrappleAttachmentTimes != null && this.m_GrappleAttachmentTimes.Length == grappleCount)
        {
            return;
        }

        this.m_GrappleAttachmentTimes = new float[grappleCount];
        this.m_GrappleCooldownUntilTimes = new float[grappleCount];
        this.m_GrappleWasAttached = new bool[grappleCount];
    }

    private void AddGrappleToCooldown(int grappleIndex, DFUNC_Grapple grapple, float time)
    {
        this.m_GrappleCooldownUntilTimes[grappleIndex] = time + this.m_GrappleCooldownSeconds;
        this.m_GrappleAttachmentTimes[grappleIndex] = 0f;
        this.m_GrappleWasAttached[grappleIndex] = false;
        ForceDetachGrapple(grapple);
        OnGrappleAddedToCooldown(grapple);
    }

    private void ForceDetachGrapple(DFUNC_Grapple grapple)
    {
        if (grapple)
        {
            grapple.ForceDetachGrappleHookAttachment();
        }
    }

    private void SetGrappleAttachedState(bool isAttached)
    {
        if (this.m_IsGrappleAttached == isAttached)
        {
            return;
        }

        this.m_IsGrappleAttached = isAttached;

        if (isAttached)
        {
            this.OnAttach();
        }
        else
        {
            this.OnDetach();
        }
    }
}