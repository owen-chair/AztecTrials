using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using VRC.SDK3.StringLoading;
using VRC.SDK3.Data;
using VRC.Udon.Common.Interfaces;
using UnityEngine.UI;

public class PaginatedRanksHandler : UdonSharpBehaviour
{
    [Header("Page URLs (0..99)")]
    [Tooltip("Inspector-provided URLs for each page, where each URL is /data/page/{base64_json} with {clientkey,page}.\nThese are set by an editor tool.")]
    [SerializeField] private VRCUrl m_PageUrl0;
    [SerializeField] private VRCUrl m_PageUrl1;
    [SerializeField] private VRCUrl m_PageUrl2;
    [SerializeField] private VRCUrl m_PageUrl3;
    [SerializeField] private VRCUrl m_PageUrl4;
    [SerializeField] private VRCUrl m_PageUrl5;
    [SerializeField] private VRCUrl m_PageUrl6;
    [SerializeField] private VRCUrl m_PageUrl7;
    [SerializeField] private VRCUrl m_PageUrl8;
    [SerializeField] private VRCUrl m_PageUrl9;
    [SerializeField] private VRCUrl m_PageUrl10;
    [SerializeField] private VRCUrl m_PageUrl11;
    [SerializeField] private VRCUrl m_PageUrl12;
    [SerializeField] private VRCUrl m_PageUrl13;
    [SerializeField] private VRCUrl m_PageUrl14;
    [SerializeField] private VRCUrl m_PageUrl15;
    [SerializeField] private VRCUrl m_PageUrl16;
    [SerializeField] private VRCUrl m_PageUrl17;
    [SerializeField] private VRCUrl m_PageUrl18;
    [SerializeField] private VRCUrl m_PageUrl19;
    [SerializeField] private VRCUrl m_PageUrl20;
    [SerializeField] private VRCUrl m_PageUrl21;
    [SerializeField] private VRCUrl m_PageUrl22;
    [SerializeField] private VRCUrl m_PageUrl23;
    [SerializeField] private VRCUrl m_PageUrl24;
    [SerializeField] private VRCUrl m_PageUrl25;
    [SerializeField] private VRCUrl m_PageUrl26;
    [SerializeField] private VRCUrl m_PageUrl27;
    [SerializeField] private VRCUrl m_PageUrl28;
    [SerializeField] private VRCUrl m_PageUrl29;
    [SerializeField] private VRCUrl m_PageUrl30;
    [SerializeField] private VRCUrl m_PageUrl31;
    [SerializeField] private VRCUrl m_PageUrl32;
    [SerializeField] private VRCUrl m_PageUrl33;
    [SerializeField] private VRCUrl m_PageUrl34;
    [SerializeField] private VRCUrl m_PageUrl35;
    [SerializeField] private VRCUrl m_PageUrl36;
    [SerializeField] private VRCUrl m_PageUrl37;
    [SerializeField] private VRCUrl m_PageUrl38;
    [SerializeField] private VRCUrl m_PageUrl39;
    [SerializeField] private VRCUrl m_PageUrl40;
    [SerializeField] private VRCUrl m_PageUrl41;
    [SerializeField] private VRCUrl m_PageUrl42;
    [SerializeField] private VRCUrl m_PageUrl43;
    [SerializeField] private VRCUrl m_PageUrl44;
    [SerializeField] private VRCUrl m_PageUrl45;
    [SerializeField] private VRCUrl m_PageUrl46;
    [SerializeField] private VRCUrl m_PageUrl47;
    [SerializeField] private VRCUrl m_PageUrl48;
    [SerializeField] private VRCUrl m_PageUrl49;
    [SerializeField] private VRCUrl m_PageUrl50;
    [SerializeField] private VRCUrl m_PageUrl51;
    [SerializeField] private VRCUrl m_PageUrl52;
    [SerializeField] private VRCUrl m_PageUrl53;
    [SerializeField] private VRCUrl m_PageUrl54;
    [SerializeField] private VRCUrl m_PageUrl55;
    [SerializeField] private VRCUrl m_PageUrl56;
    [SerializeField] private VRCUrl m_PageUrl57;
    [SerializeField] private VRCUrl m_PageUrl58;
    [SerializeField] private VRCUrl m_PageUrl59;
    [SerializeField] private VRCUrl m_PageUrl60;
    [SerializeField] private VRCUrl m_PageUrl61;
    [SerializeField] private VRCUrl m_PageUrl62;
    [SerializeField] private VRCUrl m_PageUrl63;
    [SerializeField] private VRCUrl m_PageUrl64;
    [SerializeField] private VRCUrl m_PageUrl65;
    [SerializeField] private VRCUrl m_PageUrl66;
    [SerializeField] private VRCUrl m_PageUrl67;
    [SerializeField] private VRCUrl m_PageUrl68;
    [SerializeField] private VRCUrl m_PageUrl69;
    [SerializeField] private VRCUrl m_PageUrl70;
    [SerializeField] private VRCUrl m_PageUrl71;
    [SerializeField] private VRCUrl m_PageUrl72;
    [SerializeField] private VRCUrl m_PageUrl73;
    [SerializeField] private VRCUrl m_PageUrl74;
    [SerializeField] private VRCUrl m_PageUrl75;
    [SerializeField] private VRCUrl m_PageUrl76;
    [SerializeField] private VRCUrl m_PageUrl77;
    [SerializeField] private VRCUrl m_PageUrl78;
    [SerializeField] private VRCUrl m_PageUrl79;
    [SerializeField] private VRCUrl m_PageUrl80;
    [SerializeField] private VRCUrl m_PageUrl81;
    [SerializeField] private VRCUrl m_PageUrl82;
    [SerializeField] private VRCUrl m_PageUrl83;
    [SerializeField] private VRCUrl m_PageUrl84;
    [SerializeField] private VRCUrl m_PageUrl85;
    [SerializeField] private VRCUrl m_PageUrl86;
    [SerializeField] private VRCUrl m_PageUrl87;
    [SerializeField] private VRCUrl m_PageUrl88;
    [SerializeField] private VRCUrl m_PageUrl89;
    [SerializeField] private VRCUrl m_PageUrl90;
    [SerializeField] private VRCUrl m_PageUrl91;
    [SerializeField] private VRCUrl m_PageUrl92;
    [SerializeField] private VRCUrl m_PageUrl93;
    [SerializeField] private VRCUrl m_PageUrl94;
    [SerializeField] private VRCUrl m_PageUrl95;
    [SerializeField] private VRCUrl m_PageUrl96;
    [SerializeField] private VRCUrl m_PageUrl97;
    [SerializeField] private VRCUrl m_PageUrl98;
    [SerializeField] private VRCUrl m_PageUrl99;

