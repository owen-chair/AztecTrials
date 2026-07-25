
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class boulderPoundSound : UdonSharpBehaviour
{

    [Header("Boulder Pound Sound Audio")]
    [SerializeField] private AudioSource m_BoulderThudSound;

    private void OnTriggerEnter(Collider other)
    {
        if(other == null) return;

        NetworkedChaseBoulder boulder = other.GetComponent<NetworkedChaseBoulder>();
        if (boulder == null) return;

        if(this.m_BoulderThudSound == null) return;
        this.m_BoulderThudSound.Play();
    }
}
