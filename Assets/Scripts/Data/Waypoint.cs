using System;
using UnityEngine;

namespace TirumalaAR.Data
{
    /// <summary>
    /// One node of the Alipiri route after densification and smoothing.
    /// Positions are stored in ENU metres relative to the route origin; the AR session applies
    /// its own origin transform on top of this so re-localisation never invalidates the route.
    /// </summary>
    [Serializable]
    public sealed class Waypoint
    {
        /// <summary>Zero-based index along the ordered route.</summary>
        public int id;

        public double latitude;
        public double longitude;

        /// <summary>East/Up/North metres relative to the route origin (Unity axis convention: x=East, y=Up, z=North).</summary>
        public Vector3 enuPosition;

        /// <summary>Metres above sea level. 0 when the source data carried no elevation.</summary>
        public float elevation;

        /// <summary>Unit heading toward <see cref="nextWaypointId"/> in ENU space.</summary>
        public Vector3 direction;

        /// <summary>Compass bearing to the next waypoint, degrees clockwise from true north.</summary>
        public float bearingDegrees;

        /// <summary>Index of the next waypoint, or -1 at the destination.</summary>
        public int nextWaypointId = -1;

        /// <summary>Ground distance to the next waypoint in metres.</summary>
        public float distanceToNext;

        /// <summary>Cumulative distance from the route start in metres.</summary>
        public float cumulativeDistance;

        /// <summary>True when this waypoint came from a "highway=steps" feature rather than a footway/road.</summary>
        public bool isStairs;

        /// <summary>True when this waypoint was synthesised to bridge a gap in the source GeoJSON.</summary>
        public bool isBridged;

        public GeoCoordinate Coordinate => new GeoCoordinate(latitude, longitude, elevation);

        public override string ToString() =>
            $"WP{id} ({Coordinate}) {cumulativeDistance:F0}m{(isStairs ? " [steps]" : string.Empty)}";
    }
}
