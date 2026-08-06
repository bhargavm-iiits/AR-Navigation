using System.Collections.Generic;
using UnityEngine;

namespace TirumalaAR.Core
{
    /// <summary>
    /// Prefab pool used for navigation arrows and landmark markers. Arrows are spawned and
    /// recycled continuously while walking, so allocating them per frame would cause GC spikes
    /// that break the 60 FPS budget.
    /// </summary>
    public sealed class ObjectPool
    {
        readonly GameObject m_Prefab;
        readonly Transform m_Parent;
        readonly Stack<GameObject> m_Available = new Stack<GameObject>();
        readonly HashSet<GameObject> m_InUse = new HashSet<GameObject>();
        readonly int m_MaxRetained;

        public int CountActive => m_InUse.Count;
        public int CountInactive => m_Available.Count;

        public ObjectPool(GameObject prefab, Transform parent, int prewarm = 0, int maxRetained = 64)
        {
            m_Prefab = prefab;
            m_Parent = parent;
            m_MaxRetained = Mathf.Max(1, maxRetained);

            for (var i = 0; i < prewarm; i++)
            {
                var instance = CreateInstance();
                instance.SetActive(false);
                m_Available.Push(instance);
            }
        }

        GameObject CreateInstance()
        {
            var instance = Object.Instantiate(m_Prefab, m_Parent);
            instance.name = $"{m_Prefab.name}_pooled";
            return instance;
        }

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            var instance = m_Available.Count > 0 ? m_Available.Pop() : CreateInstance();

            var t = instance.transform;
            t.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            m_InUse.Add(instance);
            return instance;
        }

        public void Release(GameObject instance)
        {
            if (instance == null || !m_InUse.Remove(instance))
                return;

            instance.SetActive(false);

            // Detach from any AR anchor before pooling so the anchor can be removed cleanly.
            if (instance.transform.parent != m_Parent)
                instance.transform.SetParent(m_Parent, false);

            if (m_Available.Count < m_MaxRetained)
                m_Available.Push(instance);
            else
                Object.Destroy(instance);
        }

        public void ReleaseAll()
        {
            if (m_InUse.Count == 0)
                return;

            var snapshot = new List<GameObject>(m_InUse);
            foreach (var instance in snapshot)
                Release(instance);
        }

        public void Dispose()
        {
            ReleaseAll();
            while (m_Available.Count > 0)
            {
                var instance = m_Available.Pop();
                if (instance != null)
                    Object.Destroy(instance);
            }
        }
    }
}
