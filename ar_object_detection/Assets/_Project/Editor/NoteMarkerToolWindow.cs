#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEditor.XR.ARSubsystems;
using CADInspection.Detection;
using CADInspection.Visualization;
using CADInspection.UI;

namespace CADInspection.Editor
{
    /// <summary>
    /// Dedicated Unity Editor Tool for Note Marker & Dimensional AR Projection.
    /// Easily configure note marker images, set model physical dimensions (Length x Width x Thickness in mm),
    /// and project the 3D outline with floating dimensions above the model.
    /// Strictly excludes any pass/fail, test result, or tolerance mentions.
    /// Accessible via: Tools > Note AR Setup Tool
    /// </summary>
    public class NoteMarkerToolWindow : EditorWindow
    {
        private string m_ModelName = "Engineering Notebook";
        private Texture2D m_MarkerImage;
        private float m_LengthMm = 210f;
        private float m_WidthMm = 148f;
        private float m_ThicknessMm = 20f;

        private Color m_OutlineColor = new Color(0.12f, 0.95f, 0.42f, 1f); // Vibrant AR Green
        private float m_LineWidthMm = 3.5f;
        private bool m_ShowCornerNodes = true;

        private float m_CalloutOffsetMm = 25f;
        private int m_Decimals = 1;
        private Color m_DimensionTextColor = Color.white;
        private bool m_ShowDimensionLines = true;

        // Auto-detect the note by its cover colour
        private bool m_AutoDetectByColor = true;
        private Color m_CoverColor = new Color(0.06f, 0.66f, 0.74f, 1f);
        private float m_HueTolerance = 18f;
        private bool m_ShowDetectionDebug;
        private bool m_FlipCameraImage;
        private bool m_RefineAfterLock = true;
        private Texture2D m_CoverColorFromImage;

        // ARCore marker quality check (arcoreimg) for the selected image
        private bool m_QualityChecked;
        private int m_QualityScore = -1;
        private string m_QualityMessage = "";
        private Texture2D m_QualityCheckedImage;
        private bool m_ShowBackplate = true;
        private GameObject m_OptionalModelPrefab;

        private Vector2 m_ScrollPos;
        private GUIStyle m_HeaderStyle;
        private GUIStyle m_CardStyle;
        private GUIStyle m_TitleStyle;
        private GUIStyle m_SubTitleStyle;

        [MenuItem("Tools/Note AR Setup Tool", false, 1)]
        public static void OpenWindow()
        {
            var window = GetWindow<NoteMarkerToolWindow>("Note AR Setup Tool");
            window.minSize = new Vector2(460, 720);
            window.Show();
        }

        private void OnEnable()
        {
            LoadExistingProfile();
        }

        private void LoadExistingProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<NoteMarkerProfile>("Assets/_Project/Settings/CurrentNoteMarkerProfile.asset");
            if (profile != null)
            {
                m_ModelName = profile.modelName;
                m_MarkerImage = profile.markerImage;
                m_LengthMm = profile.lengthMm;
                m_WidthMm = profile.widthMm;
                m_ThicknessMm = profile.thicknessMm;
                m_OutlineColor = profile.outlineColor;
                m_LineWidthMm = profile.lineWidthMm;
                m_ShowCornerNodes = profile.showCornerNodes;
                m_CalloutOffsetMm = profile.calloutOffsetMm;
                m_Decimals = profile.decimals;
                m_DimensionTextColor = profile.dimensionTextColor;
                m_ShowDimensionLines = profile.showDimensionLines;
                m_AutoDetectByColor = profile.autoDetectByColor;
                m_CoverColor = profile.coverColor;
                m_HueTolerance = profile.colorHueTolerance;
                m_ShowDetectionDebug = profile.showDetectionDebug;
                m_FlipCameraImage = profile.flipCameraImageVertically;
                m_RefineAfterLock = profile.refineAfterLock;
                m_CoverColorFromImage = profile.markerImage;
                m_ShowBackplate = profile.showBackplate;
                m_OptionalModelPrefab = profile.optionalModelPrefab;
            }
            else
            {
                var refFront = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/ref front.jpg");
                if (refFront != null)
                {
                    m_MarkerImage = refFront;
                }
            }
        }

