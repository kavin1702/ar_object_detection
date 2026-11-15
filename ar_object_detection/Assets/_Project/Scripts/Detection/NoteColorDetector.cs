using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.Detection
{
    /// <summary>
    /// Finds the physical notebook in the live camera image by its cover colour (taken from the note image in
    /// the Setup Tool), then turns the 2D outline into a pose on the detected table: corner rays are intersected
    /// with the cover plane (table + thickness), the long edge gives the rotation and the size is checked against
    /// the profile. NoteMarkerTracker snaps the green box to this pose; the user confirms with LOCK.
    /// </summary>
    public class NoteColorDetector : MonoBehaviour
    {
        public enum State
        {
            Disabled,
            WaitingForCamera,
            NoTable,
            NotFound,
            PartiallyVisible,
            NotRectangular,
            SizeMismatch,
            Found
        }

        private const float CaptureIntervalSeconds = 0.15f;
        private const float FreshForSeconds = 0.6f;
        private const int GridWidth = 96;
        private const float MinSizeRatio = 0.6f;
        private const float MaxSizeRatio = 1.6f;

        [SerializeField] private NoteMarkerTracker m_Tracker;
        [SerializeField] private ARCameraManager m_CameraManager;
        [SerializeField] private ARRaycastManager m_RaycastManager;
        [SerializeField] private Camera m_Camera;

        private Matrix4x4? m_DisplayMatrix;
        private NativeArray<byte> m_Buffer;
        private Color32[] m_Grid;
        private int m_GridW, m_GridH;
        private float m_NextCaptureTime;
        private static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        private Pose m_LastPose;
        private TrackableId m_LastPlaneId = TrackableId.invalidId;
        private float m_LastFoundTime = -10f;

        private Texture2D m_DebugTexture;
        private bool[] m_DebugMask;

        public State CurrentState { get; private set; } = State.WaitingForCamera;

        /// <summary>Increments every time the notebook is found, so callers can tell new detections apart.</summary>
        public int FoundCount { get; private set; }
        public Vector2 LastMeasuredSizeMm { get; private set; }

        private NoteMarkerProfile Profile => m_Tracker != null ? m_Tracker.Profile : null;
        private bool FeatureOn => Profile != null && Profile.autoDetectByColor;

        /// <summary>Latest notebook pose (centre on the table, Z along the long edge), if found recently.</summary>
        public bool TryGetFreshPose(out Pose pose, out TrackableId planeId)
        {
            pose = m_LastPose;
            planeId = m_LastPlaneId;
            return FeatureOn && CurrentState == State.Found && Time.unscaledTime - m_LastFoundTime <= FreshForSeconds;
        }

        /// <summary>Short guidance for the HUD, or null when there is nothing specific to say.</summary>
        public string StatusMessage
        {
            get
            {
                switch (CurrentState)
                {
                    case State.Found: return "Notebook found! Check the green box, then tap LOCK.";
                    case State.NotFound: return "Looking for the notebook... point the camera at it.";
                    case State.PartiallyVisible: return "Move back a little so the whole notebook is in view.";
                    case State.NotRectangular: return "Hold steady and look down at the notebook from above.";
                    case State.SizeMismatch: return $"Found something {LastMeasuredSizeMm.x:F0} x {LastMeasuredSizeMm.y:F0} mm - is the note size set correctly?";
                    case State.NoTable: return "Move the phone slowly over the table to detect the surface...";
                    default: return null;
                }
            }
        }

        private void Awake()
        {
            if (m_Tracker == null) m_Tracker = GetComponent<NoteMarkerTracker>();
            if (m_Tracker == null) m_Tracker = FindFirstObjectByType<NoteMarkerTracker>();
            if (m_CameraManager == null) m_CameraManager = FindFirstObjectByType<ARCameraManager>();
            if (m_RaycastManager == null) m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (m_Camera == null)
            {
                m_Camera = m_CameraManager != null ? m_CameraManager.GetComponent<Camera>() : null;
                if (m_Camera == null) m_Camera = Camera.main;
            }
        }

        private void OnEnable()
        {
            if (m_CameraManager != null) m_CameraManager.frameReceived += OnFrameReceived;
        }

        private void OnDisable()
        {
            if (m_CameraManager != null) m_CameraManager.frameReceived -= OnFrameReceived;
        }

        private void OnDestroy()
        {
            if (m_Buffer.IsCreated) m_Buffer.Dispose();
            if (m_DebugTexture != null) Destroy(m_DebugTexture);
        }

        private void OnFrameReceived(ARCameraFrameEventArgs args)
        {
            if (args.displayMatrix.HasValue) m_DisplayMatrix = args.displayMatrix.Value;
        }

        private void Update()
        {
            if (!FeatureOn) { CurrentState = State.Disabled; return; }
            // After LOCK keep running only if the tracker uses it to hold the outline on the notebook
            if (m_Tracker != null && m_Tracker.IsLocked && !Profile.refineAfterLock) return;
            if (Time.unscaledTime < m_NextCaptureTime) return;
            m_NextCaptureTime = Time.unscaledTime + CaptureIntervalSeconds;

            if (!CaptureScreenAlignedImage())
            {
                CurrentState = State.WaitingForCamera;
                return;
            }

            var settings = NoteColorBlobDetector.Settings.FromColor(Profile.coverColor, Profile.colorHueTolerance);
            var result = NoteColorBlobDetector.Detect(m_Grid, m_GridW, m_GridH, settings, Profile.showDetectionDebug);
            m_DebugMask = result.mask;

            switch (result.outcome)
            {
                case NoteColorBlobDetector.Outcome.NoColorMatch: CurrentState = State.NotFound; return;
                case NoteColorBlobDetector.Outcome.PartiallyVisible: CurrentState = State.PartiallyVisible; return;
                case NoteColorBlobDetector.Outcome.NotRectangular: CurrentState = State.NotRectangular; return;
            }

            CurrentState = ReconstructPose(result);
            if (CurrentState == State.Found)
            {
                m_LastFoundTime = Time.unscaledTime;
                FoundCount++;
            }
        }

        /// <summary>
        /// Samples the latest CPU camera image into a small grid that matches what is on screen, using the same
        /// display matrix the AR background shader uses (viewport uv * matrix = camera texture uv, row 0 = top row).
        /// </summary>
        private bool CaptureScreenAlignedImage()
        {
            if (m_CameraManager == null || !m_CameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                return false;

            using (image)
            {
                int factor = Mathf.Max(1, image.width / 192);
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
                {
                    outputDimensions = new Vector2Int(image.width / factor, image.height / factor)
                };

                int size = image.GetConvertedDataSize(conversion);
                if (!m_Buffer.IsCreated || m_Buffer.Length != size)
                {
                    if (m_Buffer.IsCreated) m_Buffer.Dispose();
                    m_Buffer = new NativeArray<byte>(size, Allocator.Persistent);
                }

                try
                {
                    image.Convert(conversion, m_Buffer);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[NoteColorDetector] Camera image conversion failed: " + ex.Message);
                    return false;
                }

                int imgW = conversion.outputDimensions.x;
                int imgH = conversion.outputDimensions.y;
                int stride = size / imgH; // bytes per row (usually imgW * 4)

                m_GridW = GridWidth;
                m_GridH = Mathf.Clamp(Mathf.RoundToInt(GridWidth * (float)Screen.height / Mathf.Max(1, Screen.width)), 16, 256);
                if (m_Grid == null || m_Grid.Length != m_GridW * m_GridH) m_Grid = new Color32[m_GridW * m_GridH];

                bool flip = Profile != null && Profile.flipCameraImageVertically;
                for (int gy = 0; gy < m_GridH; gy++)
                {
                    float v = (gy + 0.5f) / m_GridH;
                    for (int gx = 0; gx < m_GridW; gx++)
                    {
                        float u = (gx + 0.5f) / m_GridW;
                        float s, t;
                        if (m_DisplayMatrix.HasValue)
                        {
                            Matrix4x4 m = m_DisplayMatrix.Value;
                            // Row vector * matrix, exactly as ARCoreBackground.shader does.
                            s = u * m.m00 + v * m.m10 + m.m20;
                            t = u * m.m01 + v * m.m11 + m.m21;
                        }
                        else
                        {
                            s = u;
                            t = 1f - v;
                        }
                        if (flip) t = 1f - t;

                        Color32 c = new Color32(0, 0, 0, 255);
                        if (s >= 0f && s < 1f && t >= 0f && t < 1f)
                        {
                            int px = (int)(s * imgW);
                            int row = (int)(t * imgH);
                            int i = row * stride + px * 4;
                            c = new Color32(m_Buffer[i], m_Buffer[i + 1], m_Buffer[i + 2], 255);
                        }
                        m_Grid[gy * m_GridW + gx] = c;
                    }
                }
            }
            return true;
        }

        private State ReconstructPose(NoteColorBlobDetector.Result result)
        {
            if (m_RaycastManager == null || m_Camera == null) return State.NoTable;

            Vector2 screenCenter = new Vector2(result.center.x * Screen.width, result.center.y * Screen.height);
            if (!m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds) &&
                !m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneEstimated | TrackableType.PlaneWithinInfinity))
            {
                return State.NoTable;
            }

            ARRaycastHit hit = s_Hits[0];
            Vector3 normal = hit.pose.up;
            float thickness = Profile.thicknessMm / 1000f;
            // The camera sees the top cover, so intersect the corner rays with the cover plane.
            var coverPlane = new Plane(normal, hit.pose.position + normal * thickness);

            var corners = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                Vector2 sp = new Vector2(result.corners[k].x * Screen.width, result.corners[k].y * Screen.height);
                Ray ray = m_Camera.ScreenPointToRay(sp);
                if (!coverPlane.Raycast(ray, out float enter)) return State.NoTable;
                corners[k] = ray.GetPoint(enter);
            }

            float edgeA = 0.5f * (Vector3.Distance(corners[0], corners[1]) + Vector3.Distance(corners[2], corners[3]));
            float edgeB = 0.5f * (Vector3.Distance(corners[1], corners[2]) + Vector3.Distance(corners[3], corners[0]));
            bool aIsLong = edgeA >= edgeB;
            float measuredLength = (aIsLong ? edgeA : edgeB) * 1000f;
            float measuredWidth = (aIsLong ? edgeB : edgeA) * 1000f;
            LastMeasuredSizeMm = new Vector2(measuredLength, measuredWidth);

            float lengthRatio = measuredLength / Mathf.Max(1f, Profile.lengthMm);
            float widthRatio = measuredWidth / Mathf.Max(1f, Profile.widthMm);
            if (lengthRatio < MinSizeRatio || lengthRatio > MaxSizeRatio || widthRatio < MinSizeRatio || widthRatio > MaxSizeRatio)
            {
                return State.SizeMismatch;
            }

            Vector3 longDir = aIsLong
                ? (corners[1] - corners[0]) + (corners[2] - corners[3])
                : (corners[2] - corners[1]) + (corners[3] - corners[0]);
            Vector3 forward = Vector3.ProjectOnPlane(longDir, normal).normalized;
            Vector3 camForward = Vector3.ProjectOnPlane(m_Camera.transform.forward, normal);
            if (Vector3.Dot(forward, camForward) < 0f) forward = -forward; // +Z points away from the user, like the guide

            Vector3 centerTop = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            m_LastPose = new Pose(centerTop - normal * thickness, Quaternion.LookRotation(forward, normal));
            m_LastPlaneId = hit.trackableId;
            return State.Found;
        }

        private void OnGUI()
        {
            if (!FeatureOn || !Profile.showDetectionDebug || m_DebugMask == null || m_Grid == null) return;

            if (m_DebugTexture == null || m_DebugTexture.width != m_GridW || m_DebugTexture.height != m_GridH)
            {
                if (m_DebugTexture != null) Destroy(m_DebugTexture);
                m_DebugTexture = new Texture2D(m_GridW, m_GridH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            }

            var px = new Color32[m_Grid.Length];
            for (int i = 0; i < px.Length && i < m_DebugMask.Length; i++)
            {
                Color32 g = m_Grid[i];
                px[i] = m_DebugMask[i] ? new Color32(255, 0, 255, 255) : new Color32((byte)(g.r / 2), (byte)(g.g / 2), (byte)(g.b / 2), 255);
            }
            m_DebugTexture.SetPixels32(px);
            m_DebugTexture.Apply();

            float w = Screen.width * 0.3f;
            float h = w * m_GridH / m_GridW;
            // Unity draws textures upright (row 0 at the bottom), so this thumbnail should look like the
            // camera view. If it appears upside down compared with the screen, tick "Flip camera image".
            GUI.DrawTexture(new Rect(10, 10, w, h), m_DebugTexture);
            GUI.Label(new Rect(10, 14 + h, Screen.width - 20, 60), $"<size=28><color=yellow>{CurrentState}  {LastMeasuredSizeMm.x:F0}x{LastMeasuredSizeMm.y:F0} mm</color></size>");
        }
    }
}
