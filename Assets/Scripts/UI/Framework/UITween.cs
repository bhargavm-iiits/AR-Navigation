using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TirumalaAR.UI.Framework
{
    /// <summary>
    /// Small coroutine-driven tween helpers for the UI's fades/scale-ins/slides/pulses.
    ///
    /// No tween library is referenced by this project, and pulling one in for a handful of short,
    /// simple curves would be a heavier dependency than writing them directly against Unity's own
    /// coroutines. Every method is a static entry point that runs on a single hidden, persistent
    /// runner object, since the callers are ordinary <see cref="MonoBehaviour"/>s scattered across
    /// screens rather than one owner that could host the coroutines itself.
    /// </summary>
    public static class UITween
    {
        sealed class Runner : MonoBehaviour { }

        static Runner s_Runner;

        static Runner EnsureRunner()
        {
            if (s_Runner != null)
                return s_Runner;

            var go = new GameObject("UITween Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_Runner = go.AddComponent<Runner>();
            return s_Runner;
        }

        public static Coroutine FadeCanvasGroup(CanvasGroup group, float from, float to, float duration,
            Action onComplete = null)
        {
            if (group == null)
                return null;

            return EnsureRunner().StartCoroutine(FadeRoutine(group, from, to, duration, onComplete));
        }

        static IEnumerator FadeRoutine(CanvasGroup group, float from, float to, float duration, Action onComplete)
        {
            group.alpha = from;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, EaseOutCubic(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            group.alpha = to;
            onComplete?.Invoke();
        }

        public static Coroutine ScaleIn(RectTransform rect, float from = 0.9f, float to = 1f, float duration = 0.2f)
        {
            if (rect == null)
                return null;

            return EnsureRunner().StartCoroutine(ScaleRoutine(rect, from, to, duration));
        }

        static IEnumerator ScaleRoutine(RectTransform rect, float from, float to, float duration)
        {
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var scale = Mathf.LerpUnclamped(from, to, EaseOutBack(Mathf.Clamp01(elapsed / duration)));
                rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            rect.localScale = new Vector3(to, to, 1f);
        }

        /// <summary>Slides a rect in from <paramref name="offset"/> pixels below its resting position.</summary>
        public static Coroutine SlideUp(RectTransform rect, float offset = 60f, float duration = 0.35f)
        {
            if (rect == null)
                return null;

            return EnsureRunner().StartCoroutine(SlideRoutine(rect, offset, duration));
        }

        static IEnumerator SlideRoutine(RectTransform rect, float offset, float duration)
        {
            var rest = rect.anchoredPosition;
            var start = rest - new Vector2(0f, offset);
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                rect.anchoredPosition =
                    Vector2.LerpUnclamped(start, rest, EaseOutCubic(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            rect.anchoredPosition = rest;
        }

        /// <summary>
        /// Continuous gentle scale "breathing" loop, used by the pause ring. Stop it with
        /// <see cref="StopPulse"/>, which also resets the rect to its resting scale.
        /// </summary>
        public static Coroutine Pulse(RectTransform rect, float minScale = 0.94f, float maxScale = 1.06f,
            float period = 1.1f)
        {
            if (rect == null)
                return null;

            return EnsureRunner().StartCoroutine(PulseRoutine(rect, minScale, maxScale, period));
        }

        static IEnumerator PulseRoutine(RectTransform rect, float minScale, float maxScale, float period)
        {
            var t = 0f;

            while (true)
            {
                t += Time.unscaledDeltaTime;
                var phase = Mathf.Sin(t / period * Mathf.PI * 2f) * 0.5f + 0.5f;
                var scale = Mathf.Lerp(minScale, maxScale, phase);
                rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
        }

        public static void StopPulse(RectTransform rect, Coroutine handle)
        {
            if (handle != null)
                EnsureRunner().StopCoroutine(handle);

            if (rect != null)
                rect.localScale = Vector3.one;
        }

        static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            var x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }

    /// <summary>
    /// Press-scale feedback (0.96 → 1.0) attached automatically to every button the design system
    /// builds (see <see cref="UIFactory"/>), so "every button scales on press" lives in one place
    /// instead of being repeated at each button call site.
    /// </summary>
    [AddComponentMenu("Tirumala AR/UI/Button Press Feedback")]
    public sealed class ButtonPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler
    {
        const float PressedScale = 0.96f;
        const float Duration = 0.08f;

        RectTransform m_Rect;
        Coroutine m_Routine;

        void Awake() => m_Rect = (RectTransform)transform;

        public void OnPointerDown(PointerEventData eventData) => AnimateTo(PressedScale);
        public void OnPointerUp(PointerEventData eventData) => AnimateTo(1f);
        public void OnPointerExit(PointerEventData eventData) => AnimateTo(1f);

        void AnimateTo(float target)
        {
            if (!isActiveAndEnabled)
                return;

            if (m_Routine != null)
                StopCoroutine(m_Routine);

            m_Routine = StartCoroutine(Animate(target));
        }

        IEnumerator Animate(float target)
        {
            var start = m_Rect.localScale.x;
            var elapsed = 0f;

            while (elapsed < Duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var scale = Mathf.Lerp(start, target, elapsed / Duration);
                m_Rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            m_Rect.localScale = new Vector3(target, target, 1f);
            m_Routine = null;
        }

        void OnDisable()
        {
            if (m_Rect != null)
                m_Rect.localScale = Vector3.one;
        }
    }
}
