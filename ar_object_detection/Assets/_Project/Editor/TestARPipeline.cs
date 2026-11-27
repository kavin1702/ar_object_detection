using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using CADInspection.AR;
using CADInspection.Detection;
using CADInspection.Visualization;
using CADInspection.UI;

namespace CADInspection.Editor
{
    public static class TestARPipeline
    {
        public static void ApplyConfigurationAndRunTest()
        {
            string scenePath = "Assets/Scenes/AR_Detection.unity";
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var profile = AssetDatabase.LoadAssetAtPath<NoteMarkerProfile>("Assets/_Project/Settings/CurrentNoteMarkerProfile.asset");
            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>("Assets/_Project/Settings/NoteMarkerLibrary.asset");

            if (profile != null && library != null)
            {
                NoteMarkerToolWindow.ConfigureScene(profile, library);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("<color=#20E060>[TestARPipeline] Applied NoteMarkerToolWindow scene configuration and saved scene.</color>");
            }

            RunTest();
        }

        [MenuItem("Tools/CAD AR Inspection/Run Automated Pipeline Test", false, 200)]
        public static void RunTest()
        {
            Debug.Log("=================================================");
            Debug.Log("[TestARPipeline] STARTING AUTOMATED PIPELINE TEST");
            Debug.Log("=================================================");

            int passed = 0;
            int failed = 0;

            void AssertTrue(string testName, bool condition, string detail = "")
            {
                if (condition)
                {
                    Debug.Log($"<color=#20E060>[PASS]</color> {testName} {detail}");
                    passed++;
                }
                else
                {
                    Debug.LogError($"<color=#FF3030>[FAIL]</color> {testName} - {detail}");
                    failed++;
                }
            }

            try
            {
                // 1. Load Scene
                string scenePath = "Assets/Scenes/AR_Detection.unity";
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                AssertTrue("Scene Loaded", scene.IsValid(), scenePath);

                // 2. Verify AR Session
                ARSession session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
                AssertTrue("ARSession Component", session != null, "Found in scene");

                // 3. Verify Tracked Image Manager
                ARTrackedImageManager imageManager = UnityEngine.Object.FindFirstObjectByType<ARTrackedImageManager>();
                AssertTrue("ARTrackedImageManager Component", imageManager != null, "Found on XR Origin");
                // Either a trackable cover is in the library and image tracking is on,
                // or the cover cannot be tracked, there is no library, and image tracking is off (Align & Lock only).
                var serializedLibrary = imageManager != null
                    ? new SerializedObject(imageManager).FindProperty("m_SerializedLibrary").objectReferenceValue as XRReferenceImageLibrary
                    : null;
                bool imageModeConsistent = imageManager != null &&
                    (serializedLibrary != null ? (serializedLibrary.count > 0 && imageManager.enabled) : !imageManager.enabled);
                AssertTrue("Image Tracking Setup Consistent", imageModeConsistent,
                    serializedLibrary != null
                        ? $"Library images: {serializedLibrary.count}, manager enabled: {imageManager.enabled}"
                        : $"No library (Align & Lock only), manager enabled: {imageManager?.enabled}");

                // 3b. Align & Lock needs table detection
                ARPlaneManager planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
                AssertTrue("ARPlaneManager Enabled (table detection for LOCK)", planeManager != null && planeManager.enabled,
                    planeManager != null ? $"enabled={planeManager.enabled}, mode={planeManager.requestedDetectionMode}" : "Missing");

                // 4. Verify Note Marker Tracker
                NoteMarkerTracker tracker = UnityEngine.Object.FindFirstObjectByType<NoteMarkerTracker>();
                AssertTrue("NoteMarkerTracker Component", tracker != null, "Found on InspectionManagers");
                AssertTrue("NoteMarkerProfile Assigned", tracker != null && tracker.Profile != null, 
                    tracker?.Profile != null ? $"Profile: {tracker.Profile.modelName} ({tracker.Profile.lengthMm}x{tracker.Profile.widthMm}x{tracker.Profile.thicknessMm}mm)" : "Null");

                // 5. Verify Marker Image is ref front.jpg
                Texture2D markerTex = tracker?.Profile?.markerImage;
                AssertTrue("Marker Image Assigned", markerTex != null, 
                    markerTex != null ? $"Texture: {markerTex.name} ({markerTex.width}x{markerTex.height})" : "Null");

                // 5b. Cover-colour auto-detection
                NoteColorDetector colorDetector = UnityEngine.Object.FindFirstObjectByType<NoteColorDetector>();
                AssertTrue("NoteColorDetector Component", colorDetector != null, "Found on InspectionManagers");
                if (tracker != null && tracker.Profile != null)
                {
                    Color.RGBToHSV(tracker.Profile.coverColor, out float ch, out float cs, out float cv);
                    AssertTrue("Cover Colour Usable For Detection", !tracker.Profile.autoDetectByColor || cs >= 0.25f,
                        $"HSV({ch * 360f:F0}°, {cs:F2}, {cv:F2}), autoDetect={tracker.Profile.autoDetectByColor}");

                    // Synthetic camera frame: the cover colour drawn as a slanted rectangle on a wood-coloured table
                    const int w = 96, h = 160;
                    var frame = new Color32[w * h];
                    Color32 table = new Color32(150, 105, 70, 255);
                    Color32 cover = tracker.Profile.coverColor;
                    float angle = 20f * Mathf.Deg2Rad, ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float dx = x - w * 0.5f, dy = y - h * 0.5f;
                            float a = dx * ca + dy * sa, b = -dx * sa + dy * ca; // rotated frame
                            frame[y * w + x] = Mathf.Abs(a) < 22f && Mathf.Abs(b) < 32f ? cover : table;
                        }
                    var detection = NoteColorBlobDetector.Detect(frame, w, h,
                        NoteColorBlobDetector.Settings.FromColor(tracker.Profile.coverColor, tracker.Profile.colorHueTolerance));
                    bool sizeOk = false;
                    if (detection.corners != null)
                    {
                        Vector2 Px(Vector2 c) => new Vector2(c.x * w, c.y * h);
                        float e01 = Vector2.Distance(Px(detection.corners[0]), Px(detection.corners[1]));
                        float e12 = Vector2.Distance(Px(detection.corners[1]), Px(detection.corners[2]));
                        float longEdge = Mathf.Max(e01, e12), shortEdge = Mathf.Min(e01, e12);
                        sizeOk = Mathf.Abs(longEdge - 64f) < 6f && Mathf.Abs(shortEdge - 44f) < 6f;
                    }
                    AssertTrue("Colour Detector Finds Slanted Notebook In Synthetic Frame",
                        detection.outcome == NoteColorBlobDetector.Outcome.Found && sizeOk,
                        $"outcome={detection.outcome}, rectangularity={detection.rectangularity:F2}, center=({detection.center.x:F2},{detection.center.y:F2})");
                }

                // 6. Verify Note Outline Renderer
                NoteOutlineRenderer renderer = UnityEngine.Object.FindFirstObjectByType<NoteOutlineRenderer>();
                AssertTrue("NoteOutlineRenderer Component", renderer != null, "Found on InspectionManagers");

                // 7. Verify UI Elements & Buttons
                NoteMarkerHUD hud = UnityEngine.Object.FindFirstObjectByType<NoteMarkerHUD>();
                AssertTrue("NoteMarkerHUD Component", hud != null, "Found on InspectionHUD_Root");

                var rotLeft = GameObject.Find("RotateLeftButton");
                AssertTrue("RotateLeftButton Exists", rotLeft != null, "Found in scene hierarchy");

                var rotRight = GameObject.Find("RotateRightButton");
                AssertTrue("RotateRightButton Exists", rotRight != null, "Found in scene hierarchy");

                var lockBtn = GameObject.Find("LockAndInspectButton");
                AssertTrue("LockAndInspectButton Exists", lockBtn != null, "Found in scene hierarchy");

                // 8. Test Simulation Detection
                Debug.Log("[TestARPipeline] Testing Simulation Detection...");
                tracker.SimulateDetection();

                AssertTrue("Tracker IsLocked after Simulation", tracker.IsLocked, "Status locked onto target");

                GameObject outlineRoot = GameObject.Find("Note_Outline_Root");
                AssertTrue("Note_Outline_Root Created", outlineRoot != null, "3D root instantiated in scene");

                if (outlineRoot != null)
                {
                    Transform outlineBox = outlineRoot.transform.Find("Outline");
                    int edgeCount = outlineBox != null ? outlineBox.GetComponentsInChildren<LineRenderer>().Length : 0;
                    AssertTrue("12 Outline Box Edges Created", edgeCount == 12, $"Edge count: {edgeCount}");

                    TextMesh[] textMeshes = outlineRoot.GetComponentsInChildren<TextMesh>();
                    AssertTrue("3 Edge Dimension Callouts Created", textMeshes != null && textMeshes.Length == 3,
                        $"TextMesh count: {textMeshes?.Length ?? 0}");

                    bool allHaveFont = textMeshes != null && textMeshes.Length > 0 &&
                                       Array.TrueForAll(textMeshes, tm => tm.font != null && tm.GetComponent<MeshRenderer>().sharedMaterial != null);
                    AssertTrue("Callout Text Has Font & Material (visible on device)", allHaveFont, "LegacyRuntime font assigned");

                    if (textMeshes != null && textMeshes.Length > 0 && tracker != null && tracker.Profile != null)
                    {
                        string allText = string.Join(" | ", Array.ConvertAll(textMeshes, tm => tm.text.Replace("\n", " ")));
                        string fmt = "F" + Mathf.Clamp(tracker.Profile.decimals, 0, 2);
                        string expL = tracker.Profile.lengthMm.ToString(fmt);
                        string expW = tracker.Profile.widthMm.ToString(fmt);
                        string expT = tracker.Profile.thicknessMm.ToString(fmt);
                        bool hasDimensions = allText.Contains("Width (W)") && allText.Contains(expW) &&
                                             allText.Contains("Length (L)") && allText.Contains(expL) &&
                                             allText.Contains("Depth (T)") && allText.Contains(expT);
                        AssertTrue("Callouts Show Profile Dimensions", hasDimensions,
                            $"Callouts: '{allText}' (Expected: W={expW}, L={expL}, T={expT})");
                    }
                }

                // 9. Test Reset
                Debug.Log("[TestARPipeline] Testing Tracking Reset...");
                tracker.ResetTracking();

                AssertTrue("Tracker IsLocked is False after Reset", !tracker.IsLocked, "Ready for next detection");
                GameObject outlineAfterReset = GameObject.Find("Note_Outline_Root");
                AssertTrue("Note_Outline_Root Cleaned Up", outlineAfterReset == null, "No ghost objects left in scene");

                // 10. Test Surface Lock & Rigid Anchor Safety
                Debug.Log("[TestARPipeline] Testing Surface Lock & Anchor Safety...");
                tracker.Rotate(15f);
                AssertTrue("Tracker Rotation Updated", Mathf.Approximately(tracker.CurrentYaw, 15f), $"Yaw: {tracker.CurrentYaw}");

                tracker.LockCurrentPose();
                AssertTrue("Tracker IsLocked after LockCurrentPose", tracker.IsLocked, "Pose locked to world");

                tracker.ResetTracking();
                AssertTrue("Tracker Reset cleanly after LockCurrentPose without exceptions", !tracker.IsLocked, "Anchor removed cleanly");

                // 11. Test ARAnchorManager MissingReference Guard
                Debug.Log("[TestARPipeline] Testing ARAnchorManager MissingReference Guard...");
                NoteMarkerTracker.SafeClearAnchorManagerInstanceIfInvalid();
                AssertTrue("SafeClearAnchorManagerInstanceIfInvalid Executed Without Error", true, "Static guard active");

                // Summary
                Debug.Log("=================================================");
                Debug.Log($"[TestARPipeline] RESULTS: PASSED={passed}, FAILED={failed}");
                Debug.Log("=================================================");

                if (failed > 0)
                {
                    throw new Exception($"Automated pipeline tests failed with {failed} failures.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestARPipeline] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }
}
