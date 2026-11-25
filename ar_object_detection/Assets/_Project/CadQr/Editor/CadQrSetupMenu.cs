using System.IO;
using CADInspection.CadQr;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace CADInspection.Editor
{
    /// <summary>
    /// Menu: Tools > CAD QR Overlay.
    ///   Build Demo Scene  - makes the sample models, the model library, the materials and a new scene
    ///                       (Assets/_Project/CadQr/CadQrOverlay.unity) with everything wired up, and puts
    ///                       that scene first in Build Settings.
    ///   Export QR Cards   - saves one PNG per model into a "QR_Cards" folder next to Assets, for printing.
    /// Running it again is safe: existing library entries and materials are kept.
    /// </summary>
    public static class CadQrSetupMenu
    {
        const string Root = "Assets/_Project/CadQr";
        const string ModelsFolder = Root + "/Models";
        const string MaterialsFolder = Root + "/Materials";
        const string LibraryPath = Root + "/CadModelLibrary.asset";
        const string ScenePath = Root + "/CadQrOverlay.unity";

        [MenuItem("Tools/CAD QR Overlay/Build Demo Scene", priority = 0)]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ConfigureModelImports();
            CadModelLibrary library = CreateOrUpdateLibrary();
            Material wire = GetOrCreateMaterial("CadWire", new Color(0.1f, 1f, 0.35f, 1f), false);
            Material box = GetOrCreateMaterial("CadBox", new Color(1f, 0.85f, 0.2f, 1f), false);
            Material ghost = GetOrCreateMaterial("CadGhost", new Color(0.25f, 0.75f, 1f, 0.22f), true);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            CreateArRig(out XROrigin origin, out Camera camera);

            // Image tracking: disabled on purpose. The controller fills the library at runtime, then enables it.
            // Note: no "??" here. Unity objects can be "fake null" in the Editor, and ?? does not see that.
            var imageManager = origin.GetComponent<ARTrackedImageManager>();
            if (imageManager == null) imageManager = origin.gameObject.AddComponent<ARTrackedImageManager>();
            imageManager.referenceLibrary = null;
            imageManager.requestedMaxNumberOfMovingImages = 1;
            imageManager.enabled = false;
            var anchorManager = origin.GetComponent<ARAnchorManager>();
            if (anchorManager == null) anchorManager = origin.gameObject.AddComponent<ARAnchorManager>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            var app = new GameObject("CadQrApp");
            var controller = app.AddComponent<CadQrOverlayController>();
            var hud = app.AddComponent<CadQrHUD>();

            var so = new SerializedObject(controller);
            so.FindProperty("m_Library").objectReferenceValue = library;
            so.FindProperty("m_ImageManager").objectReferenceValue = imageManager;
            so.FindProperty("m_AnchorManager").objectReferenceValue = anchorManager;
            so.FindProperty("m_Camera").objectReferenceValue = camera;
            so.FindProperty("m_WireMaterial").objectReferenceValue = wire;
            so.FindProperty("m_BoxMaterial").objectReferenceValue = box;
            so.FindProperty("m_GhostMaterial").objectReferenceValue = ghost;
            so.ApplyModifiedPropertiesWithoutUndo();

            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("m_Controller").objectReferenceValue = controller;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            PutSceneFirstInBuildSettings(ScenePath);
            Selection.activeGameObject = app;

            Debug.Log($"[CadQr] Demo scene ready: {ScenePath}. It is now first in Build Settings. " +
                      "Next: Tools > CAD QR Overlay > Export QR Cards, print one, then build to your phone.");
        }

        [MenuItem("Tools/CAD QR Overlay/Export QR Cards (PNG)", priority = 1)]
        public static void ExportQrCards()
        {
            var library = AssetDatabase.LoadAssetAtPath<CadModelLibrary>(LibraryPath);
            if (library == null)
            {
                EditorUtility.DisplayDialog("CAD QR Overlay", "No model library yet. Run Build Demo Scene first.", "OK");
                return;
            }

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "QR_Cards"));
            Directory.CreateDirectory(folder);
            foreach (CadModelEntry entry in library.models)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                Texture2D card = QrTextureFactory.CreateMarkerCard(entry.id);
                string file = Path.Combine(folder, $"{entry.id}_{entry.markerWidthMeters * 1000f:0}mm.png");
                File.WriteAllBytes(file, card.EncodeToPNG());
                Object.DestroyImmediate(card);
            }
            Debug.Log("[CadQr] QR cards saved to " + folder + ". Print each one so the outer black frame is exactly the size in its file name.");
            EditorUtility.RevealInFinder(folder);
        }

        // ------------------------------------------------------------------

        static void CreateArRig(out XROrigin origin, out Camera camera)
        {
            // Prefer AR Foundation's own menu items: they set up the camera exactly like Unity's templates.
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");

            if (Object.FindFirstObjectByType<ARSession>() == null)
            {
                var session = new GameObject("AR Session");
                session.AddComponent<ARSession>();
                session.AddComponent<ARInputManager>();
            }

            origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                // Fallback: build the same rig by hand.
                var originGo = new GameObject("XR Origin (Mobile AR)");
                origin = originGo.AddComponent<XROrigin>();
                var offset = new GameObject("Camera Offset");
                offset.transform.SetParent(originGo.transform, false);
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                camGo.transform.SetParent(offset.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 30f;
                camGo.AddComponent<ARCameraManager>();
                camGo.AddComponent<ARCameraBackground>();
                var driver = camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                driver.positionInput = new InputActionProperty(new InputAction("Position", InputActionType.Value,
                    "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3"));
                driver.rotationInput = new InputActionProperty(new InputAction("Rotation", InputActionType.Value,
                    "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion"));
                origin.Camera = cam;
                origin.CameraFloorOffsetObject = offset;
            }

            camera = origin.Camera;
            if (camera != null) camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.05f); // allow close-up inspection
        }

        static void ConfigureModelImports()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                bool changed = !importer.isReadable || importer.useFileScale || !Mathf.Approximately(importer.globalScale, 1f);
                // Read/Write: the app reads the vertices on the phone to build the wireframe.
                importer.isReadable = true;
                // Scale 1, no unit conversion: 1 unit in the file = 1 unit in Unity. The library entry's
                // unitsToMeters (0.001 for millimetres) does the conversion, so it is always explicit.
                importer.useFileScale = false;
                importer.globalScale = 1f;
                if (changed) importer.SaveAndReimport();
            }
        }

        static CadModelLibrary CreateOrUpdateLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<CadModelLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CadModelLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            AddSample(library, "notebook", "Notebook 240 x 180", "notebook.obj", MarkerPlacement.TopCentre, 0.10f);
            // The L bracket's top-centre is empty air, so its card goes on the flat plate in front of the leg.
            CadModelEntry bracket = AddSample(library, "l_bracket", "L Bracket", "l_bracket.obj", MarkerPlacement.Custom, 0.05f);
            if (bracket != null) bracket.customMarkerPositionMm = new Vector3(0f, 8f, -4f);
            AddSample(library, "hex_block", "Hex Block", "hex_block.obj", MarkerPlacement.TopCentre, 0.045f);

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static CadModelEntry AddSample(CadModelLibrary library, string id, string displayName, string file, MarkerPlacement placement, float cardMeters)
        {
            if (library.Find(id) != null) return null; // keep whatever the user changed
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsFolder}/{file}");
            if (prefab == null) { Debug.LogWarning($"[CadQr] Sample model {file} not found."); return null; }
            var entry = new CadModelEntry
            {
                id = id,
                displayName = displayName,
                modelPrefab = prefab,
                unitsToMeters = 0.001f,
                markerWidthMeters = cardMeters,
                markerPlacement = placement
            };
            library.models.Add(entry);
            return entry;
        }

        static Material GetOrCreateMaterial(string name, Color color, bool transparent)
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder(Root, "Materials");
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f); // 1 = Transparent
                material.SetFloat("_Blend", 0f);   // 0 = Alpha
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void PutSceneFirstInBuildSettings(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == scenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
