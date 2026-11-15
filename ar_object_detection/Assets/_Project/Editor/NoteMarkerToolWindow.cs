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
        private float m_LengthMm = 230f;
        private float m_WidthMm = 154f;
        private float m_ThicknessMm = 50f;

        private Color m_OutlineColor = new Color(0.12f, 0.95f, 0.42f, 1f); // Vibrant AR Green
        private float m_LineWidthMm = 3.5f;
        private bool m_ShowCornerNodes = true;

        private float m_DimensionHeightOffsetMm = 40f;
        private DimensionDisplayFormat m_DimensionFormat = DimensionDisplayFormat.CompactWithLabels;
        private Color m_DimensionTextColor = Color.white;
        private bool m_ShowLeaderLine = true;
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
                m_DimensionHeightOffsetMm = profile.dimensionHeightOffsetMm;
                m_DimensionFormat = profile.dimensionFormat;
                m_DimensionTextColor = profile.dimensionTextColor;
                m_ShowLeaderLine = profile.showLeaderLine;
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
            EditorGUILayout.LabelField("Assign Note Image & Physical Dimensions -> Project 3D Outline with Floating Dimensions", m_SubTitleStyle);
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
            EditorGUILayout.LabelField("4. 3D Outline & Floating Dimension Styling", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            m_OutlineColor = EditorGUILayout.ColorField("Outline Color", m_OutlineColor);
            m_LineWidthMm = EditorGUILayout.Slider("Line Width (mm)", m_LineWidthMm, 1f, 10f);
            m_ShowCornerNodes = EditorGUILayout.Toggle("Corner Accent Nodes", m_ShowCornerNodes);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Dimension Floating Above Model:", EditorStyles.miniBoldLabel);
            m_DimensionHeightOffsetMm = EditorGUILayout.Slider("Height Above Model (mm)", m_DimensionHeightOffsetMm, 15f, 150f);
            m_DimensionFormat = (DimensionDisplayFormat)EditorGUILayout.EnumPopup("Display Format", m_DimensionFormat);
            m_DimensionTextColor = EditorGUILayout.ColorField("Text Color", m_DimensionTextColor);
            m_ShowLeaderLine = EditorGUILayout.Toggle("Show Leader Line", m_ShowLeaderLine);
            m_ShowBackplate = EditorGUILayout.Toggle("Show Dark Backplate", m_ShowBackplate);

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

            // 1. Build or Update XRReferenceImageLibrary
            string libraryPath = $"{settingsDir}/NoteMarkerLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(libraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, libraryPath);
            }

            // Populate library with image & physical dimensions
            var serializedLib = new SerializedObject(library);
            var imagesProp = serializedLib.FindProperty("m_Images");
            imagesProp.ClearArray();
            imagesProp.InsertArrayElementAtIndex(0);

            var entry = imagesProp.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("m_Name").stringValue = m_ModelName;
            entry.FindPropertyRelative("m_Texture").objectReferenceValue = m_MarkerImage;
            entry.FindPropertyRelative("m_SpecifySize").boolValue = true;
            // Physical size in meters (X = width, Y = length)
            entry.FindPropertyRelative("m_Size").vector2Value = new Vector2(m_WidthMm / 1000f, m_LengthMm / 1000f);

            serializedLib.ApplyModifiedProperties();
            EditorUtility.SetDirty(library);

            // 2. Build or Update NoteMarkerProfile
            string profilePath = $"{settingsDir}/CurrentNoteMarkerProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<NoteMarkerProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<NoteMarkerProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            profile.modelName = m_ModelName;
            profile.markerImage = m_MarkerImage;
            profile.imageLibrary = library;
            profile.lengthMm = m_LengthMm;
            profile.widthMm = m_WidthMm;
            profile.thicknessMm = m_ThicknessMm;
            profile.outlineColor = m_OutlineColor;
            profile.lineWidthMm = m_LineWidthMm;
            profile.showCornerNodes = m_ShowCornerNodes;
            profile.dimensionHeightOffsetMm = m_DimensionHeightOffsetMm;
            profile.dimensionFormat = m_DimensionFormat;
            profile.dimensionTextColor = m_DimensionTextColor;
            profile.showLeaderLine = m_ShowLeaderLine;
            profile.showBackplate = m_ShowBackplate;
            profile.optionalModelPrefab = m_OptionalModelPrefab;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 3. Configure the active AR Scene
            ConfigureScene(profile, library);

            EditorUtility.DisplayDialog(
                "Note Marker Configured!",
                $"Note Marker '{m_ModelName}' successfully configured:\n\n" +
                $"• Dimensions: {m_LengthMm:F0} × {m_WidthMm:F0} × {m_ThicknessMm:F0} mm\n" +
                $"• Real Size in AR: {(m_LengthMm / 10f):F1} cm × {(m_WidthMm / 10f):F1} cm × {(m_ThicknessMm / 10f):F1} cm\n" +
                $"• Marker Image: {m_MarkerImage.name}\n" +
                $"• Floating Dimension: Directly above model ({m_DimensionHeightOffsetMm:F0} mm offset)\n" +
                $"• Rotate & Lock Controls: Active\n\n" +
                "The AR scene and app are now fully configured!",
                "Great!");
        }

        public static void ConfigureScene(NoteMarkerProfile profile, XRReferenceImageLibrary library)
        {
            // Clean up ghost / residual objects
            string[] ghostNames = new string[]
            {
                "CAD_Wireframe_Root",
                "Note_Outline_Root",
                "Inspected_Notebook_Body",
                "Notebook_Body",
                "Locked_QC_Anchor",
                "Locked_Note_Anchor"
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
                trackedImageManager.referenceLibrary = library;
                trackedImageManager.requestedMaxNumberOfMovingImages = 1;
                EditorUtility.SetDirty(trackedImageManager);
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

            // Disable legacy QC components on InspectionManagers to avoid conflicting events and hardcoded values
            var qcUI = managers.GetComponent<CADInspection.UI.QCInspectionUIManager>();
            if (qcUI != null) { qcUI.enabled = false; EditorUtility.SetDirty(qcUI); }

            var markerlessQC = managers.GetComponent<CADInspection.Detection.MarkerlessQCController>();
            if (markerlessQC != null) { markerlessQC.enabled = false; EditorUtility.SetDirty(markerlessQC); }

            var oldDetector = managers.GetComponent<CADInspection.Detection.NotebookDetector>();
            if (oldDetector != null) { oldDetector.enabled = false; EditorUtility.SetDirty(oldDetector); }

            var oldPlaneTap = managers.GetComponent<CADInspection.Detection.PlaneTapToSpawnCAD>();
            if (oldPlaneTap != null) { oldPlaneTap.enabled = false; EditorUtility.SetDirty(oldPlaneTap); }

            var oldEngine = managers.GetComponent<CADInspection.Measurement.InspectionEngine>();
            if (oldEngine != null) { oldEngine.enabled = false; EditorUtility.SetDirty(oldEngine); }

            var oldWireframe = managers.GetComponent<CADInspection.Visualization.CADWireframeOverlay>();
            if (oldWireframe != null) { oldWireframe.enabled = false; EditorUtility.SetDirty(oldWireframe); }

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
