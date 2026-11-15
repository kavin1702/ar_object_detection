using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using CADInspection.Visualization;
using CADInspection.Measurement;

namespace CADInspection.Detection
{
    /// <summary>
    /// Automatic Image-Based AR Inspection Controller.
    /// Uses ARTrackedImageManager to recognize the physical target (e.g. notebook cover or marker),
    /// then automatically snaps the precision CAD wireframe outline and 3D dimensions with zero manual alignment.
    /// </summary>
    public class ARImageInspectionController : MonoBehaviour
    {
        [Header("Active Target Profile")]
        [SerializeField] private ARTargetProfile m_TargetProfile;

        [Header("AR System References")]
        [SerializeField] private ARTrackedImageManager m_TrackedImageManager;
        [SerializeField] private ARAnchorManager m_AnchorManager;
        [SerializeField] private CADWireframeOverlay m_WireframeOverlay;
        [SerializeField] private InspectionEngine m_InspectionEngine;

        private ARAnchor m_CurrentAnchor;
        private bool m_IsLocked = false;
        private TrackableId m_CurrentTrackedId = TrackableId.invalidId;

        public ARTargetProfile CurrentProfile => m_TargetProfile;
        public bool IsLocked => m_IsLocked;

#pragma warning disable CS0067
        public event System.Action<DimensionRecord[]> OnInspectionEvaluated;
#pragma warning restore CS0067
        public event System.Action<string, float, float, float> OnTargetDetected;
        public event System.Action OnTargetLost;

        private void Awake()
        {
            if (m_TrackedImageManager == null)
                m_TrackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();

            if (m_WireframeOverlay == null)
                m_WireframeOverlay = FindFirstObjectByType<CADWireframeOverlay>();

            if (m_InspectionEngine == null)
                m_InspectionEngine = FindFirstObjectByType<InspectionEngine>();

            ApplyTargetProfile();
        }

        private void OnEnable()
        {
            if (m_TrackedImageManager != null)
            {
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            }
        }

        private void OnDisable()
        {
            if (m_TrackedImageManager != null)
            {
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            }
        }

        public void SetTargetProfile(ARTargetProfile profile)
        {
            m_TargetProfile = profile;
            ApplyTargetProfile();
        }

        public void ApplyTargetProfile()
        {
            if (m_TargetProfile != null && m_TrackedImageManager != null && m_TargetProfile.imageLibrary != null)
            {
                m_TrackedImageManager.referenceLibrary = m_TargetProfile.imageLibrary;
                m_TrackedImageManager.requestedMaxNumberOfMovingImages = 1;
            }
        }

        private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            // Only lock onto ONE target
            if (m_IsLocked) return;

            foreach (var trackedImage in args.added)
            {
                HandleImageTracked(trackedImage);
                return;
            }

            foreach (var trackedImage in args.updated)
            {
                if (trackedImage.trackingState == TrackingState.Tracking)
                {
                    HandleImageTracked(trackedImage);
                    return;
                }
            }
        }

        private void HandleImageTracked(ARTrackedImage image)
        {
            if (image == null || image.trackingState != TrackingState.Tracking) return;

            m_IsLocked = true;
            m_CurrentTrackedId = image.trackableId;

            float lengthMm = m_TargetProfile != null ? m_TargetProfile.lengthMm : 230f;
            float widthMm = m_TargetProfile != null ? m_TargetProfile.widthMm : 154f;
            float thicknessMm = m_TargetProfile != null ? m_TargetProfile.thicknessMm : 50f;

            // 1. Attach Anchor at tracked image pose
            if (m_AnchorManager != null)
            {
                var anchorObj = new GameObject("Locked_Image_Anchor");
                anchorObj.transform.position = image.transform.position;
                anchorObj.transform.rotation = image.transform.rotation;
                m_CurrentAnchor = anchorObj.AddComponent<ARAnchor>();
            }

            Transform anchorTransform = m_CurrentAnchor != null ? m_CurrentAnchor.transform : image.transform;

            // 2. Display pure wireframe outline projected from cover to desk
            if (m_WireframeOverlay != null)
            {
                m_WireframeOverlay.DisplayOverlayForImageTarget(
                    anchorTransform, 
                    image.transform.position, 
                    image.transform.rotation, 
                    lengthMm, 
                    widthMm, 
                    thicknessMm);
            }

            // Metrology pass/fail evaluation disabled - displaying pure 3D dimensions above model
            string modelName = m_TargetProfile != null ? m_TargetProfile.modelName : "Target Model";
            OnTargetDetected?.Invoke(modelName, lengthMm, widthMm, thicknessMm);
        }

        public void ResetScan()
        {
            m_IsLocked = false;
            m_CurrentTrackedId = TrackableId.invalidId;

            if (m_CurrentAnchor != null)
            {
                Destroy(m_CurrentAnchor.gameObject);
                m_CurrentAnchor = null;
            }

            if (m_WireframeOverlay != null)
            {
                m_WireframeOverlay.HideOverlay();
            }

            OnTargetLost?.Invoke();
        }
    }
}
