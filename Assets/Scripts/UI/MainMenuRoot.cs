using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TirumalaAR.UI
{
    /// <summary>
    /// Minimal two-button Main Menu.
    ///
    /// Auto-installs itself when the MainMenu scene loads — no manual Unity Editor wiring.
    /// Destroys any pre-existing hand-authored Canvas so only this UI is visible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuRoot : MonoBehaviour
    {
        static UITheme Theme => UITheme.Current;

        [Tooltip("Navigation scene to load when Start Walk is pressed.")]
        [SerializeField] string m_NavigationSceneName = "NavigationScene";

        // -------------------------------------------------------------------------------
        // Auto-install
        // -------------------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu")
                return;

            if (FindAnyObjectByType<MainMenuRoot>() != null)
                return;

            var go = new GameObject("MainMenuRoot");
            go.AddComponent<MainMenuRoot>();
        }

        // -------------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------------

        void Awake()
        {
            DestroyOldCanvases();
            EnsureEventSystem();
            Build();
        }

        void DestroyOldCanvases()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.transform.parent == null)
                    Destroy(canvas.gameObject);
        }

        // -------------------------------------------------------------------------------
        // Build
        // -------------------------------------------------------------------------------

        void Build()
        {
            var canvasGo = new GameObject("UI Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            canvasGo.AddComponent<ResponsiveUIScaler>();

            var root = (RectTransform)canvasGo.transform;

            // --- Background ---
            var bg = UIFactory.Rect(root, "Background");
            UIFactory.Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = Theme.background;
            bgImg.raycastTarget = false;

            // Subtle gradient overlay at bottom (mimics the design's vignette)
            var vignette = UIFactory.Rect(root, "Vignette");
            UIFactory.Stretch(vignette);
            var vigImg = vignette.gameObject.AddComponent<Image>();
            vigImg.color = new Color(0f, 0f, 0f, 0f);
            vigImg.raycastTarget = false;

            // --- Safe area ---
            var safe = UIFactory.Rect(root, "Safe Area");
            UIFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ---------------------------------------------------------------
            // Header block — temple icon + title + subtitle (upper 55%)
            // ---------------------------------------------------------------
            var header = UIFactory.Rect(safe, "Header");
            header.anchorMin = new Vector2(0f, 0.38f);
            header.anchorMax = new Vector2(1f, 1f);
            header.offsetMin = Vector2.zero;
            header.offsetMax = Vector2.zero;

            // Temple icon
            var icon = UIFactory.Icon(header, "Temple", IconGraphic.IconType.Temple,
                120f, Theme.templeGold);
            icon.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            icon.rectTransform.pivot    = new Vector2(0.5f, 1f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceXl);

            // App title
            var title = UIFactory.Label(header, "Title", "Tirumala AR Navigation",
                Theme.textTitle, Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            title.rectTransform.anchorMin = new Vector2(0f, 0.42f);
            title.rectTransform.anchorMax = new Vector2(1f, 0.74f);
            title.rectTransform.offsetMin = new Vector2(Theme.spaceLg, 0f);
            title.rectTransform.offsetMax = new Vector2(-Theme.spaceLg, 0f);
            title.textWrappingMode = TextWrappingModes.Normal;

            // Subtitle
            var subtitle = UIFactory.Label(header, "Subtitle",
                "Alipiri Mettu  \u00b7  Offline guidance",
                Theme.textBody, Theme.textTertiary, TextAlignmentOptions.Center);
            subtitle.rectTransform.anchorMin = new Vector2(0f, 0.20f);
            subtitle.rectTransform.anchorMax = new Vector2(1f, 0.42f);
            subtitle.rectTransform.offsetMin = new Vector2(Theme.spaceLg, 0f);
            subtitle.rectTransform.offsetMax = new Vector2(-Theme.spaceLg, 0f);

            // Thin divider
            var divider = UIFactory.Rect(header, "Divider", new Vector2(0f, 1f));
            divider.anchorMin = new Vector2(0.15f, 0f);
            divider.anchorMax = new Vector2(0.85f, 0f);
            divider.pivot     = new Vector2(0.5f, 0f);
            divider.offsetMin = Vector2.zero;
            divider.offsetMax = Vector2.zero;
            var dividerImg = divider.gameObject.AddComponent<Image>();
            dividerImg.color = Theme.border;

            // ---------------------------------------------------------------
            // Two action buttons (lower 38%)
            // ---------------------------------------------------------------
            var buttonArea = UIFactory.Rect(safe, "Buttons");
            buttonArea.anchorMin = new Vector2(0f, 0f);
            buttonArea.anchorMax = new Vector2(1f, 0.38f);
            buttonArea.offsetMin = new Vector2(Theme.spaceLg, 0f);
            buttonArea.offsetMax = new Vector2(-Theme.spaceLg, 0f);

            // ---- Button 1: Start Walk (large blue) ----
            var startBg = UIFactory.Pill(buttonArea, "StartWalk", Theme.accent,
                new Vector2(0f, Theme.buttonHeight));
            UIFactory.Stretch(startBg.rectTransform);
            startBg.rectTransform.anchorMin = new Vector2(0f, 0.62f);
            startBg.rectTransform.anchorMax = new Vector2(1f, 0.88f);
            startBg.rectTransform.offsetMin = Vector2.zero;
            startBg.rectTransform.offsetMax = Vector2.zero;
            startBg.raycastTarget = true;

            var startBtn = startBg.gameObject.AddComponent<Button>();
            startBtn.targetGraphic = startBg;

            // Icon + Label row
            var startIcon = UIFactory.Icon(startBg.transform, "Icon",
                IconGraphic.IconType.Navigate, 48f, Theme.textPrimary);
            startIcon.rectTransform.anchorMin = new Vector2(0.25f, 0.5f);
            startIcon.rectTransform.anchorMax = new Vector2(0.25f, 0.5f);
            startIcon.rectTransform.anchoredPosition = Vector2.zero;

            var startLabel = UIFactory.Label(startBg.transform, "Label", "Start Walk",
                Theme.textBody, Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.SemiBold);
            UIFactory.Stretch(startLabel.rectTransform, Theme.spaceSm);

            startBg.gameObject.AddComponent<ButtonPressFeedback>();
            startBtn.onClick.AddListener(StartWalk);

            // ---- Button 2: View Map (glass) ----
            var mapBg = UIFactory.Pill(buttonArea, "ViewMap", Theme.surfaceRaised,
                new Vector2(0f, Theme.buttonHeight));
            mapBg.rectTransform.anchorMin = new Vector2(0f, 0.30f);
            mapBg.rectTransform.anchorMax = new Vector2(1f, 0.56f);
            mapBg.rectTransform.offsetMin = Vector2.zero;
            mapBg.rectTransform.offsetMax = Vector2.zero;
            mapBg.raycastTarget = true;

            var mapBtn = mapBg.gameObject.AddComponent<Button>();
            mapBtn.targetGraphic = mapBg;

            var mapIcon = UIFactory.Icon(mapBg.transform, "Icon",
                IconGraphic.IconType.Map, 48f, Theme.textSecondary);
            mapIcon.rectTransform.anchorMin = new Vector2(0.25f, 0.5f);
            mapIcon.rectTransform.anchorMax = new Vector2(0.25f, 0.5f);
            mapIcon.rectTransform.anchoredPosition = Vector2.zero;

            var mapLabel = UIFactory.Label(mapBg.transform, "Label", "View Map",
                Theme.textBody, Theme.textSecondary, TextAlignmentOptions.Center, FontWeight.Medium);
            UIFactory.Stretch(mapLabel.rectTransform, Theme.spaceSm);

            mapBg.gameObject.AddComponent<ButtonPressFeedback>();
            mapBtn.onClick.AddListener(GoToMap);

            // ---- Settings gear (small, bottom-right) ----
            var settings = UIFactory.IconButton(buttonArea, "Settings",
                IconGraphic.IconType.Gear, Theme.surfaceGlass);
            var sRect = settings.GetComponent<RectTransform>();
            sRect.anchorMin = new Vector2(1f, 0f);
            sRect.anchorMax = new Vector2(1f, 0f);
            sRect.pivot     = new Vector2(1f, 0f);
            sRect.anchoredPosition = new Vector2(0f, Theme.spaceLg);

            // Version label
            var ver = UIFactory.Label(buttonArea, "Version",
                $"v{Application.version}", Theme.textCaption, Theme.textTertiary,
                TextAlignmentOptions.Left);
            ver.rectTransform.anchorMin = new Vector2(0f, 0f);
            ver.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            ver.rectTransform.pivot     = new Vector2(0f, 0f);
            ver.rectTransform.anchoredPosition = new Vector2(0f, Theme.spaceLg + 28f);
            ver.rectTransform.sizeDelta = new Vector2(0f, 44f);

            // Settings listener — load NavigationScene to reach the Settings screen
            settings.onClick.AddListener(StartWalk);
        }

        // -------------------------------------------------------------------------------
        // Actions
        // -------------------------------------------------------------------------------

        void StartWalk()
        {
            SceneManager.LoadScene(m_NavigationSceneName);
        }

        void GoToMap()
        {
            // Load the navigation scene and request the Map tab on first open
            PlayerPrefs.SetInt("OpenMapOnStart", 1);
            PlayerPrefs.Save();
            SceneManager.LoadScene(m_NavigationSceneName);
        }

        // -------------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------------

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
