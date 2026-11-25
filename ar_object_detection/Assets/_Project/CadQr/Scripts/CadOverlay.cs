using System.Collections.Generic;
using UnityEngine;

namespace CADInspection.CadQr
{
    /// <summary>
    /// The virtual CAD model that is drawn over the real object: wireframe, optional see-through
    /// "ghost" surfaces, a bounding box and three dimension labels (W, H, D in mm).
    ///
    /// The controller creates this at runtime with <see cref="Create"/>. Hierarchy:
    ///   CadOverlay (this)        position/rotation = where the model's origin sits in the room
    ///     ModelSpace             localScale = unitsToMeters (turns mm into metres)
    ///       Wireframe            feature-edge tubes
    ///       BoundingBox          12 thin tubes around the model
    ///       Ghost                a copy of the CAD meshes with a transparent material
    ///     Labels                 3 TextMesh labels that always face the camera
    /// </summary>
    public class CadOverlay : MonoBehaviour
    {
        public CadModelEntry Entry { get; private set; }
        public CadModelInfo Info { get; private set; }

        /// <summary>Where the marker card sits, relative to this overlay's origin, in metres.</summary>
        public Pose MarkerInModel { get; private set; }

        GameObject m_Ghost;
        readonly List<Transform> m_Labels = new List<Transform>();
        Camera m_Camera;

        public static CadOverlay Create(CadModelEntry entry, Material wireMaterial, Material boxMaterial, Material ghostMaterial,
                                        float wireThicknessMeters, Camera camera)
        {
            var root = new GameObject("CadOverlay_" + entry.id);
            var overlay = root.AddComponent<CadOverlay>();
            overlay.Build(entry, wireMaterial, boxMaterial, ghostMaterial, wireThicknessMeters, camera);
            return overlay;
        }

        void Build(CadModelEntry entry, Material wireMaterial, Material boxMaterial, Material ghostMaterial,
                   float wireThicknessMeters, Camera camera)
        {
            Entry = entry;
            m_Camera = camera;
            float scale = Mathf.Max(1e-6f, entry.unitsToMeters);

            var modelSpace = new GameObject("ModelSpace").transform;
            modelSpace.SetParent(transform, false);
            modelSpace.localScale = Vector3.one * scale;

            // 1. A copy of the CAD meshes. Used for analysis and as the see-through ghost.
            m_Ghost = Instantiate(entry.modelPrefab, modelSpace, false);
            m_Ghost.name = "Ghost";
            m_Ghost.transform.localPosition = Vector3.zero;
            m_Ghost.transform.localRotation = Quaternion.identity;
            m_Ghost.transform.localScale = Vector3.one;
            foreach (Collider c in m_Ghost.GetComponentsInChildren<Collider>()) Destroy(c);

            // 2. Read size + sharp edges from the meshes (relative to ModelSpace, so in model units).
            Info = CadModelAnalyzer.Analyze(modelSpace.gameObject, scale);

            foreach (Renderer r in m_Ghost.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // 3. Wireframe and bounding box. Thickness is given in metres, the mesh is in model units.
            float thickness = wireThicknessMeters / scale;
            AddMeshChild(modelSpace, "Wireframe", CadModelAnalyzer.BuildTubeMesh(Info.featureEdges, thickness), wireMaterial);
            AddMeshChild(modelSpace, "BoundingBox", CadModelAnalyzer.BuildTubeMesh(BoxEdges(Info.localBounds), thickness * 0.5f), boxMaterial);

            // 4. Dimension labels (positions in metres, relative to this root).
            Bounds b = Info.localBounds;
            Vector3 min = b.min * scale, max = b.max * scale, c3 = b.center * scale;
            float gap = 0.012f;
            AddLabel($"W {Info.sizeMm.x:0.#} mm", new Vector3(c3.x, min.y - gap, min.z - gap));
            AddLabel($"H {Info.sizeMm.y:0.#} mm", new Vector3(min.x - gap, c3.y, min.z - gap));
            AddLabel($"D {Info.sizeMm.z:0.#} mm", new Vector3(max.x + gap, min.y - gap, c3.z));

            MarkerInModel = ComputeMarkerPose(entry, b, scale);
        }

        public void SetGhostVisible(bool visible)
        {
            if (m_Ghost != null) m_Ghost.SetActive(visible);
        }

        public bool GhostVisible => m_Ghost != null && m_Ghost.activeSelf;

        void LateUpdate()
        {
            // Billboard: turn every label to face the camera so it is always readable.
            if (m_Camera == null) m_Camera = Camera.main;
            if (m_Camera == null) return;
            foreach (Transform label in m_Labels)
                label.rotation = Quaternion.LookRotation(label.position - m_Camera.transform.position, m_Camera.transform.up);
        }

        static Pose ComputeMarkerPose(CadModelEntry entry, Bounds b, float scale)
        {
            // Card frame: +Y = card normal (out of the paper), +Z = the card's "up" arrow.
            switch (entry.markerPlacement)
            {
                case MarkerPlacement.FrontCentre:
                    return new Pose(new Vector3(b.center.x, b.center.y, b.min.z) * scale, Quaternion.Euler(-90f, 0f, 0f));
                case MarkerPlacement.Custom:
                    return new Pose(entry.customMarkerPositionMm * 0.001f, Quaternion.Euler(entry.customMarkerEuler));
                default:
                    return new Pose(new Vector3(b.center.x, b.max.y, b.center.z) * scale, Quaternion.identity);
            }
        }

        static List<Vector3> BoxEdges(Bounds b)
        {
            Vector3 n = b.min, x = b.max;
            Vector3[] c =
            {
                new Vector3(n.x, n.y, n.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, n.y, x.z), new Vector3(n.x, n.y, x.z),
                new Vector3(n.x, x.y, n.z), new Vector3(x.x, x.y, n.z), new Vector3(x.x, x.y, x.z), new Vector3(n.x, x.y, x.z)
            };
            int[] pairs = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
            var list = new List<Vector3>(24);
            foreach (int i in pairs) list.Add(c[i]);
            return list;
        }

        static void AddMeshChild(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void AddLabel(string text, Vector3 localPosition)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.text = text;
            mesh.fontSize = 64;
            mesh.characterSize = 0.0012f; // about 9 mm tall letters
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(1f, 0.92f, 0.2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            m_Labels.Add(go.transform);
        }
    }
}
