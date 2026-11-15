using System;
using UnityEngine;
using UnityEngine.UI;
using CADInspection.Detection;

namespace CADInspection.UI
{
    /// <summary>
    /// Streamlined AR HUD for Note CAD Projection.
    /// Manages real-time alignment status, model identification pill,
    /// manual rotation adjustment buttons, and the dual-state Lock/Rescan control.
    /// Strictly excludes any pass/fail, test result, or tolerance mentions.
    /// </summary>
    public class NoteMarkerHUD : MonoBehaviour
    {
        [Header("Tracker Reference")]
        [SerializeField] private NoteMarkerTracker m_Tracker;

        [Header("UI Status Elements")]
        [SerializeField] private Text m_StatusText;
        [SerializeField] private Text m_ModelPillText;
        [SerializeField] private Text m_DistanceText;

        [Header("Controls")]
        [SerializeField] private GameObject m_BottomControlsBar;
        [SerializeField] private Button m_RotateLeftButton;
        [SerializeField] private Button m_RotateRightButton;
        [SerializeField] private Button m_LockAndInspectButton;
        [SerializeField] private Text m_LockAndInspectLabel;

        private void Awake()
        {
            if (m_Tracker == null)
                m_Tracker = FindFirstObjectByType<NoteMarkerTracker>();

            AutoWireMissingControls();
        }

        private void AutoWireMissingControls()
        {
            if (m_BottomControlsBar == null)
            {
                var bar = transform.Find("AlignmentPanel/BottomControlsBar");
                if (bar != null) m_BottomControlsBar = bar.gameObject;
            }

            if (m_RotateLeftButton == null && m_BottomControlsBar != null)
            {
                var btn = m_BottomControlsBar.transform.Find("RotateLeftButton")?.GetComponent<Button>();
                if (btn != null) m_RotateLeftButton = btn;
            }

            if (m_RotateRightButton == null && m_BottomControlsBar != null)
            {
                var btn = m_BottomControlsBar.transform.Find("RotateRightButton")?.GetComponent<Button>();
                if (btn != null) m_RotateRightButton = btn;
            }

            if (m_LockAndInspectButton == null && m_BottomControlsBar != null)
            {
                var btn = m_BottomControlsBar.transform.Find("LockAndInspectButton")?.GetComponent<Button>();
                if (btn != null) m_LockAndInspectButton = btn;
            }

            if (m_LockAndInspectLabel == null && m_LockAndInspectButton != null)
            {
                var txt = m_LockAndInspectButton.GetComponentInChildren<Text>();
                if (txt != null) m_LockAndInspectLabel = txt;
            }
        }

        private void OnEnable()
        {
            AutoWireMissingControls();

            if (m_Tracker != null)
            {
                m_Tracker.OnNoteDetected += HandleNoteDetected;
                m_Tracker.OnNoteLost += HandleNoteLost;
                m_Tracker.OnAlignmentUpdated += HandleAlignmentUpdated;
            }

            if (m_RotateLeftButton != null)
            {
                m_RotateLeftButton.onClick.RemoveAllListeners();
                m_RotateLeftButton.onClick.AddListener(OnRotateLeftClicked);
            }

            if (m_RotateRightButton != null)
            {
                m_RotateRightButton.onClick.RemoveAllListeners();
                m_RotateRightButton.onClick.AddListener(OnRotateRightClicked);
            }

            if (m_LockAndInspectButton != null)
            {
                m_LockAndInspectButton.onClick.RemoveAllListeners();
                m_LockAndInspectButton.onClick.AddListener(OnLockOrRescanClicked);
            }

            UpdateInitialUI();
        }

        private void OnDisable()
        {
            if (m_Tracker != null)
            {
                m_Tracker.OnNoteDetected -= HandleNoteDetected;
                m_Tracker.OnNoteLost -= HandleNoteLost;
                m_Tracker.OnAlignmentUpdated -= HandleAlignmentUpdated;
            }

            if (m_RotateLeftButton != null)
                m_RotateLeftButton.onClick.RemoveListener(OnRotateLeftClicked);

            if (m_RotateRightButton != null)
                m_RotateRightButton.onClick.RemoveListener(OnRotateRightClicked);

            if (m_LockAndInspectButton != null)
                m_LockAndInspectButton.onClick.RemoveListener(OnLockOrRescanClicked);
        }

        private void UpdateInitialUI()
        {
            if (m_Tracker != null && m_Tracker.IsLocked)
            {
                HandleNoteDetected(m_Tracker.Profile);
            }
            else
            {
                HandleNoteLost();
            }
        }

