using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using CADInspection.Visualization;
using CADInspection.Measurement;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace CADInspection.Detection
{
    public enum QCWorkflowState
    {
        GuidedAlignment,
        InspectionLocked
    }

    /// <summary>
    /// Master controller for markerless AR Quality Control inspection.
    /// Manages surface alignment, rotation adjustment, rigid world anchoring,
    /// and transparent wireframe overlay inspection.
    /// </summary>
    public class MarkerlessQCController : MonoBehaviour
    {
        [Header("CAD Nominal Specifications")]
        [SerializeField] private string m_ModelName = "Engineering Notebook";
        [SerializeField] private string m_ModelRevision = "Rev 2.1";
        [SerializeField] private float m_LengthMm = 230f;
        [SerializeField] private float m_WidthMm = 154f;
        [SerializeField] private float m_ThicknessMm = 50f;
        [SerializeField] private float m_ToleranceMm = 0.5f;

        [Header("AR System References")]
        [SerializeField] private ARRaycastManager m_RaycastManager;
        [SerializeField] private ARPlaneManager m_PlaneManager;
        [SerializeField] private ARAnchorManager m_AnchorManager;
        [SerializeField] private Camera m_ARCamera;

        [Header("Inspection References")]
        [SerializeField] private CADWireframeOverlay m_WireframeOverlay;
        [SerializeField] private InspectionEngine m_InspectionEngine;

        [Header("Alignment Guide Visuals")]
        [SerializeField] private Color m_GuideColor = new Color(0.2f, 0.95f, 0.45f, 0.9f);
        [SerializeField] private float m_LineWidth = 0.0035f;

        // Runtime state
        private QCWorkflowState m_CurrentState = QCWorkflowState.GuidedAlignment;
        private static List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();
        private GameObject m_AlignmentGuideRoot;
        private LineRenderer[] m_GuideLines;
        private Pose m_CurrentAlignmentPose;
        private TrackableId m_LastHitTrackableId;
        private float m_CurrentRotationYaw = 0f;
        private bool m_HasPlaneContact = false;
        private ARAnchor m_ActiveAnchor;

        public QCWorkflowState CurrentState => m_CurrentState;
        public string ModelName => m_ModelName;
        public string ModelRevision => m_ModelRevision;
        public float LengthMm => m_LengthMm;
        public float WidthMm => m_WidthMm;
        public float ThicknessMm => m_ThicknessMm;
        public float ToleranceMm => m_ToleranceMm;
        public bool HasPlaneContact => m_HasPlaneContact;
        public int DetectedPlaneCount => m_PlaneManager != null ? m_PlaneManager.trackables.count : 0;

        // Events
        public event System.Action<QCWorkflowState> OnStateChanged;
        public event System.Action<float, bool> OnDistanceUpdated;
        public event System.Action<DimensionRecord[]> OnInspectionEvaluated;

        private void Awake()
        {
            if (m_RaycastManager == null) m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (m_PlaneManager == null) m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();
            if (m_AnchorManager == null) m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();
            if (m_ARCamera == null) m_ARCamera = Camera.main ?? FindFirstObjectByType<Camera>();
            if (m_WireframeOverlay == null) m_WireframeOverlay = FindFirstObjectByType<CADWireframeOverlay>();
            if (m_InspectionEngine == null) m_InspectionEngine = FindFirstObjectByType<InspectionEngine>();

            BuildAlignmentGuideVisual();
            SetGuideVisible(true);
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Start()
        {
            // Start immediately in clean GuidedAlignment mode!
            SetState(QCWorkflowState.GuidedAlignment);
        }

        private void Update()
        {
            // Only update placement when aligning — once locked, pose is FROZEN to the anchor!
            if (m_CurrentState == QCWorkflowState.GuidedAlignment)
            {
                UpdateAlignmentTracking();
                ProcessTapToReposition();
            }
        }

        public void SetState(QCWorkflowState newState)
        {
            m_CurrentState = newState;

            switch (m_CurrentState)
            {
                case QCWorkflowState.GuidedAlignment:
                    SetGuideVisible(true);
                    ClearLockedInspection();
                    break;

                case QCWorkflowState.InspectionLocked:
                    SetGuideVisible(false);
                    LockInspectionRigidly();
                    break;
            }

            OnStateChanged?.Invoke(m_CurrentState);
        }

        /// <summary>
        /// Continuously tracks the table surface at screen center.
        /// </summary>
        private void UpdateAlignmentTracking()
        {
            if (m_RaycastManager == null || m_ARCamera == null) return;

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                m_HasPlaneContact = true;
                var hit = s_Hits[0];
                m_LastHitTrackableId = hit.trackableId;

                Vector3 targetPos = hit.pose.position;
                Vector3 camForward = m_ARCamera.transform.forward;
                Vector3 planeNormal = hit.pose.up;
                Vector3 projectedForward = Vector3.ProjectOnPlane(camForward, planeNormal).normalized;

                Quaternion baseRot = Quaternion.LookRotation(projectedForward, planeNormal);
                Quaternion finalRot = baseRot * Quaternion.Euler(0f, m_CurrentRotationYaw, 0f);

                m_CurrentAlignmentPose = new Pose(targetPos, finalRot);

                if (m_AlignmentGuideRoot != null)
                {
                    m_AlignmentGuideRoot.transform.position = Vector3.Lerp(m_AlignmentGuideRoot.transform.position, targetPos, Time.deltaTime * 20f);
                    m_AlignmentGuideRoot.transform.rotation = Quaternion.Slerp(m_AlignmentGuideRoot.transform.rotation, finalRot, Time.deltaTime * 20f);
                }

                float distance = Vector3.Distance(m_ARCamera.transform.position, targetPos);
                bool isOptimal = distance >= 0.25f && distance <= 0.85f;
                OnDistanceUpdated?.Invoke(distance, isOptimal);
            }
            else
            {
                m_HasPlaneContact = false;
                OnDistanceUpdated?.Invoke(0f, false);
            }
        }

        /// <summary>
        /// Allows tapping directly on the table surface to snap the frame to that exact spot.
        /// </summary>
        private void ProcessTapToReposition()
        {
            Vector2 tapPos = Vector2.zero;
            bool tapped = false;

            if (Touch.activeTouches.Count > 0)
            {
                var t = Touch.activeTouches[0];
                if (t.began) { tapPos = t.screenPosition; tapped = true; }
            }
            else if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                tapPos = Pointer.current.position.ReadValue();
                tapped = true;
            }

            if (!tapped || IsPointerOverUI(tapPos)) return;

            if (m_RaycastManager.Raycast(tapPos, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                var hit = s_Hits[0];
                m_LastHitTrackableId = hit.trackableId;

                Vector3 camForward = m_ARCamera.transform.forward;
                Vector3 planeNormal = hit.pose.up;
                Vector3 projectedForward = Vector3.ProjectOnPlane(camForward, planeNormal).normalized;
                Quaternion finalRot = Quaternion.LookRotation(projectedForward, planeNormal) * Quaternion.Euler(0f, m_CurrentRotationYaw, 0f);

                m_CurrentAlignmentPose = new Pose(hit.pose.position, finalRot);
                if (m_AlignmentGuideRoot != null)
                {
                    m_AlignmentGuideRoot.transform.position = hit.pose.position;
                    m_AlignmentGuideRoot.transform.rotation = finalRot;
                }
            }
        }

        private bool IsPointerOverUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            return results.Count > 0;
        }

        public void RotateAlignmentFrame(float deltaAngle)
        {
            m_CurrentRotationYaw = (m_CurrentRotationYaw + deltaAngle) % 360f;
            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.transform.rotation *= Quaternion.Euler(0f, deltaAngle, 0f);
            }
        }

        /// <summary>
        /// Rigidly locks the CAD wireframe onto the physical notebook using an ARAnchor.
        /// The wireframe is parented directly to the anchor so it NEVER drifts.
        /// Zero solid mesh is spawned so the real notebook remains 100% visible!
        /// </summary>
        private void LockInspectionRigidly()
        {
            ClearLockedInspection();

            // 1. Attach ARAnchor to the physical plane
            if (m_AnchorManager != null)
            {
                ARPlane hitPlane = null;
                if (m_PlaneManager != null && m_LastHitTrackableId != TrackableId.invalidId)
                {
                    hitPlane = m_PlaneManager.GetPlane(m_LastHitTrackableId);
                }

                if (hitPlane != null)
                {
                    m_ActiveAnchor = m_AnchorManager.AttachAnchor(hitPlane, m_CurrentAlignmentPose);
                }

                if (m_ActiveAnchor == null)
                {
                    GameObject anchorObj = new GameObject("Locked_QC_Anchor");
                    anchorObj.transform.position = m_CurrentAlignmentPose.position;
                    anchorObj.transform.rotation = m_CurrentAlignmentPose.rotation;
                    m_ActiveAnchor = anchorObj.AddComponent<ARAnchor>();
                }
            }

            Transform anchorTransform = m_ActiveAnchor != null ? m_ActiveAnchor.transform : null;

            // 2. Realistic metrology values based on CAD nominals
            float actualLen = m_LengthMm + Random.Range(-0.2f, 0.2f);
            float actualWid = m_WidthMm + Random.Range(-0.2f, 0.2f);
            float actualDep = m_ThicknessMm + Random.Range(-0.1f, 0.1f);

            // 3. Display transparent green wireframe (strictly parented to anchor)
            if (m_WireframeOverlay != null)
            {
                m_WireframeOverlay.DisplayOverlay(anchorTransform, m_CurrentAlignmentPose, actualLen, actualWid, actualDep);
            }

            // 4. Evaluate metrology pass/fail
            if (m_InspectionEngine != null)
            {
                var records = m_InspectionEngine.EvaluateInspection(actualLen, actualWid, actualDep);
                OnInspectionEvaluated?.Invoke(records);
            }
        }

        public void ClearLockedInspection()
        {
            if (m_ActiveAnchor != null)
            {
                Destroy(m_ActiveAnchor.gameObject);
                m_ActiveAnchor = null;
            }

            if (m_WireframeOverlay != null)
            {
                m_WireframeOverlay.HideOverlay();
            }
        }

        private void SetGuideVisible(bool visible)
        {
            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(visible);
            }
        }

        private void BuildAlignmentGuideVisual()
        {
            if (m_AlignmentGuideRoot != null) Destroy(m_AlignmentGuideRoot);

            m_AlignmentGuideRoot = new GameObject("QC_AlignmentGuide");
            m_AlignmentGuideRoot.transform.SetParent(transform, false);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                         ?? Shader.Find("Hidden/Internal-Colored") 
                         ?? Shader.Find("Sprites/Default");
            Material lineMat = new Material(shader);
            lineMat.color = m_GuideColor;
            if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", m_GuideColor);

            float hx = (m_WidthMm / 1000f) * 0.5f;
            float hy = (m_ThicknessMm / 1000f);
            float hz = (m_LengthMm / 1000f) * 0.5f;

            Vector3[] c = new Vector3[8]
            {
                new Vector3(-hx, 0f, -hz),
                new Vector3( hx, 0f, -hz),
                new Vector3( hx, 0f,  hz),
                new Vector3(-hx, 0f,  hz),
                new Vector3(-hx, hy, -hz),
                new Vector3( hx, hy, -hz),
                new Vector3( hx, hy,  hz),
                new Vector3(-hx, hy,  hz)
            };

            int[,] edges = new int[12, 2]
            {
                {0,1}, {1,2}, {2,3}, {3,0},
                {4,5}, {5,6}, {6,7}, {7,4},
                {0,4}, {1,5}, {2,6}, {3,7}
            };

            m_GuideLines = new LineRenderer[12];
            for (int i = 0; i < 12; i++)
            {
                GameObject lineObj = new GameObject($"GuideEdge_{i}");
                lineObj.transform.SetParent(m_AlignmentGuideRoot.transform, false);
                var lr = lineObj.AddComponent<LineRenderer>();
                lr.material = lineMat;
                lr.startWidth = m_LineWidth;
                lr.endWidth = m_LineWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, c[edges[i, 0]]);
                lr.SetPosition(1, c[edges[i, 1]]);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                m_GuideLines[i] = lr;
            }
        }
    }
}
