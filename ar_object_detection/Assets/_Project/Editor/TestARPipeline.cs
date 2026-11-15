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
                AssertTrue("ReferenceImageLibrary Assigned", imageManager != null && imageManager.referenceLibrary != null, 
                    imageManager?.referenceLibrary != null ? $"Library count: {imageManager.referenceLibrary.count}" : "Null");

                // 4. Verify Note Marker Tracker
                NoteMarkerTracker tracker = UnityEngine.Object.FindFirstObjectByType<NoteMarkerTracker>();
                AssertTrue("NoteMarkerTracker Component", tracker != null, "Found on InspectionManagers");
                AssertTrue("NoteMarkerProfile Assigned", tracker != null && tracker.Profile != null, 
                    tracker?.Profile != null ? $"Profile: {tracker.Profile.modelName} ({tracker.Profile.lengthMm}x{tracker.Profile.widthMm}x{tracker.Profile.thicknessMm}mm)" : "Null");

                // 5. Verify Marker Image is ref front.jpg
                Texture2D markerTex = tracker?.Profile?.markerImage;
                AssertTrue("Marker Image Assigned", markerTex != null, 
                    markerTex != null ? $"Texture: {markerTex.name} ({markerTex.width}x{markerTex.height})" : "Null");
                AssertTrue("Marker Image is ref front.jpg", markerTex != null && markerTex.name.Contains("ref front"), 
                    $"Actual name: {markerTex?.name}");

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
                    LineRenderer[] lines = outlineRoot.GetComponentsInChildren<LineRenderer>();
                    AssertTrue("12 CAD Wireframe Edges + Leader Line Created", lines != null && (lines.Length == 12 || lines.Length == 13), 
                        $"Line count: {lines?.Length ?? 0} (12 box edges + 1 dimension leader line)");

                    TextMesh[] textMeshes = outlineRoot.GetComponentsInChildren<TextMesh>();
                    AssertTrue("Floating Dimension Badges Created", textMeshes != null && textMeshes.Length > 0, 
                        $"TextMesh count: {textMeshes?.Length ?? 0}");

                    if (textMeshes != null && textMeshes.Length > 0 && tracker != null && tracker.Profile != null)
                    {
                        string badgeText = textMeshes[0].text;
                        string expL = tracker.Profile.lengthMm.ToString("F0");
                        string expW = tracker.Profile.widthMm.ToString("F0");
                        string expT = tracker.Profile.thicknessMm.ToString("F0");
                        bool hasDimensions = badgeText.Contains(expL) && badgeText.Contains(expW) && badgeText.Contains(expT);
                        AssertTrue("Dimension Badge Contains Correct CAD Specs From Profile", hasDimensions, 
                            $"Badge Content: '{badgeText.Replace("\n", " ")}' (Expected: L={expL}, W={expW}, T={expT})");
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