        private void HandleNoteDetected(NoteMarkerProfile profile)
        {
            if (m_StatusText != null)
            {
                m_StatusText.text = "Note Locked! 3D Outline & Dimensions Active";
                m_StatusText.color = new Color(0.2f, 0.95f, 0.45f); // Vibrant AR Green
            }

            if (m_ModelPillText != null && profile != null)
            {
                m_ModelPillText.text = $"NOTE: {profile.modelName} ({profile.lengthMm:F0} × {profile.widthMm:F0} × {profile.thicknessMm:F0} mm)";
            }

            if (m_BottomControlsBar != null)
                m_BottomControlsBar.SetActive(true);

            // Hide rotation controls when locked
            if (m_RotateLeftButton != null)
                m_RotateLeftButton.gameObject.SetActive(false);

            if (m_RotateRightButton != null)
                m_RotateRightButton.gameObject.SetActive(false);

            // Switch action button to RESCAN
            if (m_LockAndInspectButton != null)
            {
                m_LockAndInspectButton.gameObject.SetActive(true);
                m_LockAndInspectButton.interactable = true; // RESCAN must always be available
                if (m_LockAndInspectLabel != null)
                    m_LockAndInspectLabel.text = "RESCAN NOTE";
            }
        }

        private void HandleNoteLost()
        {
            if (m_StatusText != null)
            {
#if UNITY_EDITOR
                m_StatusText.text = "Align green box over note & tap LOCK (or press [Space] to test)...";
#else
                bool imageDetection = m_Tracker != null && m_Tracker.Profile != null && m_Tracker.Profile.imageLibrary != null;
                m_StatusText.text = imageDetection
                    ? "Align green box over note, then tap LOCK (or aim at marker cover)..."
                    : "Align green box over note, then tap LOCK...";
#endif
                m_StatusText.color = Color.white;
            }

            if (m_Tracker != null && m_Tracker.Profile != null && m_ModelPillText != null)
            {
                var p = m_Tracker.Profile;
                m_ModelPillText.text = $"TARGET: {p.modelName} ({p.lengthMm:F0} × {p.widthMm:F0} × {p.thicknessMm:F0} mm)";
            }

            if (m_BottomControlsBar != null)
                m_BottomControlsBar.SetActive(true);

            // Show rotation and lock controls during alignment
            if (m_RotateLeftButton != null)
                m_RotateLeftButton.gameObject.SetActive(true);

            if (m_RotateRightButton != null)
                m_RotateRightButton.gameObject.SetActive(true);

            if (m_LockAndInspectButton != null)
            {
                m_LockAndInspectButton.gameObject.SetActive(true);
                if (m_LockAndInspectLabel != null)
                    m_LockAndInspectLabel.text = "LOCK & INSPECT";
            }
        }

        private void HandleAlignmentUpdated(float distanceMeters, bool hasPlane)
        {
            // Without a detected table surface the guide floats in front of the camera, so locking
            // would freeze the outline in mid-air. Allow it in the Editor for testing without AR.
            if (m_LockAndInspectButton != null && m_Tracker != null && !m_Tracker.IsLocked)
            {
                m_LockAndInspectButton.interactable = hasPlane || Application.isEditor;
            }

            // Guidance from the cover-colour auto-detection (found / move back / looking...)
            if (m_StatusText != null && m_Tracker != null && !m_Tracker.IsLocked)
            {
                string hint = m_Tracker.AlignmentHint;
                if (!string.IsNullOrEmpty(hint))
                {
                    m_StatusText.text = hint;
                    m_StatusText.color = m_Tracker.IsAutoDetected ? new Color(0.2f, 0.95f, 0.45f) : Color.white;
                }
            }

            if (m_DistanceText == null) return;

            if (hasPlane && distanceMeters > 0f)
            {
                m_DistanceText.gameObject.SetActive(true);
                bool isOptimal = distanceMeters >= 0.25f && distanceMeters <= 0.85f;
                m_DistanceText.text = $"Dist: {(distanceMeters * 100f):F0} cm " + (isOptimal ? "(Optimal)" : "");
                m_DistanceText.color = isOptimal ? Color.green : Color.cyan;
            }
            else
            {
                m_DistanceText.gameObject.SetActive(true);
                m_DistanceText.text = "Aim at table surface...";
                m_DistanceText.color = Color.yellow;
            }
        }

        private void OnRotateLeftClicked()
        {
            if (m_Tracker != null)
                m_Tracker.Rotate(-15f);
        }

        private void OnRotateRightClicked()
        {
            if (m_Tracker != null)
                m_Tracker.Rotate(15f);
        }

        private void OnLockOrRescanClicked()
        {
            if (m_Tracker == null) return;

            if (!m_Tracker.IsLocked)
            {
                m_Tracker.LockCurrentPose();
            }
            else
            {
                m_Tracker.ResetTracking();
            }
        }
    }
}
