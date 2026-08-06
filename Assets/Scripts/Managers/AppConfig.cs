using TirumalaAR.Data;
using TirumalaAR.GeoJson;
using UnityEngine;

namespace TirumalaAR.Managers
{
    /// <summary>
    /// All tunable configuration in one authored asset, so behaviour can be adjusted without
    /// recompiling and without hunting through inspector fields on a dozen components.
    ///
    /// Create via: Assets ▸ Create ▸ Tirumala AR ▸ App Config.
    /// The instance must live at Assets/Resources/AppConfig.asset so it can be loaded at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "AppConfig", menuName = "Tirumala AR/App Config", order = 0)]
    public sealed class AppConfig : ScriptableObject
    {
        public const string ResourcePath = "AppConfig";

        [Header("Data files (relative to StreamingAssets)")]
        public string routeGeoJsonPath = "GeoJSON/alipiri_mettu.geojson";
        public string landmarkDatabasePath = "Database/landmarks.json";

        [Header("Route construction")]
        public RouteBuildSettings routeSettings = new RouteBuildSettings();

        [Tooltip("Coordinate the route is anchored to — the start of the Alipiri walk. " +
                 "Used to decide which end of the GeoJSON is the beginning.")]
        public double routeStartLatitude = 13.646192003954598;
        public double routeStartLongitude = 79.40582499436083;

        [Header("Arrows")]
        [Range(0.3f, 3f)] public float arrowSpacing = 0.75f;
        [Range(3, 30)] public int visibleArrowCount = 10;
        public Color arrowColor = new Color(0.13f, 0.48f, 1f, 1f);

        [Header("Navigation")]
        [Tooltip("Distance from the destination at which arrival is announced, in metres.")]
        public float arrivalRadius = 25f;

        [Tooltip("Anchors are shared between arrows within this radius, in metres.")]
        public float anchorSpacing = 4f;

        [Header("Performance")]
        [Tooltip("Target frame rate. 60 on capable devices; the app drops to 30 automatically if it cannot hold it.")]
        public int targetFrameRate = 60;

        [Tooltip("Keep the screen awake while navigating.")]
        public bool preventScreenSleep = true;

        [Header("Defaults")]
        public bool voiceEnabledByDefault = true;
        public bool darkModeByDefault = true;
        public bool debugModeByDefault;

        public GeoCoordinate RouteStart => new GeoCoordinate(routeStartLatitude, routeStartLongitude);

        /// <summary>Loads the authored config, falling back to defaults if the asset is missing.</summary>
        public static AppConfig LoadOrDefault()
        {
            var config = Resources.Load<AppConfig>(ResourcePath);

            if (config != null)
                return config;

            Debug.LogWarning(
                $"[Config] No AppConfig found at Resources/{ResourcePath}. Using built-in defaults. " +
                "Create one via Assets ▸ Create ▸ Tirumala AR ▸ App Config.");

            return CreateInstance<AppConfig>();
        }
    }
}
