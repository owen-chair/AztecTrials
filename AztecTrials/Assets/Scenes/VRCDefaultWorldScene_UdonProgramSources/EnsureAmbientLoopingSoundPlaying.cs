
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class EnsureAmbientLoopingSoundPlaying : UdonSharpBehaviour
{
    public AudioSource m_Source;

    void Start()
    {
        if (this.m_Source == null)
        {
            this.m_Source = this.transform.parent.GetComponent<AudioSource>();
        }  
    }

    private void OnEnable()
    {
        if(this.m_Source == null) return;

        this.m_Source.Stop();
        this.m_Source.Play();
    }
}
