using System;
using System.Collections.Generic;
using UnityEngine;

namespace CADInspection.CadQr
{
    /// <summary>Where the printed QR card is stuck on the physical object.</summary>
    public enum MarkerPlacement
    {
        /// <summary>Flat on the top face, centred, card "up" arrow pointing to the model's +Z (back).</summary>
        TopCentre,
        /// <summary>On the front face (the face looking at -Z), centred, arrow pointing up (+Y).</summary>
        FrontCentre,
        /// <summary>Use <see cref="CadModelEntry.customMarkerPositionMm"/> and <see cref="CadModelEntry.customMarkerEuler"/>.</summary>
        Custom
    }

    /// <summary>One CAD model the user can pick in the app.</summary>
    [Serializable]
    public class CadModelEntry
    {
        [Tooltip("Short unique id. It becomes the QR text and the name of the AR reference image. Letters, digits and _ only.")]
        public string id = "my_part";

        [Tooltip("Name shown in the app.")]
        public string displayName = "My Part";

        [Tooltip("The imported CAD model (FBX / OBJ / GLB converted from STEP). Read/Write must be enabled in its import settings.")]
        public GameObject modelPrefab;

        [Tooltip("How many metres one model unit is. CAD exported in millimetres = 0.001.")]
        public float unitsToMeters = 0.001f;

        [Tooltip("Printed width of the WHOLE marker card (including the black frame) in metres. Print it at exactly this size.")]
        public float markerWidthMeters = 0.10f;

        public MarkerPlacement markerPlacement = MarkerPlacement.TopCentre;

        [Tooltip("Custom only: card centre in model space, millimetres.")]
        public Vector3 customMarkerPositionMm;

        [Tooltip("Custom only: card rotation in model space. Card normal = local +Y, card 'up' arrow = local +Z.")]
        public Vector3 customMarkerEuler;
    }

    /// <summary>
    /// The list of CAD models the app knows. A ScriptableObject is a data asset that lives in the
    /// project (like a config file you can edit in the Inspector).
    /// Create one with: right-click in the Project window > Create > CAD QR > Model Library,
    /// or let Tools > CAD QR Overlay > Build Demo Scene create it with three sample models.
    /// </summary>
    [CreateAssetMenu(menuName = "CAD QR/Model Library", fileName = "CadModelLibrary")]
    public class CadModelLibrary : ScriptableObject
    {
        public List<CadModelEntry> models = new List<CadModelEntry>();

        public CadModelEntry Find(string id) => models.Find(m => m != null && m.id == id);
    }
}
