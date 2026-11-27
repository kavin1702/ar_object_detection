using System.Collections.Generic;
using UnityEngine;

namespace CADInspection.Detection
{
    /// <summary>
    /// Pure image-processing core for finding the notebook by its cover colour.
    /// Works on a small screen-aligned RGBA image (row 0 = bottom, like Unity textures) and returns the
    /// notebook outline as an oriented rectangle in normalized viewport coordinates (0..1, origin bottom-left).
    /// No AR or rendering dependencies, so it can be tested in the Editor on a photo.
    /// </summary>
    public static class NoteColorBlobDetector
    {
        // A rectangle fills its detected 4-corner outline (~1.0). A circle would give ~1.57, a blob with bulges > 1.2.
        private const float MaxQuadFill = 1.2f;

        public struct Settings
        {
            public float targetHue;          // 0..1
            public float targetSaturation;   // 0..1
            public float targetValue;        // 0..1
            public float hueTolerance;       // 0..0.5 (fraction of the hue circle)
            public float minAreaFraction;    // smallest blob, as a fraction of the image
            public float minRectangularity;  // blob area / oriented bounding box area

            public static Settings FromColor(Color cover, float hueToleranceDegrees)
            {
                Color.RGBToHSV(cover, out float h, out float s, out float v);
                return new Settings
                {
                    targetHue = h,
                    targetSaturation = s,
                    targetValue = v,
                    hueTolerance = Mathf.Clamp(hueToleranceDegrees, 4f, 60f) / 360f,
                    minAreaFraction = 0.01f,
                    minRectangularity = 0.80f
                };
            }
        }

        public enum Outcome
        {
            Found,
            NoColorMatch,       // nothing of the cover colour in view
            PartiallyVisible,   // the notebook touches the image border, corners are unreliable
            NotRectangular      // a colour blob was found but it does not look like a flat rectangle
        }

        public struct Result
        {
            public Outcome outcome;
            public Vector2 center;           // viewport 0..1
            public Vector2[] corners;        // 4 corners, viewport 0..1, in order around the rectangle
            public float areaFraction;
            public float rectangularity;
            public bool[] mask;              // optional debug mask (width * height)
        }

        public static Result Detect(Color32[] pixels, int width, int height, Settings settings, bool keepMask = false)
        {
            var result = new Result { outcome = Outcome.NoColorMatch };
            if (pixels == null || pixels.Length < width * height || width < 8 || height < 8) return result;

            // 1. Colour mask
            int n = width * height;
            var mask = new bool[n];
            float minS = Mathf.Max(0.18f, settings.targetSaturation * 0.40f);
            float minV = Mathf.Max(0.12f, settings.targetValue * 0.30f);
            for (int i = 0; i < n; i++)
            {
                Color32 c = pixels[i];
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (s < minS || v < minV) continue;
                float dh = Mathf.Abs(h - settings.targetHue);
                if (dh > 0.5f) dh = 1f - dh;
                mask[i] = dh <= settings.hueTolerance;
            }

            // 2. Close small holes (printed text/logos on the cover), then remove speckles
            mask = Dilate(mask, width, height);
            mask = Erode(mask, width, height);
            mask = Erode(mask, width, height);
            mask = Dilate(mask, width, height);
            if (keepMask) result.mask = mask;

            // 3. Largest connected component
            var labels = new int[n];
            var queue = new Queue<int>();
            int bestLabel = 0, bestCount = 0, label = 0;
            for (int i = 0; i < n; i++)
            {
                if (!mask[i] || labels[i] != 0) continue;
                label++;
                int count = 0;
                labels[i] = label;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int p = queue.Dequeue();
                    count++;
                    int x = p % width, y = p / width;
                    if (x > 0) Visit(p - 1);
                    if (x < width - 1) Visit(p + 1);
                    if (y > 0) Visit(p - width);
                    if (y < height - 1) Visit(p + width);
                }
                if (count > bestCount) { bestCount = count; bestLabel = label; }

                void Visit(int q)
                {
                    if (mask[q] && labels[q] == 0) { labels[q] = label; queue.Enqueue(q); }
                }
            }

