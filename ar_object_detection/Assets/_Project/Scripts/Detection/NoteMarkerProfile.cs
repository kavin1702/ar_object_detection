using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.Detection
{
    public enum DimensionDisplayFormat
    {
        Standard,           // "230 mm × 154 mm × 50 mm"
        Detailed,           // "L: 230 mm | W: 154 mm | H: 50 mm"
        CompactWithLabels,  // "230 mm × 154 mm × 50 mm\nL: 230 mm  W: 154 mm  H: 50 mm"
        Compact             // "230 × 154 × 50 mm"
    }

    /// <summary>
    /// Persistent profile storing note marker image, real-world physical dimensions,
    /// and visual outline/dimension styling.
    /// </summary>
    [CreateAssetMenu(fileName = "CurrentNoteMarkerProfile", menuName = "CAD AR/Note Marker Profile")]
    public class NoteMarkerProfile : ScriptableObject
    {
        [Header("Note Identification")]
        public string modelName = "Engineering Notebook";

        [Header("Marker Image (Physical Cover)")]
        public Texture2D markerImage;
        public XRReferenceImageLibrary imageLibrary;

        [Header("Physical Dimensions (mm)")]
        [Tooltip("Length along Z axis in millimeters")]
        public float lengthMm = 230f;

        [Tooltip("Width along X axis in millimeters")]
        public float widthMm = 154f;

        [Tooltip("Thickness / Height along Y axis in millimeters")]
        public float thicknessMm = 50f;

        [Header("Outline Styling")]
        public Color outlineColor = new Color(0.12f, 0.95f, 0.42f, 1f); // Vibrant AR Green
        public float lineWidthMm = 3.5f;
        public bool showCornerNodes = true;

        [Header("Dimension Above Model")]
        [Tooltip("Height offset above the top of the model in millimeters")]
        public float dimensionHeightOffsetMm = 40f;
        public DimensionDisplayFormat dimensionFormat = DimensionDisplayFormat.CompactWithLabels;
        public Color dimensionTextColor = Color.white;
        public bool showLeaderLine = true;
        public bool showBackplate = true;

        [Header("Optional 3D Mesh / Model Prefab")]
        [Tooltip("Optional 3D model/mesh prefab to display inside the outline")]
        public GameObject optionalModelPrefab;
    }
}
