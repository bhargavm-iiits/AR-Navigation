using TirumalaAR.Audio;
using TirumalaAR.Database;
using TirumalaAR.Managers;
using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// Settings page (screen 5). Matches the design exactly:
    /// Voice Guidance → Language → Units → Auto Brightness → Haptic Feedback → Offline Maps → About.
    ///
    /// Section headers are intentionally omitted to match the design sheet.
    /// Every change writes straight through to the settings repository so a mid-walk
    /// adjustment survives the app being killed on the hillside.
    /// </summary>
    public sealed class SettingsScreen : UIScreen
    {
        public override string Title => "Settings";
        public override IconGraphic.IconType TabIcon => IconGraphic.IconType.Gear;

        AppBootstrap m_Bootstrap;
        ISettingsRepository m_Settings;
        VoiceNavigationManager m_Voice;

        TMP_Text m_UnitsValue;

        protected override void BuildContent(RectTransform body)
        {
            m_Bootstrap = FindAnyObjectByType<AppBootstrap>();
            m_Settings  = m_Bootstrap?.Database?.Settings;
            m_Voice     = FindAnyObjectByType<VoiceNavigationManager>();

            // Back arrow on the left, matching the ← in the design header.
            var content = BuildHeader(body, Title, out var backBtn, out _, showBack: true);

            backBtn?.onClick.AddListener(() =>
                UIRoot.Instance?.ShowScreen<ARNavigationScreen>());

            UIFactory.ScrollColumn(content, "Scroll", Theme.spaceXs, out var column,
                new RectOffset(0, 0, 0, (int)Theme.spaceLg));

            // 1. Voice Guidance
            var voice = UIFactory.SettingsToggleRow(column, "Voice Guidance",
                m_Settings?.GetBool(SettingsKeys.VoiceEnabled, true) ?? true);
            voice.onValueChanged.AddListener(value =>
            {
                m_Settings?.Set(SettingsKeys.VoiceEnabled, value);
                m_Voice?.SetEnabled(value);
            });

            // 2. Language (single locale today — disclosure row)
            UIFactory.SettingsValueRow(column, "Language", "English");

            // 3. Units — tap to toggle metric / imperial
            m_UnitsValue = UIFactory.SettingsValueRow(column, "Units",
                (m_Settings?.GetBool(SettingsKeys.UnitsMetric, true) ?? true)
                    ? "Metric (m, km)" : "Imperial (ft, mi)");

            var unitsButton = m_UnitsValue.transform.parent.GetComponent<Button>();
            unitsButton?.onClick.AddListener(() =>
            {
                var metric = !(m_Settings?.GetBool(SettingsKeys.UnitsMetric, true) ?? true);
                m_Settings?.Set(SettingsKeys.UnitsMetric, metric);
                m_UnitsValue.text = metric ? "Metric (m, km)" : "Imperial (ft, mi)";
            });

            // 4. Auto Brightness
            var autoBrightness = UIFactory.SettingsToggleRow(column, "Auto Brightness",
                m_Settings?.GetBool(SettingsKeys.AutoBrightness, true) ?? true);
            autoBrightness.onValueChanged.AddListener(value =>
                m_Settings?.Set(SettingsKeys.AutoBrightness, value));

            // 5. Haptic Feedback
            var haptics = UIFactory.SettingsToggleRow(column, "Haptic Feedback",
                m_Settings?.GetBool(SettingsKeys.HapticFeedback, true) ?? true);
            haptics.onValueChanged.AddListener(value =>
                m_Settings?.Set(SettingsKeys.HapticFeedback, value));

            // 6. Offline Maps
            UIFactory.SettingsValueRow(column, "Offline Maps", "1 route cached");

            // 7. About — shows app version
            UIFactory.SettingsValueRow(column, "About", $"v{Application.version}");
        }
    }
}
