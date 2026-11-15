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
                var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
                plate.name = "Backplate";
                var collider = plate.GetComponent<Collider>();
                if (collider != null) SafeDestroy(collider);
                plate.transform.SetParent(root.transform, false);
                plate.transform.localPosition = new Vector3(0f, 0f, 0.001f); // just behind the text
                var plateRenderer = plate.GetComponent<MeshRenderer>();
                plateRenderer.sharedMaterial = plateMaterial;
                plateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                plateRenderer.receiveShadows = false;
                plateRenderer.enabled = false; // shown once fitted to the text, avoids a one-frame flash
                label.m_Plate = plate.transform;
                label.m_PlateRenderer = plateRenderer;
            }

            return label;
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
                    m_Plate.localScale = new Vector3(b.size.x + PlatePaddingMeters * 2f, b.size.y + PlatePaddingMeters * 2f, 1f);
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
