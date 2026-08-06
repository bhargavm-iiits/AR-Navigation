using System;
using System.Collections.Generic;
using TirumalaAR.Data;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.Navigation
{
    /// <summary>Where the user sits relative to the route centreline.</summary>
    public struct RouteProjection
    {
        /// <summary>Nearest point on the route polyline, in ENU metres.</summary>
        public Vector3 position;

        /// <summary>Index of the waypoint at or immediately before <see cref="position"/>.</summary>
        public int waypointId;

        /// <summary>Distance travelled along the route to reach <see cref="position"/>.</summary>
        public float distanceAlongRoute;

        /// <summary>Perpendicular distance from the user to the centreline.</summary>
        public float lateralOffset;

        /// <summary>Route heading at this point, degrees clockwise from north.</summary>
        public float bearing;

        public bool isValid;
    }

    public interface IRouteQuery
    {
        IReadOnlyList<Waypoint> Waypoints { get; }
        GeoCoordinate Origin { get; }
        float TotalDistance { get; }
        bool IsReady { get; }

        int FindNearestWaypoint(Vector3 enuPosition);
        RouteProjection ProjectOntoRoute(Vector3 enuPosition, int searchHintWaypointId = -1);
        IReadOnlyList<Waypoint> FindPath(int fromWaypointId, int toWaypointId);

        /// <summary>Samples the route centreline between two distances, for arrow placement.</summary>
        void SampleAhead(float fromDistance, float toDistance, float spacing, List<Vector3> results);
    }

    /// <summary>
    /// The navigation graph (System 2).
    ///
    /// The Alipiri route is a single chain, so A* is degenerate on it — but the graph is built as
    /// a real adjacency structure anyway so that side paths (the footway spur, future alternate
    /// routes) can be added without touching the pathfinding. Nearest-waypoint search is backed
    /// by a uniform spatial hash; a linear scan over 2400 waypoints every GPS tick would be
    /// wasteful, and the search is also called per-frame by the arrow manager.
    /// </summary>
    public sealed class NavigationGraph : IRouteQuery
    {
        const float k_CellSize = 25f; // metres; ~8 waypoints per cell at 3 m spacing

        readonly List<Waypoint> m_Waypoints = new List<Waypoint>();
        readonly Dictionary<int, List<int>> m_Adjacency = new Dictionary<int, List<int>>();
        readonly Dictionary<long, List<int>> m_SpatialHash = new Dictionary<long, List<int>>();

        // Scratch buffers reused by A* so pathfinding does not allocate per call.
        readonly Dictionary<int, float> m_GScore = new Dictionary<int, float>();
        readonly Dictionary<int, int> m_CameFrom = new Dictionary<int, int>();
        readonly PriorityQueue m_OpenSet = new PriorityQueue();
        readonly HashSet<int> m_Closed = new HashSet<int>();

        public IReadOnlyList<Waypoint> Waypoints => m_Waypoints;
        public GeoCoordinate Origin { get; private set; }
        public float TotalDistance { get; private set; }
        public bool IsReady => m_Waypoints.Count >= 2;

        public void Build(IReadOnlyList<Waypoint> waypoints, GeoCoordinate origin)
        {
            m_Waypoints.Clear();
            m_Adjacency.Clear();
            m_SpatialHash.Clear();

            Origin = origin;

            if (waypoints == null || waypoints.Count == 0)
            {
                TotalDistance = 0f;
                return;
            }

            m_Waypoints.AddRange(waypoints);
            TotalDistance = m_Waypoints[m_Waypoints.Count - 1].cumulativeDistance;

            for (var i = 0; i < m_Waypoints.Count; i++)
            {
                var neighbours = new List<int>(2);

                if (i > 0)
                    neighbours.Add(i - 1);

                var next = m_Waypoints[i].nextWaypointId;
                if (next >= 0 && next < m_Waypoints.Count)
                    neighbours.Add(next);

                m_Adjacency[i] = neighbours;
                Insert(i, m_Waypoints[i].enuPosition);
            }
        }

        /// <summary>Adds a connection between two waypoints, e.g. where a side path rejoins the steps.</summary>
        public void AddEdge(int a, int b)
        {
            if (a == b || !m_Adjacency.ContainsKey(a) || !m_Adjacency.ContainsKey(b))
                return;

            if (!m_Adjacency[a].Contains(b)) m_Adjacency[a].Add(b);
            if (!m_Adjacency[b].Contains(a)) m_Adjacency[b].Add(a);
        }

        // -----------------------------------------------------------------------------------
        // Spatial hash
        // -----------------------------------------------------------------------------------

        static long CellKey(Vector3 position)
        {
            var cx = Mathf.FloorToInt(position.x / k_CellSize);
            var cz = Mathf.FloorToInt(position.z / k_CellSize);
            return ((long)cx << 32) ^ (uint)cz;
        }

        void Insert(int index, Vector3 position)
        {
            var key = CellKey(position);

            if (!m_SpatialHash.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>();
                m_SpatialHash[key] = bucket;
            }

            bucket.Add(index);
        }

        public int FindNearestWaypoint(Vector3 enuPosition)
        {
            if (m_Waypoints.Count == 0)
                return -1;

            var best = -1;
            var bestDistance = float.MaxValue;

            // Widen the search ring until something is found. One ring covers 75 m, which is far
            // beyond any plausible GPS excursion from the path.
            for (var ring = 1; ring <= 4 && best < 0; ring++)
            {
                var cx = Mathf.FloorToInt(enuPosition.x / k_CellSize);
                var cz = Mathf.FloorToInt(enuPosition.z / k_CellSize);

                for (var dx = -ring; dx <= ring; dx++)
                {
                    for (var dz = -ring; dz <= ring; dz++)
                    {
                        var key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);

                        if (!m_SpatialHash.TryGetValue(key, out var bucket))
                            continue;

                        foreach (var index in bucket)
                        {
                            var delta = m_Waypoints[index].enuPosition - enuPosition;
                            var distance = delta.x * delta.x + delta.z * delta.z;

                            if (distance >= bestDistance)
                                continue;

                            bestDistance = distance;
                            best = index;
                        }
                    }
                }
            }

            // Fall back to a full scan if the user is somehow far off the map.
            if (best < 0)
            {
                for (var i = 0; i < m_Waypoints.Count; i++)
                {
                    var delta = m_Waypoints[i].enuPosition - enuPosition;
                    var distance = delta.x * delta.x + delta.z * delta.z;

                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        // -----------------------------------------------------------------------------------
        // Projection / snapping
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Projects a position onto the route centreline. When <paramref name="searchHintWaypointId"/>
        /// is supplied only a window around it is examined, which is both faster and — more
        /// importantly — prevents the projection from jumping to a different part of the route
        /// where the path doubles back on itself near the hairpins above Gali Gopuram.
        /// </summary>
        public RouteProjection ProjectOntoRoute(Vector3 enuPosition, int searchHintWaypointId = -1)
        {
            var result = new RouteProjection { isValid = false, waypointId = -1 };

            if (m_Waypoints.Count < 2)
                return result;

            int first, last;

            if (searchHintWaypointId >= 0 && searchHintWaypointId < m_Waypoints.Count)
            {
                const int window = 40; // ±120 m at 3 m spacing
                first = Mathf.Max(0, searchHintWaypointId - window);
                last = Mathf.Min(m_Waypoints.Count - 2, searchHintWaypointId + window);
            }
            else
            {
                var nearest = FindNearestWaypoint(enuPosition);
                if (nearest < 0)
                    return result;

                first = Mathf.Max(0, nearest - 2);
                last = Mathf.Min(m_Waypoints.Count - 2, nearest + 2);
            }

            var bestDistance = float.MaxValue;

            for (var i = first; i <= last; i++)
            {
                var a = m_Waypoints[i];
                var b = m_Waypoints[i + 1];

                var projected = PolylineUtility.ClosestPointOnSegment(
                    a.enuPosition, b.enuPosition, enuPosition, out var t);

                // Compare horizontally only — a metre of altitude error must not change which
                // segment the pilgrim is considered to be on.
                var dx = projected.x - enuPosition.x;
                var dz = projected.z - enuPosition.z;
                var distance = dx * dx + dz * dz;

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                result.position = projected;
                result.waypointId = i;
                result.distanceAlongRoute = a.cumulativeDistance + a.distanceToNext * t;
                result.bearing = a.bearingDegrees;
                result.isValid = true;
            }

            if (result.isValid)
                result.lateralOffset = Mathf.Sqrt(bestDistance);

            return result;
        }

        public void SampleAhead(float fromDistance, float toDistance, float spacing, List<Vector3> results)
        {
            results.Clear();

            if (m_Waypoints.Count < 2 || spacing <= 0f)
                return;

            fromDistance = Mathf.Clamp(fromDistance, 0f, TotalDistance);
            toDistance = Mathf.Clamp(toDistance, fromDistance, TotalDistance);

            for (var distance = fromDistance; distance <= toDistance; distance += spacing)
                results.Add(PositionAtDistance(distance));
        }

        /// <summary>Interpolated point on the centreline at a given distance from the start.</summary>
        public Vector3 PositionAtDistance(float distance)
        {
            if (m_Waypoints.Count == 0)
                return Vector3.zero;

            if (m_Waypoints.Count == 1 || distance <= 0f)
                return m_Waypoints[0].enuPosition;

            if (distance >= TotalDistance)
                return m_Waypoints[m_Waypoints.Count - 1].enuPosition;

            var index = FindWaypointAtDistance(distance);
            var a = m_Waypoints[index];

            if (a.distanceToNext <= 1e-4f || index + 1 >= m_Waypoints.Count)
                return a.enuPosition;

            var t = (distance - a.cumulativeDistance) / a.distanceToNext;
            return Vector3.Lerp(a.enuPosition, m_Waypoints[index + 1].enuPosition, Mathf.Clamp01(t));
        }

        /// <summary>Binary search over cumulative distance — the array is sorted by construction.</summary>
        public int FindWaypointAtDistance(float distance)
        {
            var low = 0;
            var high = m_Waypoints.Count - 1;

            while (low < high)
            {
                var mid = (low + high + 1) / 2;

                if (m_Waypoints[mid].cumulativeDistance <= distance)
                    low = mid;
                else
                    high = mid - 1;
            }

            return low;
        }

        // -----------------------------------------------------------------------------------
        // A*
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A* over the waypoint graph. The heuristic is straight-line ENU distance, which is
        /// admissible because no edge is ever shorter than the straight line between its nodes.
        /// </summary>
        public IReadOnlyList<Waypoint> FindPath(int fromWaypointId, int toWaypointId)
        {
            var path = new List<Waypoint>();

            if (fromWaypointId < 0 || fromWaypointId >= m_Waypoints.Count ||
                toWaypointId < 0 || toWaypointId >= m_Waypoints.Count)
                return path;

            if (fromWaypointId == toWaypointId)
            {
                path.Add(m_Waypoints[fromWaypointId]);
                return path;
            }

            m_GScore.Clear();
            m_CameFrom.Clear();
            m_Closed.Clear();
            m_OpenSet.Clear();

            var goal = m_Waypoints[toWaypointId].enuPosition;

            m_GScore[fromWaypointId] = 0f;
            m_OpenSet.Push(fromWaypointId, Heuristic(m_Waypoints[fromWaypointId].enuPosition, goal));

            while (m_OpenSet.Count > 0)
            {
                var current = m_OpenSet.Pop();

                if (current == toWaypointId)
                    return Reconstruct(current, path);

                if (!m_Closed.Add(current))
                    continue;

                if (!m_Adjacency.TryGetValue(current, out var neighbours))
                    continue;

                var currentG = m_GScore[current];

                foreach (var neighbour in neighbours)
                {
                    if (m_Closed.Contains(neighbour))
                        continue;

                    var stepCost = Vector3.Distance(
                        m_Waypoints[current].enuPosition, m_Waypoints[neighbour].enuPosition);

                    var tentativeG = currentG + stepCost;

                    if (m_GScore.TryGetValue(neighbour, out var existing) && tentativeG >= existing)
                        continue;

                    m_GScore[neighbour] = tentativeG;
                    m_CameFrom[neighbour] = current;
                    m_OpenSet.Push(neighbour, tentativeG + Heuristic(m_Waypoints[neighbour].enuPosition, goal));
                }
            }

            return path; // empty: unreachable
        }

        static float Heuristic(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

        List<Waypoint> Reconstruct(int current, List<Waypoint> path)
        {
            path.Add(m_Waypoints[current]);

            while (m_CameFrom.TryGetValue(current, out var previous))
            {
                current = previous;
                path.Add(m_Waypoints[current]);
            }

            path.Reverse();
            return path;
        }

        /// <summary>Binary min-heap keyed by f-score. Avoids the O(n) scan a list-based open set needs.</summary>
        sealed class PriorityQueue
        {
            readonly List<(int node, float priority)> m_Heap = new List<(int, float)>();

            public int Count => m_Heap.Count;
            public void Clear() => m_Heap.Clear();

            public void Push(int node, float priority)
            {
                m_Heap.Add((node, priority));

                var child = m_Heap.Count - 1;
                while (child > 0)
                {
                    var parent = (child - 1) / 2;
                    if (m_Heap[parent].priority <= m_Heap[child].priority)
                        break;

                    (m_Heap[parent], m_Heap[child]) = (m_Heap[child], m_Heap[parent]);
                    child = parent;
                }
            }

            public int Pop()
            {
                var root = m_Heap[0].node;
                var last = m_Heap.Count - 1;
                m_Heap[0] = m_Heap[last];
                m_Heap.RemoveAt(last);

                var parent = 0;
                while (true)
                {
                    var left = 2 * parent + 1;
                    var right = left + 1;
                    var smallest = parent;

                    if (left < m_Heap.Count && m_Heap[left].priority < m_Heap[smallest].priority)
                        smallest = left;
                    if (right < m_Heap.Count && m_Heap[right].priority < m_Heap[smallest].priority)
                        smallest = right;

                    if (smallest == parent)
                        break;

                    (m_Heap[parent], m_Heap[smallest]) = (m_Heap[smallest], m_Heap[parent]);
                    parent = smallest;
                }

                return root;
            }
        }
    }
}
