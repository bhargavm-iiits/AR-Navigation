using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Framework
{
    /// <summary>
    /// Vector icons drawn as generated meshes.
    ///
    /// The first pass used Unicode symbols (☰, ⚙, 🔊) as text. They rendered as tofu boxes on
    /// device because TMP's default LiberationSans atlas contains almost none of them, and the
    /// fallback behaviour differs across Android OEM font stacks — so the UI looked different, and
    /// broken, depending on the phone. Drawing icons as geometry removes the font dependency
    /// entirely: identical output everywhere, no atlas, no fallback chain, no import step.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Icon")]
    public sealed class IconGraphic : Graphic
    {
        public enum IconType
        {
            Menu,
            Speaker,
            SpeakerMuted,
            Gear,
            Search,
            Navigate,
            Map,
            List,
            Chart,
            Check,
            Pause,
            Target,
            North,
            Brightness,
            Close,
            Warning,
            Temple,
            WaterDrop,
            Statue,
            Footsteps
        }

        [SerializeField] IconType m_Icon = IconType.Menu;
        [SerializeField] float m_Stroke = 5f;

        public IconType Icon
        {
            get => m_Icon;
            set { m_Icon = value; SetVerticesDirty(); }
        }

        public float Stroke
        {
            get => m_Stroke;
            set { m_Stroke = Mathf.Max(1f, value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            var rect = GetPixelAdjustedRect();
            var size = Mathf.Min(rect.width, rect.height);

            if (size <= 2f)
                return;

            // Icons are authored on a normalised -0.5..0.5 box, then scaled to the rect. That
            // keeps every icon optically consistent at any control size.
            var c = rect.center;
            var s = size;
            var stroke = Mathf.Max(2f, m_Stroke * (s / 48f));

            switch (m_Icon)
            {
                case IconType.Menu:
                    for (var i = -1; i <= 1; i++)
                        Bar(helper, c + new Vector2(0f, i * s * 0.22f), s * 0.62f, stroke);
                    break;

                case IconType.Speaker:
                    Speaker(helper, c, s, stroke);
                    for (var i = 1; i <= 2; i++)
                        Arc(helper, c + new Vector2(s * 0.08f, 0f), s * (0.16f + i * 0.11f), -50f, 50f, stroke);
                    break;

                case IconType.SpeakerMuted:
                    Speaker(helper, c, s, stroke);
                    Stroke2(helper, c + new Vector2(s * 0.14f, -s * 0.14f),
                                    c + new Vector2(s * 0.40f, s * 0.14f), stroke);
                    Stroke2(helper, c + new Vector2(s * 0.14f, s * 0.14f),
                                    c + new Vector2(s * 0.40f, -s * 0.14f), stroke);
                    break;

                case IconType.Gear:
                    Ring(helper, c, s * 0.20f, s * 0.30f, 20);
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * 45f * Mathf.Deg2Rad;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Stroke2(helper, c + dir * (s * 0.28f), c + dir * (s * 0.42f), stroke * 1.5f);
                    }
                    break;

                case IconType.Search:
                    Ring(helper, c + new Vector2(-s * 0.06f, s * 0.06f), s * 0.20f, s * 0.20f + stroke, 24);
                    Stroke2(helper, c + new Vector2(s * 0.09f, -s * 0.09f),
                                    c + new Vector2(s * 0.28f, -s * 0.28f), stroke);
                    break;

                case IconType.Navigate:
                    // Paper-plane style arrow.
                    Tri(helper, c + new Vector2(0f, s * 0.40f),
                                c + new Vector2(-s * 0.28f, -s * 0.34f),
                                c + new Vector2(0f, -s * 0.14f));
                    Tri(helper, c + new Vector2(0f, s * 0.40f),
                                c + new Vector2(0f, -s * 0.14f),
                                c + new Vector2(s * 0.28f, -s * 0.34f));
                    break;

                case IconType.Map:
                    Stroke2(helper, c + new Vector2(-s * 0.34f, s * 0.28f),
                                    c + new Vector2(-s * 0.34f, -s * 0.30f), stroke);
                    Stroke2(helper, c + new Vector2(0f, s * 0.34f), c + new Vector2(0f, -s * 0.24f), stroke);
                    Stroke2(helper, c + new Vector2(s * 0.34f, s * 0.28f),
                                    c + new Vector2(s * 0.34f, -s * 0.30f), stroke);
                    Stroke2(helper, c + new Vector2(-s * 0.34f, s * 0.28f), c + new Vector2(0f, s * 0.34f), stroke);
                    Stroke2(helper, c + new Vector2(0f, s * 0.34f), c + new Vector2(s * 0.34f, s * 0.28f), stroke);
                    Stroke2(helper, c + new Vector2(-s * 0.34f, -s * 0.30f), c + new Vector2(0f, -s * 0.24f), stroke);
                    Stroke2(helper, c + new Vector2(0f, -s * 0.24f), c + new Vector2(s * 0.34f, -s * 0.30f), stroke);
                    break;

                case IconType.List:
                    for (var i = -1; i <= 1; i++)
                    {
                        var y = c.y - i * s * 0.24f;
                        Disc(helper, new Vector2(c.x - s * 0.30f, y), stroke * 0.9f, 8);
                        Bar(helper, new Vector2(c.x + s * 0.08f, y), s * 0.44f, stroke);
                    }
                    break;

                case IconType.Chart:
                    Column(helper, c + new Vector2(-s * 0.26f, 0f), s * 0.30f, stroke * 1.8f);
                    Column(helper, c, s * 0.52f, stroke * 1.8f);
                    Column(helper, c + new Vector2(s * 0.26f, 0f), s * 0.72f, stroke * 1.8f);
                    break;

                case IconType.Check:
                    Stroke2(helper, c + new Vector2(-s * 0.28f, s * 0.02f),
                                    c + new Vector2(-s * 0.08f, -s * 0.20f), stroke * 1.3f);
                    Stroke2(helper, c + new Vector2(-s * 0.08f, -s * 0.20f),
                                    c + new Vector2(s * 0.30f, s * 0.24f), stroke * 1.3f);
                    break;

                case IconType.Pause:
                    Column(helper, c + new Vector2(-s * 0.14f, 0f), s * 0.56f, stroke * 1.8f);
                    Column(helper, c + new Vector2(s * 0.14f, 0f), s * 0.56f, stroke * 1.8f);
                    break;

                case IconType.Target:
                    Ring(helper, c, s * 0.22f, s * 0.22f + stroke, 24);
                    Disc(helper, c, stroke * 1.4f, 10);
                    for (var i = 0; i < 4; i++)
                    {
                        var a = i * 90f * Mathf.Deg2Rad;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Stroke2(helper, c + dir * (s * 0.30f), c + dir * (s * 0.44f), stroke);
                    }
                    break;

                case IconType.North:
                    Tri(helper, c + new Vector2(0f, s * 0.38f),
                                c + new Vector2(-s * 0.26f, -s * 0.30f),
                                c + new Vector2(s * 0.26f, -s * 0.30f));
                    break;

                case IconType.Brightness:
                    Disc(helper, c, s * 0.18f, 18);
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * 45f * Mathf.Deg2Rad;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Stroke2(helper, c + dir * (s * 0.26f), c + dir * (s * 0.40f), stroke);
                    }
                    break;

                case IconType.Close:
                    Stroke2(helper, c + new Vector2(-s * 0.24f, -s * 0.24f),
                                    c + new Vector2(s * 0.24f, s * 0.24f), stroke);
                    Stroke2(helper, c + new Vector2(-s * 0.24f, s * 0.24f),
                                    c + new Vector2(s * 0.24f, -s * 0.24f), stroke);
                    break;

                case IconType.Warning:
                    Stroke2(helper, c + new Vector2(0f, s * 0.40f),
                                    c + new Vector2(-s * 0.36f, -s * 0.28f), stroke);
                    Stroke2(helper, c + new Vector2(-s * 0.36f, -s * 0.28f),
                                    c + new Vector2(s * 0.36f, -s * 0.28f), stroke);
                    Stroke2(helper, c + new Vector2(s * 0.36f, -s * 0.28f),
                                    c + new Vector2(0f, s * 0.40f), stroke);
                    Stroke2(helper, c + new Vector2(0f, s * 0.12f),
                                    c + new Vector2(0f, -s * 0.08f), stroke * 1.2f);
                    Disc(helper, c + new Vector2(0f, -s * 0.20f), stroke * 0.7f, 8);
                    break;

                case IconType.Temple:
                    Quad(helper, c + new Vector2(-s * 0.38f, -s * 0.40f), c + new Vector2(s * 0.38f, -s * 0.40f),
                                 c + new Vector2(s * 0.30f, -s * 0.22f), c + new Vector2(-s * 0.30f, -s * 0.22f));
                    Quad(helper, c + new Vector2(-s * 0.26f, -s * 0.22f), c + new Vector2(s * 0.26f, -s * 0.22f),
                                 c + new Vector2(s * 0.18f, -s * 0.04f), c + new Vector2(-s * 0.18f, -s * 0.04f));
                    Quad(helper, c + new Vector2(-s * 0.16f, -s * 0.04f), c + new Vector2(s * 0.16f, -s * 0.04f),
                                 c + new Vector2(s * 0.10f, s * 0.14f), c + new Vector2(-s * 0.10f, s * 0.14f));
                    Tri(helper, c + new Vector2(-s * 0.08f, s * 0.14f), c + new Vector2(s * 0.08f, s * 0.14f),
                                c + new Vector2(0f, s * 0.40f));
                    break;

                case IconType.WaterDrop:
                    Tri(helper, c + new Vector2(0f, s * 0.40f), c + new Vector2(-s * 0.22f, s * 0.02f),
                                c + new Vector2(s * 0.22f, s * 0.02f));
                    Disc(helper, c + new Vector2(0f, -s * 0.08f), s * 0.24f, 20);
                    break;

                case IconType.Statue:
                    Quad(helper, c + new Vector2(-s * 0.30f, -s * 0.40f), c + new Vector2(s * 0.30f, -s * 0.40f),
                                 c + new Vector2(s * 0.30f, -s * 0.28f), c + new Vector2(-s * 0.30f, -s * 0.28f));
                    Quad(helper, c + new Vector2(-s * 0.14f, -s * 0.28f), c + new Vector2(s * 0.14f, -s * 0.28f),
                                 c + new Vector2(s * 0.10f, s * 0.10f), c + new Vector2(-s * 0.10f, s * 0.10f));
                    Disc(helper, c + new Vector2(0f, s * 0.26f), s * 0.14f, 16);
                    break;

                case IconType.Footsteps:
                    Disc(helper, c + new Vector2(-s * 0.16f, s * 0.20f), s * 0.12f, 14);
                    Disc(helper, c + new Vector2(-s * 0.16f, -s * 0.02f), s * 0.08f, 12);
                    Disc(helper, c + new Vector2(s * 0.16f, -s * 0.14f), s * 0.12f, 14);
                    Disc(helper, c + new Vector2(s * 0.16f, -s * 0.34f), s * 0.08f, 12);
                    break;
            }
        }

        // -------------------------------------------------------------------------------
        // Primitives
        // -------------------------------------------------------------------------------

        void Speaker(VertexHelper helper, Vector2 c, float s, float stroke)
        {
            // Body
            Quad(helper,
                c + new Vector2(-s * 0.34f, -s * 0.12f),
                c + new Vector2(-s * 0.18f, -s * 0.12f),
                c + new Vector2(-s * 0.18f, s * 0.12f),
                c + new Vector2(-s * 0.34f, s * 0.12f));

            // Cone
            Tri(helper, c + new Vector2(-s * 0.18f, -s * 0.12f),
                        c + new Vector2(-s * 0.02f, -s * 0.30f),
                        c + new Vector2(-s * 0.02f, s * 0.30f));

            Tri(helper, c + new Vector2(-s * 0.18f, -s * 0.12f),
                        c + new Vector2(-s * 0.02f, s * 0.30f),
                        c + new Vector2(-s * 0.18f, s * 0.12f));
        }

        void Bar(VertexHelper helper, Vector2 centre, float width, float thickness) =>
            Stroke2(helper, centre - new Vector2(width * 0.5f, 0f),
                            centre + new Vector2(width * 0.5f, 0f), thickness);

        void Column(VertexHelper helper, Vector2 bottomCentre, float height, float thickness) =>
            Stroke2(helper, bottomCentre - new Vector2(0f, height * 0.5f),
                            bottomCentre + new Vector2(0f, height * 0.5f), thickness);

        void Stroke2(VertexHelper helper, Vector2 a, Vector2 b, float thickness)
        {
            var direction = b - a;

            if (direction.sqrMagnitude < 1e-6f)
                return;

            var normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
            Quad(helper, a - normal, b - normal, b + normal, a + normal);
        }

        void Quad(VertexHelper helper, Vector2 a, Vector2 b, Vector2 cc, Vector2 d)
        {
            var start = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = a; helper.AddVert(vertex);
            vertex.position = b; helper.AddVert(vertex);
            vertex.position = cc; helper.AddVert(vertex);
            vertex.position = d; helper.AddVert(vertex);

            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }

        void Tri(VertexHelper helper, Vector2 a, Vector2 b, Vector2 cc)
        {
            var start = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = a; helper.AddVert(vertex);
            vertex.position = b; helper.AddVert(vertex);
            vertex.position = cc; helper.AddVert(vertex);

            helper.AddTriangle(start, start + 1, start + 2);
        }

        void Disc(VertexHelper helper, Vector2 centre, float radius, int segments)
        {
            var start = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = centre;
            helper.AddVert(vertex);

            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                vertex.position = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                helper.AddVert(vertex);
            }

            for (var i = 0; i < segments; i++)
                helper.AddTriangle(start, start + i + 1, start + (i + 1) % segments + 1);
        }

        void Ring(VertexHelper helper, Vector2 centre, float inner, float outer, int segments)
        {
            var start = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            for (var i = 0; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                vertex.position = centre + dir * inner; helper.AddVert(vertex);
                vertex.position = centre + dir * outer; helper.AddVert(vertex);
            }

            for (var i = 0; i < segments; i++)
            {
                var b = start + i * 2;
                helper.AddTriangle(b, b + 1, b + 3);
                helper.AddTriangle(b, b + 3, b + 2);
            }
        }

        void Arc(VertexHelper helper, Vector2 centre, float radius, float fromDeg, float toDeg, float thickness)
        {
            const int steps = 10;
            var previous = Vector2.zero;

            for (var i = 0; i <= steps; i++)
            {
                var a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                var point = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;

                if (i > 0)
                    Stroke2(helper, previous, point, thickness);

                previous = point;
            }
        }
    }
}
