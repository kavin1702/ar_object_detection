using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using CADInspection.Visualization;

namespace CADInspection.Detection
{
    /// <summary>
    /// Dual-mode Hybrid AR Tracker for Notes / Notebooks.
    /// Mode 1: Automatic Image Marker Detection via ARTrackedImageManager.
    /// Mode 2: Real-time Surface Alignment with Rotate Controls & Rigid Table Anchoring.
    /// Commands NoteOutlineRenderer to project the 3D model outline and floating dimension badge.
    /// Includes fail-safe protection against ARAnchorManager MissingReferenceException on teardown.
    /// </summary>
    public class NoteMarkerTracker : MonoBehaviour
    {
        [Header("Configuration Profile")]
        [SerializeField] private NoteMarkerProfile m_Profile;

        [Header("AR Foundation References")]
        [SerializeField] private ARTrackedImageManager m_TrackedImageManager;
        [SerializeField] private ARRaycastManager m_RaycastManager;
        [SerializeField] private ARPlaneManager m_PlaneManager;
        [SerializeField] private ARAnchorManager m_AnchorManager;
        [SerializeField] private Camera m_ARCamera;
        [SerializeField] private NoteOutlineRenderer m_OutlineRenderer;

        // Alignment Visual Guide
        private GameObject m_AlignmentGuideRoot;
        private LineRenderer[] m_GuideLines;
        private Material m_GuideLineMaterial;

        // Runtime Tracking State
        private bool m_IsLocked = false;
        private ARAnchor m_CurrentAnchor;
        private GameObject m_WorldAnchorRoot;
        private GameObject m_SimulatedAnchorRoot;
        private Pose m_CurrentAlignmentPose;
        private TrackableId m_LastHitTrackableId = TrackableId.invalidId;
        private float m_CurrentYaw = 0f;
        private bool m_HasPlaneContact = false;
        private static List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();
        private bool m_IsQuitting = false;

        // Reflection caching for ARAnchorManager.instance safety guard
        private static System.Reflection.PropertyInfo s_AnchorManagerInstanceProp;
        private static bool s_AnchorManagerPropSearched = false;

        // Public Accessors
        public NoteMarkerProfile Profile => m_Profile;
        public bool IsLocked => m_IsLocked;
        public float CurrentYaw => m_CurrentYaw;
        public bool HasPlaneContact => m_HasPlaneContact;
        public Pose CurrentAlignmentPose => m_CurrentAlignmentPose;

        // Events
        public event Action<NoteMarkerProfile> OnNoteDetected;
        public event Action OnNoteLost;
        public event Action<float, bool> OnAlignmentUpdated; // (distanceMeters, hasPlane)

        #region Static ARAnchorManager Safety Helpers
        private static System.Reflection.PropertyInfo GetAnchorManagerInstanceProperty()
        {
            if (!s_AnchorManagerPropSearched)
            {
                s_AnchorManagerPropSearched = true;
                try
                {
                    Type baseType = typeof(ARAnchorManager).BaseType;
                    if (baseType != null)
                    {
                        s_AnchorManagerInstanceProp = baseType.GetProperty("instance", 
                            System.Reflection.BindingFlags.Static | 
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.NonPublic);
                    }
                }
                catch { }
            }
            return s_AnchorManagerInstanceProp;
        }

        public static ARAnchorManager GetAnchorManagerInstance()
        {
            var prop = GetAnchorManagerInstanceProperty();
            if (prop != null)
            {
                try { return prop.GetValue(null) as ARAnchorManager; } catch { }
            }
            return null;
        }

        public static void SetAnchorManagerInstance(ARAnchorManager mgr)
        {
            var prop = GetAnchorManagerInstanceProperty();
            if (prop != null)
            {
                try { prop.SetValue(null, mgr); } catch { }
            }
        }

