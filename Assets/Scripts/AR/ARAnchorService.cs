using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace TirumalaAR.AR
{
    /// <summary>
    /// Manages the AR anchors that arrows are parented to (System 6).
    ///
    /// Arrows are not placed at raw world coordinates. ARCore continuously refines its estimate
    /// of the environment, and an unanchored object stays at a stale coordinate while the world
    /// moves under it — that is exactly what makes AR arrows appear to slide or float. Anchoring
    /// hands that correction to ARCore: the anchor's transform is updated by the platform every
    /// frame, so anything parented to it stays welded to the real staircase.
    ///
    /// One anchor per arrow would be wasteful — ARCore's tracking cost grows with anchor count.
    /// Instead anchors are shared: one every few metres along the path, with nearby arrows
    /// parented to whichever is closest.
    /// </summary>
    public sealed class ARAnchorService
    {
        sealed class AnchorRecord
        {
            public ARAnchor anchor;
            public Vector3 worldPosition;
            public int referenceCount;
            public float lastUsedTime;
        }

        readonly ARAnchorManager m_AnchorManager;
        readonly Transform m_Parent;
        readonly List<AnchorRecord> m_Anchors = new List<AnchorRecord>();

        /// <summary>Anchors closer together than this are merged into one.</summary>
        readonly float m_AnchorSpacing;

        /// <summary>Anchors unused for this long are released.</summary>
        const float k_IdleTimeoutSeconds = 12f;

        /// <summary>Hard cap; ARCore degrades noticeably past a few dozen anchors.</summary>
        const int k_MaxAnchors = 24;

        public int ActiveAnchorCount => m_Anchors.Count;

        public ARAnchorService(ARAnchorManager anchorManager, Transform parent, float anchorSpacing = 4f)
        {
            m_AnchorManager = anchorManager;
            m_Parent = parent;
            m_AnchorSpacing = Mathf.Max(1f, anchorSpacing);
        }

        /// <summary>
        /// Returns a transform near <paramref name="worldPosition"/> that arrows can be parented
        /// to. Falls back to the plain parent transform when anchoring is unavailable, so the app
        /// still renders on devices where anchor creation fails.
        /// </summary>
        public Transform Acquire(Vector3 worldPosition)
        {
            var existing = FindNearest(worldPosition);

            if (existing != null)
            {
                existing.referenceCount++;
                existing.lastUsedTime = Time.time;
                return existing.anchor != null ? existing.anchor.transform : m_Parent;
            }

            if (m_AnchorManager == null || m_Anchors.Count >= k_MaxAnchors)
                return m_Parent;

            var anchor = CreateAnchor(worldPosition);

            if (anchor == null)
                return m_Parent;

            m_Anchors.Add(new AnchorRecord
            {
                anchor = anchor,
                worldPosition = worldPosition,
                referenceCount = 1,
                lastUsedTime = Time.time
            });

            return anchor.transform;
        }

        /// <summary>Signals that one user of the anchor covering this position has gone away.</summary>
        public void Release(Vector3 worldPosition)
        {
            var record = FindNearest(worldPosition);

            if (record == null)
                return;

            record.referenceCount = Mathf.Max(0, record.referenceCount - 1);
        }

        AnchorRecord FindNearest(Vector3 worldPosition)
        {
            AnchorRecord best = null;
            var bestDistance = m_AnchorSpacing;

            foreach (var record in m_Anchors)
            {
                if (record.anchor == null)
                    continue;

                var distance = Vector3.Distance(record.anchor.transform.position, worldPosition);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = record;
            }

            return best;
        }

        /// <summary>
        /// Creates an anchor by adding an <see cref="ARAnchor"/> component to a new GameObject.
        /// AR Foundation adopts any GameObject carrying that component into the anchor subsystem,
        /// which avoids the async anchor-creation API and its awaitable plumbing.
        /// </summary>
        ARAnchor CreateAnchor(Vector3 worldPosition)
        {
            var go = new GameObject("NavAnchor");
            go.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);

            if (m_Parent != null)
                go.transform.SetParent(m_Parent.parent, true);

            var anchor = go.AddComponent<ARAnchor>();

            if (anchor != null)
                return anchor;

            Object.Destroy(go);
            return null;
        }

        /// <summary>Releases anchors that nothing is using. Call periodically, not every frame.</summary>
        public void PruneIdle()
        {
            for (var i = m_Anchors.Count - 1; i >= 0; i--)
            {
                var record = m_Anchors[i];

                if (record.anchor == null)
                {
                    m_Anchors.RemoveAt(i);
                    continue;
                }

                if (record.referenceCount > 0)
                {
                    record.lastUsedTime = Time.time;
                    continue;
                }

                if (Time.time - record.lastUsedTime < k_IdleTimeoutSeconds)
                    continue;

                Object.Destroy(record.anchor.gameObject);
                m_Anchors.RemoveAt(i);
            }
        }

        public void Clear()
        {
            foreach (var record in m_Anchors)
            {
                if (record.anchor != null)
                    Object.Destroy(record.anchor.gameObject);
            }

            m_Anchors.Clear();
        }
    }
}
