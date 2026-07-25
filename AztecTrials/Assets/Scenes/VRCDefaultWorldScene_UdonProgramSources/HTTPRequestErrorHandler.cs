
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

public class HTTPRequestErrorHandler : UdonSharpBehaviour
{
    [Header("Leaderboard Objects (hide during diagnostics)")]
    public GameObject m_LeaderboardObj1;
    public GameObject m_LeaderboardObj2;
    public GameObject m_LeaderboardObj3;

    [Header("Error Panels")]
    public GameObject m_HTTPGeneralError;
    public TMP_Text m_HTTPGeneralErrorCodeText;
    public TMP_Text m_HTTPGeneralErrorText;

    public GameObject m_AllowUntrustedUrlsError;
    public TMP_Text m_AllowUntrustedUrlsErrorCodeText;
    public TMP_Text m_AllowUntrustedUrlsErrorText;

    public GameObject m_RanksServerError;
    public TMP_Text m_RanksServerErrorCodeText;
    public TMP_Text m_RanksServerErrorText;

    [Header("Test URLs")]
    [Tooltip("First test URL (expected to succeed if HTTPS networking works)")]
    [SerializeField]
    private VRCUrl m_PastebinUrl;

    [Tooltip("Second test URL (expected to fail if 'untrusted URLs' safety setting is blocking common sites)")]
    [SerializeField]
    private VRCUrl m_GoogleUrl;

    [Header("Ranks Handler")]
    [Tooltip("Reference to your existing Top10 handler so we can reuse its configured URL")]
    public Top10RankRequestHandler m_Top10Handler;

    private const float WAIT_SECONDS = 6f;

    // 0=pastebin, 1=google, 2=ranks
    private int m_Step = -1;

    private int m_LastErrorCode = 0;
    private string m_LastError = "";

    void OnEnable()
    {
        _SetLeaderboardActive(false);
        _HideAllErrors();

        // Requirement: first request must wait 6 seconds because a previous request triggered this.
        m_Step = 0;
        SendCustomEventDelayedSeconds(nameof(_DoStepRequest), WAIT_SECONDS);
    }

    public void BeginDiagnostics(Top10RankRequestHandler source, int errorCode, string error)
    {
        // This method is intended to be called by normal handlers when they hit an HTTP error.
        m_Top10Handler = source;
        m_LastErrorCode = errorCode;
        m_LastError = error;

        // This component may be a child of a disabled parent container.
        // Enabling the parent is required for this object to become active in the hierarchy.
        GameObject parentObj = transform.parent != null ? transform.parent.gameObject : null;
        if (parentObj != null && !parentObj.activeSelf)
        {
            parentObj.SetActive(true);
        }
        else if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    public void _DoStepRequest()
    {
        _HideAllErrors();

        if (m_Step == 0)
        {
            string url = m_PastebinUrl.Get();
            if (string.IsNullOrEmpty(url))
            {
                _ShowPanel(m_HTTPGeneralError, m_HTTPGeneralErrorCodeText, m_HTTPGeneralErrorText, 0,
                    "Missing Pastebin URL (assign m_PastebinUrl in inspector)");
                return;
            }
            VRCStringDownloader.LoadUrl(m_PastebinUrl, (IUdonEventReceiver)this);
            return;
        }

        if (m_Step == 1)
        {
            string url = m_GoogleUrl.Get();
            if (string.IsNullOrEmpty(url))
            {
                _ShowPanel(m_HTTPGeneralError, m_HTTPGeneralErrorCodeText, m_HTTPGeneralErrorText, 0,
                    "Missing Google URL (assign m_GoogleUrl in inspector)");
                return;
            }
            VRCStringDownloader.LoadUrl(m_GoogleUrl, (IUdonEventReceiver)this);
            return;
        }

        if (m_Step == 2)
        {
            if (m_Top10Handler == null)
            {
                _ShowPanel(m_HTTPGeneralError, m_HTTPGeneralErrorCodeText, m_HTTPGeneralErrorText, 0,
                    "Missing Top10 handler reference (assign m_Top10Handler or call BeginDiagnostics)");
                return;
            }

            VRCUrl top10 = m_Top10Handler.GetTop10Url();
            string url = top10.Get();
            if (string.IsNullOrEmpty(url))
            {
                _ShowPanel(m_HTTPGeneralError, m_HTTPGeneralErrorCodeText, m_HTTPGeneralErrorText, 0,
                    "Top10 handler URL is empty");
                return;
            }

            VRCStringDownloader.LoadUrl(top10, (IUdonEventReceiver)this);
            return;
        }
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        // Requirement: wait 6 seconds between requests.
        if (m_Step == 0)
        {
            m_Step = 1;
            SendCustomEventDelayedSeconds(nameof(_DoStepRequest), WAIT_SECONDS);
            return;
        }

        if (m_Step == 1)
        {
            m_Step = 2;
            SendCustomEventDelayedSeconds(nameof(_DoStepRequest), WAIT_SECONDS);
            return;
        }

        if (m_Step == 2)
        {
            // Intermittent issue: everything works now.
            _HideAllErrors();
            _SetLeaderboardActive(true);
            gameObject.SetActive(false);
            return;
        }
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        m_LastErrorCode = result.ErrorCode;
        m_LastError = result.Error;

        if (m_Step == 0)
        {
            _ShowPanel(m_HTTPGeneralError, m_HTTPGeneralErrorCodeText, m_HTTPGeneralErrorText, result.ErrorCode, result.Error);
            return;
        }
        if (m_Step == 1)
        {
            _ShowPanel(m_AllowUntrustedUrlsError, m_AllowUntrustedUrlsErrorCodeText, m_AllowUntrustedUrlsErrorText, result.ErrorCode,
                result.Error);
            return;
        }
        if (m_Step == 2)
        {
            _ShowPanel(m_RanksServerError, m_RanksServerErrorCodeText, m_RanksServerErrorText, result.ErrorCode, result.Error);
            return;
        }
    }

    private void _HideAllErrors()
    {
        if (m_HTTPGeneralError != null) m_HTTPGeneralError.SetActive(false);
        if (m_AllowUntrustedUrlsError != null) m_AllowUntrustedUrlsError.SetActive(false);
        if (m_RanksServerError != null) m_RanksServerError.SetActive(false);
    }

    private void _SetLeaderboardActive(bool active)
    {
        if (m_LeaderboardObj1 != null) m_LeaderboardObj1.SetActive(active);
        if (m_LeaderboardObj2 != null) m_LeaderboardObj2.SetActive(active);
        if (m_LeaderboardObj3 != null) m_LeaderboardObj3.SetActive(active);
    }

    private void _ShowPanel(GameObject panel, TMP_Text codeText, TMP_Text errText, int code, string err)
    {
        if (panel != null) panel.SetActive(true);
        if (codeText != null) codeText.text = "Code: " + code.ToString();
        if (errText != null) errText.text = "Error: " + err;
    }
}
