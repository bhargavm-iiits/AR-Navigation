using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Framework
{
    /// <summary>
    /// Rounded rectangle drawn as a generated mesh.
    ///
    /// Using a mesh rather than a 9-sliced sprite means the corner radius is a live number, so it
    /// stays visually correct at every canvas scale — a sliced sprite's corners distort when the
    /// same asset is stretched across a 4.7" phone and a 11" tablet. It also means the entire
    /// design system ships without a single texture.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Rounded Rect")]
    public class RoundedRectGraphic : Graphic
    {
        [SerializeField] float m_Radius = 20f;
        [SerializeField, Range(2, 16)] int m_CornerSegments = 6;

        [Tooltip("When true the radius is clamped to half the shorter side, producing a pill.")]
        [SerializeField] bool m_Pill;

        public float Radius
        {
            get => m_Radius;
            set { m_Radius = Mathf.Max(0f, value); SetVerticesDirty(); }
        }

        public bool Pill
        {
            get => m_Pill;
            set { m_Pill = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            var rect = GetPixelAdjustedRect();

            if (rect.width <= 0f || rect.height <= 0f)
                return;

            var limit = Mathf.Min(rect.width, rect.height) * 0.5f;
            var radius = m_Pill ? limit : Mathf.Min(m_Radius, limit);

            var perimeter = BuildPerimeter(rect, radius, m_CornerSegments);

            // Triangle fan from the centre. Valid for any convex shape, and a rounded rect
            // always is.
            var centre = UIVertex.simpleVert;
            centre.color = color;
            centre.position = rect.center;
            helper.AddVert(centre);

            for (var i = 0; i < perimeter.Count; i++)
            {
                var vertex = UIVertex.simpleVert;
                vertex.color = color;
                vertex.position = perimeter[i];
                helper.AddVert(vertex);
            }

            for (var i = 0; i < perimeter.Count; i++)
            {
                var next = (i + 1) % perimeter.Count;
                helper.AddTriangle(0, i + 1, next + 1);
            }
        }

        /// <summary>Perimeter points, counter-clockwise, starting at the top-right corner arc.</summary>
        internal static List<Vector2> BuildPerimeter(Rect rect, float radius, int segments)
        {
            var points = new List<Vector2>((segments + 1) * 4);

            if (radius <= 0.01f)
            {
                points.Add(new Vector2(rect.xMax, rect.yMax));
                points.Add(new Vector2(rect.xMin, rect.yMax));
                points.Add(new Vector2(rect.xMin, rect.yMin));
                points.Add(new Vector2(rect.xMax, rect.yMin));
                return points;
            }

            // Corner centres and the angle each arc sweeps through.
            Vector2[] centres =
            {
                new Vector2(rect.xMax - radius, rect.yMax - radius), // top-right
                new Vector2(rect.xMin + radius, rect.yMax - radius), // top-left
                new Vector2(rect.xMin + radius, rect.yMin + radius), // bottom-left
                new Vector2(rect.xMax - radius, rect.yMin + radius)  // bottom-right
            };

            for (var corner = 0; corner < 4; corner++)
            {
                var startAngle = corner * 90f;

                for (var s = 0; s <= segments; s++)
                {
                    var angle = (startAngle + 90f * s / segments) * Mathf.Deg2Rad;
                    points.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }

            return points;
        }
    }

    /// <summary>
    /// Circular progress ring used by the Progress screen. Draws an annulus swept clockwise from
    /// twelve o'clock, plus an optional dimmed track behind it.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Progress Ring")]
    public sealed class ProgressRingGraphic : Graphic
    {
        [SerializeField, Range(0f, 1f)] float m_Fill = 0.45f;
        [SerializeField] float m_Thickness = 26f;
        [SerializeField, Range(12, 180)] int m_Segments = 96;
        [SerializeField] Color m_TrackColor = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>Rounds the ends of the filled arc, as in the reference design.</summary>
        [SerializeField] bool m_RoundedCaps = true;

        public float Fill
        {
            get => m_Fill;
            set { m_Fill = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        public float Thickness
        {
            get => m_Thickness;
            set { m_Thickness = Mathf.Max(1f, value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            var rect = GetPixelAdjustedRect();
            var outer = Mathf.Min(rect.width, rect.height) * 0.5f;

            if (outer <= 1f)
                return;

            var inner = Mathf.Max(1f, outer - m_Thickness);
            var centre = rect.center;

            AddArc(helper, centre, inner, outer, 0f, 1f, m_TrackColor);

            if (m_Fill > 0.001f)
            {
                AddArc(helper, centre, inner, outer, 0f, m_Fill, color);

                if (m_RoundedCaps)
                {
                    var capRadius = (outer - inner) * 0.5f;
                    var capCentre = (outer + inner) * 0.5f;

                    AddDisc(helper, centre + AngleToPoint(0f) * capCentre, capRadius, color);
                    AddDisc(helper, centre + AngleToPoint(m_Fill) * capCentre, capRadius, color);
                }
            }
        }

        /// <summary>Maps a 0-1 sweep to a point on the unit circle, clockwise from the top.</summary>
        static Vector2 AngleToPoint(float t)
        {
            var radians = Mathf.PI * 0.5f - t * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        void AddArc(VertexHelper helper, Vector2 centre, float inner, float outer,
            float from, float to, Color arcColor)
        {
            var span = Mathf.Abs(to - from);

            if (span <= 0.0005f)
                return;

            var steps = Mathf.Max(2, Mathf.CeilToInt(m_Segments * span));
            var start = helper.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = arcColor;

            for (var i = 0; i <= steps; i++)
            {
                var t = Mathf.Lerp(from, to, i / (float)steps);
                var direction = AngleToPoint(t);

                vertex.position = centre + direction * inner;
                helper.AddVert(vertex);

                vertex.position = centre + direction * outer;
                helper.AddVert(vertex);
            }

            for (var i = 0; i < steps; i++)
            {
                var b = start + i * 2;
                helper.AddTriangle(b, b + 1, b + 3);
                helper.AddTriangle(b, b + 3, b + 2);
            }
        }

        static void AddDisc(VertexHelper helper, Vector2 centre, float radius, Color discColor)
        {
            const int segments = 12;
            var start = helper.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = discColor;
            vertex.position = centre;
            helper.AddVert(vertex);

            for (var i = 0; i < segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                vertex.position = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                helper.AddVert(vertex);
            }

            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;
                helper.AddTriangle(start, start + i + 1, start + next + 1);
            }
        }
    }

    /// <summary>
    /// Filled line chart used for the elevation profile. Values are normalised 0-1 and drawn
    /// left to right with a soft area fill beneath the stroke.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Sparkline")]
    public sealed class SparklineGraphic : Graphic
    {
        [SerializeField] float m_StrokeWidth = 4f;
        [SerializeField] Color m_FillColor = new Color(0.21f, 0.78f, 0.35f, 0.22f);

        readonly List<float> m_Values = new List<float>();

        public IReadOnlyList<float> Values => m_Values;

        public void SetValues(IReadOnlyList<float> values)
        {
            m_Values.Clear();

            if (values != null)
            {
                foreach (var value in values)
                    m_Values.Add(Mathf.Clamp01(value));
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            if (m_Values.Count < 2)
                return;

            var rect = GetPixelAdjustedRect();
            var step = rect.width / (m_Values.Count - 1);

            // Area fill first so the stroke draws over it.
            var fillStart = helper.currentVertCount;
            var fillVertex = UIVertex.simpleVert;
            fillVertex.color = m_FillColor;

            for (var i = 0; i < m_Values.Count; i++)
            {
                var x = rect.xMin + step * i;
                var y = rect.yMin + rect.height * m_Values[i];

                fillVertex.position = new Vector2(x, rect.yMin);
                helper.AddVert(fillVertex);

                fillVertex.position = new Vector2(x, y);
                helper.AddVert(fillVertex);
            }

            for (var i = 0; i < m_Values.Count - 1; i++)
            {
                var b = fillStart + i * 2;
                helper.AddTriangle(b, b + 1, b + 3);
                helper.AddTriangle(b, b + 3, b + 2);
            }

            // Stroke.
            for (var i = 0; i < m_Values.Count - 1; i++)
            {
                var a = new Vector2(rect.xMin + step * i, rect.yMin + rect.height * m_Values[i]);
                var b = new Vector2(rect.xMin + step * (i + 1), rect.yMin + rect.height * m_Values[i + 1]);

                AddStrokeSegment(helper, a, b);
            }
        }

        void AddStrokeSegment(VertexHelper helper, Vector2 a, Vector2 b)
        {
            var direction = b - a;

            if (direction.sqrMagnitude < 1e-6f)
                return;

            var normal = new Vector2(-direction.y, direction.x).normalized * (m_StrokeWidth * 0.5f);
            var start = helper.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = a - normal; helper.AddVert(vertex);
            vertex.position = a + normal; helper.AddVert(vertex);
            vertex.position = b + normal; helper.AddVert(vertex);
            vertex.position = b - normal; helper.AddVert(vertex);

            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }
    }

    /// <summary>
    /// Chevron used for the AR arrows drawn in screen space (turn instructions, the "next" cue)
    /// and for list disclosure indicators, so no icon atlas is required.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Chevron")]
    public sealed class ChevronGraphic : Graphic
    {
        public enum Direction { Up, Down, Left, Right }

        [SerializeField] Direction m_Direction = Direction.Right;
        [SerializeField] float m_Thickness = 6f;

        public Direction Facing
        {
            get => m_Direction;
            set { m_Direction = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            var rect = GetPixelAdjustedRect();
            var centre = rect.center;
            var half = new Vector2(rect.width, rect.height) * 0.5f;

            // A chevron is two strokes meeting at a point.
            Vector2 tip, wingA, wingB;

            switch (m_Direction)
            {
                case Direction.Up:
                    tip = centre + new Vector2(0f, half.y);
                    wingA = centre + new Vector2(-half.x, -half.y);
                    wingB = centre + new Vector2(half.x, -half.y);
                    break;

                case Direction.Down:
                    tip = centre + new Vector2(0f, -half.y);
                    wingA = centre + new Vector2(-half.x, half.y);
                    wingB = centre + new Vector2(half.x, half.y);
                    break;

                case Direction.Left:
                    tip = centre + new Vector2(-half.x, 0f);
                    wingA = centre + new Vector2(half.x, half.y);
                    wingB = centre + new Vector2(half.x, -half.y);
                    break;

                default:
                    tip = centre + new Vector2(half.x, 0f);
                    wingA = centre + new Vector2(-half.x, half.y);
                    wingB = centre + new Vector2(-half.x, -half.y);
                    break;
            }

            AddStroke(helper, wingA, tip);
            AddStroke(helper, tip, wingB);
        }

        void AddStroke(VertexHelper helper, Vector2 a, Vector2 b)
        {
            var direction = b - a;

            if (direction.sqrMagnitude < 1e-6f)
                return;

            var normal = new Vector2(-direction.y, direction.x).normalized * (m_Thickness * 0.5f);
            var start = helper.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = a - normal; helper.AddVert(vertex);
            vertex.position = a + normal; helper.AddVert(vertex);
            vertex.position = b + normal; helper.AddVert(vertex);
            vertex.position = b - normal; helper.AddVert(vertex);

            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
