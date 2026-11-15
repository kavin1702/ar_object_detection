using UnityEngine;

namespace CADInspection.Visualization
{
    /// <summary>
    /// A floating 3D dimension label ("Width (W)" / "148.0 mm") that always faces the camera
    /// and sizes its dark backplate to fit the text once the text mesh has been generated.
    /// </summary>
    public class DimensionLabel : MonoBehaviour
    {
        private const float PlatePaddingMeters = 0.005f;

        // Labels keep a roughly constant on-screen size: full size at this camera distance,
        // scaled with distance (clamped) so far edges stay as readable as near ones.
        private const float ReferenceDistanceMeters = 0.40f;
        private const float MinScale = 0.6f;
        private const float MaxScale = 3.0f;

        private Camera m_Camera;
        private MeshRenderer m_TextRenderer;
        private Transform m_Plate;
        private MeshRenderer m_PlateRenderer;
        private bool m_PlateFitted;
        private Vector3 m_AnchorLocalPosition;
        private Vector3 m_LiftLocal;

        /// <summary>
        /// Builds a two-line label (title, bold value) under <paramref name="parent"/>, placed at
        /// <paramref name="anchorLocalPosition"/> + <paramref name="liftLocal"/> (the lift scales with the label).
        /// Pass a null <paramref name="plateMaterial"/> to skip the backplate.
        /// </summary>
        public static DimensionLabel Create(Transform parent, Vector3 anchorLocalPosition, Vector3 liftLocal, string title, string value,
                                            Color textColor, Material plateMaterial, float lineHeightMeters)
        {
            var root = new GameObject("DimensionLabel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = anchorLocalPosition + liftLocal;

            var label = root.AddComponent<DimensionLabel>();
            label.m_AnchorLocalPosition = anchorLocalPosition;
            label.m_LiftLocal = liftLocal;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, false);
            var tm = textObj.AddComponent<TextMesh>();
            tm.richText = true;
            tm.text = $"{title}\n<b>{value}</b>";
            tm.fontSize = 64;
            // TextMesh line height is roughly characterSize * fontSize / 10 world units.
            tm.characterSize = lineHeightMeters * 10f / tm.fontSize;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = textColor;
            TextMeshFontUtil.ApplyDefaultFont(tm);
            label.m_TextRenderer = textObj.GetComponent<MeshRenderer>();

            if (plateMaterial != null)
            {
                // Rounded-rectangle plate (mesh built once the text size is known, so corners stay round)
                var plate = new GameObject("Backplate");
                plate.AddComponent<MeshFilter>();
                var plateRenderer = plate.AddComponent<MeshRenderer>();
                plate.transform.SetParent(root.transform, false);
                plate.transform.localPosition = new Vector3(0f, 0f, 0.001f); // just behind the text
                plateRenderer.sharedMaterial = plateMaterial;
                plateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                plateRenderer.receiveShadows = false;
                plateRenderer.enabled = false; // shown once fitted to the text, avoids a one-frame flash
                label.m_Plate = plate.transform;
                label.m_PlateRenderer = plateRenderer;
            }

            return label;
        }

        private const float CornerRadiusFraction = 0.28f; // corner radius as a share of the plate's shorter side
        private const int CornerSegments = 6;

        /// <summary>
        /// Flat rounded rectangle centred on the origin in the XY plane (a convex outline filled as a fan).
        /// </summary>
        private static Mesh BuildRoundedRect(float width, float height, float radius)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(hw, hh));

            // Corner centres in counter-clockwise order starting top-right, each sweeping 90°
            var centres = new[]
            {
                new Vector2( hw - radius,  hh - radius),
                new Vector2(-hw + radius,  hh - radius),
                new Vector2(-hw + radius, -hh + radius),
                new Vector2( hw - radius, -hh + radius)
            };

