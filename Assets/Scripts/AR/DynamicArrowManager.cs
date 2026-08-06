using System.Collections.Generic;
using TirumalaAR.Core;
using TirumalaAR.Localization;
using TirumalaAR.Navigation;
using UnityEngine;

namespace TirumalaAR.AR
{
    /// <summary>
    /// Spawns, moves and retires the blue navigation arrows (Systems 7 and 9).
    ///
    /// The arrow ring is expressed in *route distance*, not in world positions: the manager keeps
    /// arrows at fixed intervals ahead of the pilgrim's projected position along the route. As the
    /// pilgrim advances, the window slides — arrows that fall behind are retired and new ones are
    /// spawned at the far end. That keeps a constant count on screen regardless of walking speed
    /// and means arrows never need to be repositioned in bulk when the localisation frame is
    /// corrected: they are re-derived from route distance every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DynamicArrowManager : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] GameObject m_ArrowPrefab;

        [Header("Placement")]
        [Tooltip("Distance between consecutive arrows along the route, in metres.")]
        [SerializeField] float m_Spacing = 0.75f;

        [Tooltip("How many arrows are visible at once.")]
        [SerializeField] int m_VisibleCount = 10;

        [Tooltip("Arrows start this far ahead of the pilgrim so none are spawned underfoot.")]
        [SerializeField] float m_LeadOffset = 1.5f;

        [Tooltip("An arrow is retired once it falls this far behind the pilgrim.")]
        [SerializeField] float m_TrailingCutoff = 1.0f;

        [Header("Appearance")]
        [SerializeField] Color m_ArrowColor = new Color(0.13f, 0.48f, 1f, 1f);

        [Tooltip("Colour used where the route crosses a gap that was interpolated, not surveyed.")]
        [SerializeField] Color m_BridgedColor = new Color(1f, 0.65f, 0.1f, 1f);

        [Header("Height")]
        [Tooltip("Arrows are lifted this far above the solved ground so they do not z-fight with it.")]
        [SerializeField] float m_GroundClearance = 0.03f;

        // Runtime dependencies, injected by AppBootstrap.
        IRouteQuery m_Route;
        HybridLocalizationEngine m_Localization;
        GroundPlacementService m_Ground;
        ARAnchorService m_Anchors;
        Transform m_Camera;

        ObjectPool m_Pool;
        readonly List<NavigationArrow> m_Active = new List<NavigationArrow>();
        readonly List<NavigationArrow> m_Retiring = new List<NavigationArrow>();

        float m_PruneTimer;
        bool m_Enabled;

        public int ActiveArrowCount => m_Active.Count;
        public int AnchorCount => m_Anchors?.ActiveAnchorCount ?? 0;

        public void Configure(IRouteQuery route, HybridLocalizationEngine localization,
            GroundPlacementService ground, ARAnchorService anchors, Transform camera)
        {
            m_Route = route;
            m_Localization = localization;
            m_Ground = ground;
            m_Anchors = anchors;
            m_Camera = camera;

            if (m_ArrowPrefab == null)
            {
                Debug.LogError("[Arrows] No arrow prefab assigned — nothing will be drawn.");
                return;
            }

            m_Pool ??= new ObjectPool(m_ArrowPrefab, transform, m_VisibleCount + 4, m_VisibleCount + 8);
            m_Enabled = true;
        }

        public void SetSpacing(float spacing)
        {
            m_Spacing = Mathf.Clamp(spacing, 0.3f, 3f);
            ClearAll();
        }

        public void SetVisibleCount(int count)
        {
            m_VisibleCount = Mathf.Clamp(count, 3, 30);
            ClearAll();
        }

        void Update()
        {
            if (!m_Enabled || m_Camera == null || m_Localization == null || m_Route == null)
                return;

            var deltaTime = Time.deltaTime;

            m_Ground?.Tick(deltaTime);

            if (!m_Localization.IsInitialized)
            {
                ClearAll();
                return;
            }

            var projection = m_Localization.Projection;

            if (!projection.isValid)
            {
                RetireAll();
                TickArrows(deltaTime);
                return;
            }

            SyncArrowWindow(projection.distanceAlongRoute);
            TickArrows(deltaTime);

            m_PruneTimer += deltaTime;
            if (m_PruneTimer > 4f)
            {
                m_PruneTimer = 0f;
                m_Anchors?.PruneIdle();
            }
        }

        /// <summary>
        /// Reconciles the set of live arrows with the window of route distances that should be
        /// showing right now.
        /// </summary>
        void SyncArrowWindow(float userDistance)
        {
            var first = userDistance + m_LeadOffset;

            // Quantise to a global grid so arrows sit at stable route distances instead of
            // sliding forward with the pilgrim.
            first = Mathf.Ceil(first / m_Spacing) * m_Spacing;

            // Retire arrows that have fallen behind or run past the end of the window.
            var windowEnd = first + m_Spacing * (m_VisibleCount - 1);

            for (var i = m_Active.Count - 1; i >= 0; i--)
            {
                var arrow = m_Active[i];
                var distance = arrow.RouteDistance;

                if (distance >= userDistance - m_TrailingCutoff && distance <= windowEnd + m_Spacing * 0.5f)
                    continue;

                arrow.Retire();
                m_Active.RemoveAt(i);
                m_Retiring.Add(arrow);
            }

            // Spawn whatever is missing from the window.
            for (var i = 0; i < m_VisibleCount; i++)
            {
                var distance = first + m_Spacing * i;

                if (distance > m_Route.TotalDistance)
                    break;

                if (HasArrowAt(distance))
                    continue;

                Spawn(distance);
            }

            // Keep every live arrow pinned to its route distance.
            foreach (var arrow in m_Active)
            {
                if (TrySolvePose(arrow.RouteDistance, out var position, out var rotation))
                    arrow.SetTarget(position, rotation);
            }
        }

