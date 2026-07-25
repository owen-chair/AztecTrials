
using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;

public class FINISHED : UdonSharpBehaviour
{
    [Header("References")]
    public timestarttrigger m_TimeStartTrigger;

    [Tooltip("Text that shows completion time in the form: Time: 00:00:00")]
    public TMP_Text m_TimeText;

    [Header("Output")]
    [Tooltip("Latched completion time (seconds). Calculated once on first enable.")]
    public float m_CompletionTime = -1f;

    [System.NonSerialized] private bool _latched;

    private void Awake()
    {
        _latched = (m_CompletionTime >= 0f);
    }

    private void OnEnable()
    {
        TryLatchNow();
    }

    public void TryLatchNow()
    {
        // Some systems (occlusion/pooling) can enable this object before the run starts,
        // which would latch a bogus 0s completion time. Allow re-latching if we have
        // a valid start and the completion time is still not > 0.
        if (m_TimeStartTrigger != null && m_TimeStartTrigger.m_HasStarted)
        {
            float elapsed = (float)Networking.GetServerTimeInSeconds() - m_TimeStartTrigger.m_StartTime;
            if (elapsed < 0f) elapsed = 0f;

            if (!_latched || !(m_CompletionTime > 0f))
            {
                m_CompletionTime = elapsed;
                _latched = true;
            }
        }

        UpdateTimeText();
    }

    private void UpdateTimeText()
    {
        if (m_TimeText == null) return;

        float t = m_CompletionTime;
        if (t < 0f) t = 0f;

        int totalSeconds = Mathf.FloorToInt(t);
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        m_TimeText.text = string.Concat(
            "Time: ",
            hours.ToString("D2"), ":",
            minutes.ToString("D2"), ":",
            seconds.ToString("D2")
        );
    }
}
