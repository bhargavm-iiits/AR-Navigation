using System;
using UnityEngine;
using TirumalaAR.Data;

namespace TirumalaAR.Utilities
{
    /// <summary>
    /// WGS84 geodesy. The app converts every latitude/longitude to a local East-North-Up frame
    /// anchored at the route origin (the Alipiri start point), because AR Foundation works in
    /// flat metres and the whole Alipiri route spans only ~6 km — well inside the range where a
    /// single tangent-plane projection stays sub-centimetre accurate.
    ///
    /// Unity axis mapping used throughout the project: x = East, y = Up, z = North.
    /// </summary>
    public static class GeoMath
    {
        public const double EarthSemiMajorAxis = 6378137.0;              // a, metres
        public const double EarthFlattening = 1.0 / 298.257223563;       // f
        const double k_E2 = EarthFlattening * (2.0 - EarthFlattening);   // first eccentricity squared
        const double k_Deg2Rad = Math.PI / 180.0;
        const double k_Rad2Deg = 180.0 / Math.PI;

        /// <summary>Mean earth radius used by the haversine helpers.</summary>
        public const double MeanEarthRadius = 6371008.8;

        // -----------------------------------------------------------------------------------
        // ECEF / ENU
        // -----------------------------------------------------------------------------------

        public static void GeodeticToEcef(double latDeg, double lonDeg, double altitude,
            out double x, out double y, out double z)
        {
            var lat = latDeg * k_Deg2Rad;
            var lon = lonDeg * k_Deg2Rad;
            var sinLat = Math.Sin(lat);
            var cosLat = Math.Cos(lat);
            var sinLon = Math.Sin(lon);
            var cosLon = Math.Cos(lon);

            // Radius of curvature in the prime vertical.
            var n = EarthSemiMajorAxis / Math.Sqrt(1.0 - k_E2 * sinLat * sinLat);

            x = (n + altitude) * cosLat * cosLon;
            y = (n + altitude) * cosLat * sinLon;
            z = (n * (1.0 - k_E2) + altitude) * sinLat;
        }

        /// <summary>
        /// Converts a geodetic coordinate to East/North/Up metres relative to <paramref name="origin"/>.
        /// Returned as a Unity vector with x=East, y=Up, z=North.
        /// </summary>
        public static Vector3 GeodeticToEnu(GeoCoordinate coordinate, GeoCoordinate origin)
        {
            GeodeticToEnuDouble(coordinate, origin, out var east, out var north, out var up);
            return new Vector3((float)east, (float)up, (float)north);
        }

        public static void GeodeticToEnuDouble(GeoCoordinate coordinate, GeoCoordinate origin,
            out double east, out double north, out double up)
        {
            GeodeticToEcef(coordinate.latitude, coordinate.longitude, coordinate.altitude,
                out var x, out var y, out var z);
            GeodeticToEcef(origin.latitude, origin.longitude, origin.altitude,
                out var x0, out var y0, out var z0);

            var dx = x - x0;
            var dy = y - y0;
            var dz = z - z0;

            var lat0 = origin.latitude * k_Deg2Rad;
            var lon0 = origin.longitude * k_Deg2Rad;
            var sinLat = Math.Sin(lat0);
            var cosLat = Math.Cos(lat0);
            var sinLon = Math.Sin(lon0);
            var cosLon = Math.Cos(lon0);

            east = -sinLon * dx + cosLon * dy;
            north = -sinLat * cosLon * dx - sinLat * sinLon * dy + cosLat * dz;
            up = cosLat * cosLon * dx + cosLat * sinLon * dy + sinLat * dz;
        }

        /// <summary>Inverse of <see cref="GeodeticToEnu"/>. Used to report the user's fused position as lat/lon.</summary>
        public static GeoCoordinate EnuToGeodetic(Vector3 enu, GeoCoordinate origin)
        {
            double east = enu.x, up = enu.y, north = enu.z;

            var lat0 = origin.latitude * k_Deg2Rad;
            var lon0 = origin.longitude * k_Deg2Rad;
            var sinLat = Math.Sin(lat0);
            var cosLat = Math.Cos(lat0);
            var sinLon = Math.Sin(lon0);
            var cosLon = Math.Cos(lon0);

            GeodeticToEcef(origin.latitude, origin.longitude, origin.altitude,
                out var x0, out var y0, out var z0);

            var x = x0 + (-sinLon * east - sinLat * cosLon * north + cosLat * cosLon * up);
            var y = y0 + (cosLon * east - sinLat * sinLon * north + cosLat * sinLon * up);
            var z = z0 + (cosLat * north + sinLat * up);

            EcefToGeodetic(x, y, z, out var lat, out var lon, out var alt);
            return new GeoCoordinate(lat, lon, alt);
        }

