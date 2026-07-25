
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class AnubisCheaterBtnDeleteThis : UdonSharpBehaviour
{
    public GameObject m_Eyeofhorus;
    public GameObject m_Bloom;

    public CheckpointUnlockTrigger[] m_Checkpoints;
    void Start()
    {
        
    }

    public override void Interact()
    {
        if (m_Eyeofhorus != null)
        {
            m_Eyeofhorus.SetActive(true);
        }
        if (m_Bloom != null)
        {
            m_Bloom.SetActive(true);
        }

        for (int i = 0; i < m_Checkpoints.Length; i++)
        {
            if (m_Checkpoints[i] != null)
            {
                m_Checkpoints[i]._unlocked = true;
            }
        }
    }
}
