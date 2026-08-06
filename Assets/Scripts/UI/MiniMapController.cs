using System.Collections.Generic;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Managers;
using TirumalaAR.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI
{
    /// <summary>
    /// Fully offline mini-map (System 14).
    ///
    /// There are no map tiles and no network. The map is drawn from the same ENU route geometry
    /// the navigation uses, rendered into a UI <see cref="LineRenderer"/>-equivalent built from a
    /// procedural mesh. The route is simplified once at startup — pushing 2400 vertices through
    /// a canvas mesh every frame would cost more than the entire AR pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MiniMapController : Graphic
    {
        [Header("Wiring")]
        [SerializeField] AppBootstrap m_Bootstrap;
        [SerializeField] RectTransform m_UserMarker;
        [SerializeField] RectTransform m_DestinationMarker;
        [SerializeField] RectTransform m_NorthIndicator;

        [Header("Appearance")]
        [SerializeField] float m_RouteThickness = 4f;
        [SerializeField] Color m_RouteColor = new Color(0.25f, 0.55f, 1f, 0.9f);
        [SerializeField] Color m_TravelledColor = new Color(0.45f, 0.85f, 0.5f, 0.9f);

        [Header("View")]
        [Tooltip("Metres visible across the width of the map.")]
        [SerializeField] float m_ZoomMeters = 160f;
        [SerializeField] float m_MinZoom = 60f;
        [SerializeField] float m_MaxZoom = 1200f;

        [Tooltip("Rotate the map so the pilgrim's heading always points up.")]
        [SerializeField] bool m_RotateWithHeading = true;

        readonly List<Vector3> m_SimplifiedRoute = new List<Vector3>();
        Vector3 m_UserEnu;
        float m_HeadingDegrees;
        float m_TravelledDistance;
        bool m_RouteReady;
        float m_RebuildTimer;

        public float Zoom
        {
            get => m_ZoomMeters;
            set
            {
                m_ZoomMeters = Mathf.Clamp(value, m_MinZoom, m_MaxZoom);
                SetVerticesDirty();
            }
        }

        public bool RotateWithHeading
        {
            get => m_RotateWithHeading;
            set { m_RotateWithHeading = value; SetVerticesDirty(); }
        }

        protected override void Awake()
        {
            base.Awake();

            if (m_Bootstrap == null)
                m_Bootstrap = FindAnyObjectByType<AppBootstrap>();
        }

        /// <summary>
        /// Supplies the marker transforms when the map is built from code rather than authored in
        /// a scene. The screens construct their own hierarchy, so there is no inspector pass to
        /// wire these up.
        /// </summary>
        public void AssignMarkers(RectTransform user, RectTransform destination, RectTransform north)
        {
            m_UserMarker = user;
            m_DestinationMarker = destination;
            m_NorthIndicator = north;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EventBus.Subscribe<UserPoseUpdatedEvent>(OnPose);
            EventBus.Subscribe<RouteProgressEvent>(OnProgress);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EventBus.Unsubscribe<UserPoseUpdatedEvent>(OnPose);
            EventBus.Unsubscribe<RouteProgressEvent>(OnProgress);
        }

        void OnPose(UserPoseUpdatedEvent evt)
        {
            m_UserEnu = evt.enuPosition;
            m_HeadingDegrees = evt.headingDegrees;
        }

        void OnProgress(RouteProgressEvent evt) => m_TravelledDistance = evt.completedDistance;

        void Update()
        {
            if (!m_RouteReady)
                TryCacheRoute();

            // Redrawing the polyline at 60 Hz is unnecessary; the map only needs to keep up with
            // walking pace.
            m_RebuildTimer += Time.deltaTime;

            if (m_RebuildTimer < 0.1f)
                return;

            m_RebuildTimer = 0f;
            SetVerticesDirty();
            UpdateMarkers();
        }

        void TryCacheRoute()
        {
            var graph = m_Bootstrap?.Graph;

            if (graph == null || !graph.IsReady)
                return;

            var points = new List<Vector3>(graph.Waypoints.Count);

            foreach (var waypoint in graph.Waypoints)
                points.Add(waypoint.enuPosition);

            // 4 m tolerance keeps every switchback legible while cutting the vertex count by
            // roughly an order of magnitude.
            m_SimplifiedRoute.Clear();
            m_SimplifiedRoute.AddRange(PolylineUtility.Simplify(points, 4f));
            m_RouteReady = true;

            SetVerticesDirty();
        }

        /// <summary>Converts an ENU position into local canvas coordinates.</summary>
        Vector2 EnuToMap(Vector3 enu)
        {
            var rect = rectTransform.rect;
            var scale = rect.width / Mathf.Max(1f, m_ZoomMeters);

            var relative = new Vector2(enu.x - m_UserEnu.x, enu.z - m_UserEnu.z) * scale;

            if (!m_RotateWithHeading)
                return relative;

            // Rotate the world by -heading so "ahead" is up.
            var radians = m_HeadingDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            return new Vector2(relative.x * cos - relative.y * sin, relative.x * sin + relative.y * cos);
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();

            if (!m_RouteReady || m_SimplifiedRoute.Count < 2)
                return;

            var graph = m_Bootstrap?.Graph;

            if (graph == null)
                return;

            var rect = rectTransform.rect;
            var halfWidth = rect.width * 0.5f;
            var halfHeight = rect.height * 0.5f;
            var cumulative = 0f;

            for (var i = 0; i < m_SimplifiedRoute.Count - 1; i++)
            {
                var aEnu = m_SimplifiedRoute[i];
                var bEnu = m_SimplifiedRoute[i + 1];

                cumulative += Vector3.Distance(aEnu, bEnu);

                var a = EnuToMap(aEnu);
                var b = EnuToMap(bEnu);

                // Cull segments entirely outside the visible square.
                if (Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(b.x)) > halfWidth * 1.5f ||
                    Mathf.Max(Mathf.Abs(a.y), Mathf.Abs(b.y)) > halfHeight * 1.5f)
                    continue;

                var segmentColor = cumulative <= m_TravelledDistance ? m_TravelledColor : m_RouteColor;
                AddSegment(helper, a, b, segmentColor);
            }
        }

        void AddSegment(VertexHelper helper, Vector2 a, Vector2 b, Color segmentColor)
        {
            var direction = b - a;

            if (direction.sqrMagnitude < 1e-6f)
                return;

            var normal = new Vector2(-direction.y, direction.x).normalized * (m_RouteThickness * 0.5f);
            var index = helper.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = segmentColor;

            vertex.position = a - normal; helper.AddVert(vertex);
            vertex.position = a + normal; helper.AddVert(vertex);
            vertex.position = b + normal; helper.AddVert(vertex);
            vertex.position = b - normal; helper.AddVert(vertex);

            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index, index + 2, index + 3);
        }

        void UpdateMarkers()
        {
            // The user is always at the centre; only the marker's rotation changes.
            if (m_UserMarker != null)
            {
                m_UserMarker.anchoredPosition = Vector2.zero;
                m_UserMarker.localRotation = m_RotateWithHeading
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 0f, -m_HeadingDegrees);
            }

            if (m_NorthIndicator != null)
            {
                m_NorthIndicator.localRotation = m_RotateWithHeading
                    ? Quaternion.Euler(0f, 0f, m_HeadingDegrees)
                    : Quaternion.identity;
            }

            if (m_DestinationMarker == null)
                return;

            var graph = m_Bootstrap?.Graph;

            if (graph == null || !graph.IsReady)
                return;

            var destination = graph.Waypoints[graph.Waypoints.Count - 1].enuPosition;
            var mapped = EnuToMap(destination);

            // Clamp the destination marker to the edge of the map when it is off-screen, so the
            // pilgrim always has a bearing to the temple.
            var rect = rectTransform.rect;
            var limit = new Vector2(rect.width * 0.5f - 12f, rect.height * 0.5f - 12f);

            if (Mathf.Abs(mapped.x) > limit.x || Mathf.Abs(mapped.y) > limit.y)
            {
                var scale = Mathf.Min(limit.x / Mathf.Max(Mathf.Abs(mapped.x), 0.001f),
                                      limit.y / Mathf.Max(Mathf.Abs(mapped.y), 0.001f));
                mapped *= scale;
            }

            m_DestinationMarker.anchoredPosition = mapped;
        }
    }
}
