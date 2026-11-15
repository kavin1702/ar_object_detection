using UnityEngine;

namespace CADInspection.Visualization
{
    /// <summary>
    /// Renders the precision CAD wireframe overlay and 3D dimension callouts
    /// strictly parented to the ARAnchor so it never drifts in physical space.
    /// Wireframe-only (completely transparent interior) so the real object is fully visible.
    /// </summary>
    public class CADWireframeOverlay : MonoBehaviour
    {
        [Header("Nominal Dimensions (mm)")]
        [SerializeField] private float m_LengthMm = 230f;
        [SerializeField] private float m_WidthMm = 154f;
        [SerializeField] private float m_ThicknessMm = 50f;

        [Header("Wireframe Styling")]
        [SerializeField] private Color m_WireframeColor = new Color(0.15f, 1f, 0.4f, 1f); // Vibrant AR Green
        [SerializeField] private float m_WireframeLineWidth = 0.0035f; // 3.5mm thick line

        private GameObject m_OverlayRoot;
        private Material m_WireframeMaterial;

        private void Awake()
        {
            PurgeGhostObjects();
            SetupMaterial();
        }

        /// <summary>
        /// Cleans up any stray or serialized ghost objects from the scene so nothing floats in mid-air.
        /// </summary>
        public static void PurgeGhostObjects()
        {
            string[] ghostNames = new string[]
            {
                "CAD_Wireframe_Root",
                "Inspected_Notebook_Body",
                "Notebook_Body",
                "Locked_QC_Anchor",
                "Spawned_CAD_Notebook"
            };

            foreach (var name in ghostNames)
            {
                var objs = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var obj in objs)
                {
                    if (obj != null && obj.name == name && obj.scene.isLoaded)
                    {
                        if (Application.isPlaying) Destroy(obj);
                        else DestroyImmediate(obj);
                    }
                }
            }
        }

        private void SetupMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") 
                        ?? Shader.Find("Hidden/Internal-Colored") 
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Mobile/Particles/Alpha Blended");

