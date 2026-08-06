using System;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.GPS;
using TirumalaAR.Localization;
using UnityEngine;

namespace TirumalaAR.Navigation
{
    /// <summary>
    /// Tracks how far along the route the pilgrim is (System 13) and publishes a progress event
    /// once per second.
    ///
    /// Completed distance is monotonic on purpose. The projected distance can tick backwards when
    /// the localisation frame is corrected, and a progress bar that goes backwards — or an ETA
    /// that jumps up — reads as a bug to the user even when the underlying estimate improved.
    /// </summary>
    public sealed class RouteProgressTracker
    {
        readonly IRouteQuery m_Route;
        readonly HybridLocalizationEngine m_Localization;
        readonly IDeviceSensors m_Sensors;

        /// <summary>Fallback pace when no speed has been measured yet: a steady pilgrim climb.</summary>
        const float k_DefaultSpeedMps = 0.55f;

        /// <summary>Progress is only rolled back if the correction is larger than this — small
        /// jitter is ignored, a genuine re-localisation is honoured.</summary>
        const float k_RollbackThreshold = 25f;

        float m_PublishTimer;
        float m_MaxDistanceReached;
        int m_VisitedLandmarks;
        int m_StepsAtStart;
        bool m_Started;

        public float CompletedDistance { get; private set; }
        public float RemainingDistance { get; private set; }
        public float ProgressNormalized { get; private set; }
        public float EstimatedSecondsRemaining { get; private set; }
        public int StepCount { get; private set; }
        public DateTime StartedUtc { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public bool HasArrived { get; private set; }

        /// <summary>Distance from the destination at which arrival is declared.</summary>
        public float ArrivalRadius { get; set; } = 25f;

        public RouteProgressTracker(IRouteQuery route, HybridLocalizationEngine localization, IDeviceSensors sensors)
        {
            m_Route = route;
            m_Localization = localization;
            m_Sensors = sensors;
        }

        public void Begin()
        {
            StartedUtc = DateTime.UtcNow;
            ElapsedSeconds = 0f;
            m_MaxDistanceReached = 0f;
            m_VisitedLandmarks = 0;
            m_StepsAtStart = m_Sensors?.StepCount ?? 0;
            HasArrived = false;
            m_Started = true;
        }

        public void RegisterLandmarkVisit() => m_VisitedLandmarks++;

        public void Tick(float deltaTime)
        {
            if (!m_Started || m_Route == null || !m_Route.IsReady)
                return;

            ElapsedSeconds += deltaTime;

            var projection = m_Localization.Projection;

            if (projection.isValid)
            {
                var raw = projection.distanceAlongRoute;

                // Accept a large backward jump (a real re-localisation), ignore small ones.
                if (raw > m_MaxDistanceReached || m_MaxDistanceReached - raw > k_RollbackThreshold)
                    m_MaxDistanceReached = raw;
            }

            CompletedDistance = Mathf.Clamp(m_MaxDistanceReached, 0f, m_Route.TotalDistance);
            RemainingDistance = Mathf.Max(0f, m_Route.TotalDistance - CompletedDistance);
            ProgressNormalized = m_Route.TotalDistance > 0f ? CompletedDistance / m_Route.TotalDistance : 0f;

            StepCount = Mathf.Max(0, (m_Sensors?.StepCount ?? 0) - m_StepsAtStart);

            // Blend measured pace with the session average so the ETA does not swing wildly every
            // time the pilgrim pauses at a mandapam.
            var instantaneous = m_Localization.SpeedMetersPerSecond;
            var average = ElapsedSeconds > 30f ? CompletedDistance / ElapsedSeconds : 0f;
            var pace = Mathf.Max(0.15f, Mathf.Lerp(average > 0f ? average : k_DefaultSpeedMps,
                instantaneous > 0.1f ? instantaneous : k_DefaultSpeedMps, 0.35f));

            EstimatedSecondsRemaining = RemainingDistance / pace;

            CheckArrival();

            m_PublishTimer += deltaTime;

            if (m_PublishTimer < 1f)
                return;

            m_PublishTimer = 0f;
            Publish(projection);
        }

        void CheckArrival()
        {
            if (HasArrived || RemainingDistance > ArrivalRadius)
                return;

            HasArrived = true;

            EventBus.Publish(new DestinationReachedEvent
            {
                totalDistance = CompletedDistance,
                totalSeconds = ElapsedSeconds
            });
        }

        void Publish(RouteProjection projection)
        {
            EventBus.Publish(new RouteProgressEvent
            {
                currentWaypointId = projection.isValid ? projection.waypointId : -1,
                completedDistance = CompletedDistance,
                remainingDistance = RemainingDistance,
                progressNormalized = ProgressNormalized,
                estimatedSecondsRemaining = EstimatedSecondsRemaining,
                currentSpeed = m_Localization.SpeedMetersPerSecond,
                estimatedSteps = StepCount,
                visitedLandmarkCount = m_VisitedLandmarks
            });
        }

        public NavigationSessionRecordSnapshot Snapshot(bool completed) => new NavigationSessionRecordSnapshot
        {
            distanceCovered = CompletedDistance,
            durationSeconds = ElapsedSeconds,
            landmarksVisited = m_VisitedLandmarks,
            completed = completed
        };

        public struct NavigationSessionRecordSnapshot
        {
            public float distanceCovered;
            public float durationSeconds;
            public int landmarksVisited;
            public bool completed;
        }
    }
}
