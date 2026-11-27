using UnityEngine;
using CADInspection.Detection;

namespace CADInspection.Visualization
{
    /// <summary>
    /// Projects the 3D outline of the physical note and draws engineering-style edge dimension callouts:
    /// Width (W) along the far top edge, Length (L) along the left top edge and Depth (T) on the
    /// front-right vertical edge, each with an arrowed dimension line and a camera-facing label.
    /// Local frame: X = width, Y = up (thickness), Z = length; the user stands on the -Z side when aligning.
    /// </summary>
    public class NoteOutlineRenderer : MonoBehaviour
    {
        public const string OutlineRootName = "Note_Outline_Root";

        private const float LabelLineHeightMeters = 0.0095f;   // ~9.5 mm text line height at 40 cm (labels scale with distance)
        private const float LabelLiftMeters = 0.020f;          // label sits just above its dimension line
        private const float DepthLabelSideMeters = 0.046f;     // depth label sits beside the vertical edge

        [Header("Active Profile")]
        [SerializeField] private NoteMarkerProfile m_Profile;

        private GameObject m_OutlineRoot;
        private Material m_OutlineMaterial;
        private Material m_DimensionLineMaterial;
        private Material m_BackplateMaterial;

        public bool IsShowing => m_OutlineRoot != null;

        /// <summary>Root of the locked outline (parented to the anchor), or null when hidden.</summary>
        public Transform OutlineRoot => m_OutlineRoot != null ? m_OutlineRoot.transform : null;
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

        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        /// <summary>Removes outline roots left behind in the open scene (editor only).</summary>
        public static void PurgeExistingOutlines()
        {
#if UNITY_EDITOR
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (obj != null && obj.name == OutlineRootName && obj.scene.isLoaded)
                {
                    SafeDestroy(obj);
                }
            }
#endif
        }

        public void InitializeMaterials()
        {
            Color outline = m_Profile != null ? m_Profile.outlineColor : new Color(0.12f, 0.95f, 0.42f, 1f);
            Color dimension = m_Profile != null ? m_Profile.dimensionTextColor : Color.white;

            m_OutlineMaterial = ARVisualMaterials.CreateUnlit(outline);
            m_DimensionLineMaterial = ARVisualMaterials.CreateUnlit(new Color(dimension.r, dimension.g, dimension.b, 0.95f), transparent: true);
            m_BackplateMaterial = ARVisualMaterials.CreateUnlit(new Color(0.08f, 0.09f, 0.10f, 0.82f), transparent: true);
        }

        public void SetProfile(NoteMarkerProfile profile)
        {
            m_Profile = profile;
            InitializeMaterials();
        }

