using System;
using UnityEngine;
using CADInspection.Detection;

namespace CADInspection.Visualization
{
    /// <summary>
    /// Projects the 3D outline of the note model and displays the dimension badge
    /// floating directly above the model in AR space.
    /// Billboarded to face the camera from all angles.
    /// Completely free of any pass/fail, test, or QC text.
    /// </summary>
    public class NoteOutlineRenderer : MonoBehaviour
    {
        [Header("Active Profile")]
        [SerializeField] private NoteMarkerProfile m_Profile;

        private GameObject m_OutlineRoot;
        private Material m_LineMaterial;
        private Material m_BackplateMaterial;

        public bool IsShowing => m_OutlineRoot != null;
        public NoteMarkerProfile Profile => m_Profile;

        private void Awake()
        {
            PurgeExistingOutlines();
            InitializeMaterials();
        }

        private void OnDestroy()
        {
            HideOutline();
        }

        public static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        public static void PurgeExistingOutlines()
        {
#if UNITY_EDITOR
            string[] targetNames = new string[]
            {
                "Note_Outline_Root",
                "CAD_Wireframe_Root",
                "Inspected_Notebook_Body",
                "Notebook_Body"
            };

            foreach (var name in targetNames)
            {
                var objs = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var obj in objs)
                {
                    if (obj != null && obj.name == name && obj.scene.isLoaded)
                    {
                        SafeDestroy(obj);
                    }
                }
            }
#endif
        }

        public void InitializeMaterials()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") 
                              ?? Shader.Find("Sprites/Default") 
                              ?? Shader.Find("Mobile/Particles/Alpha Blended")
                              ?? Shader.Find("Hidden/Internal-Colored");

            if (unlitShader != null)
            {
                m_LineMaterial = new Material(unlitShader);
                Color wireColor = m_Profile != null ? m_Profile.outlineColor : new Color(0.12f, 0.95f, 0.42f, 1f);
                m_LineMaterial.color = wireColor;
                if (m_LineMaterial.HasProperty("_BaseColor"))
                    m_LineMaterial.SetColor("_BaseColor", wireColor);
            }

