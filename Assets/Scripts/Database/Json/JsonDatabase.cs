using System;
using System.Collections.Generic;
using System.IO;
using TirumalaAR.Data;
using UnityEngine;

namespace TirumalaAR.Database.Json
{
    /// <summary>
    /// File-backed implementation of <see cref="IDatabase"/> that writes to
    /// Application.persistentDataPath. This is the default backend and needs no native plugin,
    /// so the app runs on a clean checkout.
    ///
    /// It satisfies the same contract as the SQLite backend; swapping between them is a one-line
    /// change in AppBootstrap. The dataset is small enough (2.4k waypoints, 41 landmarks) that a
    /// loaded-once in-memory store with debounced writes outperforms per-row SQL anyway.
    /// </summary>
    public sealed class JsonDatabase : IDatabase
    {
        [Serializable]
        sealed class PersistentState
        {
            public double originLatitude;
            public double originLongitude;
            public double originAltitude;
            public List<int> visitedLandmarkIds = new List<int>();
            public List<string> settingKeys = new List<string>();
            public List<string> settingValues = new List<string>();
            public List<NavigationSessionRecord> history = new List<NavigationSessionRecord>();
        }

        readonly string m_StatePath;
        readonly PersistentState m_State;
        bool m_Dirty;

        public IWaypointRepository Waypoints { get; }
        public ILandmarkRepository Landmarks { get; }
        public ISettingsRepository Settings { get; }
        public IHistoryRepository History { get; }

        public string BackendName => "JSON (persistentDataPath)";

        public JsonDatabase(string fileName = "tirumala_ar_state.json")
        {
            m_StatePath = Path.Combine(Application.persistentDataPath, fileName);
            m_State = Load(m_StatePath);

            var waypoints = new InMemoryWaypointRepository(m_State, MarkDirty);
            var landmarks = new InMemoryLandmarkRepository(m_State, MarkDirty);

            Waypoints = waypoints;
            Landmarks = landmarks;
            Settings = new InMemorySettingsRepository(m_State, MarkDirty);
            History = new InMemoryHistoryRepository(m_State, MarkDirty);
        }

        void MarkDirty() => m_Dirty = true;