    [Header("UI Outputs (visible window size 10)")]
    [Tooltip("10 TMP_Text references for rank numbers (index 0 = first visible rank line)")]
    public TMP_Text[] m_RankTexts;

    [Tooltip("10 TMP_Text references for player names (index 0 = first visible rank line)")]
    public TMP_Text[] m_PlayerNameTexts;

    [Tooltip("10 TMP_Text references for player times (index 0 = first visible rank line)")]
    public TMP_Text[] m_PlayerTimeTexts;

    [Header("Pagination Buttons")]
    [Tooltip("Shown when scrollbar is at the end (10th step).")]
    public GameObject m_NextPageButton;

    [Tooltip("Shown when current page > 0.")]
    public GameObject m_PrevPageButton;

    [Header("Scrollbar")]
    [Tooltip("Assign the Unity UI Scrollbar. The parameterless OnScrollbarValueChanged() reads Scrollbar.value.")]
    public Scrollbar m_Scrollbar;

    private const int PAGE_SIZE = 100;
    private const int WINDOW_SIZE = 10;
    private const int SCROLL_STEPS = 10; // 10 steps for 100 ranks => 10 ranks per step
    private const int MAX_PAGES = 100;

    private int _currentPage;
    private int _currentStep;

    private string[] _pageNames;
    private double[] _pageTimes;
    private bool _hasPageData;

    private bool _initialized;
    private int _enableSerial;
    private int _scheduledEnableSerial;


    void Awake()
    {
        _EnsureInit();
    }

    void Start()
    {
        _EnsureInit();
    }

