
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class eyeofhorusd : UdonSharpBehaviour
{
    [Header("References")]
    [Tooltip("Statue 1 root GameObject (rotation checked on its Transform).")]
    public GameObject m_Statue1;

    [Tooltip("Statue 2 root GameObject (rotation checked on its Transform).")]
    public GameObject m_Statue2;

    [Tooltip("Laser 1 GameObject; must be activeInHierarchy to count.")]
    public GameObject m_Laser1;

    [Tooltip("Laser 2 GameObject; must be activeInHierarchy to count.")]
    public GameObject m_Laser2;

    [Tooltip("Eye GameObject (child).")]
    public GameObject m_Eye;

    [Tooltip("Bloom GameObject (child).")]
    public GameObject m_Bloom;

    [Header("Angles")]
    [Tooltip("Statue 1 base angle in degrees (local Y).")]
    public float m_Statue1AngleBase;

    [Tooltip("Statue 2 base angle in degrees (local Y).")]
    public float m_Statue2AngleBase;

    [Tooltip("Allowed +/- degrees from base to count as aligned.")]
    public float m_AngleTolerance = 5f;

    [Header("State (read-only)")]
    [System.NonSerialized]
    public bool m_IsStatue1LookingAtEye;

    [System.NonSerialized]
    public bool m_IsStatue2LookingAtEye;

    private bool _eyeActive;
    private bool _bloomActive;
    [System.NonSerialized] private bool m_PuzzleComplete = false;

    private void Start()
    {
        m_IsStatue1LookingAtEye = false;
        m_IsStatue2LookingAtEye = false;
        SetOutputs(false, false);
    }

    private void OnEnable()
    {
        m_IsStatue1LookingAtEye = false;
        m_IsStatue2LookingAtEye = false;
        SetOutputs(false, false);
    }

    private void Update()
    {
        if (m_PuzzleComplete) return;

        // Required behavior:
        // - If exactly one statue is aligned AND its laser is active: Eye on, Bloom off
        // - If both statues are aligned AND both lasers are active: Eye on, Bloom on
        // - Else: both off

        // Expose alignment state for other scripts (e.g., statue motor speed control).
        m_IsStatue1LookingAtEye = IsStatueAligned(m_Statue1, m_Statue1AngleBase, m_AngleTolerance);
        m_IsStatue2LookingAtEye = IsStatueAligned(m_Statue2, m_Statue2AngleBase, m_AngleTolerance);

        bool statue1Ok = m_IsStatue1LookingAtEye && IsActive(m_Laser1);
        bool statue2Ok = m_IsStatue2LookingAtEye && IsActive(m_Laser2);

        bool eye = statue1Ok || statue2Ok;
        bool bloom = statue1Ok && statue2Ok;

        // Enforce the "either 1 or 2, but not both" case for eye-only.
        // When both are true, bloom is true as well.
        SetOutputs(eye, bloom);

        if (eye && bloom)
        {
            m_PuzzleComplete = true;
        }
    }

    private bool IsActive(GameObject go)
    {
        return go != null && go.activeInHierarchy;
    }

    private bool IsStatueAligned(GameObject statue, float baseAngleDeg, float toleranceDeg)
    {
        if (statue == null) return false;
        float y = statue.transform.localEulerAngles.y;
        float tol = Mathf.Max(0f, toleranceDeg);
        return Mathf.Abs(Mathf.DeltaAngle(y, baseAngleDeg)) <= tol;
    }

    private void SetOutputs(bool eye, bool bloom)
    {
        if (m_Eye != null && _eyeActive != eye)
        {
            _eyeActive = eye;
            m_Eye.SetActive(eye);
        }

        if (m_Bloom != null && _bloomActive != bloom)
        {
            _bloomActive = bloom;
            m_Bloom.SetActive(bloom);
        }
    }
}
