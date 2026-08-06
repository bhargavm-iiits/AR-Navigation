using System;
using System.Collections.Generic;
using TirumalaAR.Data;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.GeoJson
{
    /// <summary>Result of turning a GeoJSON extract into an ordered, walkable route.</summary>
    public sealed class RouteBuildResult
    {
        public GeoCoordinate origin;
        public readonly List<Waypoint> waypoints = new List<Waypoint>();
        public readonly List<string> warnings = new List<string>();

        /// <summary>Straight-line connectors inserted where the source data had no geometry.</summary>
        public readonly List<(GeoCoordinate from, GeoCoordinate to, float meters)> bridgedGaps =
            new List<(GeoCoordinate, GeoCoordinate, float)>();

        public float TotalDistance => waypoints.Count == 0 ? 0f : waypoints[waypoints.Count - 1].cumulativeDistance;
        public bool IsUsable => waypoints.Count >= 2;
    }

    [Serializable]
    public sealed class RouteBuildSettings
    {
        [Tooltip("Spacing between generated waypoints along the route, in metres.")]
        public float waypointSpacing = 3f;

        [Tooltip("Catmull-Rom samples generated between each pair of source vertices.")]
        public int smoothingSegments = 8;

        [Tooltip("Two way endpoints closer than this are treated as the same junction.")]
        public float junctionTolerance = 12f;

        [Tooltip("Largest hole in the source data that will be bridged with a straight connector.")]
        public float maxBridgeDistance = 2000f;

        [Tooltip("Only features tagged highway=steps/footway/path are considered part of the route.")]
        public bool requireWalkableTag = true;
    }

    /// <summary>
    /// Turns the OpenStreetMap extract into one continuous ordered route.
    ///
    /// This is not a trivial concatenation. The Alipiri extract is six separate LineString
    /// features that are not in walking order, some of which are digitised in the opposite
    /// direction, plus one footway spur that is not part of the pilgrim route at all. The builder
    /// therefore chains features by endpoint proximity (reversing them when needed), rejects
    /// anything too far away to belong, and records any remaining hole as an explicit bridged gap
    /// rather than silently pretending the route is continuous.
    /// </summary>
    public static class RouteBuilder
    {
        public static RouteBuildResult Build(GeoJsonDocument document, GeoCoordinate startSeed,
            RouteBuildSettings settings)
        {
            settings ??= new RouteBuildSettings();
            var result = new RouteBuildResult();
            result.warnings.AddRange(document.warnings);

            var candidates = new List<GeoFeature>();
            foreach (var line in document.lines)
            {
                if (settings.requireWalkableTag && !line.IsWalkable)
                {
                    result.warnings.Add($"Feature '{line.id}' is not tagged as walkable and was excluded.");
                    continue;
                }

                candidates.Add(line);
            }

            if (candidates.Count == 0)
            {
                result.warnings.Add("No walkable line features were found — the route cannot be built.");
                return result;
            }

            var ordered = ChainFeatures(candidates, startSeed, settings, result);
            if (ordered.Count == 0)
            {
                result.warnings.Add("Route chaining produced no features.");
                return result;
            }

            // The route origin is the very first coordinate; every ENU position is relative to it.
            result.origin = ordered[0].coordinates[0];

            BuildWaypoints(ordered, settings, result);
            return result;
        }

        // -----------------------------------------------------------------------------------
        // Feature chaining
        // -----------------------------------------------------------------------------------

        static List<GeoFeature> ChainFeatures(List<GeoFeature> candidates, GeoCoordinate startSeed,
            RouteBuildSettings settings, RouteBuildResult result)
        {
            var remaining = new List<GeoFeature>(candidates);
            var ordered = new List<GeoFeature>();

            // Pick the feature whose endpoint sits closest to the declared start of the walk.
            var seedIndex = 0;
            var seedDistance = double.MaxValue;
            var seedNeedsReverse = false;

            for (var i = 0; i < remaining.Count; i++)
            {
                var toFirst = GeoMath.HaversineDistance(startSeed, remaining[i].First);
                var toLast = GeoMath.HaversineDistance(startSeed, remaining[i].Last);

                if (toFirst < seedDistance)
                {
                    seedDistance = toFirst;
                    seedIndex = i;
                    seedNeedsReverse = false;
                }

                if (toLast < seedDistance)
                {
                    seedDistance = toLast;
                    seedIndex = i;
                    seedNeedsReverse = true;
                }
            }

            var seed = remaining[seedIndex];
            remaining.RemoveAt(seedIndex);
            if (seedNeedsReverse)
                seed.Reverse();

            ordered.Add(seed);
            result.warnings.Add(
                $"Route start anchored on '{seed.id}', {seedDistance:F0} m from the supplied start coordinate.");

            // Greedily append whichever remaining feature starts nearest the current route end.
            while (remaining.Count > 0)
            {
                var tail = ordered[ordered.Count - 1].Last;

                var bestIndex = -1;
                var bestDistance = double.MaxValue;
                var bestNeedsReverse = false;

                for (var i = 0; i < remaining.Count; i++)
                {
                    var toFirst = GeoMath.HaversineDistance(tail, remaining[i].First);
                    var toLast = GeoMath.HaversineDistance(tail, remaining[i].Last);

                    if (toFirst < bestDistance)
                    {
                        bestDistance = toFirst;
                        bestIndex = i;
                        bestNeedsReverse = false;
                    }

                    if (toLast < bestDistance)
                    {
                        bestDistance = toLast;
                        bestIndex = i;
                        bestNeedsReverse = true;
                    }
                }

                if (bestIndex < 0 || bestDistance > settings.maxBridgeDistance)
                {
                    foreach (var orphan in remaining)
                    {
                        result.warnings.Add(
                            $"Feature '{orphan.id}' is {bestDistance:F0} m from the route and was treated as a " +
                            "branch, not part of the main path.");
                    }
                    break;
                }

                var next = remaining[bestIndex];
                remaining.RemoveAt(bestIndex);
                if (bestNeedsReverse)
                    next.Reverse();

                if (bestDistance > settings.junctionTolerance)
                {
                    result.bridgedGaps.Add((tail, next.First, (float)bestDistance));
                    result.warnings.Add(
                        $"Gap of {bestDistance:F0} m between '{ordered[ordered.Count - 1].id}' and '{next.id}' " +
                        "was bridged with a straight connector — the source data has no geometry there.");
                }

                ordered.Add(next);
            }

            return ordered;
        }

        // -----------------------------------------------------------------------------------
        // Waypoint generation
        // -----------------------------------------------------------------------------------

        static void BuildWaypoints(List<GeoFeature> ordered, RouteBuildSettings settings, RouteBuildResult result)
        {
            // Flatten to ENU, remembering which samples came from a staircase feature so the AR
            // layer can adapt arrow pitch and spacing on steps.
            var rawPoints = new List<Vector3>();
            var rawIsStairs = new List<bool>();

            for (var f = 0; f < ordered.Count; f++)
            {
                var feature = ordered[f];

                for (var i = 0; i < feature.coordinates.Count; i++)
                {
                    // Skip a vertex that duplicates the previous feature's endpoint.
                    if (i == 0 && rawPoints.Count > 0)
                    {
                        var previous = rawPoints[rawPoints.Count - 1];
                        var candidate = GeoMath.GeodeticToEnu(feature.coordinates[0], result.origin);
                        if (Vector3.Distance(previous, candidate) < 0.5f)
                            continue;
                    }

                    rawPoints.Add(GeoMath.GeodeticToEnu(feature.coordinates[i], result.origin));
                    rawIsStairs.Add(feature.IsStairs);
                }
            }

            if (rawPoints.Count < 2)
            {
                result.warnings.Add("Fewer than two usable route points survived conversion.");
                return;
            }

            // Smooth, then resample at a fixed spacing so downstream code can assume uniform steps.
            var smoothed = PolylineUtility.Smooth(rawPoints, settings.smoothingSegments);
            var resampled = PolylineUtility.ResampleByDistance(smoothed, settings.waypointSpacing);

            // Map each resampled point back to the nearest raw vertex to inherit its stairs flag
            // and its elevation.
            for (var i = 0; i < resampled.Count; i++)
            {
                var position = resampled[i];
                var nearestRaw = NearestRawIndex(rawPoints, position);

                var geodetic = GeoMath.EnuToGeodetic(position, result.origin);

                result.waypoints.Add(new Waypoint
                {
                    id = i,
                    latitude = geodetic.latitude,
                    longitude = geodetic.longitude,
                    enuPosition = position,
                    elevation = (float)geodetic.altitude,
                    isStairs = rawIsStairs[nearestRaw]
                });
            }

            FlagBridgedWaypoints(result);
            LinkWaypoints(result.waypoints);
        }

        static int NearestRawIndex(List<Vector3> rawPoints, Vector3 position)
        {
            var bestIndex = 0;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < rawPoints.Count; i++)
            {
                var distance = (rawPoints[i] - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Marks waypoints that fall inside a bridged gap so the UI can warn the pilgrim that the
        /// guidance there is interpolated rather than surveyed.
        /// </summary>
        static void FlagBridgedWaypoints(RouteBuildResult result)
        {
            foreach (var (from, to, _) in result.bridgedGaps)
            {
                var a = GeoMath.GeodeticToEnu(from, result.origin);
                var b = GeoMath.GeodeticToEnu(to, result.origin);

                foreach (var waypoint in result.waypoints)
                {
                    var projected = PolylineUtility.ClosestPointOnSegment(a, b, waypoint.enuPosition, out var t);
                    if (t is > 0.001f and < 0.999f && Vector3.Distance(projected, waypoint.enuPosition) < 5f)
                        waypoint.isBridged = true;
                }
            }
        }

        /// <summary>Fills in direction, bearing, next-waypoint links and cumulative distance.</summary>
        public static void LinkWaypoints(List<Waypoint> waypoints)
        {
            if (waypoints.Count == 0)
                return;

            var cumulative = 0f;

            for (var i = 0; i < waypoints.Count; i++)
            {
                var current = waypoints[i];
                current.id = i;
                current.cumulativeDistance = cumulative;

                if (i < waypoints.Count - 1)
                {
                    var next = waypoints[i + 1];
                    var delta = next.enuPosition - current.enuPosition;

                    current.nextWaypointId = i + 1;
                    current.distanceToNext = delta.magnitude;
                    current.direction = delta.sqrMagnitude > 1e-8f ? delta.normalized : Vector3.forward;
                    current.bearingDegrees = GeoMath.EnuDirectionToBearing(current.direction);

                    cumulative += current.distanceToNext;
                }
                else
                {
                    // The destination inherits the previous heading so arrows do not spin at the end.
                    current.nextWaypointId = -1;
                    current.distanceToNext = 0f;
                    current.direction = i > 0 ? waypoints[i - 1].direction : Vector3.forward;
                    current.bearingDegrees = i > 0 ? waypoints[i - 1].bearingDegrees : 0f;
                }
            }
        }
    }
}
