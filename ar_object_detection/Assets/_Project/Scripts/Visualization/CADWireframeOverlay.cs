using UnityEngine;

namespace CADInspection.Visualization
{
    /// <summary>
    /// Renders the precision CAD wireframe overlay, optional imported CAD mesh,
    /// and 3D dimension callouts directly above the registered physical notebook (FR-09, FR-11).
    /// </summary>
    public class CADWireframeOverlay : MonoBehaviour
    {
        [Header("Nominal Dimensions (mm)")]
        [SerializeField] private float m_LengthMm = 230f;
        [SerializeField] private float m_WidthMm = 154f;
        [SerializeField] private float m_ThicknessMm = 50f;

        [Header("Optional Imported CAD Model")]
        [Tooltip("Optional 3D model prefab (FBX/OBJ) exported from CAD software. If unassigned, precision CAD wireframe is rendered.")]
        [SerializeField] private GameObject m_CadModelPrefab;

        [Header("Styling")]
        [SerializeField] private Color m_WireframeColor = new Color(0.15f, 1f, 0.35f, 1f); // Glowing AR Green
        [SerializeField] private float m_WireframeLineWidth = 0.003f; // 3mm line

        private GameObject m_OverlayRoot;
        private Material m_WireframeMaterial;

        public GameObject CadModelPrefab
        {
            get => m_CadModelPrefab;
            set => m_CadModelPrefab = value;
        }

        private void Awake()
        {
            SetupMaterial();
        }

        private void SetupMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Hidden/Internal-Colored");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");

            m_WireframeMaterial = new Material(unlit);
            m_WireframeMaterial.color = m_WireframeColor;
            m_WireframeMaterial.SetColor("_BaseColor", m_WireframeColor);
        }

        public void DisplayOverlay(Pose pose, float measuredLengthMm, float measuredWidthMm, float measuredDepthMm)
        {
            if (m_OverlayRoot != null)
            {
                Destroy(m_OverlayRoot);
            }

            m_OverlayRoot = new GameObject("CAD_Wireframe_Root");
            m_OverlayRoot.transform.position = pose.position;
            m_OverlayRoot.transform.rotation = pose.rotation;

            // 1. If user assigned an imported CAD Model (FBX/OBJ), instantiate it
            if (m_CadModelPrefab != null)
            {
                GameObject modelInstance = Instantiate(m_CadModelPrefab, m_OverlayRoot.transform);
                modelInstance.transform.localPosition = new Vector3(0f, (m_ThicknessMm / 1000f) * 0.5f, 0f);
                modelInstance.transform.localRotation = Quaternion.identity;
            }

            // 2. Build precision CAD wireframe cage
            BuildWireframeMesh();

            // 3. Build 3D dimension callouts along edges
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
                {0,1}, {1,2}, {2,3}, {3,0},
                {4,5}, {5,6}, {6,7}, {7,4},
                {0,4}, {1,5}, {2,6}, {3,7}
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

            // 1. Length Callout (Along Left Edge: Z axis)
            Vector3 lengthPos = new Vector3(-hx - 0.035f, hy * 0.5f, 0f);
            CreateFloatingBadge($"Length (L)\n{lengthMm:F1} mm", lengthPos);

            // 2. Width Callout (Along Front Edge: X axis)
            Vector3 widthPos = new Vector3(0f, hy + 0.035f, hz + 0.015f);
            CreateFloatingBadge($"Width (W)\n{widthMm:F1} mm", widthPos);

            // 3. Depth Callout (Along Corner Pillar: Y axis)
            Vector3 depthPos = new Vector3(hx + 0.035f, hy * 0.5f, hz);
            CreateFloatingBadge($"Depth (T)\n{depthMm:F1} mm", depthPos);
        }

        private TextMesh CreateFloatingBadge(string text, Vector3 localPos)
        {
            GameObject badgeObj = new GameObject("DimensionBadge");
            badgeObj.transform.SetParent(m_OverlayRoot.transform, false);
            badgeObj.transform.localPosition = localPos;

            // Billboard component
            badgeObj.AddComponent<BillboardLookAtCamera>();

            // 3D Text
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
    /// Keeps 3D floating dimension labels facing the AR camera.
    /// </summary>
    public class BillboardLookAtCamera : MonoBehaviour
    {
        private Camera m_Camera;

        private void Start()
        {
            m_Camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
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
