using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.AR
{
    /// <summary>
    /// Controls and monitors the ARCore / AR Foundation camera session and tracking state.
    /// Meets requirement FR-04 and Subsystem responsibility for ARSessionController.
    /// </summary>
    public class ARSessionController : MonoBehaviour
    {
        [Header("AR Core References")]
        [SerializeField] private ARSession m_ARSession;
        [SerializeField] private ARPlaneManager m_PlaneManager;
        [SerializeField] private ARRaycastManager m_RaycastManager;
        [SerializeField] private ARAnchorManager m_AnchorManager;

        public ARSession Session => m_ARSession;
        public ARPlaneManager PlaneManager => m_PlaneManager;
        public ARRaycastManager RaycastManager => m_RaycastManager;
        public ARAnchorManager AnchorManager => m_AnchorManager;

        public event Action<TrackingState> OnTrackingStateChanged;
        public event Action<ARSessionState> OnSessionStateChanged;

        public ARSessionState CurrentSessionState => ARSession.state;
        public bool IsTrackingReady => ARSession.state == ARSessionState.SessionTracking;

        private void Awake()
        {
            if (m_ARSession == null)
                m_ARSession = FindFirstObjectByType<ARSession>();

            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();

            if (m_AnchorManager == null && m_PlaneManager != null)
            {
                // Ensure anchor manager is present on the XR Origin
                m_AnchorManager = m_PlaneManager.gameObject.AddComponent<ARAnchorManager>();
            }
        }

        private void OnEnable()
        {
            ARSession.stateChanged += HandleSessionStateChanged;
        }

        private void OnDisable()
        {
            ARSession.stateChanged -= HandleSessionStateChanged;
        }

        private void HandleSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            OnSessionStateChanged?.Invoke(args.state);
        }

        public void SetPlaneVisualization(bool visible)
        {
            if (m_PlaneManager == null) return;

            m_PlaneManager.enabled = visible;
            foreach (var plane in m_PlaneManager.trackables)
            {
                plane.gameObject.SetActive(visible);
            }
        }

        public void ResetSession()
        {
            if (m_ARSession != null)
            {
                m_ARSession.Reset();
            }
        }
    }
}
