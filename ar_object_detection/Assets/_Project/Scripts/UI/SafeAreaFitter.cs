using UnityEngine;

namespace CADInspection.UI
{
    /// <summary>
    /// Adjusts RectTransform to stay strictly within Screen.safeArea,
    /// protecting UI from camera notches, punch holes, rounded corners, and navigation bars
    /// across all modern Android phones and tablets.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform m_RectTransform;
        private Rect m_LastSafeArea = Rect.zero;
        private Vector2Int m_LastScreenSize = Vector2Int.zero;
        private ScreenOrientation m_LastOrientation = ScreenOrientation.AutoRotation;

        private void Awake()
        {
            m_RectTransform = GetComponent<RectTransform>();
            ApplySafeArea();
        }

        private void Update()
        {
            if (m_LastSafeArea != Screen.safeArea ||
                m_LastScreenSize.x != Screen.width ||
                m_LastScreenSize.y != Screen.height ||
                m_LastOrientation != Screen.orientation)
            {
                ApplySafeArea();
            }
        }

        private void ApplySafeArea()
        {
            if (m_RectTransform == null) return;

            Rect safeArea = Screen.safeArea;
            m_LastSafeArea = safeArea;
            m_LastScreenSize = new Vector2Int(Screen.width, Screen.height);
            m_LastOrientation = Screen.orientation;

            // Convert safe area rectangle from absolute pixel coords to normalized anchor coords (0..1)
            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            m_RectTransform.anchorMin = anchorMin;
            m_RectTransform.anchorMax = anchorMax;
            m_RectTransform.offsetMin = Vector2.zero;
            m_RectTransform.offsetMax = Vector2.zero;
        }
    }
}
