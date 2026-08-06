using System.Collections.Generic;
using UnityEngine;

namespace TirumalaAR.Utilities
{
    /// <summary>
    /// Polyline resampling and smoothing. OpenStreetMap gives the Alipiri steps as ~38 m apart
    /// vertices; arrows need points every 0.75 m along a curve that actually follows the
    /// staircase, so the raw polyline is smoothed with a centripetal Catmull-Rom spline and then
    /// resampled by arc length.
    /// </summary>
    public static class PolylineUtility
    {
        /// <summary>
        /// Centripetal Catmull-Rom interpolation. The centripetal parameterisation (alpha = 0.5)
        /// is used rather than uniform because it provably never produces cusps or self-
        /// intersections on unevenly spaced control points — and OSM step vertices are extremely
        /// unevenly spaced.
        /// </summary>
        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, float alpha = 0.5f)
        {
            float GetT(float tPrev, Vector3 a, Vector3 b)
            {
                var distance = Vector3.Distance(a, b);
                return tPrev + Mathf.Pow(Mathf.Max(distance, 1e-5f), alpha);
            }

            var t0 = 0f;
            var t1 = GetT(t0, p0, p1);
            var t2 = GetT(t1, p1, p2);
            var t3 = GetT(t2, p2, p3);

            var tt = Mathf.Lerp(t1, t2, Mathf.Clamp01(t));

            var a1 = (t1 - tt) / Mathf.Max(t1 - t0, 1e-5f) * p0 + (tt - t0) / Mathf.Max(t1 - t0, 1e-5f) * p1;
            var a2 = (t2 - tt) / Mathf.Max(t2 - t1, 1e-5f) * p1 + (tt - t1) / Mathf.Max(t2 - t1, 1e-5f) * p2;
            var a3 = (t3 - tt) / Mathf.Max(t3 - t2, 1e-5f) * p2 + (tt - t2) / Mathf.Max(t3 - t2, 1e-5f) * p3;

            var b1 = (t2 - tt) / Mathf.Max(t2 - t0, 1e-5f) * a1 + (tt - t0) / Mathf.Max(t2 - t0, 1e-5f) * a2;
            var b2 = (t3 - tt) / Mathf.Max(t3 - t1, 1e-5f) * a2 + (tt - t1) / Mathf.Max(t3 - t1, 1e-5f) * a3;

            return (t2 - tt) / Mathf.Max(t2 - t1, 1e-5f) * b1 + (tt - t1) / Mathf.Max(t2 - t1, 1e-5f) * b2;
        }

        /// <summary>
        /// Subdivides a polyline with a Catmull-Rom spline, emitting <paramref name="segmentsPerSpan"/>
        /// samples between each pair of control points. Endpoints are duplicated so the curve
        /// starts and ends exactly on the original first/last vertex.
        /// </summary>
        public static List<Vector3> Smooth(IReadOnlyList<Vector3> points, int segmentsPerSpan = 8)
        {
            var result = new List<Vector3>();
            if (points == null || points.Count == 0)
                return result;

            if (points.Count < 3)
            {
                result.AddRange(points);
                return result;
            }

            segmentsPerSpan = Mathf.Max(1, segmentsPerSpan);

            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[Mathf.Max(i - 1, 0)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Mathf.Min(i + 2, points.Count - 1)];

                for (var s = 0; s < segmentsPerSpan; s++)
                    result.Add(CatmullRom(p0, p1, p2, p3, s / (float)segmentsPerSpan));
            }

            result.Add(points[points.Count - 1]);
            return result;
        }

        /// <summary>
        /// Resamples a polyline so consecutive points are exactly <paramref name="spacing"/> metres
        /// apart along the path. The final vertex is always preserved.
        /// </summary>
        public static List<Vector3> ResampleByDistance(IReadOnlyList<Vector3> points, float spacing)
        {
            var result = new List<Vector3>();
            if (points == null || points.Count == 0)
                return result;

            spacing = Mathf.Max(0.05f, spacing);
            result.Add(points[0]);

            if (points.Count == 1)
                return result;

            var carry = 0f;

            for (var i = 0; i < points.Count - 1; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                var segment = end - start;
                var length = segment.magnitude;

                if (length < 1e-6f)
                    continue;

                var direction = segment / length;
                var travelled = spacing - carry;

                while (travelled <= length)
                {
                    result.Add(start + direction * travelled);
                    travelled += spacing;
                }

                carry = length - (travelled - spacing);
            }

            var last = points[points.Count - 1];
            if (Vector3.Distance(result[result.Count - 1], last) > spacing * 0.25f)
                result.Add(last);

            return result;
        }

        /// <summary>Total length of a polyline in metres.</summary>
        public static float Length(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            var total = 0f;
            for (var i = 0; i < points.Count - 1; i++)
                total += Vector3.Distance(points[i], points[i + 1]);

            return total;
        }

        /// <summary>
        /// Projects <paramref name="point"/> onto the segment a-b and returns the closest point,
        /// with <paramref name="t"/> as the normalised position along the segment.
        /// </summary>
        public static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point, out float t)
        {
            var ab = b - a;
            var lengthSq = ab.sqrMagnitude;

            if (lengthSq < 1e-9f)
            {
                t = 0f;
                return a;
            }

            t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
            return a + ab * t;
        }

        /// <summary>
        /// Douglas-Peucker simplification, applied to the mini-map polyline so drawing the whole
        /// 6 km route does not push thousands of vertices through the LineRenderer every frame.
        /// </summary>
        public static List<Vector3> Simplify(IReadOnlyList<Vector3> points, float tolerance)
        {
            var result = new List<Vector3>();
            if (points == null || points.Count == 0)
                return result;

            if (points.Count < 3)
            {
                result.AddRange(points);
                return result;
            }

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            SimplifyRecursive(points, 0, points.Count - 1, tolerance, keep);

            for (var i = 0; i < points.Count; i++)
            {
                if (keep[i])
                    result.Add(points[i]);
            }

            return result;
        }

        static void SimplifyRecursive(IReadOnlyList<Vector3> points, int first, int last, float tolerance, bool[] keep)
        {
            if (last <= first + 1)
                return;

            var maxDistance = 0f;
            var maxIndex = -1;

            for (var i = first + 1; i < last; i++)
            {
                var projected = ClosestPointOnSegment(points[first], points[last], points[i], out _);
                var distance = Vector3.Distance(points[i], projected);

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    maxIndex = i;
                }
            }

            if (maxIndex < 0 || maxDistance <= tolerance)
                return;

            keep[maxIndex] = true;
            SimplifyRecursive(points, first, maxIndex, tolerance, keep);
            SimplifyRecursive(points, maxIndex, last, tolerance, keep);
        }
    }
}
