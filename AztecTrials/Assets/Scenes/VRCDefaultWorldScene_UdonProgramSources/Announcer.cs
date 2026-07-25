
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class Announcer : UdonSharpBehaviour
{
    [Header("Named Sounds")]
    [Tooltip("Keys used by Play(key) / EnqueueByKey(key). Must match m_SoundSources by index.")]
    public string[] m_SoundKeys;

    [Tooltip("AudioSources to play. Must match m_SoundKeys by index.")]
    public AudioSource[] m_SoundSources;

    [Header("Queue")]
    [Tooltip("Max number of sounds that can be queued at once.")]
    [SerializeField] private int m_MaxQueueSize = 24;

    private AudioSource[] m_Queue = new AudioSource[0];
    private int m_QueueCount;

    private AudioSource m_Current;

    void Start()
    {
        if (m_MaxQueueSize < 1) m_MaxQueueSize = 1;
        if (m_MaxQueueSize > 64) m_MaxQueueSize = 64;
        m_Queue = new AudioSource[m_MaxQueueSize];
        m_QueueCount = 0;
        m_Current = null;
    }

    void Update()
    {
        // If something is playing, wait.
        if (m_Current != null && m_Current.isPlaying) return;

        // If current finished (or was null), clear it and play the next queued sound.
        m_Current = null;
        this._TryPlayNext();
    }

    private void _TryPlayNext()
    {
        if (m_QueueCount <= 0) return;

        AudioSource next = m_Queue[0];
        // Shift down.
        for (int i = 1; i < m_QueueCount; i++) m_Queue[i - 1] = m_Queue[i];
        m_Queue[m_QueueCount - 1] = null;
        m_QueueCount--;

        if (next == null) return;
        if (next.isPlaying) { m_Current = next; return; }

        m_Current = next;
        next.Play();
    }

    public void ClearQueue()
    {
        for (int i = 0; i < m_QueueCount; i++) m_Queue[i] = null;
        m_QueueCount = 0;
    }

    public bool Enqueue(AudioSource source)
    {
        if (source == null) return false;

        // Prevent duplicates: not while currently playing, and not already queued.
        if (m_Current == source && m_Current != null) return false;
        for (int i = 0; i < m_QueueCount; i++)
        {
            if (m_Queue[i] == source) return false;
        }

        if (m_QueueCount >= m_Queue.Length) return false;

        m_Queue[m_QueueCount++] = source;

        // If nothing is playing right now, start immediately.
        if (m_Current == null || !m_Current.isPlaying)
        {
            this._TryPlayNext();
        }
        return true;
    }

    public bool EnqueueByKey(string key)
    {
        return this.Enqueue(this._FindByKey(key));
    }

    public void Play(string key)
    {
        this.EnqueueByKey(key);
    }

    private AudioSource _FindByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (m_SoundKeys == null || m_SoundSources == null) return null;

        // UdonSharp friendliness: avoid StringComparison and other advanced APIs.
        string search = key.ToLower();

        int count = m_SoundKeys.Length;
        if (m_SoundSources.Length < count) count = m_SoundSources.Length;

        for (int i = 0; i < count; i++)
        {
            string k = m_SoundKeys[i];
            if (string.IsNullOrEmpty(k)) continue;
            AudioSource src = m_SoundSources[i];
            if (src == null) continue;

            if (k.ToLower() == search)
            {
                return src;
            }
        }

        return null;
    }
}
