using System;
using UnityEngine;

namespace TirumalaAR.Data
{
    /// <summary>
    /// Matches the schema of StreamingAssets/Database/landmarks.json exactly so Unity's
    /// JsonUtility can deserialise it without a custom reader.
    /// </summary>
    [Serializable]
    public sealed class LandmarkData
    {
        public int id;
        public string name;
        public string type;
        public double latitude;
        public double longitude;
        public float triggerRadius = 20f;
        public string voiceText;
        public string audio;
        public string arPrefab;
        public int priority = 1;
        public bool visited;

        // --- Populated by the route builder, not present in the source JSON ---

        /// <summary>Index of the nearest route waypoint, or -1 if the landmark is off-route.</summary>
        [NonSerialized] public int nearestWaypointId = -1;

        /// <summary>Distance along the route at which this landmark is passed, in metres.</summary>
        [NonSerialized] public float routeDistance;

        /// <summary>Perpendicular offset from the route centreline, in metres.</summary>
        [NonSerialized] public float offRouteDistance;

        /// <summary>ENU position relative to the route origin.</summary>
        [NonSerialized] public Vector3 enuPosition;

        public GeoCoordinate Coordinate => new GeoCoordinate(latitude, longitude);

        public LandmarkCategory Category => type?.Trim().ToLowerInvariant() switch
        {
            "temple" => LandmarkCategory.Temple,
            "statue" => LandmarkCategory.Statue,
            "steps" => LandmarkCategory.Steps,
            "water point" => LandmarkCategory.WaterPoint,
            "medical" => LandmarkCategory.Medical,
            "shops" => LandmarkCategory.Shops,
            _ => LandmarkCategory.Unknown
        };

        public override string ToString() => $"#{id} {name} ({type})";
    }

    public enum LandmarkCategory
    {
        Unknown,
        Temple,
        Statue,
        Steps,
        WaterPoint,
        Medical,
        Shops
    }

    /// <summary>Wrapper used by JsonUtility to read the top-level "landmarks" array.</summary>
    [Serializable]
    public sealed class LandmarkCollection
    {
        public LandmarkData[] landmarks = Array.Empty<LandmarkData>();
    }
}
