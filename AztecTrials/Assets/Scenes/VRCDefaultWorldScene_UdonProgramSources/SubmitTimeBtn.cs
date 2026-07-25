
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using UdonSharp;
using System;
using System.Text;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Components;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components;
using VRC.SDK3.Video.Components.AVPro;
using VRC.SDK3.Video.Components.Base;
using VRC.SDKBase;


public class SubmitTimeBtn : UdonSharpBehaviour
{
    [SerializeField]
    private VRCUrl url;

    public VRCUrlInputField urlInputField;

    public TMP_InputField copyDataField;

    [Header("UI")]
    [Tooltip("Shown when the URL input is not a valid /time/submit URL.")]
    public TMP_Text m_InvalidInputWarning;

    [Tooltip("Submit button object to enable only when URL input is valid.")]
    public GameObject m_SubmitButton;

    [Tooltip("Shown when the URL input is unset (still the default url.Get()).")]
    public GameObject m_InstructionText;

    [Tooltip("Shown when the server successfully accepts the time.")]
    public TMP_Text m_SuccessText;

    [Header("Submit Payload Output")]
    [Tooltip("Minified JSON payload for Server.go /time/submit (generated on enable)")]
    public string m_SubmitJson;

    [Tooltip("Base64(JSON) payload for Server.go /time/submit (generated on enable)")]
    public string m_SubmitB64;

    [Header("Submit Payload Inputs")]
    [Tooltip("Client key required by the server")]
    public string m_ClientKey = "VRC_PUBLIC_CLIENT_KEY_PLACEHOLDER_0000";

    [Tooltip("Optional: completion time source. If set, uses FINISHED.m_CompletionTime")]
    public FINISHED m_Finished;

    private VRCPlayerApi _localPlayer;
    private bool _lastInputInvalid;
    private bool _submissionSucceeded;

    void Start()
    {
        _localPlayer = Networking.LocalPlayer;
        _lastInputInvalid = false;
        _submissionSucceeded = false;
        if (urlInputField != null) urlInputField.SetUrl(url);
    }

    void OnEnable()
    {
        if (urlInputField != null) urlInputField.SetUrl(url);

        BuildSubmitPayload();

        if (copyDataField != null)
        {
            copyDataField.text = m_SubmitB64;
        }

        // Keep UI state consistent when this object gets re-enabled.
        _UpdateValidationUI();
    }

    public void BuildSubmitPayload()
    {
        if (_localPlayer == null) _localPlayer = Networking.LocalPlayer;

        string playerName = "Unknown";
        if (_localPlayer != null)
        {
            playerName = _localPlayer.displayName;
            if (string.IsNullOrEmpty(playerName)) playerName = "Unknown";
        }

        float completionSeconds = -1f;
        if (m_Finished != null)
        {
            // Ensure FINISHED has had a chance to compute before we read it.
            m_Finished.TryLatchNow();
            completionSeconds = m_Finished.m_CompletionTime;
        }

        if (!(completionSeconds > 0f))
        {
            m_SubmitJson = "";
            m_SubmitB64 = "";
            string detail = "";
            if (m_Finished == null) detail = " (m_Finished is null)";
            else detail = " (m_Finished.m_CompletionTime=" + completionSeconds + ")";
            Debug.LogError("[SubmitTimeBtn] Completion time missing/invalid. Ensure FINISHED is enabled at the finish moment and that start time was latched." + detail);
            return;
        }

        DataDictionary dict = new DataDictionary();
        dict.Add("clientkey", m_ClientKey);
        dict.Add("playername", playerName);
        dict.Add("completionseconds", (double)completionSeconds);

        DataToken token;
        if (!VRCJson.TrySerializeToJson(dict, JsonExportType.Minify, out token))
        {
            m_SubmitJson = "";
            m_SubmitB64 = "";
            Debug.LogError("[SubmitTimeBtn] Failed to serialize payload to JSON");
            return;
        }

        // Note: token.ToString() returns the JSON string; it is NOT base64.
        string json = token.ToString();
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // Extra submit-only marker: insert an 'a' at index 15.
        // Server validates and strips this before decoding.
        if (b64.Length <= 15)
        {
            m_SubmitJson = "";
            m_SubmitB64 = "";
            Debug.LogError("[SubmitTimeBtn] Submit payload base64 unexpectedly short; cannot insert marker.");
            return;
        }
        b64 = b64.Substring(0, 15) + "a" + b64.Substring(15);

        m_SubmitJson = json;
        m_SubmitB64 = b64;

        Debug.Log("[SubmitTimeBtn] Built submit payload. JSON: " + m_SubmitJson);
        Debug.Log("[SubmitTimeBtn] Built submit payload. B64: " + m_SubmitB64);
    }
   