            result.areaFraction = (float)bestCount / n;
            if (bestLabel == 0 || result.areaFraction < settings.minAreaFraction) return result;

            // 4. Orientation by PCA, in pixel units corrected for non-square aspect (pixels are square on screen)
            double sx = 0, sy = 0;
            bool touchesBorder = false;
            for (int i = 0; i < n; i++)
            {
                if (labels[i] != bestLabel) continue;
                int x = i % width, y = i / width;
                sx += x; sy += y;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) touchesBorder = true;
            }
            double mx = sx / bestCount, my = sy / bestCount;
            double cxx = 0, cyy = 0, cxy = 0;
            for (int i = 0; i < n; i++)
            {
                if (labels[i] != bestLabel) continue;
                double dx = i % width - mx, dy = i / width - my;
                cxx += dx * dx; cyy += dy * dy; cxy += dx * dy;
            }
            double angle = 0.5 * System.Math.Atan2(2 * cxy, cxx - cyy);
            double ax = System.Math.Cos(angle), ay = System.Math.Sin(angle);   // major axis
            double bx = -ay, by = ax;                                            // minor axis

            double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (labels[i] != bestLabel) continue;
                double dx = i % width + 0.5 - mx, dy = i / width + 0.5 - my;
                double a = dx * ax + dy * ay, b = dx * bx + dy * by;
                if (a < minA) minA = a; if (a > maxA) maxA = a;
                if (b < minB) minB = b; if (b > maxB) maxB = b;
            }
            // pixel extents are to pixel centres; grow by half a pixel on each side
            minA -= 0.5; maxA += 0.5; minB -= 0.5; maxB += 0.5;

            double boxArea = (maxA - minA) * (maxB - minB);

            // 5. Real corners: seen at an angle the cover is a trapezoid, so take the blob pixels that reach
            //    furthest along each diagonal of the oriented box instead of the box corners themselves.
            double midA = (minA + maxA) * 0.5, midB = (minB + maxB) * 0.5;
            double halfA = System.Math.Max(1e-3, (maxA - minA) * 0.5), halfB = System.Math.Max(1e-3, (maxB - minB) * 0.5);
            int[] signA = { -1, 1, 1, -1 };
            int[] signB = { -1, -1, 1, 1 };
            var bestScore = new double[] { double.MinValue, double.MinValue, double.MinValue, double.MinValue };
            var bestPos = new Vector2[4];
            for (int i = 0; i < n; i++)
            {
                if (labels[i] != bestLabel) continue;
                double px = i % width + 0.5, py = i / width + 0.5;
                double dx = px - mx, dy = py - my;
                double a = (dx * ax + dy * ay - midA) / halfA;
                double b = (dx * bx + dy * by - midB) / halfB;
                for (int k = 0; k < 4; k++)
                {
                    double score = signA[k] * a + signB[k] * b;
                    if (score > bestScore[k]) { bestScore[k] = score; bestPos[k] = new Vector2((float)px, (float)py); }
                }
            }

            var centroid = new Vector2((float)mx + 0.5f, (float)my + 0.5f);
            var rough = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                // push half a pixel outward so the corner sits on the blob edge, not a pixel centre
                rough[k] = bestPos[k] + (bestPos[k] - centroid).normalized * 0.5f;
            }

            // 6. Sub-pixel corners: fit a straight line through the boundary pixels of each side and intersect
            //    neighbouring sides. Each corner then depends on dozens of pixels instead of one, which removes
            //    most of the frame-to-frame jitter of a single extreme pixel.
            Vector2[] refined = RefineCornersWithEdgeLines(labels, bestLabel, width, height, rough, centroid);

            result.corners = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                result.corners[k] = new Vector2(refined[k].x / width, refined[k].y / height);
            }
            result.center = (result.corners[0] + result.corners[1] + result.corners[2] + result.corners[3]) * 0.25f;

            // Shape check against the detected quad (a tilted rectangle is a trapezoid on screen, so comparing
            // with an axis box would wrongly reject it). A flat cover fills its own quad almost completely.
            double quadArea = 0, perimeter = 0;
            for (int k = 0; k < 4; k++)
            {
                Vector2 p = bestPos[k], q = bestPos[(k + 1) % 4];
                quadArea += (double)p.x * q.y - (double)q.x * p.y;
                perimeter += Vector2.Distance(p, q);
            }
            // corners are pixel centres: add the half-pixel band around the outline
            quadArea = System.Math.Abs(quadArea) * 0.5 + perimeter * 0.5 + 1.0;
            // ~1.0 for a quadrilateral; well above 1 means the blob bulges past its corners (round/irregular shape)
            result.rectangularity = quadArea > 0 ? (float)(bestCount / quadArea) : 0f;

            if (touchesBorder) { result.outcome = Outcome.PartiallyVisible; return result; }
            if (result.rectangularity < settings.minRectangularity || result.rectangularity > MaxQuadFill)
            {
                result.outcome = Outcome.NotRectangular;
                return result;
            }

            result.outcome = Outcome.Found;
            return result;
        }

        /// <summary>
        /// Picks the dominant saturated colour of a cover photo (a cropped cover or a photo of the note on a table).
        /// The centre region is weighted more, so table/background around a photographed notebook matters less.
        /// </summary>
        public static Color DominantCoverColor(Color32[] pixels, int width, int height)
        {
            const int bins = 36;
            var weight = new float[bins];
            var sumS = new float[bins];
            var sumV = new float[bins];
            var sumH = new Vector2[bins];

            for (int y = 0; y < height; y++)
            {
                float fy = Mathf.Abs((y + 0.5f) / height - 0.5f) * 2f;
                for (int x = 0; x < width; x++)
                {
                    Color.RGBToHSV(pixels[y * width + x], out float h, out float s, out float v);
                    if (s < 0.25f || v < 0.15f) continue;
                    float fx = Mathf.Abs((x + 0.5f) / width - 0.5f) * 2f;
                    float centreWeight = 1.5f - Mathf.Max(fx, fy); // 1.5 at centre, 0.5 at the edge
                    float w = s * centreWeight;
                    int b = Mathf.Min(bins - 1, (int)(h * bins));
                    weight[b] += w;
                    sumS[b] += s * w;
                    sumV[b] += v * w;
                    float ang = h * Mathf.PI * 2f;
                    sumH[b] += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * w;
                }
            }

            int best = 0;
            float bestW = 0f;
            for (int b = 0; b < bins; b++)
            {
                // include neighbours so a hue split across two bins still wins
                float w = weight[b] + 0.5f * (weight[(b + 1) % bins] + weight[(b + bins - 1) % bins]);
                if (w > bestW) { bestW = w; best = b; }
            }
            if (bestW <= 0f) return Color.gray;

            float hue = Mathf.Atan2(sumH[best].y, sumH[best].x) / (Mathf.PI * 2f);
            if (hue < 0f) hue += 1f;
            return Color.HSVToRGB(hue, sumS[best] / weight[best], sumV[best] / weight[best]);
        }

        /// <summary>
        /// Fits a line to the boundary pixels of each of the 4 sides (ignoring the rounded ends near the corners)
        /// and returns the intersections of neighbouring lines. Falls back to the rough corner when a side has too
        /// few points or the intersection lands far away. Coordinates are in pixels (origin bottom-left).
        /// </summary>
        private static Vector2[] RefineCornersWithEdgeLines(int[] labels, int label, int w, int h, Vector2[] rough, Vector2 centroid)
        {
            const float EndTrim = 0.12f;       // ignore the first/last 12% of each side (corner rounding)
            const float MaxEdgeDistance = 3f;  // boundary pixels further than this from the rough side are noise
            const float MaxCornerShift = 4f;   // refined corner must stay near the rough one

            var points = new List<Vector2>[4];
            for (int k = 0; k < 4; k++) points[k] = new List<Vector2>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (labels[i] != label) continue;
                    bool boundary = x == 0 || y == 0 || x == w - 1 || y == h - 1 ||
                                    labels[i - 1] != label || labels[i + 1] != label ||
                                    labels[i - w] != label || labels[i + w] != label;
                    if (!boundary) continue;

                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    int bestEdge = -1;
                    float bestDist = float.MaxValue, bestT = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector2 a = rough[k], b = rough[(k + 1) % 4];
                        Vector2 ab = b - a;
                        float len2 = ab.sqrMagnitude;
                        if (len2 < 1e-4f) continue;
                        float t = Vector2.Dot(p - a, ab) / len2;
                        float d = Vector2.Distance(p, a + ab * Mathf.Clamp01(t));
                        if (d < bestDist) { bestDist = d; bestEdge = k; bestT = t; }
                    }
                    if (bestEdge < 0 || bestDist > MaxEdgeDistance || bestT < EndTrim || bestT > 1f - EndTrim) continue;
                    points[bestEdge].Add(p);
                }
            }

            // Fit each side: centroid + principal direction; shift half a pixel outward onto the real edge.
            var linePoint = new Vector2[4];
            var lineDir = new Vector2[4];
            var lineOk = new bool[4];
            for (int k = 0; k < 4; k++)
            {
                var pts = points[k];
                if (pts.Count < 4) continue;
                Vector2 c = Vector2.zero;
                foreach (var p in pts) c += p;
                c /= pts.Count;
                float sxx = 0, syy = 0, sxy = 0;
                foreach (var p in pts) { Vector2 d = p - c; sxx += d.x * d.x; syy += d.y * d.y; sxy += d.x * d.y; }
                float ang = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var normal = new Vector2(-dir.y, dir.x);
                if (Vector2.Dot(normal, c - centroid) < 0f) normal = -normal;
                linePoint[k] = c + normal * 0.5f;
                lineDir[k] = dir;
                lineOk[k] = true;
            }

            var refined = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                int prev = (k + 3) % 4; // side (k-1 -> k) and side (k -> k+1) meet at corner k
                refined[k] = rough[k];
                if (!lineOk[prev] || !lineOk[k]) continue;
                if (TryIntersect(linePoint[prev], lineDir[prev], linePoint[k], lineDir[k], out Vector2 hit) &&
                    Vector2.Distance(hit, rough[k]) <= MaxCornerShift)
                {
                    refined[k] = hit;
                }
            }
            return refined;
        }

        private static bool TryIntersect(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 hit)
        {
            float denom = d1.x * d2.y - d1.y * d2.x;
            hit = default;
            if (Mathf.Abs(denom) < 1e-4f) return false; // parallel
            Vector2 diff = p2 - p1;
            float t = (diff.x * d2.y - diff.y * d2.x) / denom;
            hit = p1 + d1 * t;
            return true;
        }

        private static bool[] Dilate(bool[] src, int w, int h)
        {
            var dst = new bool[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    dst[i] = src[i] || (x > 0 && src[i - 1]) || (x < w - 1 && src[i + 1]) || (y > 0 && src[i - w]) || (y < h - 1 && src[i + w]);
                }
            return dst;
        }

        private static bool[] Erode(bool[] src, int w, int h)
        {
            var dst = new bool[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    dst[i] = src[i] && (x == 0 || src[i - 1]) && (x == w - 1 || src[i + 1]) && (y == 0 || src[i - w]) && (y == h - 1 || src[i + w]);
                }
            return dst;
        }
    }
}