    void OnEnable()
    {
        _EnsureInit();

        // Reset visible window to the top on every enable.
        _currentStep = 0;
        _UpdateButtons();
        _RenderWindow();

        // VRChat safety: delay URL requests after enabling.
        _enableSerial++;
        _scheduledEnableSerial = _enableSerial;
        SendCustomEventDelayedSeconds(nameof(_RefreshAfterEnable), 6f);
    }

    public void _RefreshAfterEnable()
    {
        // Ignore stale scheduled events from previous enable cycles.
        if (_scheduledEnableSerial != _enableSerial) return;
        if (!gameObject.activeInHierarchy) return;

        Refresh();
    }

    public void Refresh()
    {
        _EnsureInit();
        _RequestPage(_currentPage);
    }

    public void NextPage()
    {
        _currentPage++;
        _UpdateButtons();
        _RequestPage(_currentPage);
    }

    public void PrevPage()
    {
        if (_currentPage <= 0) return;
        _currentPage--;
        _UpdateButtons();
        _RequestPage(_currentPage);
    }

    public void OnScrollbarValueChanged()
    {
        if (m_Scrollbar == null)
        {
            Debug.LogError("[PagedRankRequestHandler] Missing m_Scrollbar (assign it in inspector)");
            return;
        }

        OnScrollbarValueChanged(m_Scrollbar.value);
    }

    // Hook this to your Scrollbar "On Value Changed (Single)".
    // Expecting 10 steps: value will be treated as 0..1 mapped into 0..9.
    public void OnScrollbarValueChanged(float value)
    {
        int step = Mathf.RoundToInt(value * (SCROLL_STEPS - 1));
        if (step < 0) step = 0;
        if (step > (SCROLL_STEPS - 1)) step = (SCROLL_STEPS - 1);

        _currentStep = step;
        _UpdateButtons();
        _RenderWindow();
    }

    private void _RequestPage(int page)
    {
        _EnsureInit();
        _ClearWindow();
        _ClearPageCache();

        if (page < 0 || page >= MAX_PAGES)
        {
            Debug.LogError("[PagedRankRequestHandler] Page out of range: " + page);
            return;
        }

        VRCUrl pageUrl;
        if (!_TryGetPageUrl(page, out pageUrl))
        {
            Debug.LogError("[PagedRankRequestHandler] Missing page URL field for page: " + page);
            return;
        }

        string urlString = pageUrl.Get();
        if (string.IsNullOrEmpty(urlString))
        {
            Debug.LogError("[PagedRankRequestHandler] Missing page URL at index " + page + " (set it in inspector)");
            return;
        }

        string lower = urlString.ToLower();
        if (!lower.StartsWith("https://"))
        {
            Debug.LogError("[PagedRankRequestHandler] Page URL must be https:// (VRChat blocks insecure http://)");
            return;
        }

        VRCStringDownloader.LoadUrl(pageUrl, (IUdonEventReceiver)this);
    }

