using System.Collections.Generic;
using TirumalaAR.Utilities;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace TirumalaAR.AR
{
    /// <summary>How a surface height was determined, in decreasing order of confidence.</summary>
    public enum GroundSolveMethod { None, PlaneRaycast, PlaneProximity, FeaturePoint, CameraHeightFallback }

    public struct GroundSample
    {
        public Vector3 position;
        public Vector3 normal;
        public GroundSolveMethod method;
        public float confidence;
        public bool isValid;
    }

    /// <summary>
    /// Places arrows on the real ground (Systems 8 and 17).
    ///
    /// A staircase is the hardest possible case for plane detection: ARCore fits many small
    /// horizontal planes, one per tread, at different heights. Naively raycasting downward makes
    /// arrows pop between tread levels as the user walks. So this service does three things:
    ///
    ///   * prefers a real raycast hit, but falls back through progressively weaker estimates
    ///     rather than ever returning "no answer" (an arrow with no ground is worse than an
    ///     arrow on an estimated ground);
    ///   * remembers a smoothed ground offset relative to the camera, so on a staircase the
    ///     arrows follow the average slope instead of snapping to individual treads;
    ///   * estimates the slope of the detected surfaces so arrows can be pitched to lie flat
    ///     against the steps instead of standing upright on a ramp.
    /// </summary>
    public sealed class GroundPlacementService
    {
        readonly ARRaycastManager m_RaycastManager;
        readonly ARPlaneManager m_PlaneManager;
        readonly Transform m_Camera;

        readonly List<ARRaycastHit> m_Hits = new List<ARRaycastHit>(8);
        readonly AdaptiveLowPassFilter m_CameraHeightFilter = new AdaptiveLowPassFilter(0.6f, 0.01f);

        /// <summary>Typical eye height above the tread the pilgrim is standing on.</summary>
        const float k_DefaultCameraHeight = 1.5f;

        /// <summary>Horizontal radius within which a plane is considered to cover a point.</summary>
        const float k_PlaneProximityRadius = 1.2f;

        /// <summary>Vertical band searched around the expected ground height.</summary>
        const float k_VerticalSearchBand = 2.5f;

        float m_SmoothedCameraHeight = k_DefaultCameraHeight;
        bool m_HasCameraHeight;

        /// <summary>Slope of the surface the pilgrim is walking on, in degrees. 0 is flat.</summary>
        public float SurfaceSlopeDegrees { get; private set; }

        /// <summary>Averaged up-normal of nearby detected planes.</summary>
        public Vector3 SurfaceNormal { get; private set; } = Vector3.up;

        /// <summary>How far the camera currently sits above the estimated ground.</summary>
        public float CameraHeightAboveGround => m_SmoothedCameraHeight;

        public int TrackedPlaneCount => m_PlaneManager != null ? m_PlaneManager.trackables.count : 0;

        public GroundPlacementService(ARRaycastManager raycastManager, ARPlaneManager planeManager, Transform camera)
        {
            m_RaycastManager = raycastManager;
            m_PlaneManager = planeManager;
            m_Camera = camera;
        }

        /// <summary>Refreshes the camera-height estimate and surface slope. Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            if (m_Camera == null || deltaTime <= 0f)
                return;

            UpdateSurfaceOrientation();

            // Cast straight down from the camera to learn how high the device is held.
            if (m_RaycastManager != null &&
                m_RaycastManager.Raycast(new Ray(m_Camera.position, Vector3.down), m_Hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                var height = m_Camera.position.y - m_Hits[0].pose.position.y;

                if (height is > 0.6f and < 2.6f)
                {
                    m_SmoothedCameraHeight = m_CameraHeightFilter.Filter(height, deltaTime);
                    m_HasCameraHeight = true;
                }
            }
        }

        /// <summary>
        /// Resolves the ground position under <paramref name="worldPosition"/>. The XZ components
        /// are kept; only the height and normal are solved for.
        /// </summary>
        public GroundSample Solve(Vector3 worldPosition)
        {
            var sample = new GroundSample
            {
                position = worldPosition,
                normal = Vector3.up,
                method = GroundSolveMethod.None,
                isValid = false
            };

            if (m_Camera == null)
                return sample;

            var expectedGroundY = m_Camera.position.y - m_SmoothedCameraHeight;

            // 1. Cast down from well above the expected ground.
            if (m_RaycastManager != null)
            {
                var origin = new Vector3(worldPosition.x, expectedGroundY + k_VerticalSearchBand, worldPosition.z);

                if (m_RaycastManager.Raycast(new Ray(origin, Vector3.down), m_Hits,
                        TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
                {
                    var hit = ChooseBestHit(expectedGroundY);
                    sample.position = new Vector3(worldPosition.x, hit.pose.position.y, worldPosition.z);
                    sample.normal = hit.pose.up;
                    sample.method = GroundSolveMethod.PlaneRaycast;
                    sample.confidence = 1f;
                    sample.isValid = true;
                    return sample;
                }
            }

            // 2. No polygon covers the point — borrow the height of the nearest plane instead.
            if (TryPlaneProximity(worldPosition, expectedGroundY, out var proximityY, out var proximityNormal))
            {
                sample.position = new Vector3(worldPosition.x, proximityY, worldPosition.z);
                sample.normal = proximityNormal;
                sample.method = GroundSolveMethod.PlaneProximity;
                sample.confidence = 0.7f;
                sample.isValid = true;
                return sample;
            }

            // 3. Feature points are sparse but better than nothing on unstructured ground.
            if (m_RaycastManager != null)
            {
                var origin = new Vector3(worldPosition.x, expectedGroundY + k_VerticalSearchBand, worldPosition.z);

                if (m_RaycastManager.Raycast(new Ray(origin, Vector3.down), m_Hits, TrackableType.FeaturePoint))
                {
                    sample.position = new Vector3(worldPosition.x, m_Hits[0].pose.position.y, worldPosition.z);
                    sample.normal = SurfaceNormal;
                    sample.method = GroundSolveMethod.FeaturePoint;
                    sample.confidence = 0.4f;
                    sample.isValid = true;
                    return sample;
                }
            }

            // 4. Last resort: assume the ground is a fixed distance below the camera. This keeps
            //    arrows visible and roughly correct when the steps are too uniform to track.
            sample.position = new Vector3(worldPosition.x, expectedGroundY, worldPosition.z);
            sample.normal = SurfaceNormal;
            sample.method = GroundSolveMethod.CameraHeightFallback;
            sample.confidence = m_HasCameraHeight ? 0.25f : 0.1f;
            sample.isValid = true;
            return sample;
        }

        /// <summary>
        /// Picks the raycast hit closest to the expected ground height. On a staircase the ray
        /// often passes through several tread planes; the nearest-to-expected one is the tread
        /// the pilgrim is actually about to step on.
        /// </summary>
        ARRaycastHit ChooseBestHit(float expectedGroundY)
        {
            var best = m_Hits[0];
            var bestError = Mathf.Abs(best.pose.position.y - expectedGroundY);

            for (var i = 1; i < m_Hits.Count; i++)
            {
                var error = Mathf.Abs(m_Hits[i].pose.position.y - expectedGroundY);

                if (error >= bestError)
                    continue;

                bestError = error;
                best = m_Hits[i];
            }

            return best;
        }

        bool TryPlaneProximity(Vector3 worldPosition, float expectedGroundY, out float y, out Vector3 normal)
        {
            y = expectedGroundY;
            normal = SurfaceNormal;

            if (m_PlaneManager == null || m_PlaneManager.trackables.count == 0)
                return false;

            var bestScore = float.MaxValue;
            var found = false;

            foreach (var plane in m_PlaneManager.trackables)
            {
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                var centre = plane.center;
                var horizontal = new Vector2(centre.x - worldPosition.x, centre.z - worldPosition.z).magnitude;

                if (horizontal > k_PlaneProximityRadius + plane.extents.magnitude)
                    continue;

                var vertical = Mathf.Abs(centre.y - expectedGroundY);
                if (vertical > k_VerticalSearchBand)
                    continue;

                // Weight vertical agreement more heavily — the right tread matters more than the
                // nearest one in plan view.
                var score = horizontal + vertical * 2f;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                y = centre.y;
                normal = plane.normal;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Averages the normals of nearby horizontal planes to estimate the local slope. On the
        /// Alipiri steps the tread planes are individually flat, so the useful slope signal comes
        /// from how their heights change with distance, not from their normals — that is handled
        /// by the arrow manager, which pitches arrows along the line between consecutive samples.
        /// This value is used for the debug overlay and for deciding when the surface is too
        /// uneven to trust a single raycast.
        /// </summary>
        void UpdateSurfaceOrientation()
        {
            if (m_PlaneManager == null || m_PlaneManager.trackables.count == 0)
                return;

            var accumulated = Vector3.zero;
            var count = 0;

            foreach (var plane in m_PlaneManager.trackables)
            {
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                if (m_Camera != null && Vector3.Distance(plane.center, m_Camera.position) > 12f)
                    continue;

                accumulated += plane.normal;
                count++;
            }

            if (count == 0)
                return;

            SurfaceNormal = (accumulated / count).normalized;
            SurfaceSlopeDegrees = Vector3.Angle(SurfaceNormal, Vector3.up);
        }
    }
}
