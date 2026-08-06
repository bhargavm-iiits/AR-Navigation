using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Localization;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.Navigation
{
    /// <summary>
    /// Derives turn-by-turn instructions from the route geometry.
    ///
    /// The Alipiri route is mostly a continuous climb, so the useful signal is not every small
    /// bend but the few places where the path genuinely changes direction. Bearing change is
    /// therefore measured across a lookahead window rather than between adjacent waypoints —
    /// adjacent waypoints are 3 m apart and their bearings are dominated by smoothing noise.
    /// </summary>
    public sealed class TurnInstructionService
    {
        readonly IRouteQuery m_Route;
        readonly HybridLocalizationEngine m_Localization;

        /// <summary>Distance over which the bearing change is measured.</summary>
        const float k_TurnWindowMeters = 22f;

        /// <summary>How far ahead a turn is announced.</summary>
        const float k_AnnounceDistance = 18f;

        /// <summary>Minimum gap between two announcements of the same kind.</summary>
        const float k_RepeatCooldownSeconds = 25f;

        /// <summary>Reassurance interval on long straight stretches.</summary>
        const float k_StraightReminderSeconds = 90f;

        TurnDirection m_LastAnnounced = TurnDirection.Straight;
        float m_LastAnnounceTime = -999f;
        float m_LastStraightTime;

        public TurnDirection CurrentTurn { get; private set; } = TurnDirection.Straight;
        public float DistanceToTurn { get; private set; }

        public TurnInstructionService(IRouteQuery route, HybridLocalizationEngine localization)
        {
            m_Route = route;
            m_Localization = localization;
            m_LastStraightTime = Time.time;
        }

        public void Tick()
        {
            if (m_Route == null || !m_Route.IsReady)
                return;

            var projection = m_Localization.Projection;

            if (!projection.isValid)
                return;

            var here = projection.distanceAlongRoute;

            if (m_Route.TotalDistance - here < 30f)
            {
                Announce(TurnDirection.Arrive, m_Route.TotalDistance - here, "You are arriving at the destination.");
                return;
            }

            // Compare the bearing now with the bearing one window further along the route.
            var bearingNow = BearingAt(here);
            var bearingAhead = BearingAt(here + k_TurnWindowMeters);
            var delta = GeoMath.DeltaAngle(bearingNow, bearingAhead);

            var direction = Classify(delta);
            CurrentTurn = direction;
            DistanceToTurn = k_TurnWindowMeters;

            if (direction == TurnDirection.Straight)
            {
                MaybeReassure();
                return;
            }

            if (DistanceToTurn > k_AnnounceDistance)
                return;

            Announce(direction, DistanceToTurn, BuildTurnText(direction, DistanceToTurn));
        }

        float BearingAt(float distance)
        {
            if (m_Route is not NavigationGraph graph)
                return 0f;

            var clamped = Mathf.Clamp(distance, 0f, m_Route.TotalDistance);
            var index = graph.FindWaypointAtDistance(clamped);
            return m_Route.Waypoints[index].bearingDegrees;
        }

        static TurnDirection Classify(float deltaDegrees) => deltaDegrees switch
        {
            > 100f => TurnDirection.SharpRight,
            > 45f => TurnDirection.Right,
            > 22f => TurnDirection.SlightRight,
            < -100f => TurnDirection.SharpLeft,
            < -45f => TurnDirection.Left,
            < -22f => TurnDirection.SlightLeft,
            _ => TurnDirection.Straight
        };

        static string BuildTurnText(TurnDirection direction, float distance)
        {
            var lead = distance > 8f ? $"In {Mathf.RoundToInt(distance / 5f) * 5} metres, " : string.Empty;

            var action = direction switch
            {
                TurnDirection.SlightLeft => "bear left",
                TurnDirection.Left => "turn left",
                TurnDirection.SharpLeft => "turn sharply left",
                TurnDirection.SlightRight => "bear right",
                TurnDirection.Right => "turn right",
                TurnDirection.SharpRight => "turn sharply right",
                TurnDirection.Arrive => "you have arrived",
                _ => "continue straight"
            };

            return string.IsNullOrEmpty(lead)
                ? char.ToUpperInvariant(action[0]) + action[1..] + "."
                : lead + action + ".";
        }

        void MaybeReassure()
        {
            if (Time.time - m_LastStraightTime < k_StraightReminderSeconds)
                return;

            m_LastStraightTime = Time.time;
            Announce(TurnDirection.Straight, 0f, "Continue walking straight along the steps.");
        }

        void Announce(TurnDirection direction, float distance, string text)
        {
            // Never repeat the same instruction back to back within the cooldown.
            if (direction == m_LastAnnounced && Time.time - m_LastAnnounceTime < k_RepeatCooldownSeconds)
                return;

            m_LastAnnounced = direction;
            m_LastAnnounceTime = Time.time;
            m_LastStraightTime = Time.time;

            EventBus.Publish(new TurnInstructionEvent
            {
                direction = direction,
                distanceToTurn = distance,
                spokenText = text
            });
        }
    }
}
