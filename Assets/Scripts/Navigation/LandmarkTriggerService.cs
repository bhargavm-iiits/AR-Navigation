using System.Collections.Generic;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Database;
using TirumalaAR.Localization;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.Navigation
{
    /// <summary>
    /// Fires landmark content when the pilgrim walks into range (System 11).
    ///
    /// Landmarks are pre-sorted by their distance along the route and evaluated through a sliding
    /// window, so checking 41 landmarks never costs a full scan. Visit state is persisted, so a
    /// landmark that has already been announced stays silent even if the app is restarted
    /// halfway up the hill.
    /// </summary>
    public sealed class LandmarkTriggerService
    {
        readonly ILandmarkRepository m_Repository;
        readonly IRouteQuery m_Route;
        readonly HybridLocalizationEngine m_Localization;
        readonly Transform m_ContentParent;

        readonly List<LandmarkData> m_Ordered = new List<LandmarkData>();
        readonly Dictionary<int, GameObject> m_SpawnedContent = new Dictionary<int, GameObject>();
        readonly Dictionary<string, GameObject> m_PrefabCache = new Dictionary<string, GameObject>();

        int m_WindowStart;

        /// <summary>How far ahead of the pilgrim landmarks are considered for triggering.</summary>
        const float k_LookAheadMeters = 120f;

        /// <summary>Spawned AR content further behind than this is despawned to free memory.</summary>
        const float k_DespawnBehindMeters = 80f;

        public LandmarkData NextLandmark { get; private set; }
        public float DistanceToNextLandmark { get; private set; }
        public int VisitedCount { get; private set; }

        public LandmarkTriggerService(ILandmarkRepository repository, IRouteQuery route,
            HybridLocalizationEngine localization, Transform contentParent)
        {
            m_Repository = repository;
            m_Route = route;
            m_Localization = localization;
            m_ContentParent = contentParent;
        }

        /// <summary>
        /// Projects every landmark onto the route so triggering can be driven by route distance
        /// rather than by straight-line distance. On a switchback staircase two points can be
        /// 15 m apart in a straight line but 300 m apart on foot, and a naive radius check would
        /// announce Gali Gopuram while the pilgrim is still far below it.
        /// </summary>
        public void Prepare()
        {
            m_Ordered.Clear();

            var all = m_Repository?.GetAll();

            if (all == null || !m_Route.IsReady)
                return;

            var deduplicated = new List<LandmarkData>();

            foreach (var landmark in all)
            {
                var enu = GeoMath.GeodeticToEnu(landmark.Coordinate, m_Route.Origin);
                var projection = m_Route.ProjectOntoRoute(enu);

                landmark.enuPosition = enu;
                landmark.nearestWaypointId = projection.isValid ? projection.waypointId : -1;
                landmark.routeDistance = projection.isValid ? projection.distanceAlongRoute : float.MaxValue;
                landmark.offRouteDistance = projection.isValid ? projection.lateralOffset : float.MaxValue;

                // The source data contains one landmark duplicated at identical coordinates.
                // Announcing it twice at the same spot would be a defect, so near-identical
                // entries are collapsed here rather than edited out of the supplied file.
                var isDuplicate = false;

                foreach (var existing in deduplicated)
                {
                    if (Vector3.Distance(existing.enuPosition, enu) >= 5f)
                        continue;

                    isDuplicate = true;
                    Debug.Log($"[Landmarks] '{landmark.name}' (id {landmark.id}) duplicates " +
                              $"'{existing.name}' (id {existing.id}); only one will trigger.");
                    break;
                }

                if (!isDuplicate)
                    deduplicated.Add(landmark);
            }

            deduplicated.Sort((a, b) => a.routeDistance.CompareTo(b.routeDistance));
            m_Ordered.AddRange(deduplicated);

            VisitedCount = 0;
            foreach (var landmark in m_Ordered)
            {
                if (landmark.visited)
                    VisitedCount++;
            }

            m_WindowStart = 0;

            var offRoute = 0;
            foreach (var landmark in m_Ordered)
            {
                if (landmark.offRouteDistance > 60f)
                    offRoute++;
            }

            Debug.Log($"[Landmarks] Prepared {m_Ordered.Count} landmark(s); {offRoute} sit more than 60 m " +
                      "from the route centreline.");
        }

        public void Tick()
        {
            if (m_Ordered.Count == 0)
                return;

            var projection = m_Localization.Projection;

            if (!projection.isValid)
                return;

            var userDistance = projection.distanceAlongRoute;

            // Advance the window past everything already behind us.
            while (m_WindowStart < m_Ordered.Count &&
                   m_Ordered[m_WindowStart].routeDistance < userDistance - k_DespawnBehindMeters)
            {
                DespawnContent(m_Ordered[m_WindowStart].id);
                m_WindowStart++;
            }

            NextLandmark = null;
            DistanceToNextLandmark = float.MaxValue;

            for (var i = m_WindowStart; i < m_Ordered.Count; i++)
            {
                var landmark = m_Ordered[i];
                var alongRoute = landmark.routeDistance - userDistance;

                if (alongRoute > k_LookAheadMeters)
                    break;

                if (NextLandmark == null && alongRoute > -5f)
                {
                    NextLandmark = landmark;
                    DistanceToNextLandmark = Mathf.Max(0f, alongRoute);
                }

                if (landmark.visited)
                    continue;

                // Trigger on true 3D proximity, but only for landmarks the window has already
                // established are near us on the path.
                var straightLine = Vector3.Distance(m_Localization.EnuPosition, landmark.enuPosition);
                var radius = Mathf.Max(5f, landmark.triggerRadius);

                if (straightLine > radius || Mathf.Abs(alongRoute) > radius * 2f)
                    continue;

                Trigger(landmark, straightLine);
            }
        }

        void Trigger(LandmarkData landmark, float distance)
        {
            landmark.visited = true;
            VisitedCount++;
            m_Repository?.MarkVisited(landmark.id, true);

            SpawnContent(landmark);

            EventBus.Publish(new LandmarkTriggeredEvent
            {
                landmark = landmark,
                distanceToUser = distance
            });

            Debug.Log($"[Landmarks] Triggered '{landmark.name}' at {distance:F0} m.");
        }

        void SpawnContent(LandmarkData landmark)
        {
            if (string.IsNullOrWhiteSpace(landmark.arPrefab) || m_SpawnedContent.ContainsKey(landmark.id))
                return;

            if (!m_PrefabCache.TryGetValue(landmark.arPrefab, out var prefab))
            {
                prefab = Resources.Load<GameObject>($"Landmarks/{landmark.arPrefab}");
                m_PrefabCache[landmark.arPrefab] = prefab;

                if (prefab == null)
                {
                    Debug.LogWarning($"[Landmarks] No prefab at Resources/Landmarks/{landmark.arPrefab}.");
                    return;
                }
            }

            if (prefab == null)
                return;

            var frame = m_Localization.Frame;

            if (!frame.IsInitialized)
                return;

            var world = frame.EnuToUnity(landmark.enuPosition);
            var instance = Object.Instantiate(prefab, world, Quaternion.identity, m_ContentParent);
            instance.name = $"Landmark_{landmark.id}_{landmark.name}";

            m_SpawnedContent[landmark.id] = instance;
        }

        void DespawnContent(int landmarkId)
        {
            if (!m_SpawnedContent.Remove(landmarkId, out var instance))
                return;

            if (instance != null)
                Object.Destroy(instance);
        }

        public void ResetVisits()
        {
            m_Repository?.ResetVisited();

            foreach (var landmark in m_Ordered)
                landmark.visited = false;

            foreach (var id in new List<int>(m_SpawnedContent.Keys))
                DespawnContent(id);

            VisitedCount = 0;
            m_WindowStart = 0;
        }

        public void Clear()
        {
            foreach (var id in new List<int>(m_SpawnedContent.Keys))
                DespawnContent(id);
        }
    }
}