        private void OnGUI()
        {
            InitStyles();

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            // Banner Header
            EditorGUILayout.BeginVertical(m_HeaderStyle);
            EditorGUILayout.LabelField("Note AR Setup Tool", m_TitleStyle);
            EditorGUILayout.LabelField("Assign Note Image & Physical Dimensions -> 3D Outline with Width / Length / Depth Callouts", m_SubTitleStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 1. Model Identification Section
            EditorGUILayout.BeginVertical(m_CardStyle);
            EditorGUILayout.LabelField("1. Note Identification", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            m_ModelName = EditorGUILayout.TextField("Note Model Name", m_ModelName);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 2. Reference Image Section
            EditorGUILayout.BeginVertical(m_CardStyle);
            EditorGUILayout.LabelField("2. Note Marker Image (Cover / Drawing)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select or browse the photo of your physical note cover.", MessageType.Info);
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            m_MarkerImage = (Texture2D)EditorGUILayout.ObjectField("Note Image", m_MarkerImage, typeof(Texture2D), false, GUILayout.Height(70));

            EditorGUILayout.BeginVertical(GUILayout.Width(130));
            if (GUILayout.Button("Browse Image\nfrom Disk...", GUILayout.Height(38)))
            {
                BrowseImageFile();
            }
            if (GUILayout.Button("Create Sample", GUILayout.Height(28)))
            {
                CreateSampleMarkerTexture();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (m_MarkerImage != null)
            {
                EditorGUILayout.Space(6);
                Rect previewRect = GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(true));
                GUI.DrawTexture(previewRect, m_MarkerImage, ScaleMode.ScaleToFit);
                EditorGUILayout.LabelField($"Image: {m_MarkerImage.name} ({m_MarkerImage.width} × {m_MarkerImage.height} px)", EditorStyles.centeredGreyMiniLabel);

                // Orientation check: the image's long side should match the note's long side (length).
                bool imagePortrait = m_MarkerImage.height >= m_MarkerImage.width;
                bool notePortrait = m_LengthMm >= m_WidthMm;
                if (imagePortrait != notePortrait)
                {
                    EditorGUILayout.HelpBox("The image looks rotated: its long side does not match the note's Length. " +
                        "Phone photos can carry a rotation tag that Unity ignores. Rotate the image file so the cover is upright.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space(4);
                if (m_QualityCheckedImage != m_MarkerImage)
                {
                    m_QualityChecked = false;
                }

                if (GUILayout.Button("Check Marker Quality (ARCore arcoreimg)", GUILayout.Height(24)))
                {
                    m_QualityScore = EvaluateMarkerQuality(m_MarkerImage, out m_QualityMessage);
                    m_QualityChecked = true;
                    m_QualityCheckedImage = m_MarkerImage;
                }

                if (m_QualityChecked)
                {
                    if (m_QualityScore < 0)
                    {
                        EditorGUILayout.HelpBox("Quality check failed: " + m_QualityMessage, MessageType.Warning);
                    }
                    else if (m_QualityScore >= 75)
                    {
                        EditorGUILayout.HelpBox($"ARCore score {m_QualityScore}/100. {m_QualityMessage}", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"ARCore score {m_QualityScore}/100 (75+ recommended). Too little detail for ARCore image " +
                            "tracking, so the app finds the note by its cover colour instead (below), or you align the box and tap LOCK.",
                            MessageType.Warning);
                    }
                }

                // Auto-detect by cover colour
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Auto-Detect Note by Cover Colour", EditorStyles.miniBoldLabel);
                m_AutoDetectByColor = EditorGUILayout.Toggle(new GUIContent("Enable Auto-Detect",
                    "The app looks for the cover colour in the camera view and snaps the green box onto the notebook"), m_AutoDetectByColor);

                if (m_CoverColorFromImage != m_MarkerImage)
                {
                    m_CoverColor = ComputeCoverColor(m_MarkerImage, m_CoverColor);
                    m_CoverColorFromImage = m_MarkerImage;
                }

                using (new EditorGUI.DisabledScope(!m_AutoDetectByColor))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ColorField(new GUIContent("Cover Colour (from image)"), m_CoverColor, false, false, false);
                    }
                    m_HueTolerance = EditorGUILayout.Slider(new GUIContent("Colour Tolerance (°)",
                        "Raise if the notebook is not found under warm/dim light; lower if the table is a similar colour"), m_HueTolerance, 4f, 60f);
                    m_ShowDetectionDebug = EditorGUILayout.Toggle(new GUIContent("Show Debug Thumbnail In App",
                        "Shows what the detector sees (magenta = cover colour) in the top-left corner of the app"), m_ShowDetectionDebug);
                    m_FlipCameraImage = EditorGUILayout.Toggle(new GUIContent("Flip Camera Image (debug)",
                        "Only if the debug thumbnail is upside down compared with the screen"), m_FlipCameraImage);
                    m_RefineAfterLock = EditorGUILayout.Toggle(new GUIContent("Keep Outline On Note After LOCK",
                        "After LOCK, keep checking the notebook in view and pull the outline back onto it, so AR tracking " +
                        "drift does not make it slide off when you move the phone"), m_RefineAfterLock);
                }

                Color.RGBToHSV(m_CoverColor, out float coverH, out float coverS, out _);
                if (m_AutoDetectByColor && coverS < 0.25f)
                {
                    EditorGUILayout.HelpBox("The cover colour is grey/white/black (low saturation): colour detection works best with a coloured cover " +
                        "on a table of a different colour.", MessageType.Warning);
                }
                else if (m_AutoDetectByColor)
                {
                    EditorGUILayout.HelpBox($"Cover hue {coverH * 360f:F0}°. Works best when the table is a clearly different colour.", MessageType.None);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 3. Physical Dimensions Section
            EditorGUILayout.BeginVertical(m_CardStyle);
            EditorGUILayout.LabelField("3. Physical Dimensions (Millimeters)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Real-world size of your physical note. The 3D outline will match these exact measurements.", MessageType.None);
            EditorGUILayout.Space(6);

            m_LengthMm = EditorGUILayout.FloatField(new GUIContent("Length (Z axis) mm", "Longest dimension along notebook spine"), m_LengthMm);
            m_WidthMm = EditorGUILayout.FloatField(new GUIContent("Width (X axis) mm", "Cover width from spine to outer edge"), m_WidthMm);
            m_ThicknessMm = EditorGUILayout.FloatField(new GUIContent("Thickness / Height (Y axis) mm", "Depth from desk surface to top cover"), m_ThicknessMm);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"Calculated Real Size: {(m_LengthMm / 10f):F1} cm × {(m_WidthMm / 10f):F1} cm × {(m_ThicknessMm / 10f):F1} cm", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 4. Visual Outline & Dimension Styling Section
            EditorGUILayout.BeginVertical(m_CardStyle);
            EditorGUILayout.LabelField("4. 3D Outline & Dimension Callout Styling", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            m_OutlineColor = EditorGUILayout.ColorField("Outline Color", m_OutlineColor);
            m_LineWidthMm = EditorGUILayout.Slider("Line Width (mm)", m_LineWidthMm, 1f, 10f);
            m_ShowCornerNodes = EditorGUILayout.Toggle("Corner Accent Nodes", m_ShowCornerNodes);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Edge Callouts (Width (W), Length (L), Depth (T)):", EditorStyles.miniBoldLabel);
            m_CalloutOffsetMm = EditorGUILayout.Slider("Arrow Offset From Edge (mm)", m_CalloutOffsetMm, 5f, 80f);
            m_Decimals = EditorGUILayout.IntSlider("Decimals (148.0 mm = 1)", m_Decimals, 0, 2);
            m_DimensionTextColor = EditorGUILayout.ColorField("Text & Arrow Color", m_DimensionTextColor);
            m_ShowDimensionLines = EditorGUILayout.Toggle("Show Arrow Lines", m_ShowDimensionLines);
            m_ShowBackplate = EditorGUILayout.Toggle("Show Dark Label Plate", m_ShowBackplate);

            EditorGUILayout.Space(6);
            m_OptionalModelPrefab = (GameObject)EditorGUILayout.ObjectField("Optional 3D Model Prefab", m_OptionalModelPrefab, typeof(GameObject), false);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(14);

            // 5. Main Action Button
            GUI.backgroundColor = new Color(0.18f, 0.88f, 0.45f);
            if (GUILayout.Button("SAVE PROFILE & APPLY TO AR SCENE", GUILayout.Height(50)))
            {
                ExecuteSaveAndApply();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(12);

            // 6. Editor Simulation & Play Testing Section
            EditorGUILayout.BeginVertical(m_CardStyle);
            EditorGUILayout.LabelField("5. Instant In-Editor Testing", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Press Play in Unity and click below (or press [Space]) to simulate scanning the note marker.", MessageType.None);
            EditorGUILayout.Space(4);

            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Simulate Note Detection [Space]", GUILayout.Height(32)))
                {
                    var tracker = FindFirstObjectByType<NoteMarkerTracker>();
                    if (tracker != null) tracker.SimulateDetection();
                }
                if (GUILayout.Button("Reset Scan", GUILayout.Height(32)))
                {
                    var tracker = FindFirstObjectByType<NoteMarkerTracker>();
                    if (tracker != null) tracker.ResetTracking();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("Enter Play Mode to test the projection and floating dimensions.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        private void BrowseImageFile()
        {
            string path = EditorUtility.OpenFilePanel("Select Note Marker Image", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            string targetDir = "Assets/_Project/Textures";
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string fileName = Path.GetFileName(path);
            string destAssetPath = $"{targetDir}/{fileName}";

            File.Copy(path, destAssetPath, true);
            AssetDatabase.Refresh();

            // Set texture import settings
            TextureImporter importer = AssetImporter.GetAtPath(destAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            m_MarkerImage = AssetDatabase.LoadAssetAtPath<Texture2D>(destAssetPath);
        }

        private void CreateSampleMarkerTexture()
        {
            int width = 1024;
            int height = 1024;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Color bg = Color.white;
            Color dark = new Color(0.06f, 0.10f, 0.16f, 1f);
            Color accent = new Color(0f, 0.63f, 0.86f, 1f);
            Color grid = new Color(0.70f, 0.78f, 0.86f, 1f);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    tex.SetPixel(x, y, bg);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isOuterBorder = (x < 30 || x >= width - 30 || y < 30 || y >= height - 30);
                    bool isInnerBorder = (x >= 50 && x < 66) || (x >= width - 66 && x < width - 50) ||
                                         (y >= 50 && y < 66) || (y >= height - 66 && y < height - 50);
                    if (isOuterBorder || isInnerBorder)
                        tex.SetPixel(x, y, dark);
                }
            }

            int[] cx = { 150, width - 150, 150, width - 150 };
            int[] cy = { 150, 150, height - 150, height - 150 };
            for (int i = 0; i < 4; i++)
            {
                int x0 = cx[i] - 50, x1 = cx[i] + 50;
                int y0 = cy[i] - 50, y1 = cy[i] + 50;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        bool isBorder = (x == x0 || x == x1 || y == y0 || y == y1);
                        bool isCenter = (Math.Abs(x - cx[i]) < 22 && Math.Abs(y - cy[i]) < 22);
                        if (isBorder || isCenter) tex.SetPixel(x, y, dark);
                    }
                }
            }

            int centerX = width / 2;
            int centerY = height / 2;
            for (int y = centerY - 140; y <= centerY + 140; y++)
            {
                for (int x = centerX - 140; x <= centerX + 140; x++)
                {
                    int dx = x - centerX;
                    int dy = y - centerY;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if ((r > 120 && r < 136) || (r > 70 && r < 84))
                        tex.SetPixel(x, y, dark);
                    else if (r <= 45)
                        tex.SetPixel(x, y, accent);
                }
            }

            tex.Apply();

            string dir = "Assets/_Project/Textures";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string assetPath = $"{dir}/NoteMarker_Default.png";
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            m_MarkerImage = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private void ExecuteSaveAndApply()
        {
            if (m_MarkerImage == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select or upload a Note Marker Image first.", "OK");
                return;
            }

            if (m_LengthMm <= 0 || m_WidthMm <= 0 || m_ThicknessMm <= 0)
            {
                EditorUtility.DisplayDialog("Error", "Physical dimensions must be greater than zero.", "OK");
                return;
            }

            // Ensure settings folder exists
            string settingsDir = "Assets/_Project/Settings";
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }

            // 1. Build or Update NoteMarkerProfile
            string profilePath = $"{settingsDir}/CurrentNoteMarkerProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<NoteMarkerProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<NoteMarkerProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            profile.modelName = m_ModelName;
            profile.markerImage = m_MarkerImage;
            profile.lengthMm = m_LengthMm;
            profile.widthMm = m_WidthMm;
            profile.thicknessMm = m_ThicknessMm;
            profile.outlineColor = m_OutlineColor;
            profile.lineWidthMm = m_LineWidthMm;
            profile.showCornerNodes = m_ShowCornerNodes;
            profile.calloutOffsetMm = m_CalloutOffsetMm;
            profile.decimals = m_Decimals;
            profile.dimensionTextColor = m_DimensionTextColor;
            profile.showDimensionLines = m_ShowDimensionLines;
            profile.autoDetectByColor = m_AutoDetectByColor;
            profile.coverColor = ComputeCoverColor(m_MarkerImage, m_CoverColor);
            profile.colorHueTolerance = m_HueTolerance;
            profile.showDetectionDebug = m_ShowDetectionDebug;
            profile.flipCameraImageVertically = m_FlipCameraImage;
            profile.refineAfterLock = m_RefineAfterLock;
            profile.showBackplate = m_ShowBackplate;
            profile.optionalModelPrefab = m_OptionalModelPrefab;

            EditorUtility.SetDirty(profile);
            // Save only this tool's assets. AssetDatabase.SaveAssets() writes every dirty asset in memory,
            // including read-only package assets (e.g. AR Foundation's SimulationLitGray.mat, which XR Simulation
            // marks dirty in Play mode), which triggers Unity's "immutable packages were unexpectedly altered" warning.
            AssetDatabase.SaveAssetIfDirty(profile);

            // 2. Build the image library only if ARCore can use the cover
            var library = SyncImageLibrary(profile, out int score, out string libraryNote);
            m_QualityScore = score;
            m_QualityMessage = libraryNote;
            m_QualityChecked = true;
            m_QualityCheckedImage = m_MarkerImage;

            // 3. Configure the active AR Scene
            ConfigureScene(profile, library);

            EditorUtility.DisplayDialog(
                "Note Marker Configured!",
                $"Note Marker '{m_ModelName}' successfully configured:\n\n" +
                $"• Dimensions: {m_LengthMm:F0} × {m_WidthMm:F0} × {m_ThicknessMm:F0} mm\n" +
                $"• Real Size in AR: {(m_LengthMm / 10f):F1} cm × {(m_WidthMm / 10f):F1} cm × {(m_ThicknessMm / 10f):F1} cm\n" +
                $"• Marker Image: {m_MarkerImage.name}\n" +
                $"• Dimension Callouts: Width / Length / Depth on the edges ({m_CalloutOffsetMm:F0} mm offset)\n" +
                $"• Rotate & Lock Controls: Active\n\n" +
                libraryNote + "\n\n" +
                "The AR scene and app are now fully configured!",
                "Great!");
        }

        public const string LibraryPath = "Assets/_Project/Settings/NoteMarkerLibrary.asset";

        /// <summary>
        /// Dominant cover colour of the marker image (works for a cropped cover or a photo of the note on a table).
        /// Reads pixels through a RenderTexture, so the texture does not need to be readable.
        /// </summary>
        public static Color ComputeCoverColor(Texture2D image, Color fallback)
        {
            if (image == null) return fallback;

            int w = 128;
            int h = Mathf.Clamp(Mathf.RoundToInt(128f * image.height / Mathf.Max(1, image.width)), 16, 512);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(image, rt);
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readback.Apply();
                return NoteColorBlobDetector.DominantCoverColor(readback.GetPixels32(), w, h);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                DestroyImmediate(readback);
            }
        }

        /// <summary>
        /// Creates/updates the reference image library from the profile's marker image, but only when ARCore
        /// can actually use it. ARCore's build step fails ("Failed to get enough keypoints") for plain covers
        /// and also for an empty library ("No valid input image found"), and a library without ARCore data
        /// falls back to a broken loading path on the phone. So for an unusable cover the library asset is
        /// removed and the app runs in Align &amp; Lock mode only. Returns the library, or null when removed.
        /// </summary>
        public static XRReferenceImageLibrary SyncImageLibrary(NoteMarkerProfile profile, out int score, out string note)
        {
            score = -1;
            note = "";
            if (profile == null) return null;

            Texture2D image = profile.markerImage;
            string qualityMessage = "";
            if (image != null)
            {
                score = EvaluateMarkerQuality(image, out qualityMessage);
            }

            // score 0 = arcoreimg cannot add the image at all; -1 = could not evaluate (keep the image, ARCore decides).
            bool usable = image != null && score != 0;
            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);

            if (!usable)
            {
                if (library != null)
                {
                    AssetDatabase.DeleteAsset(LibraryPath);
                }
                profile.imageLibrary = null;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);

                string scanMode = profile.autoDetectByColor
                    ? "The app finds the note by its COVER COLOUR instead: point the phone at the note, the green outline with " +
                      "Width/Length/Depth snaps onto it, then tap LOCK."
                    : "Align the green box over the note and tap LOCK.";
                note = image == null
                    ? "No cover image: " + scanMode
                    : $"Cover '{image.name}' is too plain for ARCore image tracking (score 0), so image tracking is off. " + scanMode;
                Debug.LogWarning("[NoteMarkerToolWindow] " + note);
                return null;
            }

            string dir = Path.GetDirectoryName(LibraryPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (library == null)
            {
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            // Use AR Foundation's editor API: writing m_Images through SerializedObject leaves the
            // image GUID and texture GUID at zero, and ARCore's build step then fails with
            // "XRReferenceImage ... is missing a texture", so the marker never gets baked.
            while (library.count > 0)
            {
                library.RemoveAt(library.count - 1);
            }

            library.Add();
            library.SetName(0, string.IsNullOrEmpty(profile.modelName) ? "Note" : profile.modelName);
            library.SetTexture(0, image, true);
            library.SetSpecifySize(0, true);
            // Physical size in meters (X = width, Y = length)
            library.SetSize(0, new Vector2(profile.widthMm / 1000f, profile.lengthMm / 1000f));
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);

            profile.imageLibrary = library;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);

            note = score >= 75
                ? $"Cover image ARCore score {score}/100: auto-detection enabled (Align & LOCK also available)."
                : score > 0
                    ? $"Cover image ARCore score {score}/100 (75+ recommended): auto-detection may be unreliable; Align & LOCK is available."
                    : "Cover image added; its ARCore score could not be checked.";
            return library;
        }

        /// <summary>
        /// Makes sure every reference image has a non-zero image GUID and a texture GUID that matches its texture.
        /// A zero texture GUID makes ARCore's build step fail with "XRReferenceImage ... is missing a texture",
        /// which leaves the marker out of the APK.
        /// </summary>
        public static void EnsureLibraryGuids(XRReferenceImageLibrary library)
        {
            if (library == null) return;

            // Re-applying the texture through AR Foundation's API recomputes the texture GUID.
            for (int i = 0; i < library.count; i++)
            {
                Texture2D texture = library[i].texture;
                if (texture != null)
                {
                    library.SetTexture(i, texture, true);
                }
            }

            // The image GUID has no public setter; fill any zero GUIDs through serialization.
            var so = new SerializedObject(library);
            var images = so.FindProperty("m_Images");
            for (int i = 0; i < images.arraySize; i++)
            {
                var guidProp = images.GetArrayElementAtIndex(i).FindPropertyRelative("m_SerializedGuid");
                var lowProp = guidProp.FindPropertyRelative("m_GuidLow");
                var highProp = guidProp.FindPropertyRelative("m_GuidHigh");
                if (lowProp.ulongValue == 0 && highProp.ulongValue == 0)
                {
                    byte[] bytes = Guid.NewGuid().ToByteArray();
                    lowProp.ulongValue = BitConverter.ToUInt64(bytes, 0);
                    highProp.ulongValue = BitConverter.ToUInt64(bytes, 8);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
        }

        /// <summary>
        /// Runs ARCore's arcoreimg tool on the image. Returns a 0-100 score (ARCore recommends 75+),
        /// or -1 if the tool could not run. A plain cover with too few keypoints scores 0.
        /// </summary>
        public static int EvaluateMarkerQuality(Texture2D texture, out string message)
        {
            message = "";
            string imagePath = texture != null ? Path.GetFullPath(AssetDatabase.GetAssetPath(texture)) : null;
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                message = "Image file not found on disk.";
                return -1;
            }

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.xr.arcore");
            if (package == null)
            {
                message = "ARCore XR Plugin package not found.";
                return -1;
            }

            string toolDir = Application.platform == RuntimePlatform.WindowsEditor ? "Windows"
                           : Application.platform == RuntimePlatform.OSXEditor ? "MacOS" : "Linux";
            string toolName = Application.platform == RuntimePlatform.WindowsEditor ? "arcoreimg.exe" : "arcoreimg";
            string toolPath = Path.Combine(package.resolvedPath, "Tools~", toolDir, toolName);
            if (!File.Exists(toolPath))
            {
                message = "arcoreimg tool not found at " + toolPath;
                return -1;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(toolPath, $"eval-img --input_image_path=\"{imagePath}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    output = output.Trim();

                    foreach (string line in output.Split('\n'))
                    {
                        if (int.TryParse(line.Trim(), out int score))
                        {
                            message = score >= 75 ? "Good for ARCore auto-detection." : "Low score: auto-detection will be unreliable.";
                            return score;
                        }
                    }

                    message = string.IsNullOrEmpty(output) ? "No result from arcoreimg." : output;
                    // arcoreimg prints "Failed to get enough keypoints" for plain images: treat as score 0.
                    return output.IndexOf("keypoints", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : -1;
                }
            }
            catch (Exception ex)
            {
                message = "Could not run arcoreimg: " + ex.Message;
                return -1;
            }
        }

        public static void ConfigureScene(NoteMarkerProfile profile, XRReferenceImageLibrary library)
        {
            EnsureLibraryGuids(library);

            // Clean up ghost / residual objects
            string[] ghostNames = new string[]
            {
                "CAD_Wireframe_Root",
                "Note_Outline_Root",
                "Inspected_Notebook_Body",
                "Notebook_Body",
                "Locked_QC_Anchor",
                "Locked_Note_Anchor",
                "Simulated_Note_Anchor",
                "Note_AlignmentGuide",
                "QC_AlignmentGuide",
                "ARScanningReticle"
            };

            foreach (var gName in ghostNames)
            {
                var objs = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var obj in objs)
                {
                    if (obj != null && obj.name == gName && obj.scene.isLoaded)
                    {
                        DestroyImmediate(obj);
                    }
                }
            }

            // 1. Configure XR Origin with ARTrackedImageManager
            var origin = GameObject.Find("XR Origin (AR Rig)");
            ARTrackedImageManager trackedImageManager = null;
            if (origin != null)
            {
                trackedImageManager = origin.GetComponent<ARTrackedImageManager>();
                if (trackedImageManager == null)
                {
                    trackedImageManager = origin.AddComponent<ARTrackedImageManager>();
                }
            }
            else
            {
                trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
                if (trackedImageManager == null)
                {
                    var cam = Camera.main ?? FindFirstObjectByType<Camera>();
                    GameObject host = cam != null && cam.transform.parent != null
                        ? cam.transform.parent.gameObject
                        : new GameObject("AR_TrackedImageManager");
                    trackedImageManager = host.AddComponent<ARTrackedImageManager>();
                }
            }

            if (trackedImageManager != null)
            {
                // With no usable cover image there is no library: switch image tracking off entirely
                // so the app runs Align & Lock only and no image subsystem is started on the phone.
                var so = new SerializedObject(trackedImageManager);
                so.FindProperty("m_SerializedLibrary").objectReferenceValue = library;
                so.FindProperty("m_MaxNumberOfMovingImages").intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
                trackedImageManager.enabled = library != null;
                EditorUtility.SetDirty(trackedImageManager);
            }

            // 1b. Align & Lock needs table detection: keep the plane manager on (horizontal surfaces only).
            //     It was found switched off, which left LOCK permanently disabled on the phone.
            var planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
            if (planeManager != null)
            {
                planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
                planeManager.enabled = true;
                EditorUtility.SetDirty(planeManager);
            }
            else
            {
                Debug.LogError("[NoteMarkerToolWindow] No ARPlaneManager found on the XR Origin: table detection (and LOCK) will not work.");
            }

            // 2. Configure InspectionManagers object
            var managers = GameObject.Find("InspectionManagers");
            if (managers == null)
            {
                managers = new GameObject("InspectionManagers");
            }

            // Add or update NoteOutlineRenderer
            var outlineRenderer = managers.GetComponent<NoteOutlineRenderer>();
            if (outlineRenderer == null)
            {
                outlineRenderer = managers.AddComponent<NoteOutlineRenderer>();
            }
            outlineRenderer.SetProfile(profile);
            EditorUtility.SetDirty(outlineRenderer);

            // Add or update NoteMarkerTracker
            var tracker = managers.GetComponent<NoteMarkerTracker>();
            if (tracker == null)
            {
                tracker = managers.AddComponent<NoteMarkerTracker>();
            }
            tracker.SetProfile(profile);
            EditorUtility.SetDirty(tracker);

            // Add the cover-colour detector next to the tracker (it reads its settings from the profile)
            if (managers.GetComponent<NoteColorDetector>() == null)
            {
                Undo.AddComponent<NoteColorDetector>(managers);
            }

            // Remove legacy QC components from InspectionManagers. Disabling them is not enough:
            // Unity still runs Awake() on disabled components of an active GameObject, which made
            // MarkerlessQCController build a visible static guide box and the overlays purge the scene.
            // Those scripts have been deleted, so any leftover instances show up as missing scripts.
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(managers);
            if (removed > 0)
            {
                Debug.Log($"[NoteMarkerToolWindow] Removed {removed} missing (legacy) script component(s) from InspectionManagers.");
            }

            // 3. Configure UI Canvas
            ConfigureCanvasUI(profile, tracker);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        public static void ConfigureCanvasUI(NoteMarkerProfile profile, NoteMarkerTracker tracker)
        {
            var hudRoot = GameObject.Find("InspectionHUD_Root");
            if (hudRoot == null) return;

            // Remove or disable old ResultsPanel (pass/fail table)
            var resultsPanel = hudRoot.transform.Find("ResultsPanel");
            if (resultsPanel != null)
            {
                resultsPanel.gameObject.SetActive(false);
            }

            // Configure AlignmentPanel as clean Note Marker HUD
            var alignmentPanel = hudRoot.transform.Find("AlignmentPanel");
            if (alignmentPanel != null)
            {
                alignmentPanel.gameObject.SetActive(true);

                // Title
                var titleText = alignmentPanel.Find("HeaderBar/TitleText")?.GetComponent<Text>();
                if (titleText != null)
                {
                    titleText.text = "AR Note Marker Projection";
                }

                // Model Pill
                var pillText = alignmentPanel.Find("ModelPill/PillText")?.GetComponent<Text>();
                if (pillText != null)
                {
                    pillText.text = $"NOTE: {profile.modelName} ({profile.lengthMm:F0} × {profile.widthMm:F0} × {profile.thicknessMm:F0} mm)";
                }

                // Guidance Status
                var statusText = alignmentPanel.Find("GuidanceBanner/StatusText")?.GetComponent<Text>();
                if (statusText != null)
                {
                    statusText.text = "Align green box over note, then tap LOCK (or aim at marker cover)";
                }

                // Ensure distance text is active
                var distText = alignmentPanel.Find("DistanceText");
                if (distText != null)
                {
                    distText.gameObject.SetActive(true);
                }

                // Ensure bottom controls bar and buttons are active
                var bottomControls = alignmentPanel.Find("BottomControlsBar");
                if (bottomControls != null)
                {
                    bottomControls.gameObject.SetActive(true);

                    var rotLeft = bottomControls.Find("RotateLeftButton");
                    if (rotLeft != null)
                    {
                        rotLeft.gameObject.SetActive(true);
                        var so = new SerializedObject(rotLeft.gameObject);
                        so.FindProperty("m_IsActive").boolValue = true;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(rotLeft.gameObject);
                    }

                    var rotRight = bottomControls.Find("RotateRightButton");
                    if (rotRight != null)
                    {
                        rotRight.gameObject.SetActive(true);
                        var so = new SerializedObject(rotRight.gameObject);
                        so.FindProperty("m_IsActive").boolValue = true;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(rotRight.gameObject);
                    }

                    var lockBtn = bottomControls.Find("LockAndInspectButton");
                    if (lockBtn != null)
                    {
                        lockBtn.gameObject.SetActive(true);
                        var so = new SerializedObject(lockBtn.gameObject);
                        so.FindProperty("m_IsActive").boolValue = true;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(lockBtn.gameObject);

                        var lockLabel = lockBtn.Find("Label")?.GetComponent<Text>() ?? lockBtn.GetComponentInChildren<Text>();
                        if (lockLabel != null)
                        {
                            var soText = new SerializedObject(lockLabel);
                            soText.FindProperty("m_Text").stringValue = "LOCK & INSPECT";
                            soText.ApplyModifiedProperties();
                            EditorUtility.SetDirty(lockLabel);
                        }
                    }
                }
            }

            // Attach / Configure NoteMarkerHUD component on HUD root
            var hudComp = hudRoot.GetComponent<NoteMarkerHUD>();
            if (hudComp == null)
            {
                hudComp = hudRoot.AddComponent<NoteMarkerHUD>();
            }

            var serializedHud = new SerializedObject(hudComp);
            serializedHud.FindProperty("m_Tracker").objectReferenceValue = tracker;

            var statusT = alignmentPanel?.Find("GuidanceBanner/StatusText")?.GetComponent<Text>();
            var pillT = alignmentPanel?.Find("ModelPill/PillText")?.GetComponent<Text>();
            var distT = alignmentPanel?.Find("DistanceText")?.GetComponent<Text>();
            var bottomBar = alignmentPanel?.Find("BottomControlsBar")?.gameObject;
            var rotLeftBtn = alignmentPanel?.Find("BottomControlsBar/RotateLeftButton")?.GetComponent<Button>();
            var rotRightBtn = alignmentPanel?.Find("BottomControlsBar/RotateRightButton")?.GetComponent<Button>();
            var lockAndInspectBtn = alignmentPanel?.Find("BottomControlsBar/LockAndInspectButton")?.GetComponent<Button>();
            var lockLabelT = lockAndInspectBtn != null ? (lockAndInspectBtn.transform.Find("Label")?.GetComponent<Text>() ?? lockAndInspectBtn.GetComponentInChildren<Text>()) : null;

            if (statusT != null) serializedHud.FindProperty("m_StatusText").objectReferenceValue = statusT;
            if (pillT != null) serializedHud.FindProperty("m_ModelPillText").objectReferenceValue = pillT;
            if (distT != null) serializedHud.FindProperty("m_DistanceText").objectReferenceValue = distT;
            if (bottomBar != null) serializedHud.FindProperty("m_BottomControlsBar").objectReferenceValue = bottomBar;
            if (rotLeftBtn != null) serializedHud.FindProperty("m_RotateLeftButton").objectReferenceValue = rotLeftBtn;
            if (rotRightBtn != null) serializedHud.FindProperty("m_RotateRightButton").objectReferenceValue = rotRightBtn;
            if (lockAndInspectBtn != null) serializedHud.FindProperty("m_LockAndInspectButton").objectReferenceValue = lockAndInspectBtn;
            if (lockLabelT != null) serializedHud.FindProperty("m_LockAndInspectLabel").objectReferenceValue = lockLabelT;

            serializedHud.ApplyModifiedProperties();
            EditorUtility.SetDirty(hudComp);
        }

        private void InitStyles()
        {
            if (m_HeaderStyle != null) return;

            m_HeaderStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(16, 16, 14, 14)
            };

            m_CardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(14, 14, 12, 12)
            };

            m_TitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = new Color(0.18f, 0.95f, 0.48f) }
            };

            m_SubTitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.85f, 0.88f, 0.92f) },
                wordWrap = true
            };
        }
    }
}
#endif
