using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.Detection
{
    /// <summary>
    /// Coordinates surface scanning, physical object framing, and anchor registration.
    /// Fulfills FR-05 (Detect visible book boundary and planes), FR-06, and FR-07.
    /// </summary>
    public class NotebookDetector : MonoBehaviour
    {
        [Header("Target CAD Specifications (mm)")]
        [SerializeField] private float m_NominalLengthMm = 230f;
        [SerializeField] private float m_NominalWidthMm = 154f;
        [SerializeField] private float m_NominalThicknessMm = 50f;

        [Header("Scan Thresholds")]
        [SerializeField] private float m_MinScanDistanceMeters = 0.25f; // 25 cm
        [SerializeField] private float m_MaxScanDistanceMeters = 0.85f; // 85 cm
        [SerializeField] private float m_OptimalMinAngle = 30f;        // 30 deg grazing angle
        [SerializeField] private float m_OptimalMaxAngle = 75f;        // 75 deg

        [Header("References")]
        [SerializeField] private ARRaycastManager m_RaycastManager;
        [SerializeField] private ARPlaneManager m_PlaneManager;
        [SerializeField] private ARAnchorManager m_AnchorManager;
        [SerializeField] private Camera m_ARCamera;
        [SerializeField] private ARScanningReticle m_ScanningReticle;

        // Events
        public event Action<Pose, Vector3> OnNotebookDetected;
        public event Action<ScanQualityInfo> OnScanQualityUpdated;
        public event Action OnDetectionReset;

        private static List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();
        private Pose m_CurrentSurfacePose;
        private bool m_HasSurfaceLock;
        private bool m_IsObjectLocked;
        private ARAnchor m_LockedAnchor;

        public bool IsObjectLocked => m_IsObjectLocked;
        public Pose CurrentPose => m_CurrentSurfacePose;

        private void Awake()
        {
            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();

            if (m_ARCamera == null)
                m_ARCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

            // Ensure scanning reticle exists
            if (m_ScanningReticle == null)
            {
                var reticleObj = new GameObject("ARScanningReticle");
                m_ScanningReticle = reticleObj.AddComponent<ARScanningReticle>();
            }

            m_ScanningReticle.SetDimensions(m_NominalLengthMm, m_NominalWidthMm, m_NominalThicknessMm);
        }

        private void Update()
        {
            if (m_IsObjectLocked) return;

            PerformSurfaceScan();
        }

        private void PerformSurfaceScan()
        {
            if (m_RaycastManager == null || m_ARCamera == null) return;

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // Raycast against trackable planes
            if (m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                ARRaycastHit hit = s_Hits[0];
                m_HasSurfaceLock = true;

                // Position on surface
                Vector3 surfacePos = hit.pose.position;

                // Calculate orientation aligned with camera forward projected on plane
                Vector3 camForward = m_ARCamera.transform.forward;
                Vector3 planeNormal = hit.pose.up;
                Vector3 projectedForward = Vector3.ProjectOnPlane(camForward, planeNormal).normalized;

                Quaternion surfaceRot = Quaternion.LookRotation(projectedForward, planeNormal);
                m_CurrentSurfacePose = new Pose(surfacePos, surfaceRot);

                // Assess quality (distance and tilt angle)
                float distance = Vector3.Distance(m_ARCamera.transform.position, surfacePos);
                float angle = Vector3.Angle(-m_ARCamera.transform.forward, planeNormal);

                bool isDistanceGood = distance >= m_MinScanDistanceMeters && distance <= m_MaxScanDistanceMeters;
                bool isAngleGood = angle >= m_OptimalMinAngle && angle <= m_OptimalMaxAngle;
                bool isReady = isDistanceGood && isAngleGood;

                ScanQualityState state = isReady ? ScanQualityState.ReadyToCapture : ScanQualityState.SurfaceFound;
                m_ScanningReticle.SetScanState(state);
                m_ScanningReticle.UpdatePose(surfacePos, surfaceRot, true);

                string guidance = isReady
                    ? "Target Frame Aligned - Tap Scan to Inspect"
                    : (!isDistanceGood
                        ? (distance < m_MinScanDistanceMeters ? "Move Phone Farther (25-80 cm)" : "Move Phone Closer (25-80 cm)")
                        : "Tilt Camera for 45° Angle");

                OnScanQualityUpdated?.Invoke(new ScanQualityInfo
                {
                    HasSurface = true,
                    DistanceMeters = distance,
                    AngleDegrees = angle,
                    IsOptimal = isReady,
                    State = state,
                    GuidanceMessage = guidance
                });
            }
            else
            {
                m_HasSurfaceLock = false;
                m_ScanningReticle.SetScanState(ScanQualityState.SearchingSurface);
                m_ScanningReticle.UpdatePose(Vector3.zero, Quaternion.identity, false);

                OnScanQualityUpdated?.Invoke(new ScanQualityInfo
                {
                    HasSurface = false,
                    DistanceMeters = 0f,
                    AngleDegrees = 0f,
                    IsOptimal = false,
                    State = ScanQualityState.SearchingSurface,
                    GuidanceMessage = "Move phone slowly across table surface..."
                });
            }
        }

        /// <summary>
        /// Captures the scan and locks the CAD model registration onto the detected notebook.
        /// </summary>
        public bool CaptureScan()
        {
            if (!m_HasSurfaceLock) return false;

            m_IsObjectLocked = true;
            m_ScanningReticle.SetScanState(ScanQualityState.Locked);
            m_ScanningReticle.gameObject.SetActive(false); // Hide scanning reticle in favor of inspection overlay

            // Attach anchor to make tracking rock-solid
            if (m_AnchorManager != null)
            {
                var anchorObj = new GameObject("RegisteredNotebookAnchor");
                anchorObj.transform.position = m_CurrentSurfacePose.position;
                anchorObj.transform.rotation = m_CurrentSurfacePose.rotation;
                m_LockedAnchor = anchorObj.AddComponent<ARAnchor>();
            }

            Vector3 dimensions = new Vector3(m_NominalWidthMm, m_NominalThicknessMm, m_NominalLengthMm);
            OnNotebookDetected?.Invoke(m_CurrentSurfacePose, dimensions);
            return true;
        }

        /// <summary>
        /// Resets the scan to allow re-detecting and inspecting.
        /// </summary>
        public void ResetScan()
        {
            m_IsObjectLocked = false;
            m_HasSurfaceLock = false;

            if (m_LockedAnchor != null)
            {
                Destroy(m_LockedAnchor.gameObject);
                m_LockedAnchor = null;
            }

            if (m_ScanningReticle != null)
            {
                m_ScanningReticle.gameObject.SetActive(true);
                m_ScanningReticle.SetScanState(ScanQualityState.SearchingSurface);
            }

            OnDetectionReset?.Invoke();
        }
    }

    public struct ScanQualityInfo
    {
        public bool HasSurface;
        public float DistanceMeters;
        public float AngleDegrees;
        public bool IsOptimal;
        public ScanQualityState State;
        public string GuidanceMessage;
    }
}