    // unusd but kept for reference
    // public void OnUrlInputChanged()
    // {
    //     Debug.Log("[SubmitTimeBtn] URL input changed to: " + url);
    //     Debug.Log("[SubmitTimeBtn] Updating url field." + urlInputField.GetUrl().Get());
    // }

    public void OnUrlInputEndEdit()
    {
        // If we're in the "unset" default state, show only instructions.
        if (_IsCurrentUrlUnset())
        {
            _lastInputInvalid = false;
            _SetWarningVisible(false);
            _SetSubmitVisible(false);
            _SetInstructionVisible(true);
            return;
        }

        bool ok = _IsCurrentUrlValid();
        if (!ok)
        {
            _lastInputInvalid = true;
            // Reset to last known-good values.
            if (urlInputField != null) urlInputField.SetUrl(url);
            if (copyDataField != null) copyDataField.text = m_SubmitB64;

            // Requirement: on invalid input, show BOTH instruction and warning.
            _SetWarningVisible(true);
            _SetSubmitVisible(false);
            _SetInstructionVisible(true);
            return;
        }

        _lastInputInvalid = false;

        _SetWarningVisible(!ok);
        _SetSubmitVisible(ok);
        _SetInstructionVisible(false);
    }

    public void Fetch()
    {
        if (urlInputField == null)
        {
            Debug.LogError("[SubmitTimeBtn] Missing urlInputField");
            return;
        }

        _UpdateValidationUI();
        if (!_IsCurrentUrlValid())
        {
            Debug.LogError("[SubmitTimeBtn] URL is not valid for /time/submit");
            return;
        }

        Debug.Log("[SubmitTimeBtn] url.Get(): " + urlInputField.GetUrl().Get());
        VRCStringDownloader.LoadUrl(urlInputField.GetUrl(), (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        string text = result.Result;
        Debug.Log("Downloaded string:");
        Debug.Log(text);

        _HandleServerResponse(text);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        Debug.LogError($"String load failed: {result.ErrorCode} {result.Error}");
    } 

    private void _SetWarningVisible(bool visible)
    {
        if (m_InvalidInputWarning != null)
        {
            GameObject go = m_InvalidInputWarning.gameObject;
            if (go != null) go.SetActive(visible);
        }
    }

    private void _SetSubmitVisible(bool visible)
    {
        if (m_SubmitButton != null)
        {
            m_SubmitButton.SetActive(visible);
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
        if (_submissionSucceeded)
        {
            _ShowSuccessUI();
            return;
        }

        // If the last edit was invalid, keep warning visible even though we've
        // reset the URL back to default (which would otherwise look "unset").
        if (_lastInputInvalid)
        {
            _SetWarningVisible(true);
            _SetSubmitVisible(false);
            _SetInstructionVisible(true);
            _SetSuccessVisible(false);
            _SetInputsVisible(true);
            return;
        }

        if (_IsCurrentUrlUnset())
        {
            _SetWarningVisible(false);
            _SetSubmitVisible(false);
            _SetInstructionVisible(true);
            _SetSuccessVisible(false);
            _SetInputsVisible(true);
            return;
        }

        bool ok = _IsCurrentUrlValid();
        _SetWarningVisible(!ok);
        _SetSubmitVisible(ok);
        _SetInstructionVisible(false);
        _SetSuccessVisible(false);
        _SetInputsVisible(true);
    }

    private void _SetInputsVisible(bool visible)
    {
        if (urlInputField != null)
        {
            GameObject go = urlInputField.gameObject;
            if (go != null) go.SetActive(visible);
        }

        if (copyDataField != null)
        {
            GameObject go = copyDataField.gameObject;
            if (go != null) go.SetActive(visible);
        }
    }

    private void _SetSuccessVisible(bool visible)
    {
        if (m_SuccessText != null)
        {
            GameObject go = m_SuccessText.gameObject;
            if (go != null) go.SetActive(visible);
        }
    }

    private void _ShowSuccessUI()
    {
        // Requirement: hide both input fields, submit button, warning, instructions; show success.
        _SetInputsVisible(false);
        _SetSubmitVisible(false);
        _SetWarningVisible(false);
        _SetInstructionVisible(false);
        _SetSuccessVisible(true);
    }

    private void _HandleServerResponse(string responseText)
    {
        if (string.IsNullOrEmpty(responseText)) return;

        DataToken token;
        if (!VRCJson.TryDeserializeFromJson(responseText, out token)) return;
        if (token.TokenType != TokenType.DataDictionary) return;

        DataDictionary dict = token.DataDictionary;
        if (dict == null) return;
        if (!dict.ContainsKey("message")) return;

        DataToken msgToken = dict["message"];
        if (msgToken.TokenType != TokenType.String) return;

        string msg = msgToken.String;
        if (string.IsNullOrEmpty(msg)) return;

        if (msg == "Time added" || msg == "Time improved")
        {
            _submissionSucceeded = true;
            _lastInputInvalid = false;
            _ShowSuccessUI();
        }
    }

    private bool _IsCurrentUrlValid()
    {
        if (urlInputField == null) return false;
        VRCUrl current = urlInputField.GetUrl();
        string s = current.Get();
        return _IsValidSubmitUrl(s);
    }

    private bool _IsCurrentUrlUnset()
    {
        if (urlInputField == null) return true;

        string current = urlInputField.GetUrl().Get();
        string def = url.Get();

        // "Unset" means the user hasn't provided anything and the input is still the default.
        if (string.IsNullOrEmpty(current) && string.IsNullOrEmpty(def)) return true;
        return current == def;
    }

    // Validates URL shape: {scheme}://{host}/time/submit/{base64_json}
    // Also validates that base64 decodes to JSON with the expected keys.
    private bool _IsValidSubmitUrl(string urlString)
    {
        if (string.IsNullOrEmpty(urlString)) return false;

        // VRChat blocks insecure HTTP requests.
        string lower = urlString.ToLower();
        if (!lower.StartsWith("https://")) return false;

        int idx = urlString.IndexOf("/time/submit/");
        if (idx < 0) return false;

        int payloadStart = idx + "/time/submit/".Length;
        if (payloadStart >= urlString.Length) return false;

        string b64 = urlString.Substring(payloadStart);
        if (b64.Length < 4) return false;

        // Submit-only marker requirement: base64 must include an 'a' at index 15.
        // Strip it before running normal base64 validation/decoding.
        if (b64.Length <= 15) return false;
        if (b64[15] != 'a') return false;
        b64 = b64.Substring(0, 15) + b64.Substring(16);

        // Quick character check (base64 typically uses A-Z a-z 0-9 + / =).
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

        // Encoding.UTF8.GetString does not require try/catch in UdonSharp.
        string json = Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrEmpty(json)) return false;

        DataToken token;
        if (!VRCJson.TryDeserializeFromJson(json, out token)) return false;
        if (token.TokenType != TokenType.DataDictionary) return false;

        DataDictionary dict = token.DataDictionary;
        if (dict == null) return false;

        // Must contain required fields.
        if (!dict.ContainsKey("clientkey")) return false;
        if (!dict.ContainsKey("playername")) return false;
        if (!dict.ContainsKey("completionseconds")) return false;

        // Sanity check completionseconds is > 0.
        DataToken csToken = dict["completionseconds"];
        if (csToken.TokenType == TokenType.Double)
        {
            if (!(csToken.Double > 0d)) return false;
        }
        else if (csToken.TokenType == TokenType.Int)
        {
            if (!(csToken.Int > 0)) return false;
        }
        else
        {
            return false;
        }

        return true;
    }

    // UdonSharp does not support try/catch. This decoder returns false on invalid input.
    // Supports standard base64 (+/) and URL-safe base64 (-_).
    private bool _TryDecodeBase64(string input, out byte[] output)
    {
        output = null;
        if (string.IsNullOrEmpty(input)) return false;

        // Remove whitespace (defensive). Also normalize URL-safe alphabet.
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

        // Count padding.
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

            if (outIndex < outLen)
            {
                bytes[outIndex++] = (byte)((triple >> 16) & 0xFF);
            }
            if (v2 >= 0 && outIndex < outLen)
            {
                bytes[outIndex++] = (byte)((triple >> 8) & 0xFF);
            }
            if (v3 >= 0 && outIndex < outLen)
            {
                bytes[outIndex++] = (byte)(triple & 0xFF);
            }
        }

        output = bytes;
        return true;
    }

    // Returns 0..63 for valid base64 chars; -1 for invalid.
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
