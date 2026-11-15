using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.Editor
{
    /// <summary>
    /// Runs before ARCore compiles reference image libraries (ARCore's processor uses callbackOrder 2).
    /// ARCore cannot build a library whose image has too few keypoints, or an empty library, and a library
    /// without ARCore data breaks image tracking on the phone. Stop the build with a clear fix instead.
    /// The "Tools/CAD AR Inspection/Build Android APK" menu fixes this automatically before building.
    /// </summary>
    public class NoteMarkerBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(XRReferenceImageLibrary), new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(path);
                if (library == null) continue;

                if (library.count == 0)
                {
                    throw new BuildFailedException(
                        $"[NoteMarkerBuildGuard] '{path}' has no images, and ARCore cannot build an empty image library. " +
                        "Open Tools > Note AR Setup Tool and click SAVE PROFILE & APPLY (or build with Tools > CAD AR Inspection > Build Android APK).");
                }

                for (int i = 0; i < library.count; i++)
                {
                    Texture2D texture = library[i].texture;
                    if (texture == null) continue; // ARCore reports missing textures itself

                    int score = NoteMarkerToolWindow.EvaluateMarkerQuality(texture, out string message);
                    if (score == 0)
                    {
                        throw new BuildFailedException(
                            $"[NoteMarkerBuildGuard] Image '{library[i].name}' ({texture.name}) in '{path}' cannot be tracked by ARCore: {message} " +
                            "Open Tools > Note AR Setup Tool and click SAVE PROFILE & APPLY: it turns image detection off for this cover " +
                            "and the app uses Align & LOCK (or build with Tools > CAD AR Inspection > Build Android APK, which does this automatically).");
                    }
                }
            }
        }
    }
}