        /// <summary>
        /// Clears ARAnchorManager.instance if the underlying Unity native object was destroyed or disabled,
        /// preventing ARAnchor.OnDisable() from throwing MissingReferenceException or InvalidOperationException.
        /// </summary>
        public static void SafeClearAnchorManagerInstanceIfInvalid()
        {
            var prop = GetAnchorManagerInstanceProperty();
            if (prop != null)
            {
                try
                {
                    var val = prop.GetValue(null) as UnityEngine.Object;
                    if ((object)val != null && (val == null || !((Behaviour)val).enabled))
                    {
                        prop.SetValue(null, null);
                    }
                }
                catch { }
            }
        }
        #endregion

        private void Awake()
        {
            SafeClearAnchorManagerInstanceIfInvalid();
            Application.quitting += HandleApplicationQuitting;
            FindSceneReferences();
            ApplyReferenceLibrary();
            BuildAlignmentGuideVisual();
        }

        private void HandleApplicationQuitting()
        {
            m_IsQuitting = true;
            SafeClearAnchorManagerInstanceIfInvalid();
            SetAnchorManagerInstance(null);
        }

        private void OnEnable()
        {
            SafeClearAnchorManagerInstanceIfInvalid();
            if (m_TrackedImageManager != null)
            {
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            }

            if (!m_IsLocked && m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(true);
            }
        }

        private void OnDisable()
        {
            SafeClearAnchorManagerInstanceIfInvalid();
            if (m_TrackedImageManager != null)
            {
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            }

            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Application.quitting -= HandleApplicationQuitting;
            SafeClearAnchorManagerInstanceIfInvalid();
            SetAnchorManagerInstance(null);

            if (m_IsQuitting) return;

            if (m_OutlineRenderer != null)
            {
                m_OutlineRenderer.HideOutline();
            }

            if (m_CurrentAnchor != null)
            {
                SafeDestroyAnchor(m_CurrentAnchor);
                m_CurrentAnchor = null;
            }

            if (m_WorldAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_WorldAnchorRoot);
                else DestroyImmediate(m_WorldAnchorRoot);
                m_WorldAnchorRoot = null;
            }

            if (m_SimulatedAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_SimulatedAnchorRoot);
                else DestroyImmediate(m_SimulatedAnchorRoot);
                m_SimulatedAnchorRoot = null;
            }

            if (m_AlignmentGuideRoot != null)
            {
                if (Application.isPlaying) Destroy(m_AlignmentGuideRoot);
                else DestroyImmediate(m_AlignmentGuideRoot);
                m_AlignmentGuideRoot = null;
            }
        }

        private void FindSceneReferences()
        {
            if (m_TrackedImageManager == null)
                m_TrackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();

            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();

            if (m_ARCamera == null)
                m_ARCamera = Camera.main ?? FindFirstObjectByType<Camera>();

            if (m_OutlineRenderer == null)
                m_OutlineRenderer = GetComponent<NoteOutlineRenderer>() ?? FindFirstObjectByType<NoteOutlineRenderer>();
        }

        public void SetProfile(NoteMarkerProfile profile)
        {
            m_Profile = profile;
            ApplyReferenceLibrary();
            if (m_OutlineRenderer != null)
            {
                m_OutlineRenderer.SetProfile(profile);
            }
            BuildAlignmentGuideVisual();
        }

        public void ApplyReferenceLibrary()
        {
            if (m_Profile != null && m_TrackedImageManager != null && m_Profile.imageLibrary != null)
            {
                try
                {
                    if (m_TrackedImageManager.referenceLibrary == null)
                    {
                        m_TrackedImageManager.referenceLibrary = m_Profile.imageLibrary;
                    }
                    m_TrackedImageManager.requestedMaxNumberOfMovingImages = 1;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NoteMarkerTracker] Reference library assignment notice: " + ex.Message);
                }
            }
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Allow quick simulation test with Spacebar in Editor Play Mode
            bool spacePressed = false;
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                spacePressed = UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
            }

            if (spacePressed)
            {
                if (!m_IsLocked)
                    SimulateDetection();
                else
                    ResetTracking();
                return;
            }