        /// <summary>Bowring's method — converges to millimetre accuracy without iteration for terrestrial altitudes.</summary>
        public static void EcefToGeodetic(double x, double y, double z,
            out double latDeg, out double lonDeg, out double altitude)
        {
            var b = EarthSemiMajorAxis * (1.0 - EarthFlattening);
            var ep2 = (EarthSemiMajorAxis * EarthSemiMajorAxis - b * b) / (b * b);
            var p = Math.Sqrt(x * x + y * y);

            if (p < 1e-9)
            {
                latDeg = z >= 0 ? 90.0 : -90.0;
                lonDeg = 0.0;
                altitude = Math.Abs(z) - b;
                return;
            }

            var theta = Math.Atan2(z * EarthSemiMajorAxis, p * b);
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);

            var lat = Math.Atan2(z + ep2 * b * sinTheta * sinTheta * sinTheta,
                                 p - k_E2 * EarthSemiMajorAxis * cosTheta * cosTheta * cosTheta);
            var lon = Math.Atan2(y, x);
            var n = EarthSemiMajorAxis / Math.Sqrt(1.0 - k_E2 * Math.Sin(lat) * Math.Sin(lat));

            latDeg = lat * k_Rad2Deg;
            lonDeg = lon * k_Rad2Deg;
            altitude = p / Math.Cos(lat) - n;
        }

        // -----------------------------------------------------------------------------------
        // Great-circle helpers
        // -----------------------------------------------------------------------------------

        /// <summary>Great-circle distance in metres.</summary>
        public static double HaversineDistance(GeoCoordinate a, GeoCoordinate b)
        {
            var lat1 = a.latitude * k_Deg2Rad;
            var lat2 = b.latitude * k_Deg2Rad;
            var dLat = lat2 - lat1;
            var dLon = (b.longitude - a.longitude) * k_Deg2Rad;

            var sinDLat = Math.Sin(dLat * 0.5);
            var sinDLon = Math.Sin(dLon * 0.5);
            var h = sinDLat * sinDLat + Math.Cos(lat1) * Math.Cos(lat2) * sinDLon * sinDLon;

            return 2.0 * MeanEarthRadius * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
        }

        /// <summary>Initial bearing from a to b, degrees clockwise from true north in [0,360).</summary>
        public static double InitialBearing(GeoCoordinate a, GeoCoordinate b)
        {
            var lat1 = a.latitude * k_Deg2Rad;
            var lat2 = b.latitude * k_Deg2Rad;
            var dLon = (b.longitude - a.longitude) * k_Deg2Rad;

            var y = Math.Sin(dLon) * Math.Cos(lat2);
            var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

            return NormalizeDegrees(Math.Atan2(y, x) * k_Rad2Deg);
        }

        /// <summary>Bearing of an ENU direction vector (x=East, z=North), degrees clockwise from north.</summary>
        public static float EnuDirectionToBearing(Vector3 enuDirection)
        {
            return (float)NormalizeDegrees(Math.Atan2(enuDirection.x, enuDirection.z) * k_Rad2Deg);
        }

        /// <summary>Converts a compass bearing to a horizontal ENU unit vector.</summary>
        public static Vector3 BearingToEnuDirection(float bearingDegrees)
        {
            var rad = bearingDegrees * (float)k_Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        public static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        /// <summary>Smallest signed difference between two bearings, in (-180, 180].</summary>
        public static float DeltaAngle(float fromDegrees, float toDegrees)
        {
            var delta = Mathf.Repeat(toDegrees - fromDegrees, 360f);
            return delta > 180f ? delta - 360f : delta;
        }
    }
}
