using System.Text;
using UnityEngine;
using UnityEngine.UI;
using CADInspection.Detection;
using CADInspection.Measurement;

namespace CADInspection.UI
{
    /// <summary>
    /// Streamlined, frictionless UI Manager for AR CAD Inspection.
    /// Opens directly into the AR camera with minimal clutter,
    /// dynamic guidance, and the metrology table upon locking.
    /// </summary>
    public class QCInspectionUIManager : MonoBehaviour
    {
        [Header("Master Controller")]
        [SerializeField] private MarkerlessQCController m_Controller;

        [Header("UI Panels")]
        [SerializeField] private GameObject m_AlignmentPanel;
        [SerializeField] private GameObject m_ResultsPanel;

        [Header("Alignment Controls")]
        [SerializeField] private Text m_ModelPillText;
        [SerializeField] private Text m_GuidanceStatusText;
        [SerializeField] private Text m_DistanceText;
        [SerializeField] private Button m_RotateLeftButton;
        [SerializeField] private Button m_RotateRightButton;
        [SerializeField] private Button m_LockAndInspectButton;

        [Header("Results Table Controls")]
        [SerializeField] private Text m_ResultsTableText;
        [SerializeField] private Button m_RescanButton;

        private void Awake()
        {
            if (m_Controller == null)
                m_Controller = FindFirstObjectByType<MarkerlessQCController>();
        }

        private void OnEnable()
        {
            if (m_Controller != null)
            {
                m_Controller.OnStateChanged += HandleStateChanged;
                m_Controller.OnDistanceUpdated += HandleDistanceUpdated;
                m_Controller.OnInspectionEvaluated += HandleInspectionEvaluated;
            }

            if (m_RotateLeftButton != null)
                m_RotateLeftButton.onClick.AddListener(OnRotateLeftClicked);

            if (m_RotateRightButton != null)
                m_RotateRightButton.onClick.AddListener(OnRotateRightClicked);

            if (m_LockAndInspectButton != null)
                m_LockAndInspectButton.onClick.AddListener(OnLockAndInspectClicked);

            if (m_RescanButton != null)
                m_RescanButton.onClick.AddListener(OnRescanClicked);
        }

        private void OnDisable()
        {
            if (m_Controller != null)
            {
                m_Controller.OnStateChanged -= HandleStateChanged;
                m_Controller.OnDistanceUpdated -= HandleDistanceUpdated;
                m_Controller.OnInspectionEvaluated -= HandleInspectionEvaluated;
            }

            if (m_RotateLeftButton != null)
                m_RotateLeftButton.onClick.RemoveListener(OnRotateLeftClicked);

            if (m_RotateRightButton != null)
                m_RotateRightButton.onClick.RemoveListener(OnRotateRightClicked);

            if (m_LockAndInspectButton != null)
                m_LockAndInspectButton.onClick.RemoveListener(OnLockAndInspectClicked);

            if (m_RescanButton != null)
                m_RescanButton.onClick.RemoveListener(OnRescanClicked);
        }

        private void Start()
        {
            if (m_Controller != null)
            {
                if (m_ModelPillText != null)
                {
                    m_ModelPillText.text = $"TARGET: {m_Controller.ModelName} ({m_Controller.LengthMm:F0}x{m_Controller.WidthMm:F0}x{m_Controller.ThicknessMm:F0}mm)";
                }
                HandleStateChanged(m_Controller.CurrentState);
            }
        }

        private void HandleStateChanged(QCWorkflowState state)
        {
            if (m_AlignmentPanel != null)
                m_AlignmentPanel.SetActive(state == QCWorkflowState.GuidedAlignment);

            if (m_ResultsPanel != null)
                m_ResultsPanel.SetActive(state == QCWorkflowState.InspectionLocked);
        }

        private void HandleDistanceUpdated(float distMeters, bool isOptimal)
        {
            if (m_DistanceText != null)
            {
                if (distMeters <= 0f)
                {
                    m_DistanceText.text = "Aim at table surface...";
                    m_DistanceText.color = Color.yellow;
                }
                else
                {
                    m_DistanceText.text = $"Dist: {(distMeters * 100f):F0} cm " + (isOptimal ? "(Optimal)" : "");
                    m_DistanceText.color = isOptimal ? Color.green : Color.cyan;
                }
            }

            if (m_GuidanceStatusText != null)
            {
                if (distMeters <= 0f)
                {
                    m_GuidanceStatusText.text = "Move phone slowly across table to detect surface...";
                    m_GuidanceStatusText.color = Color.yellow;
                }
                else
                {
                    m_GuidanceStatusText.text = "Align frame over your physical notebook, then tap LOCK.";
                    m_GuidanceStatusText.color = Color.white;
                }
            }
        }

        private void HandleInspectionEvaluated(DimensionRecord[] records)
        {
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

        private void OnRotateLeftClicked()
        {
            if (m_Controller != null) m_Controller.RotateAlignmentFrame(-15f);
        }

        private void OnRotateRightClicked()
        {
            if (m_Controller != null) m_Controller.RotateAlignmentFrame(15f);
        }

        private void OnLockAndInspectClicked()
        {
            if (m_Controller != null) m_Controller.SetState(QCWorkflowState.InspectionLocked);
        }

        private void OnRescanClicked()
        {
            if (m_Controller != null) m_Controller.SetState(QCWorkflowState.GuidedAlignment);
        }
    }
}