    private bool _TryGetPageUrl(int page, out VRCUrl pageUrl)
    {
        pageUrl = default(VRCUrl);
        switch (page)
        {
            case 0: pageUrl = m_PageUrl0; return true;
            case 1: pageUrl = m_PageUrl1; return true;
            case 2: pageUrl = m_PageUrl2; return true;
            case 3: pageUrl = m_PageUrl3; return true;
            case 4: pageUrl = m_PageUrl4; return true;
            case 5: pageUrl = m_PageUrl5; return true;
            case 6: pageUrl = m_PageUrl6; return true;
            case 7: pageUrl = m_PageUrl7; return true;
            case 8: pageUrl = m_PageUrl8; return true;
            case 9: pageUrl = m_PageUrl9; return true;
            case 10: pageUrl = m_PageUrl10; return true;
            case 11: pageUrl = m_PageUrl11; return true;
            case 12: pageUrl = m_PageUrl12; return true;
            case 13: pageUrl = m_PageUrl13; return true;
            case 14: pageUrl = m_PageUrl14; return true;
            case 15: pageUrl = m_PageUrl15; return true;
            case 16: pageUrl = m_PageUrl16; return true;
            case 17: pageUrl = m_PageUrl17; return true;
            case 18: pageUrl = m_PageUrl18; return true;
            case 19: pageUrl = m_PageUrl19; return true;
            case 20: pageUrl = m_PageUrl20; return true;
            case 21: pageUrl = m_PageUrl21; return true;
            case 22: pageUrl = m_PageUrl22; return true;
            case 23: pageUrl = m_PageUrl23; return true;
            case 24: pageUrl = m_PageUrl24; return true;
            case 25: pageUrl = m_PageUrl25; return true;
            case 26: pageUrl = m_PageUrl26; return true;
            case 27: pageUrl = m_PageUrl27; return true;
            case 28: pageUrl = m_PageUrl28; return true;
            case 29: pageUrl = m_PageUrl29; return true;
            case 30: pageUrl = m_PageUrl30; return true;
            case 31: pageUrl = m_PageUrl31; return true;
            case 32: pageUrl = m_PageUrl32; return true;
            case 33: pageUrl = m_PageUrl33; return true;
            case 34: pageUrl = m_PageUrl34; return true;
            case 35: pageUrl = m_PageUrl35; return true;
            case 36: pageUrl = m_PageUrl36; return true;
            case 37: pageUrl = m_PageUrl37; return true;
            case 38: pageUrl = m_PageUrl38; return true;
            case 39: pageUrl = m_PageUrl39; return true;
            case 40: pageUrl = m_PageUrl40; return true;
            case 41: pageUrl = m_PageUrl41; return true;
            case 42: pageUrl = m_PageUrl42; return true;
            case 43: pageUrl = m_PageUrl43; return true;
            case 44: pageUrl = m_PageUrl44; return true;
            case 45: pageUrl = m_PageUrl45; return true;
            case 46: pageUrl = m_PageUrl46; return true;
            case 47: pageUrl = m_PageUrl47; return true;
            case 48: pageUrl = m_PageUrl48; return true;
            case 49: pageUrl = m_PageUrl49; return true;
            case 50: pageUrl = m_PageUrl50; return true;
            case 51: pageUrl = m_PageUrl51; return true;
            case 52: pageUrl = m_PageUrl52; return true;
            case 53: pageUrl = m_PageUrl53; return true;
            case 54: pageUrl = m_PageUrl54; return true;
            case 55: pageUrl = m_PageUrl55; return true;
            case 56: pageUrl = m_PageUrl56; return true;
            case 57: pageUrl = m_PageUrl57; return true;
            case 58: pageUrl = m_PageUrl58; return true;
            case 59: pageUrl = m_PageUrl59; return true;
            case 60: pageUrl = m_PageUrl60; return true;
            case 61: pageUrl = m_PageUrl61; return true;
            case 62: pageUrl = m_PageUrl62; return true;
            case 63: pageUrl = m_PageUrl63; return true;
            case 64: pageUrl = m_PageUrl64; return true;
            case 65: pageUrl = m_PageUrl65; return true;
            case 66: pageUrl = m_PageUrl66; return true;
            case 67: pageUrl = m_PageUrl67; return true;
            case 68: pageUrl = m_PageUrl68; return true;
            case 69: pageUrl = m_PageUrl69; return true;
            case 70: pageUrl = m_PageUrl70; return true;
            case 71: pageUrl = m_PageUrl71; return true;
            case 72: pageUrl = m_PageUrl72; return true;
            case 73: pageUrl = m_PageUrl73; return true;
            case 74: pageUrl = m_PageUrl74; return true;
            case 75: pageUrl = m_PageUrl75; return true;
            case 76: pageUrl = m_PageUrl76; return true;
            case 77: pageUrl = m_PageUrl77; return true;
            case 78: pageUrl = m_PageUrl78; return true;
            case 79: pageUrl = m_PageUrl79; return true;
            case 80: pageUrl = m_PageUrl80; return true;
            case 81: pageUrl = m_PageUrl81; return true;
            case 82: pageUrl = m_PageUrl82; return true;
            case 83: pageUrl = m_PageUrl83; return true;
            case 84: pageUrl = m_PageUrl84; return true;
            case 85: pageUrl = m_PageUrl85; return true;
            case 86: pageUrl = m_PageUrl86; return true;
            case 87: pageUrl = m_PageUrl87; return true;
            case 88: pageUrl = m_PageUrl88; return true;
            case 89: pageUrl = m_PageUrl89; return true;
            case 90: pageUrl = m_PageUrl90; return true;
            case 91: pageUrl = m_PageUrl91; return true;
            case 92: pageUrl = m_PageUrl92; return true;
            case 93: pageUrl = m_PageUrl93; return true;
            case 94: pageUrl = m_PageUrl94; return true;
            case 95: pageUrl = m_PageUrl95; return true;
            case 96: pageUrl = m_PageUrl96; return true;
            case 97: pageUrl = m_PageUrl97; return true;
            case 98: pageUrl = m_PageUrl98; return true;
            case 99: pageUrl = m_PageUrl99; return true;
            default: return false;
        }
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _EnsureInit();
        string responseText = result.Result;
        if (string.IsNullOrEmpty(responseText)) return;

        DataToken token;
        if (!VRCJson.TryDeserializeFromJson(responseText, out token))
        {
            Debug.LogError("[PagedRankRequestHandler] Failed to parse JSON response");
            return;
        }

        if (token.TokenType != TokenType.DataDictionary) return;

        DataDictionary root = token.DataDictionary;
        if (root == null || !root.ContainsKey("players")) return;

        DataToken playersToken = root["players"];
        if (playersToken.TokenType != TokenType.DataList) return;

        DataList players = playersToken.DataList;
        if (players == null) return;

        int count = players.Count;
        if (count > PAGE_SIZE) count = PAGE_SIZE;

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

            _pageNames[i] = playerName;
            _pageTimes[i] = completionSeconds;
        }

