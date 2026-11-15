using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using CADInspection.Detection;

namespace CADInspection.Editor
{
    public static class BuildHelper
    {
        [MenuItem("Tools/CAD AR Inspection/Build Android APK (V8)", false, 100)]
        public static void BuildAndroidAPK()
        {
            string outputPath = @"C:\Users\Lenovo\Downloads\AR_Detector V8.apk";
            Debug.Log("[BuildHelper] Starting Android build to: " + outputPath);

            // Pre-configure and save scene to ensure exact dimensions, image library, and buttons are baked into build
            try
            {
                string scenePath = "Assets/Scenes/AR_Detection.unity";
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var profile = AssetDatabase.LoadAssetAtPath<NoteMarkerProfile>("Assets/_Project/Settings/CurrentNoteMarkerProfile.asset");
                var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>("Assets/_Project/Settings/NoteMarkerLibrary.asset");

                if (profile != null && library != null)
                {
                    NoteMarkerToolWindow.ConfigureScene(profile, library);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("<color=#20E060>[BuildHelper] Note Marker Profile & Scene configured successfully.</color>");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildHelper] Scene pre-configuration warning: " + ex.Message);
            }

            string[] scenes = new string[]
            {
                "Assets/Scenes/AR_Detection.unity"
            };

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"<color=#20E060><b>[BuildHelper] BUILD SUCCEEDED!</b></color> Output: {outputPath} ({summary.totalSize / (1024 * 1024)} MB) in {summary.totalTime.TotalSeconds:F1}s");
            }
            else if (summary.result == BuildResult.Failed)
            {
                Debug.LogError($"<color=#FF3030><b>[BuildHelper] BUILD FAILED!</b></color> Total Errors: {summary.totalErrors}");
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        {
                            Debug.LogError($"[{step.name}] {msg.content}");
                        }
                    }
                }
                throw new Exception("Build failed with " + summary.totalErrors + " errors.");
            }
        }
    }
}
