using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace CADInspection.Detection
{
    /// <summary>
    /// Persistent profile storing the CAD nominal specifications and reference image
    /// for any physical object being inspected in AR.
    /// </summary>
    [CreateAssetMenu(fileName = "CurrentTargetProfile", menuName = "CAD AR/Target Profile")]
    public class ARTargetProfile : ScriptableObject
    {
        [Header("Model Identification")]
        public string modelName = "Engineering Notebook";
        public string modelRevision = "Rev 2.1";

        [Header("Reference Target Image")]
        public Texture2D targetImage;
        public XRReferenceImageLibrary imageLibrary;

        [Header("Target CAD Dimensions (mm)")]
        [Tooltip("Length along Z axis in millimeters")]
        public float lengthMm = 230f;

        [Tooltip("Width along X axis in millimeters")]
        public float widthMm = 154f;

        [Tooltip("Thickness/Depth along Y axis in millimeters")]
        public float thicknessMm = 50f;

        [Tooltip("Acceptable tolerance in millimeters (+/-)")]
        public float toleranceMm = 0.5f;

        [Header("Tracking Mode")]
        public bool useImageTracking = true;
    }
}
