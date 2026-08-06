using System;
using System.Collections.Generic;
using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// Builds and owns the entire user interface.
    ///
    /// Drop this single component into the navigation scene and it constructs the canvas, the
    /// responsive scaler, all five screens, the bottom navigation bar and the modal overlays.
    /// Nothing else in the scene needs UI wiring, which is what keeps the generated scene from
    /// accumulating fragile inspector references.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }

        static UITheme Theme => UITheme.Current;

        [Tooltip("Screen shown when navigation starts.")]
        [SerializeField] string m_InitialScreen = nameof(ARNavigationScreen);

        readonly List<UIScreen> m_Screens = new List<UIScreen>();
        readonly Dictionary<UIScreen, RoundedRectGraphic> m_TabBackgrounds =
            new Dictionary<UIScreen, RoundedRectGraphic>();
        readonly Dictionary<UIScreen, IconGraphic> m_TabGlyphs = new Dictionary<UIScreen, IconGraphic>();
        readonly Dictionary<UIScreen, TMP_Text> m_TabLabels = new Dictionary<UIScreen, TMP_Text>();

        RectTransform m_ScreenHost;
        RectTransform m_NavBar;

        public UIScreen ActiveScreen { get; private set; }
        public UIScreen PreviousScreen { get; private set; }
        public NavigationOverlays Overlays { get; private set; }
        public UI.LandmarkPopup LandmarkPopup { get; private set; }
        public ResponsiveUIScaler Scaler { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Build()
        {
            var canvasGo = new GameObject("UI Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Well above the AR background, below nothing else.
            canvas.sortingOrder = 100;

            Scaler = canvasGo.AddComponent<ResponsiveUIScaler>();

            EnsureEventSystem();

            var root = (RectTransform)canvasGo.transform;

            m_ScreenHost = UIFactory.Rect(root, "Screens");
            UIFactory.Stretch(m_ScreenHost);

            CreateScreen<ARNavigationScreen>();
            CreateScreen<MapScreen>();
            CreateScreen<LandmarksScreen>();
            CreateScreen<ProgressScreen>();
            CreateScreen<SettingsScreen>();

            BuildNavBar(root);

            Overlays = gameObject.AddComponent<NavigationOverlays>();
            Overlays.Build(root);

            LandmarkPopup = gameObject.AddComponent<UI.LandmarkPopup>();
            LandmarkPopup.Build(root);

            gameObject.AddComponent<BackStackManager>();

            // Check if the player pressed View Map on the main menu â€” open Map tab directly.
            if (PlayerPrefs.GetInt("OpenMapOnStart", 0) == 1)
            {
                PlayerPrefs.DeleteKey("OpenMapOnStart");
                PlayerPrefs.Save();
                ShowScreen<MapScreen>();
            }
            else
            {
                ShowScreenByName(m_InitialScreen);
            }
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem));

            // The project runs with the Input System package active, so the legacy standalone
            // module would silently receive nothing.
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        T CreateScreen<T>() where T : UIScreen
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            go.transform.SetParent(m_ScreenHost, false);

            var screen = go.AddComponent<T>();
            screen.EnsureBuilt();
            screen.Hide();

            m_Screens.Add(screen);
            return screen;
        }

        // -----------------------------------------------------------------------------------
        // Bottom navigation
        // -----------------------------------------------------------------------------------

        void BuildNavBar(RectTransform root)
        {
            var bar = UIFactory.Rect(root, "Nav Bar");
            UIFactory.Stretch(bar);
            bar.gameObject.AddComponent<SafeAreaFitter>();

            var surface = UIFactory.Surface(bar, "Nav Surface", Theme.surfaceGlass, Theme.radiusLarge);
            UIFactory.AnchorBottom(surface.rectTransform, Theme.navBarHeight, Theme.spaceXs);
            surface.rectTransform.anchoredPosition = new Vector2(0f, Theme.spaceXs);
            m_NavBar = surface.rectTransform;

            var row = UIFactory.Row(m_NavBar, 0f, new RectOffset(8, 8, 8, 8));
            row.childForceExpandWidth = true;
            row.childControlWidth = true;
            row.childAlignment = TextAnchor.MiddleCenter;

            foreach (var screen in m_Screens)
            {
                if (!screen.ShowInNavBar)
                    continue;

                BuildTab(screen);
            }
        }

        void BuildTab(UIScreen screen)
        {
            var tab = UIFactory.Surface(m_NavBar, $"Tab_{screen.Title}", Color.clear, Theme.radiusMedium);
            tab.raycastTarget = true;

            var layout = tab.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;

            var button = tab.gameObject.AddComponent<Button>();
            button.targetGraphic = tab;

            var captured = screen;
            button.onClick.AddListener(() => ShowScreen(captured));

            var glyph = UIFactory.Icon(tab.transform, "Glyph", screen.TabIcon, 52f, Theme.textTertiary);
            glyph.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            glyph.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            glyph.rectTransform.pivot = new Vector2(0.5f, 1f);
            glyph.rectTransform.anchoredPosition = new Vector2(0f, -14f);

            var label = UIFactory.Label(tab.transform, "Label", ShortLabel(screen), Theme.textCaption,
                Theme.textTertiary, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0.38f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            m_TabBackgrounds[screen] = tab;
            m_TabGlyphs[screen] = glyph;
            m_TabLabels[screen] = label;
        }

        static string ShortLabel(UIScreen screen) => screen switch
        {
            ARNavigationScreen => "Navigate",
            MapScreen => "Map",
            LandmarksScreen => "Landmarks",
            ProgressScreen => "Progress",
            SettingsScreen => "Settings",
            _ => screen.Title
        };

        // -----------------------------------------------------------------------------------
        // Navigation
        // -----------------------------------------------------------------------------------

        public void ShowScreen<T>() where T : UIScreen
        {
            foreach (var screen in m_Screens)
            {
                if (screen is not T)
                    continue;

                ShowScreen(screen);
                return;
            }
        }

        public void ShowScreenByName(string typeName)
        {
            foreach (var screen in m_Screens)
            {
                if (!string.Equals(screen.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                ShowScreen(screen);
                return;
            }

            if (m_Screens.Count > 0)
                ShowScreen(m_Screens[0]);
        }

        public void ShowScreen(UIScreen target)
        {
            if (target == null || target == ActiveScreen)
                return;

            foreach (var screen in m_Screens)
            {
                if (screen == target)
                    screen.Show();
                else
                    screen.Hide();
            }

            if (ActiveScreen != null)
                PreviousScreen = ActiveScreen;

            ActiveScreen = target;

            // The nav bar sits above the AR view but must not cover the map's own bottom card,
            // so it is always drawn last.
            if (m_NavBar != null)
                m_NavBar.SetAsLastSibling();

            UpdateTabAppearance();
        }

        void UpdateTabAppearance()
        {
            foreach (var pair in m_TabBackgrounds)
            {
                var isActive = pair.Key == ActiveScreen;

                pair.Value.color = isActive ? Theme.accentSoft : Color.clear;

                if (m_TabGlyphs.TryGetValue(pair.Key, out var glyph))
                    glyph.color = isActive ? Theme.accent : Theme.textTertiary;

                if (m_TabLabels.TryGetValue(pair.Key, out var label))
                    label.color = isActive ? Theme.accent : Theme.textTertiary;
            }
        }
    }
}
