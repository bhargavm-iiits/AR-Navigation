using UnityEngine;

namespace TirumalaAR.UI.Framework
{
    /// <summary>
    /// Design tokens for the whole app.
    ///
    /// Every colour, radius, spacing and type size lives here rather than being typed into
    /// individual screens, so the night-mode variant is a token swap instead of a rewrite, and so
    /// the responsive scaler can adjust one set of numbers and have the entire UI follow.
    ///
    /// Sizes are authored in *reference pixels* against a 1080x1920 canvas. The responsive layer
    /// converts those to device pixels, so a value here means "this size on a reference phone".
    /// </summary>
    [CreateAssetMenu(fileName = "UITheme", menuName = "Tirumala AR/UI Theme", order = 1)]
    public sealed class UITheme : ScriptableObject
    {
        public const string ResourcePath = "UITheme";

        [Header("Surfaces")]
        public Color background = new Color32(0x07, 0x13, 0x1D, 0xFF);
        public Color surface = new Color32(0x12, 0x1C, 0x29, 0xFF);
        public Color surfaceRaised = new Color32(0x16, 0x24, 0x35, 0xFF);
        public Color surfaceGlass = new Color32(0x14, 0x19, 0x23, 0xB8); // rgba(20,25,35,0.72)
        public Color toolbar = new Color32(0x0E, 0x18, 0x25, 0xFF);
        public Color border = new Color32(0x24, 0x38, 0x4F, 0xFF);
        public Color divider = new Color32(0x2A, 0x39, 0x4A, 0xFF); // cardDivider

        [Header("Content")]
        public Color textPrimary = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        public Color textSecondary = new Color32(0xC8, 0xD3, 0xE0, 0xFF);
        public Color textTertiary = new Color32(0x95, 0xA7, 0xBC, 0xFF); // hint text
        public Color iconWhite = new Color32(0xF5, 0xF5, 0xF5, 0xFF);

        [Header("Accents")]
        public Color accent = new Color32(0x1E, 0x88, 0xFF, 0xFF);       // Primary Blue
        public Color accentNav = new Color32(0x16, 0x95, 0xFF, 0xFF);    // Navigation Blue
        public Color accentDeep = new Color32(0x00, 0x78, 0xFF, 0xFF);   // Accent Blue
        public Color accentSoft = new Color32(0x1E, 0x88, 0xFF, 0x33);
        public Color success = new Color32(0x44, 0xD3, 0x62, 0xFF);
        public Color warning = new Color32(0xFF, 0xC8, 0x33, 0xFF);
        public Color danger = new Color32(0xFF, 0x52, 0x52, 0xFF);
        public Color gold = new Color32(0xCF, 0xA0, 0x3D, 0xFF);         // landmark gold
        public Color templeGold = new Color32(0xD6, 0xA5, 0x3E, 0xFF);
        public Color progressTrack = new Color32(0x2B, 0x3D, 0x52, 0xFF);

        [Header("Arrow (AR)")]
        public Color arrowBlue = new Color32(0x12, 0x97, 0xFF, 0xFF);
        public Color arrowBridged = new Color32(0xFF, 0xA6, 0x1A, 0xFF);

        [Header("Radii (reference px)")]
        public float radiusSmall = 12f;
        public float radiusMedium = 20f;
        public float radiusButton = 22f;
        public float radiusLarge = 32f;
        public float radiusPopup = 30f;
        public float radiusImage = 20f;
        public float radiusProgressCard = 26f;
        public float radiusPill = 999f;
        public float borderWidth = 1f;

        [Header("Spacing (reference px)")]
        public float spaceXs = 8f;
        public float spaceSm = 14f;
        public float spaceMd = 22f;
        public float spaceLg = 32f;
        public float spaceXl = 48f;

        [Header("Type scale (reference px)")]
        public float textDisplay = 64f;
        public float textTitle = 44f;
        public float textHeadline = 36f;
        public float textBody = 30f;
        public float textLabel = 26f;
        public float textCaption = 22f;

        [Header("Controls (reference px)")]
        public float touchTargetMin = 96f;   // ~48dp — the Android accessibility minimum
        public float buttonHeight = 104f;
        public float navBarHeight = 150f;
        public float circleButton = 96f;

        static UITheme s_Runtime;

        /// <summary>Loads the authored theme, or an in-memory default if none was created.</summary>
        public static UITheme Current
        {
            get
            {
                if (s_Runtime != null)
                    return s_Runtime;

                s_Runtime = Resources.Load<UITheme>(ResourcePath);

                if (s_Runtime == null)
                {
                    s_Runtime = CreateInstance<UITheme>();
                    s_Runtime.name = "UITheme (defaults)";
                }

                return s_Runtime;
            }
        }

        /// <summary>
        /// Day-mode variant. The AR view is used outdoors in direct sun on the hillside, where
        /// the dark palette becomes unreadable — this lifts surfaces and deepens text contrast
        /// without changing any layout.
        /// </summary>
        public void ApplyLightOverrides()
        {
            background = new Color32(0xF2, 0xF5, 0xF9, 0xFF);
            surface = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
            surfaceRaised = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
            surfaceGlass = new Color32(0xFF, 0xFF, 0xFF, 0xE6);
            divider = new Color32(0x00, 0x00, 0x00, 0x1A);
            textPrimary = new Color32(0x0B, 0x0F, 0x17, 0xFF);
            textSecondary = new Color32(0x4A, 0x56, 0x68, 0xFF);
            textTertiary = new Color32(0x77, 0x83, 0x95, 0xFF);
        }

        /// <summary>
        /// Night variant. A step darker/lower-contrast than the default dark theme, for use after
        /// sunset when even the standard palette reads as too bright against a black staircase.
        /// </summary>
        public void ApplyNightOverrides()
        {
            background = new Color32(0x04, 0x0A, 0x12, 0xFF);
            surface = new Color32(0x0C, 0x14, 0x1F, 0xFF);
            surfaceRaised = new Color32(0x10, 0x1B, 0x28, 0xFF);
            surfaceGlass = new Color32(0x0A, 0x0F, 0x16, 0xC2);
            toolbar = new Color32(0x08, 0x10, 0x19, 0xFF);
            textSecondary = new Color32(0xA6, 0xB2, 0xC2, 0xFF);
            textTertiary = new Color32(0x72, 0x80, 0x92, 0xFF);
        }
    }
}
