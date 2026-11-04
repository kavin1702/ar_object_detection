using UnityEngine;

namespace CADInspection.Detection
{
    /// <summary>
    /// Renders a real-time 3D scanning reticle / bounding box on the detected surface
    /// matching the dimensions of the target notebook.
    /// Provides visual feedback as the user aligns the phone with the physical notebook.
    /// </summary>
    public class ARScanningReticle : MonoBehaviour
    {
        [Header("Target Dimensions (Millimeters)")]
        [SerializeField] private float m_LengthMm = 230f;    // Length along Z
        [SerializeField] private float m_WidthMm = 154f;     // Width along X
        [SerializeField] private float m_ThicknessMm = 50f;  // Thickness along Y

        [Header("Visual Styling")]
        [SerializeField] private Color m_SearchingColor = new Color(1f, 0.75f, 0.2f, 0.6f);
        [SerializeField] private Color m_SurfaceFoundColor = new Color(0.2f, 0.8f, 1f, 0.8f);
        [SerializeField] private Color m_ReadyToScanColor = new Color(0.1f, 1f, 0.3f, 0.95f);
        [SerializeField] private float m_LineWidth = 0.003f; // 3mm line width

        private LineRenderer[] m_EdgeRenderers;
        private Transform m_ReticleRoot;
        private Material m_LineMaterial;

        public Vector3 DimensionsMeters => new Vector3(m_WidthMm / 1000f, m_ThicknessMm / 1000f, m_LengthMm / 1000f);

        public void SetDimensions(float lengthMm, float widthMm, float thicknessMm)
        {
            m_LengthMm = lengthMm;
            m_WidthMm = widthMm;
            m_ThicknessMm = thicknessMm;
            RebuildReticleGeometry();
        }

        private void Awake()
        {
            SetupMaterial();
            RebuildReticleGeometry();
        }

        private void SetupMaterial()
        {
            Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (lineShader == null) lineShader = Shader.Find("Hidden/Internal-Colored");
            if (lineShader == null) lineShader = Shader.Find("Sprites/Default");

            m_LineMaterial = new Material(lineShader);
            m_LineMaterial.SetColor("_BaseColor", m_SurfaceFoundColor);
            m_LineMaterial.color = m_SurfaceFoundColor;
        }

        public void SetScanState(ScanQualityState state)
        {
            Color targetColor = state switch
            {
                ScanQualityState.SearchingSurface => m_SearchingColor,
                ScanQualityState.SurfaceFound => m_SurfaceFoundColor,
                ScanQualityState.ReadyToCapture => m_ReadyToScanColor,
                _ => m_SurfaceFoundColor
            };

            if (m_LineMaterial != null)
            {
                m_LineMaterial.SetColor("_BaseColor", targetColor);
                m_LineMaterial.color = targetColor;
            }

            if (m_EdgeRenderers != null)
            {
                foreach (var lr in m_EdgeRenderers)
                {
                    if (lr != null)
                    {
                        lr.startColor = targetColor;
                        lr.endColor = targetColor;
                    }
                }
            }
        }

        public void UpdatePose(Vector3 position, Quaternion rotation, bool visible)
        {
            gameObject.SetActive(visible);
            if (visible)
            {
                transform.position = Vector3.Lerp(transform.position, position, Time.deltaTime * 15f);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * 15f);
            }
        }

        private void RebuildReticleGeometry()
        {
            // Clean up existing renderers
            if (m_EdgeRenderers != null)
            {
                foreach (var lr in m_EdgeRenderers)
                {
                    if (lr != null) Destroy(lr.gameObject);
                }
            }

            Vector3 size = DimensionsMeters;
            float hx = size.x * 0.5f;
            float hy = size.y;       // Base rests on surface (y=0), top is at y=hy
            float hz = size.z * 0.5f;

            // 8 bounding box corners
            Vector3[] c = new Vector3[8]
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

            // 12 edges
            int[,] edges = new int[12, 2]
            {
                // Bottom ring
                {0,1}, {1,2}, {2,3}, {3,0},
                // Top ring
                {4,5}, {5,6}, {6,7}, {7,4},
                // Vertical pillars
                {0,4}, {1,5}, {2,6}, {3,7}
            };

            m_EdgeRenderers = new LineRenderer[12];
            for (int i = 0; i < 12; i++)
            {
                GameObject edgeObj = new GameObject($"Edge_{i}");
                edgeObj.transform.SetParent(transform, false);

                LineRenderer lr = edgeObj.AddComponent<LineRenderer>();
                lr.material = m_LineMaterial;
                lr.startWidth = m_LineWidth;
                lr.endWidth = m_LineWidth;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, c[edges[i, 0]]);
                lr.SetPosition(1, c[edges[i, 1]]);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                m_EdgeRenderers[i] = lr;
            }
        }
    }

    public enum ScanQualityState
    {
        SearchingSurface,
        SurfaceFound,
        ReadyToCapture,
        Locked
    }
}