        _hasPageData = true;
        _RenderWindow();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        Debug.LogError("[PagedRankRequestHandler] String load failed: " + result.ErrorCode + " " + result.Error);
    }

    private void _ClearPageCache()
    {
        _EnsureInit();
        for (int i = 0; i < PAGE_SIZE; i++)
        {
            _pageNames[i] = "";
            _pageTimes[i] = -1d;
        }
        _hasPageData = false;
    }

    private void _EnsureInit()
    {
        if (_initialized) return;

        _initialized = true;
        _currentPage = 0;
        _currentStep = 0;

        if (_pageNames == null || _pageNames.Length != PAGE_SIZE) _pageNames = new string[PAGE_SIZE];
        if (_pageTimes == null || _pageTimes.Length != PAGE_SIZE) _pageTimes = new double[PAGE_SIZE];
        _hasPageData = false;

        _ClearPageCache();
        _ClearWindow();
        _UpdateButtons();
    }

    private void _RenderWindow()
    {
        int start = _currentStep * WINDOW_SIZE;
        if (start < 0) start = 0;
        if (start > (PAGE_SIZE - WINDOW_SIZE)) start = (PAGE_SIZE - WINDOW_SIZE);

        for (int i = 0; i < WINDOW_SIZE; i++)
        {
            int idx = start + i;

            // Display rank number based on pagination.
            int globalRank = (_currentPage * PAGE_SIZE) + idx + 1;
            _SetRank(i, globalRank.ToString());

            string n = "";
            string t = "";

            if (_hasPageData && idx >= 0 && idx < PAGE_SIZE)
            {
                n = _pageNames[idx];
                t = _FormatSeconds(_pageTimes[idx]);
            }

            _SetName(i, n);
            _SetTime(i, t);
        }

        _UpdateButtons();
    }

    private void _UpdateButtons()
    {
        // Requirement: NextPage only visible when scrollbar is at the end (10th step).
        bool nextVisible = _currentStep >= (SCROLL_STEPS - 1);
        if (m_NextPageButton != null) m_NextPageButton.SetActive(nextVisible);

        // Requirement: PrevPage visible when page > 0.
        bool prevVisible = _currentPage > 0;
        if (m_PrevPageButton != null) m_PrevPageButton.SetActive(prevVisible);
    }

    private void _ClearWindow()
    {
        for (int i = 0; i < WINDOW_SIZE; i++)
        {
            _SetRank(i, "");
            _SetName(i, "");
            _SetTime(i, "");
        }
    }

    private void _SetRank(int index, string value)
    {
        if (m_RankTexts == null) return;
        if (index < 0 || index >= m_RankTexts.Length) return;
        TMP_Text t = m_RankTexts[index];
        if (t != null) t.text = value;
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
