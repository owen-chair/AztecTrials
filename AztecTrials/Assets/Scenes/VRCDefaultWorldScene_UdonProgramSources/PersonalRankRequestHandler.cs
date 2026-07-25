
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using System;
using System.Text;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Components;

public class PersonalRankRequestHandler : UdonSharpBehaviour
{
    [Header("Server URL")]
    [SerializeField]
    private VRCUrl url;

    [Header("UI Inputs")]
    public VRCUrlInputField urlInputField;
    public TMP_InputField copyDataField;

    [Header("UI")]
    [Tooltip("Shown when the URL input is not a valid /data/personal URL.")]
    public TMP_Text m_InvalidInputWarning;

    [Tooltip("Request button object to enable only when URL input is valid.")]
    public GameObject m_RequestButton;

    [Tooltip("Shown when the URL input is unset (still the default url.Get()).")]
    public GameObject m_InstructionText;

    [Header("Output")]
    [Tooltip("Rank output text (example: #42)")]
    public TMP_Text m_RankText;

    [Tooltip("Time output text (example: 00:03:21)")]
    public TMP_Text m_TimeText;

    [Tooltip("Shown when the server responds with 'Player not found'.")]
    public GameObject m_PlayerNotFound;

    [Header("Request Payload Output")]
    [Tooltip("Minified JSON payload for Server.go /data/personal (generated on enable)")]
    public string m_RequestJson;

    [Tooltip("Base64(JSON) payload for Server.go /data/personal (generated on enable)")]
    public string m_RequestB64;

    [Header("Request Payload Inputs")]
    [Tooltip("Client key required by the server")]
    public string m_ClientKey = "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000";

    private VRCPlayerApi _localPlayer;
    private bool _lastInputInvalid;

    void Start()
    {
        _localPlayer = Networking.LocalPlayer;
        _lastInputInvalid = false;
        if (urlInputField != null) urlInputField.SetUrl(url);

        _SetPlayerNotFoundVisible(false);
        _SetOutputsVisible(false);
    }

    void OnEnable()
    {
        if (urlInputField != null) urlInputField.SetUrl(url);

        BuildRequestPayload();

        if (copyDataField != null)
        {
            copyDataField.text = m_RequestB64;
        }

        _SetPlayerNotFoundVisible(false);

        _UpdateValidationUI();
    }

    public void BuildRequestPayload()
    {
        if (_localPlayer == null) _localPlayer = Networking.LocalPlayer;

        string playerName = "Unknown";
        if (_localPlayer != null)
        {
            playerName = _localPlayer.displayName;
            if (string.IsNullOrEmpty(playerName)) playerName = "Unknown";
        }

        DataDictionary dict = new DataDictionary();
        dict.Add("clientkey", m_ClientKey);
        dict.Add("playername", playerName);

        DataToken token;
        if (!VRCJson.TrySerializeToJson(dict, JsonExportType.Minify, out token))
        {
            m_RequestJson = "";
            m_RequestB64 = "";
            Debug.LogError("[PersonalRankRequestHandler] Failed to serialize payload to JSON");
            return;
        }

        string json = token.ToString();
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        m_RequestJson = json;
        m_RequestB64 = b64;
    }

    public void OnUrlInputEndEdit()
    {
        if (_IsCurrentUrlUnset())
        {
            _lastInputInvalid = false;
            _SetWarningVisible(false);
            _SetRequestVisible(false);
            _SetInstructionVisible(true);

            _SetPlayerNotFoundVisible(false);
            _SetOutputsVisible(false);
            return;
        }

        bool ok = _IsCurrentUrlValid();
        if (!ok)
        {
            _lastInputInvalid = true;
            if (urlInputField != null) urlInputField.SetUrl(url);
            if (copyDataField != null) copyDataField.text = m_RequestB64;

            _SetWarningVisible(true);
            _SetRequestVisible(false);
            _SetInstructionVisible(true);

            _SetPlayerNotFoundVisible(false);
            _SetOutputsVisible(false);
            return;
        }

        _lastInputInvalid = false;
        _SetWarningVisible(false);
        _SetRequestVisible(true);
        _SetInstructionVisible(false);

        // Keep outputs hidden until a successful response.
        _SetPlayerNotFoundVisible(false);
        _SetOutputsVisible(false);
    }

