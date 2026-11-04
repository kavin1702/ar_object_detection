using System.Text;
using UnityEngine;
using UnityEngine.UI;
using CADInspection.Detection;
using CADInspection.Measurement;

namespace CADInspection.UI
{
    /// <summary>
    /// Coordinates UI presentation for plane detection status, tap guidance,
    /// and the metrology inspection table matching Page 1 of the spec.
    /// </summary>
    public class ARInspectionHUD : MonoBehaviour
    {
        [Header("System References")]
        [SerializeField] private PlaneTapToSpawnCAD m_TapSpawner;
        [SerializeField] private NotebookDetector m_Detector;
        [SerializeField] private InspectionEngine m_Engine;

        [Header("UI Panels")]
        [SerializeField] private GameObject m_ScanningPanel;
        [SerializeField] private GameObject m_ResultsPanel;

        [Header("Guidance & Status UI")]
        [SerializeField] private Text m_StatusText;
        [SerializeField] private Text m_PlaneCountText;
        [SerializeField] private Button m_CaptureScanButton;

        [Header("Results Table UI")]
        [SerializeField] private Text m_ResultsTableText;
        [SerializeField] private Button m_RescanButton;

        private void Awake()
        {
            if (m_TapSpawner == null) m_TapSpawner = FindFirstObjectByType<PlaneTapToSpawnCAD>();
            if (m_Detector == null) m_Detector = FindFirstObjectByType<NotebookDetector>();
            if (m_Engine == null) m_Engine = FindFirstObjectByType<InspectionEngine>();
        }

        private void OnEnable()
        {
            if (m_TapSpawner != null)
            {
                m_TapSpawner.OnPlanesChanged += HandlePlanesChanged;
                m_TapSpawner.OnObjectSpawned += HandleObjectSpawned;
            }

            if (m_Engine != null)
            {
                m_Engine.OnInspectionCompleted += HandleInspectionCompleted;
            }

            if (m_RescanButton != null)
                m_RescanButton.onClick.AddListener(OnRescanClicked);

            if (m_CaptureScanButton != null)
                m_CaptureScanButton.onClick.AddListener(OnCaptureClicked);
        }

        private void OnDisable()
        {
            if (m_TapSpawner != null)
            {
                m_TapSpawner.OnPlanesChanged -= HandlePlanesChanged;
                m_TapSpawner.OnObjectSpawned -= HandleObjectSpawned;
            }

            if (m_Engine != null)
            {
                m_Engine.OnInspectionCompleted -= HandleInspectionCompleted;
            }

            if (m_RescanButton != null)
                m_RescanButton.onClick.RemoveListener(OnRescanClicked);

            if (m_CaptureScanButton != null)
                m_CaptureScanButton.onClick.RemoveListener(OnCaptureClicked);
        }

        private void Start()
        {
            UpdateGuidanceUI(m_TapSpawner != null ? m_TapSpawner.DetectedPlaneCount : 0);
            if (m_ResultsPanel != null) m_ResultsPanel.SetActive(false);
        }

        private void Update()
        {
            if (m_TapSpawner != null && !m_TapSpawner.HasSpawned)
            {
                UpdateGuidanceUI(m_TapSpawner.DetectedPlaneCount);
            }
        }

        private void HandlePlanesChanged(int count)
        {
            UpdateGuidanceUI(count);
        }

        private void UpdateGuidanceUI(int planeCount)
        {
            if (m_PlaneCountText != null)
            {
                m_PlaneCountText.text = $"Planes Detected: {planeCount}";
                m_PlaneCountText.color = planeCount > 0 ? Color.green : Color.yellow;
            }

            if (m_StatusText != null)
            {
                if (planeCount == 0)
                {
                    m_StatusText.text = "Move phone slowly across table surface to detect plane...";
                    m_StatusText.color = Color.yellow;
                }
                else if (m_TapSpawner != null && m_TapSpawner.HasSpawned)
                {
                    m_StatusText.text = "Notebook Placed! Tap anywhere on plane to reposition.";
                    m_StatusText.color = Color.green;
                }
                else
                {
                    m_StatusText.text = "Plane Detected! TAP ON THE DOTTED SURFACE to place notebook.";
                    m_StatusText.color = Color.cyan;
                }
            }
        }

        private void HandleObjectSpawned(Pose pose)
        {
            if (m_StatusText != null)
            {
                m_StatusText.text = "Notebook Placed! Tap anywhere on plane to reposition.";
                m_StatusText.color = Color.green;
            }
            if (m_ResultsPanel != null) m_ResultsPanel.SetActive(true);
        }

        private void HandleInspectionCompleted(DimensionRecord[] records)
        {
            if (m_ResultsPanel != null) m_ResultsPanel.SetActive(true);

            if (m_ResultsTableText != null && records != null)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<b>Dimension       CAD (mm)   Actual (mm)   Dev (mm)   Result</b>");
                sb.AppendLine("----------------------------------------------------------");
                foreach (var r in records)
                {
                    string sign = r.DeviationMm >= 0 ? "+" : "";
                    string statusColor = r.IsPass ? "#20E060" : "#FF4040";
                    string status = r.IsPass ? $"<color={statusColor}>PASS</color>" : $"<color={statusColor}>FAIL</color>";
                    sb.AppendLine($"{r.Name,-14}  {r.NominalMm,7:F1}   {r.ActualMm,10:F1}   {sign + r.DeviationMm.ToString("F1"),7}   {status}");
                }

                m_ResultsTableText.text = sb.ToString();
            }
        }

        private void OnCaptureClicked()
        {
            // If user taps screen button instead of surface, raycast from screen center
            var cam = Camera.main;
            if (cam != null && m_TapSpawner != null)
            {
                // Trigger tap at center of screen
                Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                var raycastMgr = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();
                if (raycastMgr != null)
                {
                    var hits = new System.Collections.Generic.List<UnityEngine.XR.ARFoundation.ARRaycastHit>();
                    if (raycastMgr.Raycast(center, hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon | UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinBounds))
                    {
                        m_TapSpawner.SpawnOrMoveCADObject(hits[0].pose);
                    }
                }
            }
        }

        private void OnRescanClicked()
        {
            if (m_TapSpawner != null)
            {
                m_TapSpawner.ClearObject();
            }
            if (m_ResultsPanel != null)
            {
                m_ResultsPanel.SetActive(false);
            }
            UpdateGuidanceUI(m_TapSpawner != null ? m_TapSpawner.DetectedPlaneCount : 0);
        }
    }
}
