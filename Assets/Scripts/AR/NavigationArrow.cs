using UnityEngine;

namespace TirumalaAR.AR
{
    /// <summary>
    /// A single blue navigation arrow (Systems 7 and 9).
    ///
    /// Lives on the arrow prefab. Owns its own fade-in/fade-out, distance-based scaling and
    /// smoothing, so the arrow manager only has to say "be here, point that way" and never has to
    /// animate anything itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NavigationArrow : MonoBehaviour
    {
        enum Phase { Hidden, FadingIn, Visible, FadingOut }

        [Header("Appearance")]
        [SerializeField] Renderer[] m_Renderers;
        [SerializeField] float m_FadeInSeconds = 0.35f;
        [SerializeField] float m_FadeOutSeconds = 0.3f;

        [Header("Distance scaling")]
        [Tooltip("Arrow scale at the near distance.")]
        [SerializeField] float m_NearScale = 0.55f;
        [Tooltip("Arrow scale at the far distance. Larger so distant arrows stay legible.")]
        [SerializeField] float m_FarScale = 1.5f;
        [SerializeField] float m_NearDistance = 1.5f;
        [SerializeField] float m_FarDistance = 9f;

        [Header("Smoothing")]
        [Tooltip("Seconds for the arrow to converge on a new target position.")]
        [SerializeField] float m_PositionSmoothing = 0.12f;
        [Tooltip("Degrees per second the arrow may rotate.")]
        [SerializeField] float m_RotationSpeed = 360f;

        [Header("Motion")]
        [SerializeField] float m_BobAmplitude = 0.035f;
        [SerializeField] float m_BobFrequency = 1.4f;

        static readonly int k_BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int k_ColorId = Shader.PropertyToID("_Color");

        MaterialPropertyBlock m_PropertyBlock;
        Phase m_Phase = Phase.Hidden;
        float m_PhaseTime;
        float m_Alpha;
        float m_BobPhase;

        Vector3 m_TargetPosition;
        Quaternion m_TargetRotation = Quaternion.identity;
        Vector3 m_Velocity;
        bool m_Snapped;

        Color m_BaseColor = new Color(0.13f, 0.48f, 1f, 1f);

        /// <summary>Route distance this arrow represents; used by the manager for recycling.</summary>
        public float RouteDistance { get; set; }

        public bool IsRetiring => m_Phase == Phase.FadingOut;
        public bool IsFinished => m_Phase == Phase.Hidden;

        void Awake()
        {
            m_PropertyBlock = new MaterialPropertyBlock();

            if (m_Renderers == null || m_Renderers.Length == 0)
                m_Renderers = GetComponentsInChildren<Renderer>(true);

            m_BobPhase = Random.value * Mathf.PI * 2f;
        }

        /// <summary>Places the arrow immediately and starts its fade-in.</summary>
        public void Activate(Vector3 position, Quaternion rotation, Color color, float routeDistance)
        {
            m_BaseColor = color;
            RouteDistance = routeDistance;

            m_TargetPosition = position;
            m_TargetRotation = rotation;

            transform.SetPositionAndRotation(position, rotation);
            m_Velocity = Vector3.zero;
            m_Snapped = true;

            m_Phase = Phase.FadingIn;
            m_PhaseTime = 0f;
            m_Alpha = 0f;

            ApplyAlpha();
        }

        /// <summary>Updates where the arrow should be. Called every frame by the arrow manager.</summary>
        public void SetTarget(Vector3 position, Quaternion rotation)
        {
            m_TargetPosition = position;
            m_TargetRotation = rotation;
        }

        public void Retire()
        {
            if (m_Phase is Phase.FadingOut or Phase.Hidden)
                return;

            m_Phase = Phase.FadingOut;
            m_PhaseTime = 0f;
        }

        /// <summary>Driven manually by the arrow manager so update order is deterministic.</summary>
        public void Tick(float deltaTime, Vector3 cameraPosition)
        {
            if (m_Phase == Phase.Hidden)
                return;

            AdvancePhase(deltaTime);
            MoveTowardTarget(deltaTime);
            ScaleForDistance(cameraPosition);
            ApplyAlpha();
        }

        void AdvancePhase(float deltaTime)
        {
            m_PhaseTime += deltaTime;

            switch (m_Phase)
            {
                case Phase.FadingIn:
                    m_Alpha = m_FadeInSeconds <= 0f ? 1f : Mathf.Clamp01(m_PhaseTime / m_FadeInSeconds);
                    if (m_Alpha >= 1f)
                    {
                        m_Phase = Phase.Visible;
                        m_PhaseTime = 0f;
                    }
                    break;

                case Phase.Visible:
                    m_Alpha = 1f;
                    break;

                case Phase.FadingOut:
                    m_Alpha = m_FadeOutSeconds <= 0f ? 0f : 1f - Mathf.Clamp01(m_PhaseTime / m_FadeOutSeconds);
                    if (m_Alpha <= 0f)
                    {
                        m_Phase = Phase.Hidden;
                        gameObject.SetActive(false);
                    }
                    break;
            }
        }

        void MoveTowardTarget(float deltaTime)
        {
            // A gentle vertical bob makes the arrows read as guidance markers rather than as
            // objects lying on the ground, without breaking the illusion that they are placed.
            m_BobPhase += deltaTime * m_BobFrequency * Mathf.PI * 2f;
            var bob = Vector3.up * (Mathf.Sin(m_BobPhase) * m_BobAmplitude);

            if (m_Snapped)
            {
                transform.position = m_TargetPosition + bob;
                m_Snapped = false;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position, m_TargetPosition + bob, ref m_Velocity, m_PositionSmoothing);
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, m_TargetRotation, m_RotationSpeed * deltaTime);
        }

        /// <summary>
        /// Distant arrows are scaled up so they subtend a usable angle on screen; near arrows are
        /// scaled down so they do not swamp the view when the pilgrim walks over them.
        /// </summary>
        void ScaleForDistance(Vector3 cameraPosition)
        {
            var distance = Vector3.Distance(transform.position, cameraPosition);
            var t = Mathf.InverseLerp(m_NearDistance, m_FarDistance, distance);
            var scale = Mathf.Lerp(m_NearScale, m_FarScale, t);

            // Fold the fade into scale as well, so retiring arrows shrink away instead of just
            // becoming transparent.
            scale *= Mathf.Lerp(0.6f, 1f, m_Alpha);

            transform.localScale = Vector3.one * scale;
        }

        void ApplyAlpha()
        {
            if (m_Renderers == null)
                return;

            var color = m_BaseColor;
            color.a = m_Alpha;

            foreach (var renderer in m_Renderers)
            {
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor(k_BaseColorId, color);   // URP Lit / Unlit
                m_PropertyBlock.SetColor(k_ColorId, color);       // built-in fallback
                renderer.SetPropertyBlock(m_PropertyBlock);
            }
        }
    }
}