#endif

            if (!m_IsLocked)
            {
                UpdateAlignmentTracking();
                ProcessTapToReposition();
            }
        }

        /// <summary>
        /// Continuously tracks the table surface at screen center to position the 3D alignment guide.
        /// </summary>
        private void UpdateAlignmentTracking()
        {
            if (m_ARCamera == null)
            {
                m_ARCamera = Camera.main ?? FindFirstObjectByType<Camera>();
                if (m_ARCamera == null) return;
            }

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            bool planeHit = false;

            if (m_RaycastManager != null)
            {
                if (m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
                {
                    planeHit = true;
                    var hit = s_Hits[0];
                    m_LastHitTrackableId = hit.trackableId;
                    m_HasPlaneContact = true;

                    Vector3 targetPos = hit.pose.position;
                    Vector3 camForward = m_ARCamera.transform.forward;
                    Vector3 planeNormal = hit.pose.up;
                    Vector3 projectedForward = Vector3.ProjectOnPlane(camForward, planeNormal).normalized;

                    Quaternion baseRot = Quaternion.LookRotation(projectedForward, planeNormal);
                    Quaternion finalRot = baseRot * Quaternion.Euler(0f, m_CurrentYaw, 0f);

                    m_CurrentAlignmentPose = new Pose(targetPos, finalRot);

                    float distance = Vector3.Distance(m_ARCamera.transform.position, targetPos);
                    OnAlignmentUpdated?.Invoke(distance, true);
                }
            }

            if (!planeHit)
            {
                m_HasPlaneContact = false;
                // Fallback guide floating 0.65m in front of camera
                Vector3 fallbackPos = m_ARCamera.transform.position + m_ARCamera.transform.forward * 0.65f - m_ARCamera.transform.up * 0.15f;
                Quaternion fallbackRot = Quaternion.Euler(15f, m_ARCamera.transform.eulerAngles.y + m_CurrentYaw, 0f);
                m_CurrentAlignmentPose = new Pose(fallbackPos, fallbackRot);

                OnAlignmentUpdated?.Invoke(0f, false);
            }

            // Smoothly interpolate alignment guide visual
            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(true);
                m_AlignmentGuideRoot.transform.position = Vector3.Lerp(m_AlignmentGuideRoot.transform.position, m_CurrentAlignmentPose.position, Time.deltaTime * 22f);
                m_AlignmentGuideRoot.transform.rotation = Quaternion.Slerp(m_AlignmentGuideRoot.transform.rotation, m_CurrentAlignmentPose.rotation, Time.deltaTime * 22f);
            }
        }

        /// <summary>
        /// Allows tapping directly on the table surface to snap the alignment frame to that exact spot.
        /// </summary>
        private void ProcessTapToReposition()
        {
            if (m_RaycastManager == null) return;

            Vector2 tapPos = Vector2.zero;
            bool tapped = false;

            if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
            {
                var t = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0];
                if (t.began) { tapPos = t.screenPosition; tapped = true; }
            }
            else if (UnityEngine.InputSystem.Pointer.current != null && UnityEngine.InputSystem.Pointer.current.press.wasPressedThisFrame)
            {
                tapPos = UnityEngine.InputSystem.Pointer.current.position.ReadValue();
                tapped = true;
            }

            if (!tapped || IsPointerOverUI(tapPos)) return;

            if (m_RaycastManager.Raycast(tapPos, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                var hit = s_Hits[0];
                m_LastHitTrackableId = hit.trackableId;
                m_HasPlaneContact = true;

                Vector3 targetPos = hit.pose.position;
                Vector3 camForward = m_ARCamera.transform.forward;
                Vector3 planeNormal = hit.pose.up;
                Vector3 projectedForward = Vector3.ProjectOnPlane(camForward, planeNormal).normalized;

                Quaternion baseRot = Quaternion.LookRotation(projectedForward, planeNormal);
                Quaternion finalRot = baseRot * Quaternion.Euler(0f, m_CurrentYaw, 0f);

                m_CurrentAlignmentPose = new Pose(targetPos, finalRot);

                float distance = Vector3.Distance(m_ARCamera.transform.position, targetPos);
                OnAlignmentUpdated?.Invoke(distance, true);
            }
        }

        private bool IsPointerOverUI(Vector2 screenPos)
        {
            if (UnityEngine.EventSystems.EventSystem.current == null) return false;
            var eventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            {
                position = screenPos
            };
            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(eventData, results);
            return results.Count > 0;
        }

        /// <summary>
        /// Rotates the alignment guide frame by the given degrees (e.g. -15° or +15).
        /// </summary>
        public void Rotate(float deltaDegrees)
        {
            m_CurrentYaw = (m_CurrentYaw + deltaDegrees) % 360f;

            if (!m_IsLocked && m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.transform.Rotate(Vector3.up, deltaDegrees, Space.World);
            }
        }

        /// <summary>
        /// Locks onto the physical surface at the current alignment pose,
        /// creates an ARAnchor if supported, and projects the 3D outline box and floating dimension badge.
        /// </summary>
        public void LockCurrentPose()
        {
            if (m_IsLocked) return;

            m_IsLocked = true;

            // Clear previous anchors cleanly
            if (m_CurrentAnchor != null)
            {
                SafeDestroyAnchor(m_CurrentAnchor);
                m_CurrentAnchor = null;
            }
            if (m_WorldAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_WorldAnchorRoot);
                else DestroyImmediate(m_WorldAnchorRoot);
                m_WorldAnchorRoot = null;
            }

            Transform anchorTransform = null;

            // 1. If physical plane is available and ARAnchorManager is running, attach a real ARAnchor
            if (m_PlaneManager != null && m_AnchorManager != null && m_AnchorManager.enabled && 
                m_AnchorManager.subsystem != null && m_AnchorManager.subsystem.running && 
                m_LastHitTrackableId != TrackableId.invalidId)
            {
                try
                {
                    var plane = m_PlaneManager.GetPlane(m_LastHitTrackableId);
                    if (plane != null)
                    {
                        m_CurrentAnchor = m_AnchorManager.AttachAnchor(plane, m_CurrentAlignmentPose);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NoteMarkerTracker] AttachAnchor notice: " + ex.Message);
                }
            }

            if (m_CurrentAnchor != null)
            {
                anchorTransform = m_CurrentAnchor.transform;
            }
            else
            {
                // Fallback: dedicated world-space anchor GameObject (no AddComponent<ARAnchor>() to avoid subsystem crash)
                m_WorldAnchorRoot = new GameObject("Locked_Note_Anchor");
                m_WorldAnchorRoot.transform.position = m_CurrentAlignmentPose.position;
                m_WorldAnchorRoot.transform.rotation = m_CurrentAlignmentPose.rotation;
                anchorTransform = m_WorldAnchorRoot.transform;
            }

            // 2. Hide alignment guide
            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(false);
            }

            // 3. Project 3D Outline and Floating Dimension Badge
            if (m_OutlineRenderer == null)
                m_OutlineRenderer = GetComponent<NoteOutlineRenderer>() ?? FindFirstObjectByType<NoteOutlineRenderer>();

            if (m_OutlineRenderer != null)
            {
                m_OutlineRenderer.ShowOutline(
                    anchorTransform,
                    m_CurrentAlignmentPose.position,
                    m_CurrentAlignmentPose.rotation,
                    m_Profile,
                    anchorAtTopCover: false);
            }

            OnNoteDetected?.Invoke(m_Profile);
            Debug.Log($"<color=#20E060><b>[NoteMarkerTracker]</b> Note Locked at {m_CurrentAlignmentPose.position}!</color>");
        }

        /// <summary>
        /// Automatically locks onto an image marker detected by ARTrackedImageManager.
        /// </summary>
        private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
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

            // Clear previous anchors cleanly
            if (m_CurrentAnchor != null)
            {
                SafeDestroyAnchor(m_CurrentAnchor);
                m_CurrentAnchor = null;
            }
            if (m_WorldAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_WorldAnchorRoot);
                else DestroyImmediate(m_WorldAnchorRoot);
                m_WorldAnchorRoot = null;
            }

            // The ARTrackedImage transform IS the true AR anchor updated every frame by ARCore
            Transform anchorTransform = image.transform;

            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(false);
            }

            if (m_OutlineRenderer == null)
                m_OutlineRenderer = GetComponent<NoteOutlineRenderer>() ?? FindFirstObjectByType<NoteOutlineRenderer>();

            if (m_OutlineRenderer != null)
            {
                m_OutlineRenderer.ShowOutline(
                    anchorTransform,
                    image.transform.position,
                    image.transform.rotation,
                    m_Profile,
                    anchorAtTopCover: true);
            }

            OnNoteDetected?.Invoke(m_Profile);
            Debug.Log("<color=#20E060><b>[NoteMarkerTracker]</b> Image Marker Automatically Tracked & Locked!</color>");
        }

        /// <summary>
        /// Safely destroys an ARAnchor without triggering MissingReferenceException inside ARAnchor.OnDisable().
        /// </summary>
        private void SafeDestroyAnchor(ARAnchor anchor)
        {
            if (anchor == null) return;
            GameObject anchorObj = anchor.gameObject;
            if (anchorObj == null) return;

            // 1. Tell anchor manager to remove anchor if subsystem is alive
            if (m_AnchorManager != null && m_AnchorManager.enabled && m_AnchorManager.subsystem != null && m_AnchorManager.subsystem.running)
            {
                try
                {
                    m_AnchorManager.TryRemoveAnchor(anchor);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[NoteMarkerTracker] TryRemoveAnchor notice: " + ex.Message);
                }
            }

            // 2. Clear static instance temporarily to prevent ARAnchor.OnDisable() from accessing a dead manager
            ARAnchorManager cachedInstance = null;
            try
            {
                cachedInstance = GetAnchorManagerInstance();
                SetAnchorManagerInstance(null);
            }
            catch { }

            try
            {
                if (anchorObj != null)
                {
                    if (Application.isPlaying) Destroy(anchorObj);
                    else DestroyImmediate(anchorObj);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NoteMarkerTracker] Destroy anchor notice: " + ex.Message);
            }
            finally
            {
                // Restore instance if it is still alive and enabled
                if (cachedInstance != null && cachedInstance.enabled)
                {
                    SetAnchorManagerInstance(cachedInstance);
                }
                else
                {
                    SafeClearAnchorManagerInstanceIfInvalid();
                }
            }
        }

        /// <summary>
        /// Resets the scan so the user can re-align and re-scan the note.
        /// </summary>
        public void ResetTracking()
        {
            m_IsLocked = false;

            SafeClearAnchorManagerInstanceIfInvalid();

            if (m_CurrentAnchor != null)
            {
                SafeDestroyAnchor(m_CurrentAnchor);
                m_CurrentAnchor = null;
            }

            if (m_WorldAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_WorldAnchorRoot);
                else DestroyImmediate(m_WorldAnchorRoot);
                m_WorldAnchorRoot = null;
            }

            if (m_SimulatedAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_SimulatedAnchorRoot);
                else DestroyImmediate(m_SimulatedAnchorRoot);
                m_SimulatedAnchorRoot = null;
            }

            if (m_OutlineRenderer != null)
            {
                m_OutlineRenderer.HideOutline();
            }

            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(true);
            }

            OnNoteLost?.Invoke();
        }

        /// <summary>
        /// Simulates marker detection directly in front of the camera (for Editor testing).
        /// </summary>
        public void SimulateDetection()
        {
            if (m_ARCamera == null) m_ARCamera = Camera.main ?? FindFirstObjectByType<Camera>();

            Vector3 spawnPos = m_ARCamera != null 
                ? m_ARCamera.transform.position + m_ARCamera.transform.forward * 0.7f - m_ARCamera.transform.up * 0.15f 
                : new Vector3(0f, 0f, 1f);
            Quaternion spawnRot = Quaternion.Euler(0f, m_ARCamera != null ? m_ARCamera.transform.eulerAngles.y : 0f, 0f);

            m_IsLocked = true;

            if (m_SimulatedAnchorRoot != null)
            {
                if (Application.isPlaying) Destroy(m_SimulatedAnchorRoot);
                else DestroyImmediate(m_SimulatedAnchorRoot);
            }

            m_SimulatedAnchorRoot = new GameObject("Simulated_Note_Anchor");
            m_SimulatedAnchorRoot.transform.position = spawnPos;
            m_SimulatedAnchorRoot.transform.rotation = spawnRot;

            if (m_AlignmentGuideRoot != null)
            {
                m_AlignmentGuideRoot.SetActive(false);
            }

            if (m_OutlineRenderer == null)
                m_OutlineRenderer = GetComponent<NoteOutlineRenderer>() ?? FindFirstObjectByType<NoteOutlineRenderer>();

            if (m_OutlineRenderer != null)
            {
                m_OutlineRenderer.ShowOutline(
                    m_SimulatedAnchorRoot.transform, 
                    spawnPos, 
                    spawnRot, 
                    m_Profile,
                    anchorAtTopCover: false);
            }

            OnNoteDetected?.Invoke(m_Profile);
            Debug.Log("<color=#20E060><b>[NoteMarkerTracker]</b> Simulated Note detection active at " + spawnPos + "</color>");
        }

        /// <summary>
        /// Builds the real-time 3D green alignment wireframe box that tracks the table.
        /// </summary>
        public void BuildAlignmentGuideVisual()
        {
            if (m_AlignmentGuideRoot != null)
            {
                if (Application.isPlaying) Destroy(m_AlignmentGuideRoot);
                else DestroyImmediate(m_AlignmentGuideRoot);
            }

            m_AlignmentGuideRoot = new GameObject("Note_AlignmentGuide");
            m_AlignmentGuideRoot.transform.SetParent(transform, false);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                         ?? Shader.Find("Sprites/Default") 
                         ?? Shader.Find("Mobile/Particles/Alpha Blended")
                         ?? Shader.Find("Hidden/Internal-Colored");

            if (shader != null)
            {
                m_GuideLineMaterial = new Material(shader);
                Color guideColor = m_Profile != null ? m_Profile.outlineColor : new Color(0.12f, 0.95f, 0.42f, 0.9f);
                m_GuideLineMaterial.color = guideColor;
                if (m_GuideLineMaterial.HasProperty("_BaseColor"))
                    m_GuideLineMaterial.SetColor("_BaseColor", guideColor);
            }

            if (m_Profile == null)
            {
                Debug.LogWarning("[NoteMarkerTracker] No NoteMarkerProfile assigned. Configure note dimensions in 'Tools > Note AR Setup Tool'.");
                return;
            }

            float lenMm = m_Profile.lengthMm;
            float widMm = m_Profile.widthMm;
            float thkMm = m_Profile.thicknessMm;
            float lineWidth = m_Profile.lineWidthMm / 1000f;

            float hx = (widMm / 1000f) * 0.5f;
            float hy = (thkMm / 1000f);
            float hz = (lenMm / 1000f) * 0.5f;

            // 8 corners: base on desk (Y=0), top at Y=hy
            Vector3[] c = new Vector3[8]
            {
                new Vector3(-hx, 0f, -hz), // 0: Bottom Back Left
                new Vector3( hx, 0f, -hz), // 1: Bottom Back Right
                new Vector3( hx, 0f,  hz), // 2: Bottom Front Right
                new Vector3(-hx, 0f,  hz), // 3: Bottom Front Left
                new Vector3(-hx, hy, -hz), // 4: Top Back Left
                new Vector3( hx, hy, -hz), // 5: Top Back Right
                new Vector3( hx, hy,  hz), // 6: Top Front Right
                new Vector3(-hx, hy,  hz)  // 7: Top Front Left
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
                lr.material = m_GuideLineMaterial;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, c[edges[i, 0]]);
                lr.SetPosition(1, c[edges[i, 1]]);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                m_GuideLines[i] = lr;
            }

            m_AlignmentGuideRoot.SetActive(!m_IsLocked);
        }
    }
}
