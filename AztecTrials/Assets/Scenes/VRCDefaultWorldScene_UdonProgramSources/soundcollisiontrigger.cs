using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class soundcollisiontrigger : UdonSharpBehaviour
{
    [Header("Audio")]
    [Tooltip("AudioSource to play. If null, the first AudioSource found on this object or its children is used.")]
    public AudioSource m_Audio;

    [Header("Allowed Colliders")]
    [Tooltip("If set (non-empty), only these colliders will trigger the sound. If empty, any collider will trigger.")]
    public Collider[] m_AllowedColliders;

    private void Start()
    {
        if (m_Audio == null)
        {
            m_Audio = GetComponentInChildren<AudioSource>(true);
        }
    }

    public void OnTriggerEnter(Collider other)
    {
        if (m_Audio == null) return;
        if (other == null) return;

        if (m_AllowedColliders != null && m_AllowedColliders.Length > 0)
        {
            bool allowed = false;
            for (int i = 0; i < m_AllowedColliders.Length; i++)
            {
                if (m_AllowedColliders[i] == other)
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed) return;
        }

        if (!m_Audio.isPlaying)
        {
            m_Audio.Play();
        }
    }
}
