using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Framework
{
    /// <summary>Broad device classes the layout reacts to.</summary>
    public enum DeviceClass
    {
        /// <summary>Narrow phone, roughly 16:9 and below 5.5".</summary>
        CompactPhone,

        /// <summary>Modern tall phone, 19.5:9 to 21:9.</summary>
        TallPhone,

        /// <summary>Large phone or small foldable, 6.7"+.</summary>
        LargePhone,

        /// <summary>7"+ tablet or unfolded foldable.</summary>
        Tablet
    }

    /// <summary>
    /// Adapts the canvas to the physical device (the "fit any Android device" requirement).
    ///
    /// Three separate problems get solved here, and they are genuinely distinct:
    ///
    ///   1. **Aspect ratio.** Android spans 4:3 tablets to 21:9 phones. A fixed
    ///      `matchWidthOrHeight` either overflows horizontally on narrow devices or leaves huge
    ///      dead margins on wide ones, so the match is computed from the live aspect.
    ///
    ///   2. **Physical size.** A 1080x1920 layout scaled onto a 10" tablet gives comically large
    ///      buttons, because the same pixel count covers far more inches. The reference
    ///      resolution is therefore widened for tablets, which shrinks everything proportionally.
    ///
    ///   3. **Touch ergonomics.** Android's accessibility guidance is a 48dp minimum target. On a
    ///      very dense small screen the reference-pixel sizes can fall below that, so the scaler
    ///      reports a multiplier that the factory applies to control sizes.
    ///
    /// Attach to the same GameObject as the Canvas and CanvasScaler.
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class ResponsiveUIScaler : MonoBehaviour
    {
        [Header("Reference layouts")]
        [SerializeField] Vector2 m_PhoneReference = new Vector2(1080f, 1920f);
        [SerializeField] Vector2 m_TabletReference = new Vector2(1600f, 2360f);

        [Tooltip("Diagonal in inches at or above which the tablet layout is used.")]
        [SerializeField] float m_TabletDiagonalInches = 6.9f;

        [Header("Aspect handling")]
        [Tooltip("Aspect (w/h) at or below which the scaler matches width exclusively.")]
        [SerializeField] float m_NarrowAspect = 0.44f;   // ~21:9 portrait

        [Tooltip("Aspect (w/h) at or above which the scaler matches height exclusively.")]
        [SerializeField] float m_WideAspect = 0.72f;     // ~4:3 portrait

        CanvasScaler m_Scaler;
        Vector2 m_LastScreen;

        public DeviceClass DeviceClass { get; private set; } = DeviceClass.TallPhone;

        /// <summary>Physical screen diagonal in inches, or 0 when the DPI is unknown.</summary>
        public float DiagonalInches { get; private set; }

        /// <summary>Multiplier the factory applies to control sizes to preserve touch targets.</summary>
        public float TouchScale { get; private set; } = 1f;

        public bool IsTablet => DeviceClass == DeviceClass.Tablet;

        void OnEnable()
        {
            m_Scaler = GetComponent<CanvasScaler>();
            Apply();
        }

        void Update()
        {
            var screen = new Vector2(Screen.width, Screen.height);

            // Re-evaluate only on an actual resolution change: rotation, a foldable opening, or
            // entering multi-window mode.
            if (screen == m_LastScreen)
                return;

            m_LastScreen = screen;
            Apply();
        }

        public void Apply()
        {
            if (m_Scaler == null)
                m_Scaler = GetComponent<CanvasScaler>();

            if (m_Scaler == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            DiagonalInches = ComputeDiagonalInches();
            DeviceClass = Classify(DiagonalInches);

            m_Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            m_Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            m_Scaler.referenceResolution = IsTablet ? m_TabletReference : m_PhoneReference;
            m_Scaler.matchWidthOrHeight = ComputeMatch();

            TouchScale = ComputeTouchScale();
        }

        float ComputeDiagonalInches()
        {
            var dpi = Screen.dpi;

            // Screen.dpi is unreliable or zero on some Android OEM builds; fall back to treating
            // the device as a typical phone rather than mis-classifying it as a tablet.
            if (dpi < 40f)
                return 0f;

            var widthInches = Screen.width / dpi;
            var heightInches = Screen.height / dpi;
            return Mathf.Sqrt(widthInches * widthInches + heightInches * heightInches);
        }

        DeviceClass Classify(float diagonal)
        {
            var aspect = (float)Screen.width / Screen.height;

            // Portrait-locked app, so aspect < 1. Normalise in case a tablet reports landscape.
            if (aspect > 1f)
                aspect = 1f / aspect;

            if (diagonal > 0f && diagonal >= m_TabletDiagonalInches)
                return DeviceClass.Tablet;

            // Without a trustworthy DPI, a wide aspect on a big pixel count is the best tablet
            // signal available.
            if (diagonal <= 0f && aspect > 0.68f && Mathf.Min(Screen.width, Screen.height) >= 1200)
                return DeviceClass.Tablet;

            if (diagonal >= 6.2f)
                return DeviceClass.LargePhone;

            return aspect <= 0.5f ? DeviceClass.TallPhone : DeviceClass.CompactPhone;
        }

        /// <summary>
        /// 0 matches width, 1 matches height. Narrow devices must match width or the layout
        /// overflows sideways; wide devices must match height or content runs off the bottom.
        /// </summary>
        float ComputeMatch()
        {
            var aspect = (float)Screen.width / Screen.height;

            if (aspect > 1f)
                aspect = 1f / aspect;

            return Mathf.Clamp01(Mathf.InverseLerp(m_NarrowAspect, m_WideAspect, aspect));
        }

        /// <summary>
        /// Ensures a reference-pixel control still lands above 48dp physically. On a compact
        /// dense phone the canvas scale shrinks everything, so controls are nudged back up.
        /// </summary>
        float ComputeTouchScale()
        {
            var reference = IsTablet ? m_TabletReference : m_PhoneReference;
            var canvasScale = Screen.width / reference.x;

            if (canvasScale <= 0f || Screen.dpi < 40f)
                return 1f;

            // 48dp in physical pixels. Android's density-independent pixel is 1/160 inch.
            var minimumPhysicalPx = 48f * (Screen.dpi / 160f);
            var referenceControlPx = UITheme.Current.touchTargetMin * canvasScale;

            if (referenceControlPx >= minimumPhysicalPx)
                return 1f;

            return Mathf.Clamp(minimumPhysicalPx / Mathf.Max(referenceControlPx, 1f), 1f, 1.5f);
        }
    }

    /// <summary>
    /// Insets a RectTransform to the device's safe area — notches, punch-holes, rounded corners
    /// and the gesture navigation bar.
    ///
    /// Every full-screen panel puts its content inside one of these. Without it the AR view's top
    /// pill slides under the camera cutout on most modern Android phones, and the bottom nav bar
    /// sits beneath the gesture handle.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("Apply the top inset (notch / status bar).")]
        [SerializeField] bool m_ApplyTop = true;

        [Tooltip("Apply the bottom inset (gesture bar).")]
        [SerializeField] bool m_ApplyBottom = true;

        [Tooltip("Apply left/right insets (landscape cutouts, curved edges).")]
        [SerializeField] bool m_ApplySides = true;

        RectTransform m_Rect;
        Rect m_LastSafeArea;
        Vector2Int m_LastScreen;

        void OnEnable()
        {
            m_Rect = GetComponent<RectTransform>();
            Apply();
        }

        void Update()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);

            if (Screen.safeArea == m_LastSafeArea && screen == m_LastScreen)
                return;

            Apply();
        }

        public void Apply()
        {
            if (m_Rect == null)
                m_Rect = GetComponent<RectTransform>();

            if (m_Rect == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            var safeArea = Screen.safeArea;
            m_LastSafeArea = safeArea;
            m_LastScreen = new Vector2Int(Screen.width, Screen.height);

            var min = safeArea.position;
            var max = safeArea.position + safeArea.size;

            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            // Guard against a bogus safe area (some emulators report an empty rect).
            if (float.IsNaN(min.x) || float.IsNaN(min.y) || max.x <= min.x || max.y <= min.y)
                return;

            if (!m_ApplySides)
            {
                min.x = 0f;
                max.x = 1f;
            }

            if (!m_ApplyBottom)
                min.y = 0f;

            if (!m_ApplyTop)
                max.y = 1f;

            m_Rect.anchorMin = min;
            m_Rect.anchorMax = max;
            m_Rect.offsetMin = Vector2.zero;
            m_Rect.offsetMax = Vector2.zero;
        }
    }
}