            Shader transparentShader = Shader.Find("Universal Render Pipeline/Unlit") 
                                    ?? Shader.Find("Sprites/Default")
                                    ?? Shader.Find("Hidden/Internal-Colored");
            if (transparentShader != null)
            {
                m_BackplateMaterial = new Material(transparentShader);
                Color bgColor = new Color(0.04f, 0.08f, 0.12f, 0.88f);
                m_BackplateMaterial.color = bgColor;
                if (m_BackplateMaterial.HasProperty("_BaseColor"))
                    m_BackplateMaterial.SetColor("_BaseColor", bgColor);
            }
        }

        public void SetProfile(NoteMarkerProfile profile)
        {
            m_Profile = profile;
            InitializeMaterials();
        }

        /// <summary>
        /// Projects the note outline and dimension badge parented to the tracked anchor.
        /// If anchorAtTopCover is true (image marker tracking), Y=0 is top cover and Y=-thk is desk.
        /// If anchorAtTopCover is false (table plane placement), Y=0 is desk and Y=+thk is top cover.
        /// </summary>
        public void ShowOutline(Transform anchorTransform, Vector3 position, Quaternion rotation, NoteMarkerProfile profile = null, bool anchorAtTopCover = false)
        {
            if (profile != null)
            {
                m_Profile = profile;
                InitializeMaterials();
            }

            HideOutline();

            m_OutlineRoot = new GameObject("Note_Outline_Root");

            if (anchorTransform != null)
            {
                m_OutlineRoot.transform.SetParent(anchorTransform, false);
                m_OutlineRoot.transform.localPosition = Vector3.zero;
                m_OutlineRoot.transform.localRotation = Quaternion.identity;
            }
            else
            {
                m_OutlineRoot.transform.position = position;
                m_OutlineRoot.transform.rotation = rotation;
            }

            if (m_Profile == null)
            {
                var tracker = FindFirstObjectByType<NoteMarkerTracker>();
                if (tracker != null && tracker.Profile != null)
                {
                    m_Profile = tracker.Profile;
                }
            }

            if (m_Profile == null)
            {
                Debug.LogError("[NoteOutlineRenderer] No NoteMarkerProfile assigned. Please configure the note dimensions in 'Tools > Note AR Setup Tool'.");
                return;
            }

            float lenMm = m_Profile.lengthMm;
            float widMm = m_Profile.widthMm;
            float thkMm = m_Profile.thicknessMm;
            float lineWidth = m_Profile.lineWidthMm / 1000f;

            // Build 3D outline box
            BuildOutlineBox(widMm, lenMm, thkMm, lineWidth, anchorAtTopCover);

            // Build Dimension Floating Badge directly ABOVE the note model
            BuildDimensionBadgeAboveModel(widMm, lenMm, thkMm, anchorAtTopCover);

            // Optional 3D Model Prefab overlay
            if (m_Profile != null && m_Profile.optionalModelPrefab != null)
            {
                GameObject modelInstance = Instantiate(m_Profile.optionalModelPrefab, m_OutlineRoot.transform);
                float centerY = anchorAtTopCover ? -(thkMm / 1000f) * 0.5f : (thkMm / 1000f) * 0.5f;
                modelInstance.transform.localPosition = new Vector3(0f, centerY, 0f);
                modelInstance.transform.localRotation = Quaternion.identity;
            }
        }

        public void HideOutline()
        {
            if (m_OutlineRoot != null)
            {
                SafeDestroy(m_OutlineRoot);
                m_OutlineRoot = null;
            }
        }

        private void BuildOutlineBox(float widthMm, float lengthMm, float thicknessMm, float lineWidth, bool anchorAtTopCover)
        {
            float hx = (widthMm / 1000f) * 0.5f;
            float hy = (thicknessMm / 1000f);
            float hz = (lengthMm / 1000f) * 0.5f;

            float yBottom = anchorAtTopCover ? -hy : 0f;
            float yTop = anchorAtTopCover ? 0f : hy;

            // 8 corners of the 3D note model
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(-hx, yBottom, -hz), // 0: Bottom Back Left
                new Vector3( hx, yBottom, -hz), // 1: Bottom Back Right
                new Vector3( hx, yBottom,  hz), // 2: Bottom Front Right
                new Vector3(-hx, yBottom,  hz), // 3: Bottom Front Left
                new Vector3(-hx, yTop,    -hz), // 4: Top Back Left
                new Vector3( hx, yTop,    -hz), // 5: Top Back Right
                new Vector3( hx, yTop,     hz), // 6: Top Front Right
                new Vector3(-hx, yTop,     hz)  // 7: Top Front Left
            };

            int[,] edges = new int[12, 2]
            {
                {0,1}, {1,2}, {2,3}, {3,0}, // Bottom base rectangle (on table)
                {4,5}, {5,6}, {6,7}, {7,4}, // Top cover rectangle (on marker)
                {0,4}, {1,5}, {2,6}, {3,7}  // 4 Vertical thickness corner pillars
            };

            for (int i = 0; i < 12; i++)
            {
                GameObject edgeObj = new GameObject($"Outline_Edge_{i}");
                edgeObj.transform.SetParent(m_OutlineRoot.transform, false);

                LineRenderer lr = edgeObj.AddComponent<LineRenderer>();
                lr.material = m_LineMaterial;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, corners[edges[i, 0]]);
                lr.SetPosition(1, corners[edges[i, 1]]);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }

            // Optional corner accent nodes
            if (m_Profile == null || m_Profile.showCornerNodes)
            {
                float nodeSize = lineWidth * 2.2f;
                for (int i = 0; i < 8; i++)
                {
                    GameObject node = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    node.name = $"Corner_Node_{i}";
                    node.transform.SetParent(m_OutlineRoot.transform, false);
                    node.transform.localPosition = corners[i];
                    node.transform.localScale = Vector3.one * nodeSize;

                    Collider col = node.GetComponent<Collider>();
                    if (col != null) SafeDestroy(col);

                    MeshRenderer mr = node.GetComponent<MeshRenderer>();
                    if (mr != null) mr.material = m_LineMaterial;
                }
            }
        }

        /// <summary>
        /// Builds the floating dimension badge positioned directly ABOVE the note model.
        /// </summary>
        private void BuildDimensionBadgeAboveModel(float widthMm, float lengthMm, float thicknessMm, bool anchorAtTopCover)
        {
            float offsetMm = m_Profile != null ? m_Profile.dimensionHeightOffsetMm : 40f;
            float offsetMeters = offsetMm / 1000f;
            float topY = anchorAtTopCover ? 0f : (thicknessMm / 1000f);

            // Position is directly above the center of the top cover
            Vector3 badgePos = new Vector3(0f, topY + offsetMeters, 0f);

            GameObject badgeObj = new GameObject("DimensionBadge_AboveModel");
            badgeObj.transform.SetParent(m_OutlineRoot.transform, false);
            badgeObj.transform.localPosition = badgePos;

            // Billboard so the badge always faces the user's camera
            badgeObj.AddComponent<BillboardLookAtCamera>();

            // Leader line linking top-cover center to the floating badge
            bool showLeader = m_Profile == null || m_Profile.showLeaderLine;
            if (showLeader && offsetMeters > 0.015f)
            {
                GameObject leaderObj = new GameObject("LeaderLine");
                leaderObj.transform.SetParent(m_OutlineRoot.transform, false);

                LineRenderer leaderLr = leaderObj.AddComponent<LineRenderer>();
                leaderLr.material = m_LineMaterial;
                float leaderWidth = (m_Profile != null ? m_Profile.lineWidthMm : 3.5f) * 0.5f / 1000f;
                leaderLr.startWidth = leaderWidth;
                leaderLr.endWidth = leaderWidth;
                leaderLr.positionCount = 2;
                leaderLr.useWorldSpace = false;
                leaderLr.SetPosition(0, new Vector3(0f, topY, 0f));
                leaderLr.SetPosition(1, new Vector3(0f, topY + offsetMeters - 0.012f, 0f));
                leaderLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                leaderLr.receiveShadows = false;
            }

            // Format dimension text
            string formattedText = GetFormattedDimensionString(lengthMm, widthMm, thicknessMm);

            // Optional dark translucent backplate for contrast
            bool showBackplate = m_Profile == null || m_Profile.showBackplate;
            if (showBackplate)
            {
                GameObject backplate = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backplate.name = "Backplate";
                backplate.transform.SetParent(badgeObj.transform, false);
                backplate.transform.localPosition = new Vector3(0f, 0f, 0.002f); // Slightly behind text
                backplate.transform.localRotation = Quaternion.identity;

                float plateWidth = 0.16f;
                float plateHeight = 0.052f;
                backplate.transform.localScale = new Vector3(plateWidth, plateHeight, 1f);

                Collider col = backplate.GetComponent<Collider>();
                if (col != null) SafeDestroy(col);

                MeshRenderer mr = backplate.GetComponent<MeshRenderer>();
                if (mr != null) mr.material = m_BackplateMaterial;
            }

            // 3D TextMesh for Dimension
            GameObject textObj = new GameObject("DimensionText");
            textObj.transform.SetParent(badgeObj.transform, false);
            textObj.transform.localPosition = Vector3.zero;
            textObj.transform.localRotation = Quaternion.identity;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = formattedText;
            tm.fontSize = 32;
            tm.characterSize = 0.0024f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = m_Profile != null ? m_Profile.dimensionTextColor : Color.white;
        }

        private string GetFormattedDimensionString(float lengthMm, float widthMm, float thicknessMm)
        {
            DimensionDisplayFormat format = m_Profile != null ? m_Profile.dimensionFormat : DimensionDisplayFormat.CompactWithLabels;

            switch (format)
            {
                case DimensionDisplayFormat.Compact:
                    return $"{lengthMm:F0} × {widthMm:F0} × {thicknessMm:F0} mm";

                case DimensionDisplayFormat.Standard:
                    return $"{lengthMm:F0} mm × {widthMm:F0} mm × {thicknessMm:F0} mm";

                case DimensionDisplayFormat.Detailed:
                    return $"L: {lengthMm:F0} mm  |  W: {widthMm:F0} mm  |  H: {thicknessMm:F0} mm";

                case DimensionDisplayFormat.CompactWithLabels:
                default:
                    return $"<b>{lengthMm:F0} mm × {widthMm:F0} mm × {thicknessMm:F0} mm</b>\nL: {lengthMm:F0} mm   W: {widthMm:F0} mm   H: {thicknessMm:F0} mm";
            }
        }
    }
}
