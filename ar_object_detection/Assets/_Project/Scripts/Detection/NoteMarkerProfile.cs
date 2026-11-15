using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.Detection
{
    /// <summary>
    /// Persistent profile storing the note marker image, real-world physical dimensions,
    /// and the styling of the 3D outline and edge dimension callouts.
    /// Created and edited through Tools > Note AR Setup Tool.
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
        [Tooltip("Length along Z axis in millimeters (spine direction)")]
        public float lengthMm = 210f;

        [Tooltip("Width along X axis in millimeters")]
        public float widthMm = 148f;

        [Tooltip("Thickness / depth along Y axis in millimeters")]
        public float thicknessMm = 20f;

        [Header("Auto-Detect Note by Cover Colour")]
        [Tooltip("Find the notebook in the camera view by its cover colour and snap the green box onto it")]
        public bool autoDetectByColor = true;

        [Tooltip("Cover colour, taken from the marker image by the Setup Tool")]
        public Color coverColor = new Color(0.06f, 0.66f, 0.74f, 1f);

        [Tooltip("How far the hue may differ from the cover colour, in degrees (raise for uneven lighting)")]
        [Range(4f, 60f)]
        public float colorHueTolerance = 18f;

        [Tooltip("After LOCK, keep checking the notebook in view and gently pull the outline back onto it, " +
                 "so it stays on the real notebook while you move the phone (corrects AR tracking drift)")]
        public bool refineAfterLock = true;

        [Tooltip("Show a small thumbnail of what the detector sees (magenta = cover colour) in the app")]
        public bool showDetectionDebug = false;

        [Tooltip("Only if the debug thumbnail looks upside down compared with the screen")]
        public bool flipCameraImageVertically = false;

        [Header("Outline Styling")]
        public Color outlineColor = new Color(0.12f, 0.95f, 0.42f, 1f); // Vibrant AR Green
        public float lineWidthMm = 3.5f;
        public bool showCornerNodes = true;

        [Header("Edge Dimension Callouts")]
        [Tooltip("How far the dimension arrows sit outside the notebook edges, in millimeters")]
        [FormerlySerializedAs("dimensionHeightOffsetMm")]
        public float calloutOffsetMm = 25f;

        [Tooltip("Number of decimals shown in the callouts (reference style: 1 -> 148.0 mm)")]
        [Range(0, 2)]
        public int decimals = 1;

        public Color dimensionTextColor = Color.white;

        [Tooltip("Draw the arrowed dimension lines and extension lines for each callout")]
        [FormerlySerializedAs("showLeaderLine")]
        public bool showDimensionLines = true;

        [Tooltip("Dark plate behind each callout label for contrast")]
        public bool showBackplate = true;

        [Header("Optional 3D Mesh / Model Prefab")]
        [Tooltip("Optional 3D model/mesh prefab to display inside the outline")]
        public GameObject optionalModelPrefab;
    }
}
