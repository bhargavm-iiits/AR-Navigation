using System.Collections.Generic;
using UnityEngine;

namespace TirumalaAR.Utilities
{
    /// <summary>Fixed-window moving average over scalars. Used for speed and GPS accuracy readouts.</summary>
    public sealed class MovingAverageFilter
    {
        readonly Queue<float> m_Window;
        readonly int m_Capacity;
        float m_Sum;

        public MovingAverageFilter(int capacity)
        {
            m_Capacity = Mathf.Max(1, capacity);
            m_Window = new Queue<float>(m_Capacity);
        }

        public int Count => m_Window.Count;
        public float Value => m_Window.Count == 0 ? 0f : m_Sum / m_Window.Count;

        public float Push(float sample)
        {
            if (float.IsNaN(sample) || float.IsInfinity(sample))
                return Value;

            m_Window.Enqueue(sample);
            m_Sum += sample;

            while (m_Window.Count > m_Capacity)
                m_Sum -= m_Window.Dequeue();

            return Value;
        }

        public void Reset()
        {
            m_Window.Clear();
            m_Sum = 0f;
        }
    }

    /// <summary>
    /// Circular moving average for compass headings. Averaging degrees naively breaks across the
    /// 0/360 wrap (350° and 10° must average to 0°, not 180°), so samples are accumulated as unit
    /// vectors and converted back with atan2.
    /// </summary>
    public sealed class CircularMeanFilter
    {
        readonly Queue<Vector2> m_Window;
        readonly int m_Capacity;
        Vector2 m_Sum;

        public CircularMeanFilter(int capacity)
        {
            m_Capacity = Mathf.Max(1, capacity);
            m_Window = new Queue<Vector2>(m_Capacity);
        }

        public int Count => m_Window.Count;

        /// <summary>Length of the mean resultant vector in [0,1]. Near 0 means the heading is unreliable.</summary>
        public float Consistency => m_Window.Count == 0 ? 0f : m_Sum.magnitude / m_Window.Count;

        public float Value
        {
            get
            {
                if (m_Window.Count == 0)
                    return 0f;

                var degrees = Mathf.Atan2(m_Sum.x, m_Sum.y) * Mathf.Rad2Deg;
                return Mathf.Repeat(degrees, 360f);
            }
        }

        public float Push(float degrees)
        {
            if (float.IsNaN(degrees))
                return Value;

            var rad = degrees * Mathf.Deg2Rad;
            var sample = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

            m_Window.Enqueue(sample);
            m_Sum += sample;

            while (m_Window.Count > m_Capacity)
                m_Sum -= m_Window.Dequeue();

            return Value;
        }

        public void Reset()
        {
            m_Window.Clear();
            m_Sum = Vector2.zero;
        }
    }

    /// <summary>
    /// One-euro style adaptive low-pass filter. Cuts jitter when the signal is still but stays
    /// responsive when it moves fast, which is exactly the behaviour arrows need so they neither
    /// shake while standing nor lag while walking.
    /// </summary>
    public sealed class AdaptiveLowPassFilter
    {
        readonly float m_MinCutoff;
        readonly float m_Beta;
        readonly float m_DerivativeCutoff;

        bool m_HasPrevious;
        float m_Previous;
        float m_PreviousDerivative;

        public AdaptiveLowPassFilter(float minCutoff = 1.0f, float beta = 0.02f, float derivativeCutoff = 1.0f)
        {
            m_MinCutoff = Mathf.Max(0.001f, minCutoff);
            m_Beta = beta;
            m_DerivativeCutoff = Mathf.Max(0.001f, derivativeCutoff);
        }

        static float Alpha(float cutoff, float dt)
        {
            var tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }

        public float Filter(float value, float dt)
        {
            if (dt <= 0f)
                return m_HasPrevious ? m_Previous : value;

            if (!m_HasPrevious)
            {
                m_HasPrevious = true;
                m_Previous = value;
                m_PreviousDerivative = 0f;
                return value;
            }

            var derivative = (value - m_Previous) / dt;
            var dAlpha = Alpha(m_DerivativeCutoff, dt);
            m_PreviousDerivative = Mathf.Lerp(m_PreviousDerivative, derivative, dAlpha);

            var cutoff = m_MinCutoff + m_Beta * Mathf.Abs(m_PreviousDerivative);
            var alpha = Alpha(cutoff, dt);
            m_Previous = Mathf.Lerp(m_Previous, value, alpha);

            return m_Previous;
        }

        public void Reset()
        {
            m_HasPrevious = false;
            m_Previous = 0f;
            m_PreviousDerivative = 0f;
        }
    }
}
