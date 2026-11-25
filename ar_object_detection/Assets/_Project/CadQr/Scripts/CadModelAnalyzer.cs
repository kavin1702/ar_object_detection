using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CADInspection.CadQr
{
    /// <summary>What we read from a CAD model. All sizes are in MODEL units unless the name says otherwise.</summary>
    public class CadModelInfo
    {
        /// <summary>Axis-aligned box around every mesh, in the model root's local space (model units).</summary>
        public Bounds localBounds;
        /// <summary>Width (X), height (Y), depth (Z) in millimetres.</summary>
        public Vector3 sizeMm;
        /// <summary>The "sharp" edges of the model, as pairs of points in model units (a0, b0, a1, b1, ...).</summary>
        public List<Vector3> featureEdges = new List<Vector3>();
        public int triangleCount;
    }

    /// <summary>
    /// Reads dimensions and a wireframe from a CAD mesh. No camera, no measuring: everything comes
    /// from the model file itself.
    ///
    /// Wireframe idea ("feature edges"): a mesh is made of triangles. A flat face of a box is two
    /// triangles; the diagonal between them is NOT a real edge of the part. A real edge is where two
    /// faces meet at an angle (for example 90 degrees on a box), or where the surface ends.
    /// So we keep an edge only if the angle between the two triangles that share it is bigger than
    /// <c>creaseAngle</c>, or if only one triangle uses it.
    /// </summary>
    public static class CadModelAnalyzer
    {
        public static CadModelInfo Analyze(GameObject modelRoot, float unitsToMeters, float creaseAngle = 30f, int maxEdges = 20000)
        {
            var info = new CadModelInfo();
            Matrix4x4 rootInverse = modelRoot.transform.worldToLocalMatrix;
            bool hasBounds = false;

            // Edge key = pair of welded vertex ids. Value = normal of the first triangle + sharp flag.
            var edges = new Dictionary<long, EdgeData>();
            var weld = new Dictionary<Vector3Int, int>();
            var weldedPositions = new List<Vector3>();

            foreach (MeshFilter filter in modelRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[CadModelAnalyzer] Mesh '{mesh.name}' is not readable. Enable Read/Write in its import settings.");
                    continue;
                }

                Matrix4x4 toRoot = rootInverse * filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                var rootVerts = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    rootVerts[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                    if (!hasBounds) { info.localBounds = new Bounds(rootVerts[i], Vector3.zero); hasBounds = true; }
                    else info.localBounds.Encapsulate(rootVerts[i]);
                }

                // Weld: CAD exports split vertices at every hard edge (one copy per normal).
                // Merge copies that sit at the same position so neighbouring triangles find each other.
                float cell = Mathf.Max(1e-6f, mesh.bounds.size.magnitude * 1e-5f);
                var ids = new int[rootVerts.Length];
                for (int i = 0; i < rootVerts.Length; i++)
                {
                    Vector3 p = rootVerts[i];
                    var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                    if (!weld.TryGetValue(key, out int id))
                    {
                        id = weldedPositions.Count;
                        weldedPositions.Add(p);
                        weld.Add(key, id);
                    }
                    ids[i] = id;
                }

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] tris = mesh.GetTriangles(sub);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int a = ids[tris[t]], b = ids[tris[t + 1]], c = ids[tris[t + 2]];
                        if (a == b || b == c || a == c) continue; // degenerate after welding
                        Vector3 normal = Vector3.Cross(weldedPositions[b] - weldedPositions[a], weldedPositions[c] - weldedPositions[a]);
                        if (normal.sqrMagnitude < 1e-20f) continue;
                        normal.Normalize();
                        info.triangleCount++;
                        AddEdge(edges, a, b, normal, creaseAngle);
                        AddEdge(edges, b, c, normal, creaseAngle);
                        AddEdge(edges, c, a, normal, creaseAngle);
                    }
                }
            }

            foreach (var pair in edges)
            {
                EdgeData e = pair.Value;
                if (!(e.sharp || e.faceCount == 1)) continue;
                int a = (int)(pair.Key >> 32), b = (int)(pair.Key & 0xFFFFFFFF);
                info.featureEdges.Add(weldedPositions[a]);
                info.featureEdges.Add(weldedPositions[b]);
                if (info.featureEdges.Count / 2 >= maxEdges)
                {
                    Debug.LogWarning($"[CadModelAnalyzer] More than {maxEdges} sharp edges; the wireframe is cut short. Simplify the CAD export.");
                    break;
                }
            }

            info.sizeMm = info.localBounds.size * unitsToMeters * 1000f;
            return info;
        }

        struct EdgeData
        {
            public Vector3 firstNormal;
            public int faceCount;
            public bool sharp;
        }

        static void AddEdge(Dictionary<long, EdgeData> edges, int a, int b, Vector3 normal, float creaseAngle)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (edges.TryGetValue(key, out EdgeData e))
            {
                e.faceCount++;
                if (Vector3.Angle(e.firstNormal, normal) > creaseAngle) e.sharp = true;
                edges[key] = e;
            }
            else
            {
                edges[key] = new EdgeData { firstNormal = normal, faceCount = 1 };
            }
        }

        /// <summary>
        /// Builds a mesh of thin square tubes, one per edge. Real line primitives are only 1 pixel wide
        /// on most phones, which is hard to see, so we use tubes with a real thickness instead.
        /// </summary>
        public static Mesh BuildTubeMesh(List<Vector3> edgePoints, float thickness)
        {
            int edgeCount = edgePoints.Count / 2;
            var vertices = new List<Vector3>(edgeCount * 8);
            var triangles = new List<int>(edgeCount * 24);
            float h = thickness * 0.5f;

            for (int i = 0; i < edgeCount; i++)
            {
                Vector3 a = edgePoints[2 * i], b = edgePoints[2 * i + 1];
                Vector3 dir = b - a;
                if (dir.sqrMagnitude < 1e-12f) continue;
                dir.Normalize();
                Vector3 helper = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 u = Vector3.Cross(dir, helper).normalized * h;
                Vector3 v = Vector3.Cross(dir, u).normalized * h;
                // Stretch the tube slightly past each end so corners close nicely.
                a -= dir * h;
                b += dir * h;

                int start = vertices.Count;
                Vector3[] ring = { u + v, u - v, -u - v, -u + v };
                foreach (Vector3 r in ring) vertices.Add(a + r);
                foreach (Vector3 r in ring) vertices.Add(b + r);

                for (int s = 0; s < 4; s++)
                {
                    int n = (s + 1) % 4;
                    int a0 = start + s, a1 = start + n, b0 = start + 4 + s, b1 = start + 4 + n;
                    // Both windings, so the tube is visible from any side even with back-face culling.
                    triangles.Add(a0); triangles.Add(b0); triangles.Add(a1);
                    triangles.Add(a1); triangles.Add(b0); triangles.Add(b1);
                    triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                    triangles.Add(a1); triangles.Add(b1); triangles.Add(b0);
                }
            }

            var mesh = new Mesh { name = "CadWireframe", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
