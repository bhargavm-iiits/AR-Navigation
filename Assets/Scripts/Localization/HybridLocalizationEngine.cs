using System;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.GPS;
using TirumalaAR.Navigation;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.Localization
{
    /// <summary>
    /// Hybrid localisation (System 4 + the research requirement).
    ///
    /// Five information sources are fused, each used only for what it is actually good at:
    ///
    ///   1. ARCore visual-inertial odometry — the primary motion source. Sub-centimetre over
    ///      seconds, but accumulates drift over the 7 km walk and has no global reference.
    ///   2. GPS, Kalman-filtered — the global reference. Bounds the odometry drift but is far too
    ///      noisy under the covered walkway and tree canopy to drive rendering directly.
    ///   3. Compass — initial heading, and a slow correction while the odometry yaw drifts.
    ///   4. Route snapping — a strong geometric prior. A pilgrim on the Alipiri steps is on the
    ///      steps; lateral error perpendicular to a handrailed staircase is physically bounded.
    ///   5. Landmark image re-localisation — occasional hard resets that zero the accumulated
    ///      drift at Rajagopuram, Gali Gopuram and the other recognisable structures.
    ///
    /// The output is one pose in the ENU frame, published once per frame.
    /// </summary>
    public sealed class HybridLocalizationEngine
    {
        // --- Tuning ---------------------------------------------------------------------

        /// <summary>Fraction of the GPS/AR discrepancy absorbed per second under a good fix.</summary>
        const float k_GpsCorrectionRateGood = 0.25f;
        const float k_GpsCorrectionRateAcceptable = 0.12f;
        const float k_GpsCorrectionRatePoor = 0.04f;

        /// <summary>Lateral error beyond which route snapping stops trusting the route prior —
        /// past this the pilgrim has probably genuinely left the path.</summary>
        const float k_RouteCorridorMeters = 18f;

        /// <summary>Fraction of the lateral offset removed per second by route snapping.</summary>
        const float k_RouteSnapRate = 0.35f;

        /// <summary>Compass heading correction rate. Deliberately slow: the magnetometer near the
        /// iron handrails on the Alipiri steps is unreliable, so it may nudge but never dominate.</summary>
        const float k_CompassCorrectionRate = 0.05f;

        /// <summary>A GPS fix further than this from the AR-tracked position is treated as an
        /// outlier and skipped rather than allowed to yank the frame.</summary>
        const float k_GpsOutlierRejectMeters = 45f;

        // --- Dependencies ---------------------------------------------------------------

        readonly IGpsProvider m_Gps;
        readonly IDeviceSensors m_Sensors;
        readonly IRouteQuery m_Route;
        readonly GeoAnchorFrame m_Frame;
        readonly GpsKalmanFilter m_Kalman = new GpsKalmanFilter();
        readonly MovingAverageFilter m_SpeedFilter = new MovingAverageFilter(20);

        Transform m_ArCamera;

        // --- State ----------------------------------------------------------------------

        bool m_HeadingSeeded;
        int m_LastProjectionHint = -1;
        double m_LastGpsTimestamp = -1d;
        float m_TimeSinceKalmanPredict;
        Vector3 m_PreviousEnuPosition;
        bool m_HasPreviousPosition;

        public GeoAnchorFrame Frame => m_Frame;
        public bool IsInitialized => m_Frame.IsInitialized;

        /// <summary>Fused position in ENU metres relative to the route origin.</summary>
        public Vector3 EnuPosition { get; private set; }

        /// <summary>Fused true-north heading of the direction the pilgrim is facing.</summary>
        public float HeadingDegrees { get; private set; }

        public float SpeedMetersPerSecond { get; private set; }
        public RouteProjection Projection { get; private set; }
        public RelocalizationSource LastCorrection { get; private set; }

        /// <summary>Current disagreement between the raw GPS estimate and the fused pose, in metres.
        /// This is the headline drift metric for the research write-up.</summary>
        public float GpsResidualMeters { get; private set; }

        public HybridLocalizationEngine(IGpsProvider gps, IDeviceSensors sensors, IRouteQuery route)
        {
            m_Gps = gps ?? throw new ArgumentNullException(nameof(gps));
            m_Sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
            m_Route = route ?? throw new ArgumentNullException(nameof(route));
            m_Frame = new GeoAnchorFrame();
        }

        public void AttachCamera(Transform arCamera) => m_ArCamera = arCamera;

        // -----------------------------------------------------------------------------------
        // Bootstrapping
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Establishes the AR↔ENU frame from the first usable GPS fix plus the compass. Until
        /// this succeeds the app has no idea where in the world the AR session is, so no arrows
        /// can be drawn.
        /// </summary>
        public bool TryBootstrap()
        {
            if (m_Frame.IsInitialized || m_ArCamera == null || !m_Route.IsReady)
                return false;

            if (!m_Gps.HasFix)
                return false;

            var fix = m_Gps.Latest;
            var enu = GeoMath.GeodeticToEnu(fix.coordinate, m_Route.Origin);

            // Snap the starting position onto the route. The pilgrim is standing on the steps,
            // and starting on-route avoids an ugly initial correction once walking begins.
            var projection = m_Route.ProjectOntoRoute(enu);
            var seedEnu = projection.isValid && projection.lateralOffset < 40f ? projection.position : enu;

            // Heading seed: prefer the route direction if the projection is confident, because it
            // is far more reliable than the magnetometer here. Otherwise fall back to the compass.
            float seedBearing;
            if (projection.isValid && projection.lateralOffset < 15f && m_Sensors.HeadingConfidence < 0.8f)
            {
                seedBearing = projection.bearing;
            }
            else if (m_Sensors.CompassAvailable && m_Sensors.HeadingConfidence > 0.3f)
            {
                seedBearing = m_Sensors.HeadingDegrees;
            }
            else if (projection.isValid)
            {
                seedBearing = projection.bearing;
            }
            else
            {
                return false;
            }

            // Solve for the frame yaw that makes the camera's current forward read as seedBearing.
            var cameraForward = Flatten(m_ArCamera.forward);
            var cameraYaw = Mathf.Atan2(cameraForward.x, cameraForward.z) * Mathf.Rad2Deg;

            m_Frame.Initialize(m_Route.Origin, m_ArCamera.position, seedEnu, seedBearing - cameraYaw);

            m_Kalman.Reset(seedEnu.x, seedEnu.z, fix.horizontalAccuracy);
            m_LastGpsTimestamp = fix.timestamp;
            m_HeadingSeeded = true;
            m_LastProjectionHint = projection.isValid ? projection.waypointId : -1;

            EnuPosition = seedEnu;
            HeadingDegrees = seedBearing;
            m_PreviousEnuPosition = seedEnu;
            m_HasPreviousPosition = true;

            Debug.Log($"[Localization] Frame initialised at {GeoMath.EnuToGeodetic(seedEnu, m_Route.Origin)} " +
                      $"heading {seedBearing:F0}° (fix accuracy {fix.horizontalAccuracy:F0} m).");

            EventBus.Publish(new RelocalizedEvent
            {
                source = RelocalizationSource.Gps,
                landmarkName = "Initial fix",
                correctionMagnitude = 0f,
                headingCorrectionDegrees = 0f
            });

            return true;
        }

        // -----------------------------------------------------------------------------------
        // Per-frame fusion
        // -----------------------------------------------------------------------------------

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            if (!m_Frame.IsInitialized)
            {
                TryBootstrap();
                return;
            }

            if (m_ArCamera == null)
                return;

            LastCorrection = RelocalizationSource.None;

            // 1. Odometry: the AR camera position is the trusted short-term motion source.
            EnuPosition = m_Frame.UnityToEnu(m_ArCamera.position);
            HeadingDegrees = m_Frame.UnityDirectionToBearing(Flatten(m_ArCamera.forward));

            // 2. GPS, folded into the frame rather than the pose.
            IntegrateGps(deltaTime);

            // 3. Geometric route prior.
            ApplyRouteSnapping(deltaTime);

            // 4. Slow magnetometer correction.
            ApplyCompassCorrection(deltaTime);

            // Recompute after corrections so consumers see the post-correction pose.
            EnuPosition = m_Frame.UnityToEnu(m_ArCamera.position);
            HeadingDegrees = m_Frame.UnityDirectionToBearing(Flatten(m_ArCamera.forward));

            UpdateSpeed(deltaTime);
            UpdateProjection();
            PublishPose();
        }

        void IntegrateGps(float deltaTime)
        {
            m_TimeSinceKalmanPredict += deltaTime;

            if (m_Kalman.IsInitialized && m_TimeSinceKalmanPredict > 0.2f)
            {
                m_Kalman.Predict(m_TimeSinceKalmanPredict);
                m_TimeSinceKalmanPredict = 0f;
            }

            if (!m_Gps.HasFix)
                return;

            var fix = m_Gps.Latest;

            if (Math.Abs(fix.timestamp - m_LastGpsTimestamp) < 1e-6)
                return;

            m_LastGpsTimestamp = fix.timestamp;

            var measured = GeoMath.GeodeticToEnu(fix.coordinate, m_Route.Origin);

            // Outlier gate. A fix that disagrees with visual odometry by more than the reject
            // threshold is a multipath reflection off the hillside, not a real jump.
            var residual = Vector2.Distance(
                new Vector2(measured.x, measured.z),
                new Vector2(EnuPosition.x, EnuPosition.z));

            GpsResidualMeters = residual;

            if (residual > k_GpsOutlierRejectMeters)
            {
                Debug.Log($"[Localization] Rejected GPS outlier {residual:F0} m from the tracked pose.");
                return;
            }

            m_Kalman.Update(measured.x, measured.z, fix.horizontalAccuracy);

            var target = new Vector3(m_Kalman.Position.x, EnuPosition.y, m_Kalman.Position.y);

            var rate = m_Gps.Health switch
            {
                GpsHealth.Good => k_GpsCorrectionRateGood,
                GpsHealth.Acceptable => k_GpsCorrectionRateAcceptable,
                _ => k_GpsCorrectionRatePoor
            };

            m_Frame.CorrectPosition(m_ArCamera.position, target, rate * deltaTime);
            LastCorrection = RelocalizationSource.Gps;
        }

        void ApplyRouteSnapping(float deltaTime)
        {
            var projection = m_Route.ProjectOntoRoute(EnuPosition, m_LastProjectionHint);

            if (!projection.isValid || projection.lateralOffset > k_RouteCorridorMeters)
                return;

            // Pull only perpendicular to the route. Correcting along the route would fight the
            // odometry and make the pilgrim's reported progress rubber-band.
            var routeDirection = GeoMath.BearingToEnuDirection(projection.bearing);
            var toCentre = projection.position - EnuPosition;
            toCentre.y = 0f;

            var lateral = toCentre - Vector3.Project(toCentre, routeDirection);
            var target = EnuPosition + lateral;

            m_Frame.CorrectPosition(m_ArCamera.position, target, k_RouteSnapRate * deltaTime);

            if (LastCorrection == RelocalizationSource.None)
                LastCorrection = RelocalizationSource.RouteSnap;
        }

        void ApplyCompassCorrection(float deltaTime)
        {
            if (!m_HeadingSeeded || !m_Sensors.CompassAvailable)
                return;

            // Only trust the magnetometer when successive readings agree with each other.
            if (m_Sensors.HeadingConfidence < 0.75f)
                return;

            var cameraForward = Flatten(m_ArCamera.forward);
            if (cameraForward.sqrMagnitude < 1e-6f)
                return;

            var error = Mathf.Abs(GeoMath.DeltaAngle(
                m_Frame.UnityDirectionToBearing(cameraForward), m_Sensors.HeadingDegrees));

            // A large disagreement means the magnetometer is being distorted by the handrails,
            // not that the AR session has drifted 90° in a second. Ignore it.
            if (error > 40f)
                return;

            m_Frame.CorrectHeading(cameraForward, m_Sensors.HeadingDegrees,
                m_ArCamera.position, k_CompassCorrectionRate * deltaTime);
        }

        void UpdateSpeed(float deltaTime)
        {
            if (!m_HasPreviousPosition)
            {
                m_PreviousEnuPosition = EnuPosition;
                m_HasPreviousPosition = true;
                return;
            }

            var delta = EnuPosition - m_PreviousEnuPosition;
            delta.y = 0f;

            var instantaneous = delta.magnitude / deltaTime;

            // Clamp to a plausible walking envelope; anything higher is a correction artefact,
            // not the pilgrim sprinting up the steps.
            SpeedMetersPerSecond = m_SpeedFilter.Push(Mathf.Clamp(instantaneous, 0f, 3f));
            m_PreviousEnuPosition = EnuPosition;
        }

        void UpdateProjection()
        {
            var projection = m_Route.ProjectOntoRoute(EnuPosition, m_LastProjectionHint);

            if (projection.isValid)
                m_LastProjectionHint = projection.waypointId;

            Projection = projection;
        }

        void PublishPose()
        {
            EventBus.Publish(new UserPoseUpdatedEvent
            {
                enuPosition = EnuPosition,
                headingDegrees = HeadingDegrees,
                speedMetersPerSecond = SpeedMetersPerSecond,
                horizontalAccuracy = m_Gps.HasFix ? m_Gps.Latest.horizontalAccuracy : -1f,
                gpsHealth = m_Gps.Health,
                lastCorrection = LastCorrection
            });
        }

        // -----------------------------------------------------------------------------------
        // Landmark re-localisation
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Called by <c>ReferenceImageRelocalizer</c> when a known structure is recognised. The
        /// landmark's surveyed coordinate is authoritative, so the accumulated odometry drift is
        /// removed in one step. This is what keeps error bounded over a 7 km walk instead of
        /// growing without limit.
        /// </summary>
        public void RelocalizeFromLandmark(LandmarkData landmark, Vector3 observedUnityPosition, float confidence)
        {
            if (!m_Frame.IsInitialized || landmark == null)
                return;

            var landmarkEnu = GeoMath.GeodeticToEnu(landmark.Coordinate, m_Route.Origin);

            // The pilgrim is standing near the landmark, not inside it. Place them at the closest
            // point on the route to the landmark, which is where they must physically be.
            var projection = m_Route.ProjectOntoRoute(landmarkEnu);
            var targetEnu = projection.isValid ? projection.position : landmarkEnu;

            var beforeEnu = m_Frame.UnityToEnu(observedUnityPosition);
            var correction = Vector2.Distance(
                new Vector2(beforeEnu.x, beforeEnu.z),
                new Vector2(targetEnu.x, targetEnu.z));

            // A tracked image gives a reliable position but its own yaw is only as good as the
            // image pose estimate, so heading is corrected toward the route bearing rather than
            // the image normal.
            var weight = Mathf.Clamp01(confidence);
            m_Frame.CorrectPosition(observedUnityPosition, targetEnu, weight);

            if (projection.isValid && m_ArCamera != null)
            {
                m_Frame.CorrectHeading(Flatten(m_ArCamera.forward), projection.bearing,
                    m_ArCamera.position, weight * 0.5f);
            }

            m_Kalman.ApplyCorrection(targetEnu.x, targetEnu.z, 3.0);
            m_LastProjectionHint = projection.isValid ? projection.waypointId : m_LastProjectionHint;
            LastCorrection = RelocalizationSource.TrackedImage;

            Debug.Log($"[Localization] Re-localised on '{landmark.name}': removed {correction:F1} m of drift.");

            EventBus.Publish(new RelocalizedEvent
            {
                source = RelocalizationSource.TrackedImage,
                landmarkName = landmark.name,
                correctionMagnitude = correction,
                headingCorrectionDegrees = projection.isValid
                    ? GeoMath.DeltaAngle(HeadingDegrees, projection.bearing)
                    : 0f
            });
        }

        /// <summary>
        /// Recovery path used after AR tracking is lost and regained: the odometry origin has
        /// shifted, so the frame is re-derived from the current GPS estimate.
        /// </summary>
        public void ForceReseed()
        {
            m_Frame.Reset();
            m_HeadingSeeded = false;
            m_HasPreviousPosition = false;
            m_LastProjectionHint = -1;
            m_SpeedFilter.Reset();
            TryBootstrap();
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized;
        }
    }
}