        static PersistentState Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var state = JsonUtility.FromJson<PersistentState>(json);
                    if (state != null)
                        return state;
                }
            }
            catch (Exception e)
            {
                // A corrupt state file must never stop a pilgrim from navigating; start fresh.
                Debug.LogWarning($"[JsonDatabase] Could not read '{path}' ({e.Message}). Starting with empty state.");
            }

            return new PersistentState();
        }

        public void Flush()
        {
            if (!m_Dirty)
                return;

            try
            {
                var directory = Path.GetDirectoryName(m_StatePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // Write to a temp file first so a kill mid-write cannot corrupt the saved state.
                var temp = m_StatePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(m_State));
                if (File.Exists(m_StatePath))
                    File.Delete(m_StatePath);
                File.Move(temp, m_StatePath);

                m_Dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonDatabase] Failed to persist state: {e.Message}");
            }
        }

        public void Dispose() => Flush();

        // -------------------------------------------------------------------------------
        // Repositories
        // -------------------------------------------------------------------------------

        sealed class InMemoryWaypointRepository : IWaypointRepository
        {
            readonly PersistentState m_State;
            readonly Action m_MarkDirty;
            List<Waypoint> m_Waypoints = new List<Waypoint>();

            public InMemoryWaypointRepository(PersistentState state, Action markDirty)
            {
                m_State = state;
                m_MarkDirty = markDirty;
            }

            public int Count => m_Waypoints.Count;

            public IReadOnlyList<Waypoint> GetAll() => m_Waypoints;

            public Waypoint GetById(int id) =>
                id >= 0 && id < m_Waypoints.Count ? m_Waypoints[id] : null;

            public void ReplaceAll(IReadOnlyList<Waypoint> waypoints, GeoCoordinate origin)
            {
                m_Waypoints = new List<Waypoint>(waypoints);
                m_State.originLatitude = origin.latitude;
                m_State.originLongitude = origin.longitude;
                m_State.originAltitude = origin.altitude;
                m_MarkDirty();
            }

            public GeoCoordinate GetOrigin() =>
                new GeoCoordinate(m_State.originLatitude, m_State.originLongitude, m_State.originAltitude);
        }

        sealed class InMemoryLandmarkRepository : ILandmarkRepository
        {
            readonly PersistentState m_State;
            readonly Action m_MarkDirty;
            readonly Dictionary<int, LandmarkData> m_ById = new Dictionary<int, LandmarkData>();
            List<LandmarkData> m_Landmarks = new List<LandmarkData>();

            public InMemoryLandmarkRepository(PersistentState state, Action markDirty)
            {
                m_State = state;
                m_MarkDirty = markDirty;
            }

            public IReadOnlyList<LandmarkData> GetAll() => m_Landmarks;

            public LandmarkData GetById(int id) => m_ById.GetValueOrDefault(id);

            public IReadOnlyList<LandmarkData> GetUnvisited()
            {
                var result = new List<LandmarkData>();
                foreach (var landmark in m_Landmarks)
                {
                    if (!landmark.visited)
                        result.Add(landmark);
                }
                return result;
            }

            public void MarkVisited(int id, bool visited)
            {
                if (!m_ById.TryGetValue(id, out var landmark))
                    return;

                landmark.visited = visited;

                if (visited)
                {
                    if (!m_State.visitedLandmarkIds.Contains(id))
                        m_State.visitedLandmarkIds.Add(id);
                }
                else
                {
                    m_State.visitedLandmarkIds.Remove(id);
                }

                m_MarkDirty();
            }

            public void ResetVisited()
            {
                foreach (var landmark in m_Landmarks)
                    landmark.visited = false;

                m_State.visitedLandmarkIds.Clear();
                m_MarkDirty();
            }

            public void ReplaceAll(IReadOnlyList<LandmarkData> landmarks)
            {
                m_Landmarks = new List<LandmarkData>(landmarks);
                m_ById.Clear();

                foreach (var landmark in m_Landmarks)
                {
                    m_ById[landmark.id] = landmark;

                    // Re-apply persisted visit state so a resumed walk does not re-announce.
                    landmark.visited = m_State.visitedLandmarkIds.Contains(landmark.id);
                }

                m_MarkDirty();
            }
        }

        sealed class InMemorySettingsRepository : ISettingsRepository
        {
            readonly PersistentState m_State;
            readonly Action m_MarkDirty;
            readonly Dictionary<string, string> m_Values = new Dictionary<string, string>();

            public InMemorySettingsRepository(PersistentState state, Action markDirty)
            {
                m_State = state;
                m_MarkDirty = markDirty;

                // JsonUtility cannot serialise a Dictionary, so it is stored as parallel lists.
                var count = Mathf.Min(state.settingKeys.Count, state.settingValues.Count);
                for (var i = 0; i < count; i++)
                    m_Values[state.settingKeys[i]] = state.settingValues[i];
            }

            void Write(string key, string value)
            {
                m_Values[key] = value;

                m_State.settingKeys.Clear();
                m_State.settingValues.Clear();
                foreach (var pair in m_Values)
                {
                    m_State.settingKeys.Add(pair.Key);
                    m_State.settingValues.Add(pair.Value);
                }

                m_MarkDirty();
            }

            public bool GetBool(string key, bool fallback = false) =>
                m_Values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var v) ? v : fallback;

            public float GetFloat(string key, float fallback = 0f) =>
                m_Values.TryGetValue(key, out var raw) &&
                float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)
                    ? v : fallback;

            public string GetString(string key, string fallback = null) =>
                m_Values.TryGetValue(key, out var raw) ? raw : fallback;

            public void Set(string key, bool value) => Write(key, value ? "true" : "false");

            public void Set(string key, float value) =>
                Write(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public void Set(string key, string value) => Write(key, value ?? string.Empty);
        }

        sealed class InMemoryHistoryRepository : IHistoryRepository
        {
            readonly PersistentState m_State;
            readonly Action m_MarkDirty;

            public InMemoryHistoryRepository(PersistentState state, Action markDirty)
            {
                m_State = state;
                m_MarkDirty = markDirty;
            }

            public IReadOnlyList<NavigationSessionRecord> GetAll() => m_State.history;

            public void Add(NavigationSessionRecord record)
            {
                if (record == null)
                    return;

                m_State.history.Add(record);

                // Keep the file small — a pilgrim only cares about recent walks.
                while (m_State.history.Count > 50)
                    m_State.history.RemoveAt(0);

                m_MarkDirty();
            }

            public void Update(NavigationSessionRecord record)
            {
                if (record == null)
                    return;

                for (var i = 0; i < m_State.history.Count; i++)
                {
                    if (m_State.history[i].sessionId != record.sessionId)
                        continue;

                    m_State.history[i] = record;
                    m_MarkDirty();
                    return;
                }

                Add(record);
            }

            public void Clear()
            {
                m_State.history.Clear();
                m_MarkDirty();
            }
        }
    }
}