    public void Fetch()
    {
        if (urlInputField == null)
        {
            Debug.LogError("[PersonalRankRequestHandler] Missing urlInputField");
            return;
        }

        _UpdateValidationUI();
        if (!_IsCurrentUrlValid())
        {
            Debug.LogError("[PersonalRankRequestHandler] URL is not valid for /data/personal");
            return;
        }

        _SetPlayerNotFoundVisible(false);

        VRCStringDownloader.LoadUrl(urlInputField.GetUrl(), (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        string text = result.Result;
        _HandleServerResponse(text);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        Debug.LogError("[PersonalRankRequestHandler] String load failed: " + result.ErrorCode + " " + result.Error);
    }

    private void _HandleServerResponse(string responseText)
    {
        if (string.IsNullOrEmpty(responseText)) return;

        DataToken token;
        if (!VRCJson.TryDeserializeFromJson(responseText, out token)) return;
        if (token.TokenType != TokenType.DataDictionary) return;

        DataDictionary dict = token.DataDictionary;
        if (dict == null) return;

        // If server returns a message (errors / not found)
        if (dict.ContainsKey("message"))
        {
            string msg = "";
            DataToken mTok = dict["message"];
            if (mTok.TokenType == TokenType.String) msg = mTok.String;

            bool playerNotFound = !string.IsNullOrEmpty(msg) && msg.IndexOf("Player not found", StringComparison.OrdinalIgnoreCase) >= 0;
            _SetPlayerNotFoundVisible(playerNotFound);
            _SetOutputsVisible(false);
            return;
        }

        int rank = 0;
        double seconds = -1d;

        if (dict.ContainsKey("rank"))
        {
            DataToken rTok = dict["rank"];
            if (rTok.TokenType == TokenType.Int) rank = rTok.Int;
            else if (rTok.TokenType == TokenType.Double) rank = (int)rTok.Double;
        }

        if (dict.ContainsKey("completionseconds"))
        {
            DataToken tTok = dict["completionseconds"];
            if (tTok.TokenType == TokenType.Double) seconds = tTok.Double;
            else if (tTok.TokenType == TokenType.Int) seconds = (double)tTok.Int;
        }

        _SetPlayerNotFoundVisible(false);

        bool hasRank = rank > 0;
        string formatted = _FormatSeconds(seconds);
        bool hasTime = !string.IsNullOrEmpty(formatted);

        _SetRankText(hasRank ? ("#" + rank) : "");
        _SetTimeText(formatted);
        _SetOutputsVisible(hasRank || hasTime);
    }

    private void _SetRankText(string value)
    {
        if (m_RankText != null) m_RankText.text = value;
    }

    private void _SetTimeText(string value)
    {
        if (m_TimeText != null) m_TimeText.text = value;
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

    private void _SetWarningVisible(bool visible)
    {
        if (m_InvalidInputWarning != null)
        {
            GameObject go = m_InvalidInputWarning.gameObject;
            if (go != null) go.SetActive(visible);
        }
    }

    private void _SetRequestVisible(bool visible)
    {
        if (m_RequestButton != null)
        {
            m_RequestButton.SetActive(visible);
        }
    }

    private void _SetInstructionVisible(bool visible)
    {
        if (m_InstructionText != null)
        {
            m_InstructionText.SetActive(visible);
        }
    }

    private void _UpdateValidationUI()
    {
        if (_lastInputInvalid)
        {
            _SetWarningVisible(true);
            _SetRequestVisible(false);
            _SetInstructionVisible(true);

            _SetPlayerNotFoundVisible(false);
            _SetOutputsVisible(false);
            return;
        }

        if (_IsCurrentUrlUnset())
        {
            _SetWarningVisible(false);
            _SetRequestVisible(false);
            _SetInstructionVisible(true);

            _SetPlayerNotFoundVisible(false);
            _SetOutputsVisible(false);
            return;
        }

        bool ok = _IsCurrentUrlValid();
        _SetWarningVisible(!ok);
        _SetRequestVisible(ok);
        _SetInstructionVisible(false);

        if (!ok)
        {
            _SetPlayerNotFoundVisible(false);
            _SetOutputsVisible(false);
        }
    }

    private void _SetOutputsVisible(bool visible)
    {
        if (m_RankText != null)
        {
            GameObject go = m_RankText.gameObject;
            if (go != null) go.SetActive(visible);
        }

        if (m_TimeText != null)
        {
            GameObject go = m_TimeText.gameObject;
            if (go != null) go.SetActive(visible);
        }
    }

    private void _SetPlayerNotFoundVisible(bool visible)
    {
        if (m_PlayerNotFound != null)
        {
            m_PlayerNotFound.SetActive(visible);
        }
    }

    private bool _IsCurrentUrlValid()
    {
        if (urlInputField == null) return false;
        VRCUrl current = urlInputField.GetUrl();
        string s = current.Get();
        return _IsValidPersonalUrl(s);
    }

    private bool _IsCurrentUrlUnset()
    {
        if (urlInputField == null) return true;

        string current = urlInputField.GetUrl().Get();
        string def = url.Get();

        if (string.IsNullOrEmpty(current) && string.IsNullOrEmpty(def)) return true;
        return current == def;
    }

    // Validates URL shape: {scheme}://{host}/data/personal/{base64_json}
    // Also validates that base64 decodes to JSON with expected keys.
    private bool _IsValidPersonalUrl(string urlString)
    {
        if (string.IsNullOrEmpty(urlString)) return false;

        // VRChat blocks insecure HTTP requests.
        string lower = urlString.ToLower();
        if (!lower.StartsWith("https://")) return false;

        int idx = urlString.IndexOf("/data/personal/");
        if (idx < 0) return false;

        int payloadStart = idx + "/data/personal/".Length;
        if (payloadStart >= urlString.Length) return false;

        string b64 = urlString.Substring(payloadStart);
        if (b64.Length < 4) return false;

        int b64Len = b64.Length;
        for (int i = 0; i < b64Len; i++)
        {
            char c = b64[i];
            bool ok =
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') ||
                c == '+' || c == '/' || c == '=' || c == '-' || c == '_';
            if (!ok) return false;
        }

        byte[] bytes;
        if (!_TryDecodeBase64(b64, out bytes)) return false;
        if (bytes == null || bytes.Length == 0) return false;

        string json = Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrEmpty(json)) return false;

        DataToken token;
        if (!VRCJson.TryDeserializeFromJson(json, out token)) return false;
        if (token.TokenType != TokenType.DataDictionary) return false;

        DataDictionary dict = token.DataDictionary;
        if (dict == null) return false;

        if (!dict.ContainsKey("clientkey")) return false;
        if (!dict.ContainsKey("playername")) return false;
        return true;
    }

    private bool _TryDecodeBase64(string input, out byte[] output)
    {
        output = null;
        if (string.IsNullOrEmpty(input)) return false;

        int inLen = input.Length;
        char[] cleaned = new char[inLen];
        int cleanLen = 0;
        for (int i = 0; i < inLen; i++)
        {
            char c = input[i];
            if (c == ' ' || c == '\n' || c == '\r' || c == '\t') continue;
            if (c == '-') c = '+';
            else if (c == '_') c = '/';
            cleaned[cleanLen++] = c;
        }
        if (cleanLen < 4) return false;
        if ((cleanLen % 4) != 0) return false;

        int pad = 0;
        if (cleanLen >= 1 && cleaned[cleanLen - 1] == '=') pad++;
        if (cleanLen >= 2 && cleaned[cleanLen - 2] == '=') pad++;

        int outLen = (cleanLen / 4) * 3 - pad;
        if (outLen < 0) return false;

        byte[] bytes = new byte[outLen];

        int outIndex = 0;
        for (int i = 0; i < cleanLen; i += 4)
        {
            int v0 = _Base64Value(cleaned[i]);
            int v1 = _Base64Value(cleaned[i + 1]);
            int v2 = cleaned[i + 2] == '=' ? -2 : _Base64Value(cleaned[i + 2]);
            int v3 = cleaned[i + 3] == '=' ? -2 : _Base64Value(cleaned[i + 3]);

            if (v0 < 0 || v1 < 0) return false;
            if (v2 == -1 || v3 == -1) return false;

            int triple = (v0 << 18) | (v1 << 12);
            if (v2 >= 0) triple |= (v2 << 6);
            if (v3 >= 0) triple |= v3;

            if (outIndex < outLen) bytes[outIndex++] = (byte)((triple >> 16) & 0xFF);
            if (v2 >= 0 && outIndex < outLen) bytes[outIndex++] = (byte)((triple >> 8) & 0xFF);
            if (v3 >= 0 && outIndex < outLen) bytes[outIndex++] = (byte)(triple & 0xFF);
        }

        output = bytes;
        return true;
    }

    private int _Base64Value(char c)
    {
        if (c >= 'A' && c <= 'Z') return c - 'A';
        if (c >= 'a' && c <= 'z') return (c - 'a') + 26;
        if (c >= '0' && c <= '9') return (c - '0') + 52;
        if (c == '+') return 62;
        if (c == '/') return 63;
        if (c == '=') return -2;
        return -1;
    }
}