            var vertices = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            for (int c = 0; c < 4; c++)
            {
                float start = c * 90f;
                for (int s = 0; s <= CornerSegments; s++)
                {
                    float a = (start + 90f * s / CornerSegments) * Mathf.Deg2Rad;
                    vertices.Add(new Vector3(centres[c].x + Mathf.Cos(a) * radius, centres[c].y + Mathf.Sin(a) * radius, 0f));
                }
            }

            int outline = vertices.Count - 1;
            var triangles = new int[outline * 3];
            for (int i = 0; i < outline; i++)
            {
                // clockwise when seen from the camera (which looks along +Z at the label)
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + (i + 1) % outline;
                triangles[i * 3 + 2] = 1 + i;
            }

            var mesh = new Mesh { name = "RoundedPlate" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            var filter = m_Plate != null ? m_Plate.GetComponent<MeshFilter>() : null;
            if (filter != null && filter.sharedMesh != null) SafeDestroy(filter.sharedMesh);
        }

        private static void SafeDestroy(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void Start()
        {
            m_Camera = Camera.main;
            if (m_Camera == null) m_Camera = FindFirstObjectByType<Camera>();
        }

        private void LateUpdate()
        {
            if (m_Camera != null)
            {
                Vector3 anchorWorld = transform.parent != null ? transform.parent.TransformPoint(m_AnchorLocalPosition) : m_AnchorLocalPosition;
                float distance = Vector3.Distance(m_Camera.transform.position, anchorWorld);
                float scale = Mathf.Clamp(distance / ReferenceDistanceMeters, MinScale, MaxScale);
                transform.localScale = Vector3.one * scale;
                transform.localPosition = m_AnchorLocalPosition + m_LiftLocal * scale;

                // TextMesh is readable when its forward points away from the viewer.
                Vector3 away = transform.position - m_Camera.transform.position;
                if (away.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(away, m_Camera.transform.up);
            }

            if (!m_PlateFitted && m_Plate != null && m_TextRenderer != null)
            {
                // TextMesh builds its mesh lazily, so bounds are only valid after it has been generated.
                Bounds b = m_TextRenderer.localBounds;
                if (b.size.x > 0f && b.size.y > 0f)
                {
                    float w = b.size.x + PlatePaddingMeters * 3f;
                    float h = b.size.y + PlatePaddingMeters * 2f;
                    var filter = m_Plate.GetComponent<MeshFilter>();
                    filter.sharedMesh = BuildRoundedRect(w, h, Mathf.Min(w, h) * CornerRadiusFraction);
                    m_Plate.localScale = Vector3.one;
                    m_Plate.localPosition = new Vector3(b.center.x, b.center.y, 0.001f);
                    m_PlateRenderer.enabled = true;
                    m_PlateFitted = true;
                }
            }
        }
    }

    /// <summary>
    /// A TextMesh added with AddComponent at runtime has no font and its MeshRenderer has no material,
    /// so it renders nothing on device. Assign Unity's built-in runtime font and its material.
    /// </summary>
    public static class TextMeshFontUtil
    {
        private static Font s_DefaultFont;

        public static void ApplyDefaultFont(TextMesh textMesh)
        {
            if (textMesh == null) return;

            if (s_DefaultFont == null)
                s_DefaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (s_DefaultFont == null)
            {
                Debug.LogWarning("[TextMeshFontUtil] Built-in LegacyRuntime.ttf font not found; 3D dimension text will not render.");
                return;
            }

            textMesh.font = s_DefaultFont;
            var meshRenderer = textMesh.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = s_DefaultFont.material;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }
    }

    /// <summary>
    /// Creates simple unlit materials that render whether or not URP's Unlit shader made it into the build.
    /// </summary>
    public static class ARVisualMaterials
    {
        public static Material CreateUnlit(Color color, bool transparent = false)
        {
            // Sprites/Default is built in and supports alpha. URP Unlit is preferred for opaque lines,
            // but it can be stripped from a build when no material asset uses it, so fall back.
            Shader shader = transparent ? Shader.Find("Sprites/Default") : Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }
    }
}