            if (unlit != null)
            {
                m_WireframeMaterial = new Material(unlit);
                m_WireframeMaterial.color = m_WireframeColor;
                if (m_WireframeMaterial.HasProperty("_BaseColor"))
                    m_WireframeMaterial.SetColor("_BaseColor", m_WireframeColor);
            }
        }

        public void DisplayOverlay(Pose pose, float measuredLengthMm, float measuredWidthMm, float measuredDepthMm)
        {
            DisplayOverlay(null, pose, measuredLengthMm, measuredWidthMm, measuredDepthMm);
        }

        /// <summary>
        /// Displays the wireframe overlay parented to the ARAnchor so it stays rigidly anchored to the real object.
        /// </summary>
        public void DisplayOverlay(Transform anchorTransform, Pose pose, float measuredLengthMm, float measuredWidthMm, float measuredDepthMm)
        {
            m_LengthMm = measuredLengthMm;
            m_WidthMm = measuredWidthMm;
            m_ThicknessMm = measuredDepthMm;

            HideOverlay();

            m_OverlayRoot = new GameObject("CAD_Wireframe_Root");
            
            // Parent to ARAnchor so ARCore keeps it locked to the physical tabletop
            if (anchorTransform != null)
            {
                m_OverlayRoot.transform.SetParent(anchorTransform, false);
                m_OverlayRoot.transform.localPosition = Vector3.zero;
                m_OverlayRoot.transform.localRotation = Quaternion.identity;
            }
            else
            {
                m_OverlayRoot.transform.position = pose.position;
                m_OverlayRoot.transform.rotation = pose.rotation;
            }

            // Build pure wireframe cage (no solid mesh)
            BuildWireframeMesh(false);

            // Build 3D floating dimension badges on edges
            BuildDimensionCallouts(measuredLengthMm, measuredWidthMm, measuredDepthMm, false);
        }

        /// <summary>
        /// Displays the wireframe overlay projecting downwards from the tracked top cover image to the table.
        /// </summary>
        public void DisplayOverlayForImageTarget(Transform anchorTransform, Vector3 position, Quaternion rotation, float lengthMm, float widthMm, float thicknessMm)
        {
            m_LengthMm = lengthMm;
            m_WidthMm = widthMm;
            m_ThicknessMm = thicknessMm;

            HideOverlay();

            m_OverlayRoot = new GameObject("CAD_Wireframe_Root");

            if (anchorTransform != null)
            {
                m_OverlayRoot.transform.SetParent(anchorTransform, false);
                m_OverlayRoot.transform.localPosition = Vector3.zero;
                m_OverlayRoot.transform.localRotation = Quaternion.identity;
            }
            else
            {
                m_OverlayRoot.transform.position = position;
                m_OverlayRoot.transform.rotation = rotation;
            }

            // Invert thickness downwards from top cover (Y=0) to desk (Y=-thickness)
            BuildWireframeMesh(true);
            BuildDimensionCallouts(lengthMm, widthMm, thicknessMm, true);
        }

        public void HideOverlay()
        {
            if (m_OverlayRoot != null)
            {
                Destroy(m_OverlayRoot);
                m_OverlayRoot = null;
            }
        }

        private void BuildWireframeMesh(bool projectDownwardsFromCover = false)
        {
            float hx = (m_WidthMm / 1000f) * 0.5f;
            float hy = (m_ThicknessMm / 1000f);
            float hz = (m_LengthMm / 1000f) * 0.5f;

            float yBottom = projectDownwardsFromCover ? -hy : 0f;
            float yTop = projectDownwardsFromCover ? 0f : hy;

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
                {0,1}, {1,2}, {2,3}, {3,0}, // Bottom rectangle
                {4,5}, {5,6}, {6,7}, {7,4}, // Top rectangle
                {0,4}, {1,5}, {2,6}, {3,7}  // 4 Vertical corner pillars
            };

            for (int i = 0; i < 12; i++)
            {
                GameObject edgeObj = new GameObject($"CAD_Edge_{i}");
                edgeObj.transform.SetParent(m_OverlayRoot.transform, false);

                LineRenderer lr = edgeObj.AddComponent<LineRenderer>();
                lr.material = m_WireframeMaterial;
                lr.startWidth = m_WireframeLineWidth;
                lr.endWidth = m_WireframeLineWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, corners[edges[i, 0]]);
                lr.SetPosition(1, corners[edges[i, 1]]);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }
        }

        private void BuildDimensionCallouts(float lengthMm, float widthMm, float depthMm, bool projectDownwardsFromCover = false)
        {
            float hx = (m_WidthMm / 1000f) * 0.5f;
            float hy = (m_ThicknessMm / 1000f);
            float hz = (m_LengthMm / 1000f) * 0.5f;

            float yMid = projectDownwardsFromCover ? -hy * 0.5f : hy * 0.5f;
            float yTop = projectDownwardsFromCover ? 0.04f : hy + 0.04f;

            // Main Dimension Callout Floating Directly Above Model
            Vector3 aboveModelPos = new Vector3(0f, yTop, 0f);
            CreateFloatingBadge($"<b>{lengthMm:F0} mm × {widthMm:F0} mm × {depthMm:F0} mm</b>\nL: {lengthMm:F0} mm  W: {widthMm:F0} mm  H: {depthMm:F0} mm", aboveModelPos);
        }

        private TextMesh CreateFloatingBadge(string text, Vector3 localPos)
        {
            GameObject badgeObj = new GameObject("DimensionBadge");
            badgeObj.transform.SetParent(m_OverlayRoot.transform, false);
            badgeObj.transform.localPosition = localPos;

            badgeObj.AddComponent<BillboardLookAtCamera>();

            TextMesh tm = badgeObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 32;
            tm.characterSize = 0.0035f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;

            return tm;
        }
    }

    /// <summary>
    /// Smoothly billboards floating 3D text toward the camera.
    /// </summary>
    public class BillboardLookAtCamera : MonoBehaviour
    {
        private Camera m_Camera;

        private void Start()
        {
            m_Camera = Camera.main ?? FindFirstObjectByType<Camera>();
        }

        private void LateUpdate()
        {
            if (m_Camera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - m_Camera.transform.position);
            }
        }
    }
}