        /// <summary>
        /// Projects the note outline and dimension callouts, parented to the tracked anchor.
        /// If anchorAtTopCover is true (image marker tracking), Y=0 is the top cover and Y=-thickness is the desk.
        /// If anchorAtTopCover is false (table plane placement), Y=0 is the desk and Y=+thickness is the top cover.
        /// </summary>
        public void ShowOutline(Transform anchorTransform, Vector3 position, Quaternion rotation, NoteMarkerProfile profile = null, bool anchorAtTopCover = false)
        {
            if (profile != null && profile != m_Profile)
            {
                m_Profile = profile;
                InitializeMaterials();
            }

            if (m_Profile == null)
            {
                var tracker = FindFirstObjectByType<NoteMarkerTracker>();
                if (tracker != null && tracker.Profile != null)
                {
                    m_Profile = tracker.Profile;
                    InitializeMaterials();
                }
            }

            if (m_Profile == null)
            {
                Debug.LogError("[NoteOutlineRenderer] No NoteMarkerProfile assigned. Configure the note in 'Tools > Note AR Setup Tool'.");
                return;
            }

            if (m_OutlineMaterial == null) InitializeMaterials();

            HideOutline();

            m_OutlineRoot = new GameObject(OutlineRootName);
            if (anchorTransform != null)
            {
                m_OutlineRoot.transform.SetParent(anchorTransform, false);
                m_OutlineRoot.transform.localPosition = Vector3.zero;
                m_OutlineRoot.transform.localRotation = Quaternion.identity;
            }
            else
            {
                m_OutlineRoot.transform.SetPositionAndRotation(position, rotation);
            }

            float w = m_Profile.widthMm / 1000f;
            float l = m_Profile.lengthMm / 1000f;
            float t = m_Profile.thicknessMm / 1000f;
            float yBottom = anchorAtTopCover ? -t : 0f;
            float yTop = anchorAtTopCover ? 0f : t;
            float lineWidth = Mathf.Max(0.0005f, m_Profile.lineWidthMm / 1000f);

            BuildOutlineBox(m_OutlineRoot.transform, w * 0.5f, l * 0.5f, yBottom, yTop, lineWidth);
            BuildEdgeCallouts(m_OutlineRoot.transform, w * 0.5f, l * 0.5f, yBottom, yTop, lineWidth);

            if (m_Profile.optionalModelPrefab != null)
            {
                GameObject modelInstance = Instantiate(m_Profile.optionalModelPrefab, m_OutlineRoot.transform);
                modelInstance.transform.localPosition = new Vector3(0f, (yBottom + yTop) * 0.5f, 0f);
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

        /// <summary>
        /// Builds the same outline and Width/Length/Depth callouts under <paramref name="parent"/> (desk at Y=0)
        /// for the live alignment preview, so the user sees the dimensions while scanning, before LOCK.
        /// Does not touch the locked outline. Returns the preview root (child "DimensionCallouts" holds the labels).
        /// </summary>
        public GameObject BuildPreview(Transform parent, NoteMarkerProfile profile)
        {
            if (profile != null && profile != m_Profile)
            {
                m_Profile = profile;
                InitializeMaterials();
            }
            if (m_Profile == null) return null;
            if (m_OutlineMaterial == null) InitializeMaterials();

            var root = new GameObject("Note_Preview_Root");
            root.transform.SetParent(parent, false);

            float w = m_Profile.widthMm / 1000f;
            float l = m_Profile.lengthMm / 1000f;
            float t = m_Profile.thicknessMm / 1000f;
            float lineWidth = Mathf.Max(0.0005f, m_Profile.lineWidthMm / 1000f);

            BuildOutlineBox(root.transform, w * 0.5f, l * 0.5f, 0f, t, lineWidth);
            BuildEdgeCallouts(root.transform, w * 0.5f, l * 0.5f, 0f, t, lineWidth);
            return root;
        }

        private void BuildOutlineBox(Transform parent, float hx, float hz, float yBottom, float yTop, float lineWidth)
        {
            Vector3[] corners =
            {
                new Vector3(-hx, yBottom, -hz), // 0: Bottom Front Left
                new Vector3( hx, yBottom, -hz), // 1: Bottom Front Right
                new Vector3( hx, yBottom,  hz), // 2: Bottom Back Right
                new Vector3(-hx, yBottom,  hz), // 3: Bottom Back Left
                new Vector3(-hx, yTop,    -hz), // 4: Top Front Left
                new Vector3( hx, yTop,    -hz), // 5: Top Front Right
                new Vector3( hx, yTop,     hz), // 6: Top Back Right
                new Vector3(-hx, yTop,     hz)  // 7: Top Back Left
            };

            int[,] edges =
            {
                {0,1}, {1,2}, {2,3}, {3,0}, // bottom rectangle (on the desk)
                {4,5}, {5,6}, {6,7}, {7,4}, // top rectangle (cover)
                {0,4}, {1,5}, {2,6}, {3,7}  // vertical thickness edges
            };

            var boxRoot = new GameObject("Outline");
            boxRoot.transform.SetParent(parent, false);

            for (int i = 0; i < 12; i++)
            {
                AddLine(boxRoot.transform, $"Outline_Edge_{i}", corners[edges[i, 0]], corners[edges[i, 1]], m_OutlineMaterial, lineWidth);
            }

            if (m_Profile.showCornerNodes)
            {
                float nodeSize = lineWidth * 2.4f;
                for (int i = 0; i < 8; i++)
                {
                    GameObject node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    node.name = $"Corner_Node_{i}";
                    node.transform.SetParent(boxRoot.transform, false);
                    node.transform.localPosition = corners[i];
                    node.transform.localScale = Vector3.one * nodeSize;

                    var col = node.GetComponent<Collider>();
                    if (col != null) SafeDestroy(col);

                    var mr = node.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = m_OutlineMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
        }

        private void BuildEdgeCallouts(Transform parent, float hx, float hz, float yBottom, float yTop, float outlineWidth)
        {
            float offset = Mathf.Max(0.005f, m_Profile.calloutOffsetMm / 1000f);
            float dimWidth = Mathf.Max(0.0006f, outlineWidth * 0.4f);
            Color dimColor = m_Profile.dimensionTextColor;
            Material plate = m_Profile.showBackplate ? m_BackplateMaterial : null;
            string format = "F" + Mathf.Clamp(m_Profile.decimals, 0, 2);

            var calloutRoot = new GameObject("DimensionCallouts");
            calloutRoot.transform.SetParent(parent, false);
            Transform root = calloutRoot.transform;

            // Width (W): along the far top edge (z = +hz), dimension line pushed further away (+Z).
            {
                Vector3 a = new Vector3(-hx, yTop, hz + offset);
                Vector3 b = new Vector3( hx, yTop, hz + offset);
                if (m_Profile.showDimensionLines)
                {
                    DrawDimension(root, "Width", a, b, Vector3.forward, dimWidth);
                    AddLine(root, "Width_Ext_L", new Vector3(-hx, yTop, hz), a + Vector3.forward * offset * 0.25f, m_DimensionLineMaterial, dimWidth * 0.6f);
                    AddLine(root, "Width_Ext_R", new Vector3( hx, yTop, hz), b + Vector3.forward * offset * 0.25f, m_DimensionLineMaterial, dimWidth * 0.6f);
                }
                DimensionLabel.Create(root, (a + b) * 0.5f, Vector3.up * LabelLiftMeters,
                    "Width (W)", $"{m_Profile.widthMm.ToString(format)} mm", dimColor, plate, LabelLineHeightMeters);
            }

            // Length (L): along the left top edge (x = -hx), dimension line pushed outward (-X).
            {
                Vector3 a = new Vector3(-hx - offset, yTop, -hz);
                Vector3 b = new Vector3(-hx - offset, yTop,  hz);
                if (m_Profile.showDimensionLines)
                {
                    DrawDimension(root, "Length", a, b, Vector3.left, dimWidth);
                    AddLine(root, "Length_Ext_F", new Vector3(-hx, yTop, -hz), a + Vector3.left * offset * 0.25f, m_DimensionLineMaterial, dimWidth * 0.6f);
                    AddLine(root, "Length_Ext_B", new Vector3(-hx, yTop,  hz), b + Vector3.left * offset * 0.25f, m_DimensionLineMaterial, dimWidth * 0.6f);
                }
                DimensionLabel.Create(root, (a + b) * 0.5f, Vector3.up * LabelLiftMeters,
                    "Length (L)", $"{m_Profile.lengthMm.ToString(format)} mm", dimColor, plate, LabelLineHeightMeters);
            }

            // Depth (T): on the front-right vertical edge (x = +hx, z = -hz), dimension line pushed outward (+X).
            {
                Vector3 a = new Vector3(hx + offset, yBottom, -hz);
                Vector3 b = new Vector3(hx + offset, yTop,    -hz);
                if (m_Profile.showDimensionLines)
                {
                    DrawDimension(root, "Depth", a, b, Vector3.right, dimWidth);
                    AddLine(root, "Depth_Ext_Bottom", new Vector3(hx, yBottom, -hz), a + Vector3.right * offset * 0.25f, m_DimensionLineMaterial, dimWidth * 0.6f);
                    AddLine(root, "Depth_Ext_Top",    new Vector3(hx, yTop,    -hz), b + Vector3.right * offset * 0.25f, m_DimensionLineMaterial, dimWidth * 0.6f);
                }
                Vector3 mid = (a + b) * 0.5f;
                DimensionLabel.Create(root, mid, Vector3.right * DepthLabelSideMeters,
                    "Depth (T)", $"{m_Profile.thicknessMm.ToString(format)} mm", dimColor, plate, LabelLineHeightMeters);
            }
        }

        /// <summary>Dimension line from a to b with an arrowhead at each end, drawn in the plane of (b - a) and <paramref name="side"/>.</summary>
        private void DrawDimension(Transform parent, string name, Vector3 a, Vector3 b, Vector3 side, float width)
        {
            AddLine(parent, name + "_Line", a, b, m_DimensionLineMaterial, width);

            Vector3 dir = (b - a);
            float length = dir.magnitude;
            if (length < 1e-4f) return;
            dir /= length;

            float head = Mathf.Min(0.008f, length * 0.3f);
            Vector3 spread = side.normalized * head * 0.45f;

            AddLine(parent, name + "_ArrowA1", a, a + dir * head + spread, m_DimensionLineMaterial, width);
            AddLine(parent, name + "_ArrowA2", a, a + dir * head - spread, m_DimensionLineMaterial, width);
            AddLine(parent, name + "_ArrowB1", b, b - dir * head + spread, m_DimensionLineMaterial, width);
            AddLine(parent, name + "_ArrowB2", b, b - dir * head - spread, m_DimensionLineMaterial, width);
        }

        private static LineRenderer AddLine(Transform parent, string name, Vector3 from, Vector3 to, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.startColor = Color.white; // colour comes from the material (vertex colour multiplies it on Sprites/Default)
            lr.endColor = Color.white;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.positionCount = 2;
            lr.useWorldSpace = false;
            lr.numCapVertices = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }
    }
}
