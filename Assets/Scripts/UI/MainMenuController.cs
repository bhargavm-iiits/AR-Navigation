using TirumalaAR.Database;
using TirumalaAR.Database.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TirumalaAR.UI
{
    /// <summary>
    /// Main menu logic (MainMenu scene). Deliberately does no navigation work — it only reads the
    /// saved settings and history, then hands off to NavigationScene where AppBootstrap builds the
    /// real object graph.
    ///
    /// References are no longer wired in the Inspector. Instead, MainMenuRoot builds the UI
    /// procedurally and calls ReceiveReferences() to hand everything in at runtime. This means
    /// there are no fragile Inspector links that can silently break when the scene is rebuilt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        // Runtime references — set by MainMenuRoot.Build() via ReceiveReferences()
        string m_NavigationSceneName = "NavigationScene";
        Button m_StartButton;
        Button m_ResumeButton;
        Button m_ResetProgressButton;
        Button m_QuitButton;
        Toggle m_VoiceToggle;
        Toggle m_DarkModeToggle;
        Slider m_ArrowSpacingSlider;
        TMP_Text m_ArrowSpacingLabel;
        TMP_Text m_LastWalkLabel;
        TMP_Text m_VersionLabel;

        IDatabase m_Database;

        /// <summary>
        /// Called by MainMenuRoot immediately after building the UI. Receives all widget references
        /// so the controller can bind them without Inspector wiring.
        /// </summary>
        public void ReceiveReferences(
            string navigationSceneName,
            Button startButton,
            Button resumeButton,
            Button resetButton,
            Button quitButton,
            Toggle voiceToggle,
            Toggle darkModeToggle,
            Slider arrowSpacingSlider,
            TMP_Text arrowSpacingLabel,
            TMP_Text lastWalkLabel,
            TMP_Text versionLabel)
        {
            m_NavigationSceneName = navigationSceneName;
            m_StartButton = startButton;
            m_ResumeButton = resumeButton;
            m_ResetProgressButton = resetButton;
            m_QuitButton = quitButton;
            m_VoiceToggle = voiceToggle;
            m_DarkModeToggle = darkModeToggle;
            m_ArrowSpacingSlider = arrowSpacingSlider;
            m_ArrowSpacingLabel = arrowSpacingLabel;
            m_LastWalkLabel = lastWalkLabel;
            m_VersionLabel = versionLabel;

            // The menu opens its own short-lived database handle; the navigation scene opens its
            // own. Both point at the same file, and the menu always flushes before leaving.
            m_Database = new JsonDatabase();

            BindButtons();
            LoadSettings();
            ShowLastWalk();

            if (m_VersionLabel != null)
                m_VersionLabel.text = $"v{Application.version}";
        }

        void BindButtons()
        {
            if (m_StartButton != null)
                m_StartButton.onClick.AddListener(() => StartWalk(true));

            if (m_ResumeButton != null)
                m_ResumeButton.onClick.AddListener(() => StartWalk(false));

            if (m_ResetProgressButton != null)
                m_ResetProgressButton.onClick.AddListener(ResetProgress);

            if (m_QuitButton != null)
                m_QuitButton.onClick.AddListener(Quit);

            if (m_VoiceToggle != null)
                m_VoiceToggle.onValueChanged.AddListener(v =>
                    m_Database.Settings.Set(SettingsKeys.VoiceEnabled, v));

            if (m_DarkModeToggle != null)
                m_DarkModeToggle.onValueChanged.AddListener(v =>
                    m_Database.Settings.Set(SettingsKeys.DarkMode, v));

            if (m_ArrowSpacingSlider != null)
                m_ArrowSpacingSlider.onValueChanged.AddListener(OnArrowSpacingChanged);
        }

        void LoadSettings()
        {
            var settings = m_Database.Settings;

            if (m_VoiceToggle != null)
                m_VoiceToggle.isOn = settings.GetBool(SettingsKeys.VoiceEnabled, true);

            if (m_DarkModeToggle != null)
                m_DarkModeToggle.isOn = settings.GetBool(SettingsKeys.DarkMode, true);

            if (m_ArrowSpacingSlider == null)
                return;

            m_ArrowSpacingSlider.minValue = 0.4f;
            m_ArrowSpacingSlider.maxValue = 2f;
            m_ArrowSpacingSlider.value = settings.GetFloat(SettingsKeys.ArrowSpacing, 0.75f);
            OnArrowSpacingChanged(m_ArrowSpacingSlider.value);
        }

        void OnArrowSpacingChanged(float value)
        {
            m_Database.Settings.Set(SettingsKeys.ArrowSpacing, value);

            if (m_ArrowSpacingLabel != null)
                m_ArrowSpacingLabel.text = $"Arrow spacing  {value:F2} m";
        }

        void ShowLastWalk()
        {
            if (m_LastWalkLabel == null)
                return;

            var history = m_Database.History.GetAll();

            if (history.Count == 0)
            {
                m_LastWalkLabel.text = "No previous walks recorded.";

                if (m_ResumeButton != null)
                    m_ResumeButton.interactable = false;

                return;
            }

            var last = history[history.Count - 1];

            m_LastWalkLabel.text = last.completed
                ? $"Last walk: completed, {NavigationHUD.FormatDistance(last.distanceCovered)} " +
                  $"in {NavigationHUD.FormatDuration(last.durationSeconds)}."
                : $"Last walk: {NavigationHUD.FormatDistance(last.distanceCovered)} covered, " +
                  $"{last.landmarksVisited} landmark(s) seen.";

            if (m_ResumeButton != null)
                m_ResumeButton.interactable = !last.completed;
        }

        void StartWalk(bool fromBeginning)
        {
            if (fromBeginning)
                m_Database.Landmarks.ResetVisited();

            m_Database.Flush();
            m_Database.Dispose();
            m_Database = null;

            SceneManager.LoadScene(m_NavigationSceneName);
        }

        void ResetProgress()
        {
            m_Database.Landmarks.ResetVisited();
            m_Database.History.Clear();
            m_Database.Flush();
            ShowLastWalk();
        }

        void Quit()
        {
            m_Database?.Flush();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            m_Database?.Dispose();
            m_Database = null;
        }
    }
}
