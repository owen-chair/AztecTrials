
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;

public class Top10RankRequestHandler : UdonSharpBehaviour
{
    [Header("Server URL")]
    [Tooltip("Full Server.go top10 URL (must be set in inspector), e.g. https://host/data/top10/{base64_json}")]
    [SerializeField]
    private VRCUrl m_Top10Url;

    [Header("Debug (optional)")]
    [Tooltip("If set, shows status/errors in-world (useful on Quest where you can't see the console)")]
    public TMP_Text m_DebugText;

    [Header("HTTP Diagnostics (optional)")]
    [Tooltip("If set, will activate this diagnostic UI when a download fails (helpful on Quest)")]
    public HTTPRequestErrorHandler m_HttpErrorHandler;

    [Header("UI Outputs (size 10)")]
    [Tooltip("10 TMP_Text references for player names (index 0 = rank #1)")]
    public TMP_Text[] m_PlayerNameTexts;

    [Tooltip("10 TMP_Text references for player times (index 0 = rank #1)")]
    public TMP_Text[] m_PlayerTimeTexts;

    private const int RANK_COUNT = 10;
    private const int DEBUG_MAX_CHARS = 2000;

    void OnEnable()
    {
        Refresh();
    }

    public VRCUrl GetTop10Url()
    {
        return m_Top10Url;
    }

    public void Refresh()
    {
        _ClearTexts();
        _ClearDebug();
        _SetDebug("Refreshing...");

        string urlString = m_Top10Url.Get();
        if (string.IsNullOrEmpty(urlString))
        {
            Debug.LogError("[Top10RankRequestHandler] Missing m_Top10Url (set it in the inspector)");
            _SetDebug("Error: Missing URL (m_Top10Url)");
            return;
        }

        string lower = urlString.ToLower();
        if (!lower.StartsWith("https://"))
        {
            Debug.LogError("[Top10RankRequestHandler] URL must be https:// (VRChat blocks insecure http://)");
            _SetDebug("Error: URL must start with https://");
            return;
        }

        _SetDebug("Downloading...");
        VRCStringDownloader.LoadUrl(m_Top10Url, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        string responseText = result.Result;
        if (string.IsNullOrEmpty(responseText))
        {
            _SetDebug("Error: Empty response");
            return;
        }

        DataToken token;
        if (!VRCJson.TryDeserializeFromJson(responseText, out token))
        {
            Debug.LogError("[Top10RankRequestHandler] Failed to parse JSON response");
            _SetDebug("Error: Failed to parse JSON\n" + _Truncate(responseText, 160));
            return;
        }

        if (token.TokenType != TokenType.DataDictionary) return;

        DataDictionary root = token.DataDictionary;
        if (root == null || !root.ContainsKey("players"))
        {
            _SetDebug("Error: JSON missing 'players'");
            return;
        }

        DataToken playersToken = root["players"];
        if (playersToken.TokenType != TokenType.DataList)
        {
            _SetDebug("Error: 'players' is not a list");
            return;
        }

        DataList players = playersToken.DataList;
        if (players == null) return;

        int count = players.Count;
        if (count > RANK_COUNT) count = RANK_COUNT;

        for (int i = 0; i < count; i++)
        {
            DataToken pTok = players[i];
            if (pTok.TokenType != TokenType.DataDictionary) continue;

            DataDictionary p = pTok.DataDictionary;
            if (p == null) continue;

            string playerName = "";
            double completionSeconds = -1d;

            if (p.ContainsKey("playername"))
            {
                DataToken nameTok = p["playername"];
                if (nameTok.TokenType == TokenType.String) playerName = nameTok.String;
            }

            if (p.ContainsKey("completionseconds"))
            {
                DataToken timeTok = p["completionseconds"];
                if (timeTok.TokenType == TokenType.Double) completionSeconds = timeTok.Double;
                else if (timeTok.TokenType == TokenType.Int) completionSeconds = (double)timeTok.Int;
            }

            _SetName(i, playerName);
            _SetTime(i, _FormatSeconds(completionSeconds));
        }

        _SetDebug("OK (" + count + " entries)");
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        Debug.LogError("[Top10RankRequestHandler] String load failed: " + result.ErrorCode + " " + result.Error);
        _SetDebug("Download failed: " + result.ErrorCode + "\n" + result.Error);

        if (m_HttpErrorHandler != null)
        {
            m_HttpErrorHandler.BeginDiagnostics(this, result.ErrorCode, result.Error);
        }
    }

    private void _SetDebug(string value)
    {
        if (m_DebugText == null) return;

        string cur = m_DebugText.text;
        if (string.IsNullOrEmpty(cur))
        {
            cur = value;
        }
        else
        {
            cur = cur + "\n" + value;
        }

        if (cur.Length > DEBUG_MAX_CHARS)
        {
            cur = "...\n" + cur.Substring(cur.Length - DEBUG_MAX_CHARS);
        }

        m_DebugText.text = cur;
    }

    private void _ClearDebug()
    {
        if (m_DebugText == null) return;
        m_DebugText.text = "";
    }

    private string _Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (max <= 0) return "";
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }

    private void _ClearTexts()
    {
        for (int i = 0; i < RANK_COUNT; i++)
        {
            _SetName(i, "");
            _SetTime(i, "");
        }
    }

    private void _SetName(int index, string value)
    {
        if (m_PlayerNameTexts == null) return;
        if (index < 0 || index >= m_PlayerNameTexts.Length) return;
        TMP_Text t = m_PlayerNameTexts[index];
        if (t != null) t.text = value;
    }

    private void _SetTime(int index, string value)
    {
        if (m_PlayerTimeTexts == null) return;
        if (index < 0 || index >= m_PlayerTimeTexts.Length) return;
        TMP_Text t = m_PlayerTimeTexts[index];
        if (t != null) t.text = value;
    }

    private string _FormatSeconds(double seconds)
    {
        if (!(seconds > 0d)) return "";

        int total = (int)seconds;
        int hours = total / 3600;
        int minutes = (total % 3600) / 60;
        int secs = total % 60;

        string hh = hours < 10 ? "0" + hours : "" + hours;
        string mm = minutes < 10 ? "0" + minutes : "" + minutes;
        string ss = secs < 10 ? "0" + secs : "" + secs;
        return hh + ":" + mm + ":" + ss;
    }
}
