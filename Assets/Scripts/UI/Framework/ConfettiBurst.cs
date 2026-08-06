using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TirumalaAR.UI.Framework
{
    /// <summary>
    /// One-shot celebratory burst for the arrival card.
    ///
    /// Pure UGUI (small tinted <see cref="RoundedRectGraphic"/> squares animated by one
    /// coroutine) rather than a <c>ParticleSystem</c>, so it lives on the same canvas as
    /// everything else with no extra camera, render pass, or sorting-layer bookkeeping.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Confetti Burst")]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ConfettiBurst : MonoBehaviour
    {
        const int PieceCount = 24;
        const float Lifetime = 1.2f;
        const float Gravity = 900f;

        static readonly Color32[] k_Palette =
        {
            new Color32(0x1E, 0x88, 0xFF, 0xFF),
            new Color32(0x44, 0xD3, 0x62, 0xFF),
            new Color32(0xFF, 0xC8, 0x33, 0xFF),
            new Color32(0xCF, 0xA0, 0x3D, 0xFF),
            new Color32(0xFF, 0xFF, 0xFF, 0xFF),
        };

        readonly List<RectTransform> m_Pieces = new List<RectTransform>();
        readonly List<RoundedRectGraphic> m_Graphics = new List<RoundedRectGraphic>();
        readonly List<Vector2> m_Velocity = new List<Vector2>();

        Coroutine m_Routine;

        public void Play()
        {
            if (m_Routine != null)
                StopCoroutine(m_Routine);

            EnsurePieces();
            m_Routine = StartCoroutine(Run());
        }

        void EnsurePieces()
        {
            if (m_Pieces.Count > 0)
                return;

            for (var i = 0; i < PieceCount; i++)
            {
                var size = Random.Range(10f, 18f);
                var rect = UIFactory.Rect(transform, $"Piece_{i}", new Vector2(size, size));
                var graphic = rect.gameObject.AddComponent<RoundedRectGraphic>();
                graphic.color = k_Palette[i % k_Palette.Length];
                graphic.Radius = size * 0.3f;
                graphic.raycastTarget = false;

                m_Pieces.Add(rect);
                m_Graphics.Add(graphic);
                m_Velocity.Add(Vector2.zero);
            }
        }

        IEnumerator Run()
        {
            for (var i = 0; i < m_Pieces.Count; i++)
            {
                var angle = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                var speed = Random.Range(320f, 620f);
                m_Velocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

                var piece = m_Pieces[i];
                piece.anchoredPosition = Vector2.zero;
                piece.localRotation = Quaternion.identity;
                piece.gameObject.SetActive(true);

                var color = m_Graphics[i].color;
                color.a = 1f;
                m_Graphics[i].color = color;
            }

            var elapsed = 0f;

            while (elapsed < Lifetime)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / Lifetime);

                for (var i = 0; i < m_Pieces.Count; i++)
                {
                    var velocity = m_Velocity[i] - Vector2.up * (Gravity * elapsed);
                    var piece = m_Pieces[i];
                    piece.anchoredPosition += velocity * Time.unscaledDeltaTime;
                    piece.Rotate(0f, 0f, 360f * Time.unscaledDeltaTime * (i % 2 == 0 ? 1f : -1f));

                    var color = m_Graphics[i].color;
                    color.a = 1f - t;
                    m_Graphics[i].color = color;
                }

                yield return null;
            }

            foreach (var piece in m_Pieces)
                piece.gameObject.SetActive(false);

            m_Routine = null;
        }
    }
}
