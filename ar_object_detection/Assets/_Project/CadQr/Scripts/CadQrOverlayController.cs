using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.CadQr
{
    /// <summary>
    /// The brain of the CAD-on-QR prototype. Flow:
    ///   1. SELECT   - the user picks a CAD model; we build its wireframe + read its size.
    ///   2. QR       - we make a marker card (QR + pattern) for that model; the user prints it.
    ///   3. SCAN     - ARCore finds the printed card; the CAD follows the card (smoothed).
    ///   4. LOCK     - we create an ARAnchor at the current pose and stop following the card,
    ///                 so the CAD stays put while the camera moves.
    ///
    /// Put this on an empty GameObject called "CadQrApp". Inspector setup is described in the guide.
    /// The HUD (<see cref="CadQrHUD"/>) only calls the public methods below.
    /// </summary>
    public class CadQrOverlayController : MonoBehaviour
    {
        public enum AppState { Starting, Scanning, Following, Locked, Error }

        [Header("Data")]
        [SerializeField] CadModelLibrary m_Library;

        [Header("AR Foundation (on the XR Origin)")]
        [Tooltip("Leave DISABLED in the scene. The controller fills its image library at runtime and then enables it.")]
        [SerializeField] ARTrackedImageManager m_ImageManager;
        [SerializeField] ARAnchorManager m_AnchorManager;
        [SerializeField] Camera m_Camera;

        [Header("Look")]
        [SerializeField] Material m_WireMaterial;
        [SerializeField] Material m_BoxMaterial;
        [SerializeField] Material m_GhostMaterial;
        [SerializeField, Range(0.3f, 5f)] float m_WireThicknessMm = 1.2f;

        [Header("Behaviour")]
        [Tooltip("How fast the CAD catches up with the card. Higher = snappier, lower = smoother.")]
        [SerializeField, Range(1f, 30f)] float m_SmoothingSpeed = 12f;
        [Tooltip("If the card has not been seen for this long, the CAD stays where it was last seen.")]
        [SerializeField] float m_LostAfterSeconds = 0.4f;
        [SerializeField] int m_MarkerTexturePixels = 1024;

        // ---- State the HUD reads ----
        public AppState State { get; private set; } = AppState.Starting;
        public string StatusMessage { get; private set; } = "Starting AR...";
        public int SelectedIndex { get; private set; } = -1;
        public CadModelEntry SelectedEntry => m_Library != null && SelectedIndex >= 0 && SelectedIndex < m_Library.models.Count ? m_Library.models[SelectedIndex] : null;
        public CadModelInfo SelectedInfo => m_Overlay != null ? m_Overlay.Info : null;
        public int ModelCount => m_Library != null ? m_Library.models.Count : 0;
        public bool IsLocked => State == AppState.Locked;
        public bool CanLock => m_Overlay != null && m_HasCardPose && (State == AppState.Following);
        public bool GhostVisible => m_Overlay != null && m_Overlay.GhostVisible;
        public float MarkerYawDegrees => m_UserYaw;

        public event Action SelectionChanged;

        CadOverlay m_Overlay;
        ARAnchor m_Anchor;
        Pose m_CardPose;
        bool m_HasCardPose;
        bool m_OverlayPlaced;
        float m_LastSeenTime = -999f;
        float m_UserYaw;
        bool m_GhostWanted = true;
        readonly Dictionary<string, Texture2D> m_MarkerTextures = new Dictionary<string, Texture2D>();
        readonly List<string> m_RejectedImages = new List<string>();

        void Awake()
        {
            if (m_Camera == null) m_Camera = Camera.main;
            if (m_ImageManager == null) m_ImageManager = FindFirstObjectByType<ARTrackedImageManager>();
            if (m_AnchorManager == null) m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();
            EnsureMaterials();
        }

        void OnEnable()
        {
            if (m_ImageManager != null) m_ImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        }

        void OnDisable()
        {
            if (m_ImageManager != null) m_ImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
        }

        IEnumerator Start()
        {
            if (m_Library == null || m_Library.models.Count == 0)
            {
                Fail("No CAD models. Assign a Cad Model Library with at least one model.");
                yield break;
            }
            if (m_ImageManager == null)
            {
                Fail("No AR Tracked Image Manager found on the XR Origin.");
                yield break;
            }

            SelectModel(0);
            yield return SetUpImageLibrary();
        }

        // ------------------------------------------------------------------
        // Step 1: model selection
        // ------------------------------------------------------------------

        public void NextModel() => SelectModel((SelectedIndex + 1) % Math.Max(1, ModelCount));
        public void PreviousModel() => SelectModel((SelectedIndex - 1 + ModelCount) % Math.Max(1, ModelCount));

        public void SelectModel(int index)
        {
            if (m_Library == null || index < 0 || index >= m_Library.models.Count) return;
            if (IsLocked) Unlock();

            CadModelEntry entry = m_Library.models[index];
            if (entry == null || entry.modelPrefab == null)
            {
                StatusMessage = $"Model {index} has no prefab assigned.";
                return;
            }

            if (m_Overlay != null) Destroy(m_Overlay.gameObject);
            SelectedIndex = index;
            m_Overlay = CadOverlay.Create(entry, m_WireMaterial, m_BoxMaterial, m_GhostMaterial, m_WireThicknessMm * 0.001f, m_Camera);
            m_Overlay.SetGhostVisible(m_GhostWanted);
            m_Overlay.gameObject.SetActive(false); // hidden until its card is found
            m_HasCardPose = false;
            m_OverlayPlaced = false;
            m_UserYaw = 0f;

            if (State != AppState.Starting && State != AppState.Error) SetScanning();
            SelectionChanged?.Invoke();
        }

        // ------------------------------------------------------------------
        // Step 2: the QR marker card
        // ------------------------------------------------------------------

        /// <summary>The marker card for a model. Made once, then cached. Same id = same picture, always.</summary>
        public Texture2D GetMarkerTexture(string modelId)
        {
            if (!m_MarkerTextures.TryGetValue(modelId, out Texture2D texture))
            {
                texture = QrTextureFactory.CreateMarkerCard(modelId, m_MarkerTexturePixels);
                m_MarkerTextures[modelId] = texture;
            }
            return texture;
        }

        /// <summary>Saves the selected model's card as a PNG on the phone and returns the file path.</summary>
        public string SaveSelectedMarkerPng()
        {
            CadModelEntry entry = SelectedEntry;
            if (entry == null) return null;
            string folder = Path.Combine(Application.persistentDataPath, "CadQr");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{entry.id}_{entry.markerWidthMeters * 1000f:0}mm.png");
            File.WriteAllBytes(path, GetMarkerTexture(entry.id).EncodeToPNG());
            Debug.Log("[CadQr] Saved marker card to " + path);
            return path;
        }

        /// <summary>
        /// ARCore can only find images that are in its "reference image library".
        /// We make an empty library at runtime and add one card per CAD model, named by the model id.
        /// Adding all of them (not only the selected one) means any printed card works after a restart.
        /// </summary>
        IEnumerator SetUpImageLibrary()
        {
            // Wait until ARCore has checked the device and is ready (or said it is not supported).
            while (ARSession.state == ARSessionState.None ||
                   ARSession.state == ARSessionState.CheckingAvailability ||
                   ARSession.state == ARSessionState.Installing)
                yield return null;

            if (ARSession.state == ARSessionState.Unsupported || ARSession.state == ARSessionState.NeedsInstall)
            {
                Fail("AR is not available on this device. Use a phone that supports Google Play Services for AR.");
                yield break;
            }

            RuntimeReferenceImageLibrary runtimeLibrary = null;
            try
            {
                runtimeLibrary = m_ImageManager.CreateRuntimeLibrary();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (!(runtimeLibrary is MutableRuntimeReferenceImageLibrary mutableLibrary))
            {
                Fail("This device cannot add tracking images at runtime (no mutable image library).");
                yield break;
            }

            var jobs = new List<KeyValuePair<string, AddReferenceImageJobState>>();
            foreach (CadModelEntry entry in m_Library.models)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                AddReferenceImageJobState job = mutableLibrary.ScheduleAddImageWithValidationJob(
                    GetMarkerTexture(entry.id), entry.id, entry.markerWidthMeters);
                jobs.Add(new KeyValuePair<string, AddReferenceImageJobState>(entry.id, job));
            }

            m_ImageManager.referenceLibrary = mutableLibrary;
            m_ImageManager.requestedMaxNumberOfMovingImages = 1;
            m_ImageManager.enabled = true;

            StatusMessage = "Preparing marker cards...";
            foreach (var job in jobs)
            {
                while (!job.Value.status.IsComplete()) yield return null;
                if (job.Value.status.IsError())
                {
                    m_RejectedImages.Add(job.Key);
                    Debug.LogWarning($"[CadQr] ARCore rejected the card for '{job.Key}': {job.Value.status}");
                }
            }

            SetScanning();
        }

        // ------------------------------------------------------------------
        // Step 3: follow the card
        // ------------------------------------------------------------------

        void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (ARTrackedImage image in args.added) HandleImage(image);
            foreach (ARTrackedImage image in args.updated) HandleImage(image);
        }

        void HandleImage(ARTrackedImage image)
        {
            if (IsLocked || State == AppState.Error) return;
            if (image.trackingState != TrackingState.Tracking) return; // Limited = old or guessed pose, ignore

            string id = image.referenceImage.name;
            if (SelectedEntry == null || SelectedEntry.id != id)
            {
                // The card says which CAD model it belongs to: switch to that model automatically.
                int index = m_Library.models.FindIndex(m => m != null && m.id == id);
                if (index < 0) return;
                SelectModel(index);
            }

            m_CardPose = new Pose(image.transform.position, image.transform.rotation);
            m_HasCardPose = true;
            m_LastSeenTime = Time.time;
        }

        void Update()
        {
            if (m_Overlay == null || IsLocked || State == AppState.Starting || State == AppState.Error) return;
            if (!m_HasCardPose) { SetScanning(); return; }

            Pose target = ModelPoseFromCard(m_CardPose);
            Transform t = m_Overlay.transform;
            if (!m_OverlayPlaced)
            {
                t.SetPositionAndRotation(target.position, target.rotation); // first time: jump there
                m_Overlay.gameObject.SetActive(true);
                m_OverlayPlaced = true;
            }
            else
            {
                // Exponential smoothing: removes the small shake of image tracking.
                float k = 1f - Mathf.Exp(-m_SmoothingSpeed * Time.deltaTime);
                t.SetPositionAndRotation(Vector3.Lerp(t.position, target.position, k), Quaternion.Slerp(t.rotation, target.rotation, k));
            }

            State = AppState.Following;
            bool cardVisible = Time.time - m_LastSeenTime < m_LostAfterSeconds;
            StatusMessage = cardVisible
                ? "Card found. Hold still, check the fit, then press LOCK & INSPECT."
                : "Card out of view. The model stays at its last position. Point at the card again, or LOCK now.";
        }

        /// <summary>
        /// The card's pose is known (from ARCore). Where the card sits on the model is known (from the
        /// library entry). So the model's pose = card pose "minus" the card's offset inside the model.
        /// </summary>
        Pose ModelPoseFromCard(Pose card)
        {
            Quaternion cardRotation = card.rotation * Quaternion.AngleAxis(m_UserYaw, Vector3.up);
            Pose markerInModel = m_Overlay.MarkerInModel;
            Quaternion modelRotation = cardRotation * Quaternion.Inverse(markerInModel.rotation);
            Vector3 modelPosition = card.position - modelRotation * markerInModel.position;
            return new Pose(modelPosition, modelRotation);
        }

        /// <summary>Turns the model 90 degrees around the card. Use it if the card was stuck on turned.</summary>
        public void RotateAroundCard()
        {
            if (IsLocked) return;
            m_UserYaw = (m_UserYaw + 90f) % 360f;
        }

        public void ToggleGhost()
        {
            m_GhostWanted = !m_GhostWanted;
            if (m_Overlay != null) m_Overlay.SetGhostVisible(m_GhostWanted);
        }

        // ------------------------------------------------------------------
        // Step 4: lock
        // ------------------------------------------------------------------

        public void ToggleLock()
        {
            if (IsLocked) Unlock();
            else Lock();
        }

        public async void Lock()
        {
            if (!CanLock) return;
            State = AppState.Locked;
            StatusMessage = "LOCKED. Walk around and inspect. The model stays fixed in the room.";
            Pose pose = new Pose(m_Overlay.transform.position, m_Overlay.transform.rotation);

            if (m_AnchorManager == null || !m_AnchorManager.enabled)
            {
                Debug.Log("[CadQr] No ARAnchorManager: the model is locked in world space without an anchor.");
                return;
            }

            // An anchor is a point ARCore promises to keep fixed to the real world. When ARCore
            // corrects its map (drift), it moves the anchor, and our model (its child) moves with it.
            var result = await m_AnchorManager.TryAddAnchorAsync(pose);
            if (this == null) return;

            if (!result.status.IsSuccess())
            {
                Debug.LogWarning("[CadQr] Could not create an anchor; locked in world space instead.");
                return;
            }

            if (!IsLocked || m_Overlay == null)
            {
                m_AnchorManager.TryRemoveAnchor(result.value); // user unlocked while we were waiting
                return;
            }

            m_Anchor = result.value;
            m_Overlay.transform.SetParent(m_Anchor.transform, true);
        }

        public void Unlock()
        {
            if (!IsLocked) return;
            if (m_Overlay != null) m_Overlay.transform.SetParent(null, true);
            if (m_Anchor != null)
            {
                if (m_AnchorManager != null && !m_AnchorManager.TryRemoveAnchor(m_Anchor))
                    Destroy(m_Anchor.gameObject);
                m_Anchor = null;
            }
            State = AppState.Following;
            StatusMessage = "Unlocked. The model follows the card again.";
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        void SetScanning()
        {
            if (State == AppState.Error) return;
            State = AppState.Scanning;
            CadModelEntry e = SelectedEntry;
            string name = e != null ? e.displayName : "the model";
            StatusMessage = m_RejectedImages.Count > 0 && e != null && m_RejectedImages.Contains(e.id)
                ? $"ARCore rejected the card for {name}. See the guide."
                : $"Point the camera at the printed card for {name} (about 20 to 40 cm away).";
        }

        void Fail(string message)
        {
            State = AppState.Error;
            StatusMessage = message;
            Debug.LogError("[CadQr] " + message);
        }

        void EnsureMaterials()
        {
            // Normally the editor tool assigns real material assets (so the shader is in the build).
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (m_WireMaterial == null && unlit != null) m_WireMaterial = new Material(unlit) { color = new Color(0.1f, 1f, 0.35f) };
            if (m_BoxMaterial == null && unlit != null) m_BoxMaterial = new Material(unlit) { color = new Color(1f, 0.85f, 0.2f) };
            if (m_GhostMaterial == null && unlit != null) m_GhostMaterial = new Material(unlit) { color = new Color(0.2f, 0.7f, 1f) };
        }
    }
}
