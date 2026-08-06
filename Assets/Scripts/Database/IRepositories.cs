using System;
using System.Collections.Generic;
using TirumalaAR.Data;

namespace TirumalaAR.Database
{
    /// <summary>A completed or abandoned walk.</summary>
    [Serializable]
    public sealed class NavigationSessionRecord
    {
        public string sessionId;
        public string startedUtc;
        public string endedUtc;
        public float distanceCovered;
        public float durationSeconds;
        public int landmarksVisited;
        public bool completed;
    }

    public interface IWaypointRepository
    {
        IReadOnlyList<Waypoint> GetAll();
        Waypoint GetById(int id);
        void ReplaceAll(IReadOnlyList<Waypoint> waypoints, GeoCoordinate origin);
        GeoCoordinate GetOrigin();
        int Count { get; }
    }

    public interface ILandmarkRepository
    {
        IReadOnlyList<LandmarkData> GetAll();
        LandmarkData GetById(int id);
        IReadOnlyList<LandmarkData> GetUnvisited();
        void MarkVisited(int id, bool visited);
        void ResetVisited();
        void ReplaceAll(IReadOnlyList<LandmarkData> landmarks);
    }

    public interface ISettingsRepository
    {
        bool GetBool(string key, bool fallback = false);
        float GetFloat(string key, float fallback = 0f);
        string GetString(string key, string fallback = null);
        void Set(string key, bool value);
        void Set(string key, float value);
        void Set(string key, string value);
    }

    public interface IHistoryRepository
    {
        IReadOnlyList<NavigationSessionRecord> GetAll();
        void Add(NavigationSessionRecord record);
        void Update(NavigationSessionRecord record);
        void Clear();
    }

    /// <summary>
    /// Aggregate over the four repositories, so the composition root passes one object around
    /// instead of four. <see cref="Flush"/> persists any pending writes.
    /// </summary>
    public interface IDatabase : IDisposable
    {
        IWaypointRepository Waypoints { get; }
        ILandmarkRepository Landmarks { get; }
        ISettingsRepository Settings { get; }
        IHistoryRepository History { get; }

        string BackendName { get; }
        void Flush();
    }

    /// <summary>Well-known settings keys, kept in one place so the UI and services cannot drift apart.</summary>
    public static class SettingsKeys
    {
        public const string VoiceEnabled = "voice.enabled";
        public const string VoiceVolume = "voice.volume";
        public const string DarkMode = "ui.darkMode";
        public const string DebugMode = "ui.debugMode";
        public const string ArrowSpacing = "ar.arrowSpacing";
        public const string VisibleArrowCount = "ar.visibleArrows";
        public const string MiniMapZoom = "ui.miniMapZoom";
        public const string UnitsMetric = "ui.metric";
        public const string LastWaypointId = "nav.lastWaypoint";
        public const string AutoBrightness = "ui.autoBrightness";
        public const string HapticFeedback = "ui.hapticFeedback";
        public const string NightMode = "ui.nightMode";
    }
}
