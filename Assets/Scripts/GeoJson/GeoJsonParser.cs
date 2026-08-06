using System;
using System.Collections.Generic;
using TirumalaAR.Data;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.GeoJson
{
    /// <summary>A single GeoJSON feature reduced to what the navigation layer needs.</summary>
    public sealed class GeoFeature
    {
        public string id;
        public string geometryType;
        public readonly Dictionary<string, string> properties = new Dictionary<string, string>();
        public readonly List<GeoCoordinate> coordinates = new List<GeoCoordinate>();

        public bool IsLine => geometryType == "LineString" && coordinates.Count >= 2;
        public bool IsPoint => geometryType == "Point" && coordinates.Count == 1;

        public string Highway => properties.TryGetValue("highway", out var v) ? v : null;
        public bool IsStairs => string.Equals(Highway, "steps", StringComparison.OrdinalIgnoreCase);
        public bool IsWalkable => IsStairs ||
                                  string.Equals(Highway, "footway", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(Highway, "path", StringComparison.OrdinalIgnoreCase);

        public string Name => properties.TryGetValue("name", out var v) ? v : id;

        public GeoCoordinate First => coordinates[0];
        public GeoCoordinate Last => coordinates[coordinates.Count - 1];

        public void Reverse() => coordinates.Reverse();
    }

    public sealed class GeoJsonDocument
    {
        public readonly List<GeoFeature> lines = new List<GeoFeature>();
        public readonly List<GeoFeature> points = new List<GeoFeature>();
        public readonly List<string> warnings = new List<string>();
    }

    /// <summary>
    /// Reads an RFC 7946 FeatureCollection. Only the geometry types present in the Alipiri
    /// extract are materialised (LineString, Point); MultiLineString is flattened into separate
    /// line features so the route stitcher can treat every piece uniformly.
    /// </summary>
    public static class GeoJsonParser
    {
        public static GeoJsonDocument Parse(string json)
        {
            var document = new GeoJsonDocument();

            if (!JsonNode.TryParse(json, out var root, out var error))
            {
                document.warnings.Add($"GeoJSON is not valid JSON: {error}");
                return document;
            }

            var features = root["features"];
            if (features.Type != JsonType.Array)
            {
                document.warnings.Add("GeoJSON has no 'features' array — nothing to load.");
                return document;
            }

            foreach (var featureNode in features.Items)
            {
                var geometry = featureNode["geometry"];
                var type = geometry["type"].AsString();

                if (string.IsNullOrEmpty(type))
                    continue;

                var id = featureNode["id"].AsString() ?? featureNode["properties"]["@id"].AsString() ?? "unnamed";

                switch (type)
                {
                    case "LineString":
                        AddFeature(document, featureNode, id, "LineString",
                            ReadCoordinateList(document, id, geometry["coordinates"]));
                        break;

                    case "Point":
                        var single = new List<GeoCoordinate>();
                        if (TryReadCoordinate(geometry["coordinates"], out var point))
                            single.Add(point);
                        AddFeature(document, featureNode, id, "Point", single);
                        break;

                    case "MultiLineString":
                        var part = 0;
                        foreach (var lineNode in geometry["coordinates"].Items)
                        {
                            var partId = $"{id}#{part++}";
                            AddFeature(document, featureNode, partId, "LineString",
                                ReadCoordinateList(document, partId, lineNode));
                        }
                        break;

                    default:
                        document.warnings.Add($"Feature '{id}' has unsupported geometry '{type}' and was skipped.");
                        break;
                }
            }

            return document;
        }

        /// <summary>GeoJSON positions are always [longitude, latitude, (elevation)] — note the order.</summary>
        static bool TryReadCoordinate(JsonNode position, out GeoCoordinate coordinate)
        {
            coordinate = default;

            if (position.Type != JsonType.Array || position.Count < 2)
                return false;

            coordinate = new GeoCoordinate(
                position[1].AsDouble(),
                position[0].AsDouble(),
                position.Count >= 3 ? position[2].AsDouble() : 0d);

            return coordinate.IsValid;
        }

        static List<GeoCoordinate> ReadCoordinateList(GeoJsonDocument document, string id, JsonNode array)
        {
            var result = new List<GeoCoordinate>(array.Count);

            foreach (var position in array.Items)
            {
                if (TryReadCoordinate(position, out var coordinate))
                    result.Add(coordinate);
                else
                    document.warnings.Add($"Feature '{id}' contains an unusable position; it was dropped.");
            }

            return result;
        }

        static void AddFeature(GeoJsonDocument document, JsonNode featureNode, string id,
            string geometryType, List<GeoCoordinate> coordinates)
        {
            if (coordinates.Count == 0)
                return;

            var feature = new GeoFeature { id = id, geometryType = geometryType };

            foreach (var member in featureNode["properties"].Members)
            {
                var value = member.Value.AsString();
                if (value != null)
                    feature.properties[member.Key] = value;
            }

            feature.coordinates.AddRange(coordinates);

            if (feature.IsLine)
                document.lines.Add(feature);
            else if (feature.IsPoint)
                document.points.Add(feature);
        }
    }
}