        bool HasArrowAt(float distance)
        {
            const float tolerance = 0.01f;

            foreach (var arrow in m_Active)
            {
                if (Mathf.Abs(arrow.RouteDistance - distance) < tolerance)
                    return true;
            }

            return false;
        }

        void Spawn(float routeDistance)
        {
            if (m_Pool == null || !TrySolvePose(routeDistance, out var position, out var rotation))
                return;

            var instance = m_Pool.Get(position, rotation);
            var arrow = instance.GetComponent<NavigationArrow>();

            if (arrow == null)
            {
                Debug.LogError("[Arrows] The arrow prefab is missing its NavigationArrow component.");
                m_Pool.Release(instance);
                return;
            }

            // Parent to a shared AR anchor so ARCore's own corrections move the arrow with the world.
            var anchor = m_Anchors?.Acquire(position);
            if (anchor != null)
                instance.transform.SetParent(anchor, true);

            var waypointIndex = m_Route.Waypoints.Count > 0
                ? Mathf.Clamp(FindWaypointIndex(routeDistance), 0, m_Route.Waypoints.Count - 1)
                : 0;

            var isBridged = m_Route.Waypoints.Count > 0 && m_Route.Waypoints[waypointIndex].isBridged;

            arrow.Activate(position, rotation, isBridged ? m_BridgedColor : m_ArrowColor, routeDistance);
            m_Active.Add(arrow);
        }

        int FindWaypointIndex(float routeDistance)
        {
            if (m_Route is NavigationGraph graph)
                return graph.FindWaypointAtDistance(routeDistance);

            return 0;
        }

        /// <summary>
        /// Converts a route distance into a world pose sitting on the real ground.
        /// The arrow's forward direction comes from the route tangent — sampled slightly ahead so
        /// it follows the curvature of the staircase rather than the local segment noise — and its
        /// pitch comes from the height difference between here and there, which is what makes
        /// arrows lie along the slope of the steps instead of standing upright on them.
        /// </summary>
        bool TrySolvePose(float routeDistance, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var frame = m_Localization.Frame;

            if (!frame.IsInitialized)
                return false;

            var hereEnu = SampleRoute(routeDistance);
            var aheadEnu = SampleRoute(Mathf.Min(routeDistance + Mathf.Max(m_Spacing, 1.2f), m_Route.TotalDistance));

            var hereWorld = frame.EnuToUnity(hereEnu);
            var aheadWorld = frame.EnuToUnity(aheadEnu);

            // Solve both ends against the real ground so the pitch reflects the actual staircase.
            var hereGround = m_Ground != null ? m_Ground.Solve(hereWorld) : default;
            var aheadGround = m_Ground != null ? m_Ground.Solve(aheadWorld) : default;

            if (m_Ground != null && hereGround.isValid)
                hereWorld = hereGround.position;

            if (m_Ground != null && aheadGround.isValid)
                aheadWorld = aheadGround.position;

            position = hereWorld + Vector3.up * m_GroundClearance;

            var forward = aheadWorld - hereWorld;

            if (forward.sqrMagnitude < 1e-6f)
            {
                var flat = frame.EnuToUnityDirection(Vector3.forward);
                flat.y = 0f;
                forward = flat.sqrMagnitude < 1e-6f ? Vector3.forward : flat;
            }

            // Limit the pitch: the Alipiri steps reach roughly 30°, and anything steeper than
            // that is a bad ground solve rather than a real gradient.
            var horizontal = new Vector3(forward.x, 0f, forward.z);

            if (horizontal.sqrMagnitude > 1e-6f)
            {
                var pitch = Mathf.Atan2(forward.y, horizontal.magnitude) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(pitch, -35f, 35f);

                var yaw = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
                rotation = yaw * Quaternion.Euler(-pitch, 0f, 0f);
            }
            else
            {
                rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }

            return true;
        }

        Vector3 SampleRoute(float distance)
        {
            if (m_Route is NavigationGraph graph)
                return graph.PositionAtDistance(distance);

            return Vector3.zero;
        }

        void TickArrows(float deltaTime)
        {
            var cameraPosition = m_Camera.position;

            foreach (var arrow in m_Active)
                arrow.Tick(deltaTime, cameraPosition);

            for (var i = m_Retiring.Count - 1; i >= 0; i--)
            {
                var arrow = m_Retiring[i];
                arrow.Tick(deltaTime, cameraPosition);

                if (!arrow.IsFinished)
                    continue;

                m_Anchors?.Release(arrow.transform.position);
                m_Pool.Release(arrow.gameObject);
                m_Retiring.RemoveAt(i);
            }
        }

        void RetireAll()
        {
            foreach (var arrow in m_Active)
            {
                arrow.Retire();
                m_Retiring.Add(arrow);
            }

            m_Active.Clear();
        }

        public void ClearAll()
        {
            RetireAll();

            foreach (var arrow in m_Retiring)
            {
                if (arrow != null && m_Pool != null)
                    m_Pool.Release(arrow.gameObject);
            }

            m_Retiring.Clear();
        }

        void OnDestroy()
        {
            m_Pool?.Dispose();
            m_Anchors?.Clear();
        }
    }
}
