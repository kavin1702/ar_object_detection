using System;
using UnityEngine;

namespace CADInspection.Measurement
{
    [Serializable]
    public struct DimensionRecord
    {
        public string Name;
        public float NominalMm;
        public float ActualMm;
        public float DeviationMm;
        public bool IsPass;
    }

    /// <summary>
    /// Handles metrology dimension calculation, nominal vs actual comparison,
    /// tolerance checking, and pass/fail evaluation (FR-10, FR-11).
    /// </summary>
    public class InspectionEngine : MonoBehaviour
    {
        [Header("Nominal CAD Specs (mm)")]
        [SerializeField] private float m_CadLengthMm = 230.0f;
        [SerializeField] private float m_CadWidthMm = 154.0f;
        [SerializeField] private float m_CadThicknessMm = 50.0f;

        [Header("Tolerances (mm)")]
        [SerializeField] private float m_ToleranceMm = 0.5f;

        public float CadLengthMm => m_CadLengthMm;
        public float CadWidthMm => m_CadWidthMm;
        public float CadThicknessMm => m_CadThicknessMm;
        public float ToleranceMm => m_ToleranceMm;

        public event Action<DimensionRecord[]> OnInspectionCompleted;

        /// <summary>
        /// Evaluates measured dimensions against nominal CAD specs.
        /// </summary>
        public DimensionRecord[] EvaluateInspection(float measuredLength, float measuredWidth, float measuredThickness)
        {
            var records = new DimensionRecord[3];

            // 1. Length
            records[0] = CreateRecord("Length (L)", m_CadLengthMm, measuredLength);

            // 2. Width
            records[1] = CreateRecord("Width (W)", m_CadWidthMm, measuredWidth);

            // 3. Depth
            records[2] = CreateRecord("Depth (T)", m_CadThicknessMm, measuredThickness);

            OnInspectionCompleted?.Invoke(records);
            return records;
        }

        private DimensionRecord CreateRecord(string name, float nominal, float actual)
        {
            float deviation = actual - nominal;
            bool pass = Mathf.Abs(deviation) <= m_ToleranceMm;

            return new DimensionRecord
            {
                Name = name,
                NominalMm = nominal,
                ActualMm = actual,
                DeviationMm = deviation,
                IsPass = pass
            };
        }
    }
}
