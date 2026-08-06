using System;
using UnityEngine;

namespace TirumalaAR.Data
{
    /// <summary>WGS84 position. Altitude is metres above the ellipsoid; 0 when unknown.</summary>
    [Serializable]
    public struct GeoCoordinate : IEquatable<GeoCoordinate>
    {
        public double latitude;
        public double longitude;
        public double altitude;

        public GeoCoordinate(double latitude, double longitude, double altitude = 0d)
        {
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitude = altitude;
        }

        public bool IsValid =>
            latitude is >= -90d and <= 90d &&
            longitude is >= -180d and <= 180d &&
            !double.IsNaN(latitude) && !double.IsNaN(longitude);

        public bool Equals(GeoCoordinate other) =>
            Math.Abs(latitude - other.latitude) < 1e-9 &&
            Math.Abs(longitude - other.longitude) < 1e-9;

        public override bool Equals(object obj) => obj is GeoCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(latitude, longitude);

        public override string ToString() =>
            $"{latitude.ToString("F7", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"{longitude.ToString("F7", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
