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
            SetupMaterial();
        }

        private void SetupMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") 
                        ?? Shader.Find("Hidden/Internal-Colored") 
                        ?? Shader.Find("Sprites/Default");

            m_WireframeMaterial = new Material(unlit);
            m_WireframeMaterial.color = m_WireframeColor;
            if (m_WireframeMaterial.HasProperty("_BaseColor"))
                m_WireframeMaterial.SetColor("_BaseColor", m_WireframeColor);
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
            BuildWireframeMesh();

            // Build 3D floating dimension badges on edges
            BuildDimensionCallouts(measuredLengthMm, measuredWidthMm, measuredDepthMm);
        }

        public void HideOverlay()
        {
            if (m_OverlayRoot != null)
            {
                Destroy(m_OverlayRoot);
                m_OverlayRoot = null;
            }
        }

        private void BuildWireframeMesh()
        {
            float hx = (m_WidthMm / 1000f) * 0.5f;
            float hy = (m_ThicknessMm / 1000f);
            float hz = (m_LengthMm / 1000f) * 0.5f;

            Vector3[] corners = new Vector3[8]
            {
                new Vector3(-hx, 0f, -hz), // 0: Bottom Back Left
                new Vector3( hx, 0f, -hz), // 1: Bottom Back Right
                new Vector3( hx, 0f,  hz), // 2: Bottom Front Right
                new Vector3(-hx, 0f,  hz), // 3: Bottom Front Left
                new Vector3(-hx, hy, -hz), // 4: Top Back Left
                new Vector3( hx, hy, -hz), // 5: Top Back Right
                new Vector3( hx, hy,  hz), // 6: Top Front Right
                new Vector3(-hx, hy,  hz)  // 7: Top Front Left
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

        private void BuildDimensionCallouts(float lengthMm, float widthMm, float depthMm)
        {
            float hx = (m_WidthMm / 1000f) * 0.5f;
            float hy = (m_ThicknessMm / 1000f);
            float hz = (m_LengthMm / 1000f) * 0.5f;

            // 1. Length Callout (Along Side: Z axis)
            Vector3 lengthPos = new Vector3(-hx - 0.045f, hy * 0.5f, 0f);
            CreateFloatingBadge($"Length (L)\n{lengthMm:F1} mm", lengthPos);

            // 2. Width Callout (Along Top Front: X axis)
            Vector3 widthPos = new Vector3(0f, hy + 0.04f, hz + 0.02f);
            CreateFloatingBadge($"Width (W)\n{widthMm:F1} mm", widthPos);

            // 3. Depth Callout (Along Vertical Corner: Y axis)
            Vector3 depthPos = new Vector3(hx + 0.045f, hy * 0.5f, hz);
            CreateFloatingBadge($"Depth (T)\n{depthMm:F1} mm", depthPos);
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
