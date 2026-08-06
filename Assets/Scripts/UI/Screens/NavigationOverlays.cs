using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Database;
using TirumalaAR.Managers;
using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// The transient overlays from the design: turn instruction (7), arrival (8), weak GPS (9),
    /// paused (10), plus the exit-confirmation dialog the hardware back button opens from the AR
    /// screen (not itself one of the numbered mockup panels).
    ///
    /// They share one component because they are mutually exclusive by nature — the pilgrim
    /// should never see a turn card stacked on an arrival card — and a single owner makes that
    /// guarantee structural rather than a convention someone has to remember.
    /// </summary>
    public sealed class NavigationOverlays : MonoBehaviour
    {
        enum Overlay { None, Turn, Arrival, GpsWarning, Paused, ExitConfirm }

        static UITheme Theme => UITheme.Current;

        AppBootstrap m_Bootstrap;
        RectTransform m_Root;

        // Turn
        RectTransform m_TurnPanel;
        CanvasGroup m_TurnGroup;
        ChevronGraphic m_TurnArrow;
        TMP_Text m_TurnTitle;
        TMP_Text m_TurnDistance;
        TMP_Text m_TurnTowards;

        // Arrival
        RectTransform m_ArrivalPanel;
        CanvasGroup m_ArrivalGroup;
        TMP_Text m_ArrivalName;
        ConfettiBurst m_Confetti;

        // GPS
        RectTransform m_GpsPanel;
        CanvasGroup m_GpsGroup;
        TMP_Text m_GpsBody;

        // Paused
        RectTransform m_PausedPanel;
        CanvasGroup m_PausedGroup;
        RectTransform m_PauseRingRect;
        Coroutine m_PausePulse;

        // Exit confirmation (driven by BackStackManager, not part of the numbered mockups)
        RectTransform m_ExitConfirmPanel;
        CanvasGroup m_ExitConfirmGroup;

        Overlay m_Active = Overlay.None;
        float m_HideAt;

        public bool IsPaused { get; private set; }
        public bool IsExitConfirmShown => m_Active == Overlay.ExitConfirm;
        public bool HasBlockingOverlay => m_Active is Overlay.Turn or Overlay.Arrival or Overlay.GpsWarning;

        public void Build(RectTransform parent)
        {
            m_Bootstrap = FindAnyObjectByType<AppBootstrap>();

            m_Root = UIFactory.Rect(parent, "Overlays");
            UIFactory.Stretch(m_Root);

            var safe = UIFactory.Rect(m_Root, "Safe Area");
            safe.gameObject.AddComponent<SafeAreaFitter>();

            BuildTurnPanel(safe);
            BuildArrivalPanel(safe);
            BuildGpsPanel(safe);
            BuildPausedPanel(safe);
            BuildExitConfirmPanel(safe);

            HideAll();
        }

        // -------------------------------------------------------------------------------
        // Panels
        // -------------------------------------------------------------------------------

        void BuildTurnPanel(RectTransform parent)
        {
            var card = UIFactory.Surface(parent, "Turn Panel", Theme.surfaceGlass, Theme.radiusPopup,
                new Vector2(620f, 620f));
            card.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_TurnPanel = card.rectTransform;
            m_TurnGroup = card.gameObject.AddComponent<CanvasGroup>();

            m_TurnTitle = UIFactory.Label(card.transform, "Title", "Turn Left", Theme.textTitle,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            UIFactory.AnchorTop(m_TurnTitle.rectTransform, 80f, Theme.spaceMd);
            m_TurnTitle.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceLg);

            m_TurnDistance = UIFactory.Label(card.transform, "Distance", "in 50 m", Theme.textBody,
                Theme.textSecondary, TextAlignmentOptions.Center);
            UIFactory.AnchorTop(m_TurnDistance.rectTransform, 60f, Theme.spaceMd);
            m_TurnDistance.rectTransform.anchoredPosition = new Vector2(0f, -(Theme.spaceLg + 84f));

            var arrowRect = UIFactory.Rect(card.transform, "Arrow", new Vector2(230f, 190f));
            arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(0f, 10f);

            m_TurnArrow = arrowRect.gameObject.AddComponent<ChevronGraphic>();
            m_TurnArrow.Facing = ChevronGraphic.Direction.Left;
            m_TurnArrow.color = Theme.accent;
            m_TurnArrow.raycastTarget = false;

            m_TurnTowards = UIFactory.Label(card.transform, "Towards", "", Theme.textLabel,
                Theme.textSecondary, TextAlignmentOptions.Center);
            UIFactory.AnchorBottom(m_TurnTowards.rectTransform, 110f, Theme.spaceMd);
            m_TurnTowards.rectTransform.anchoredPosition = new Vector2(0f, Theme.spaceMd + 46f);

            BuildPaginationDots(card.transform);
        }

        /// <summary>
        /// Purely decorative step markers under the "towards" label, matching the design — the
        /// turn service doesn't expose a multi-step lookahead queue to bind a real position to.
        /// </summary>
        static void BuildPaginationDots(Transform parent)
        {
            const int dotCount = 4;
            const float dotSize = 14f;
            const float gap = 12f;

            var row = UIFactory.Rect(parent, "Dots", new Vector2(dotCount * dotSize + (dotCount - 1) * gap, dotSize));
            row.anchorMin = new Vector2(0.5f, 0f);
            row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, Theme.spaceMd);

            var layout = UIFactory.Row(row, gap);
            layout.childAlignment = TextAnchor.MiddleCenter;

            for (var i = 0; i < dotCount; i++)
            {
                var dot = UIFactory.Pill(row, $"Dot_{i}", i == 0 ? Theme.accent : Theme.surfaceRaised,
                    new Vector2(dotSize, dotSize));
                dot.gameObject.AddComponent<LayoutElement>().preferredWidth = dotSize;
            }
        }

        void BuildArrivalPanel(RectTransform parent)
        {
            var card = UIFactory.Surface(parent, "Arrival Panel", Theme.surfaceGlass, Theme.radiusPopup,
                new Vector2(660f, 640f));
            card.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_ArrivalPanel = card.rectTransform;
            m_ArrivalGroup = card.gameObject.AddComponent<CanvasGroup>();

            var badge = UIFactory.Pill(card.transform, "Check", Theme.success, new Vector2(170f, 170f));
            badge.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            badge.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            badge.rectTransform.pivot = new Vector2(0.5f, 1f);
            badge.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceLg);

            var tick = UIFactory.Icon(badge.transform, "Tick", IconGraphic.IconType.Check, 96f);
            tick.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            tick.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            tick.rectTransform.anchoredPosition = Vector2.zero;

            m_Confetti = badge.gameObject.AddComponent<ConfettiBurst>();

            var caption = UIFactory.Label(card.transform, "Caption", "You have reached",
                Theme.textBody, Theme.textSecondary, TextAlignmentOptions.Center);
            caption.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            caption.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            caption.rectTransform.sizeDelta = new Vector2(0f, 56f);
            caption.rectTransform.anchoredPosition = new Vector2(0f, -20f);

            m_ArrivalName = UIFactory.Label(card.transform, "Name", "", Theme.textTitle,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            m_ArrivalName.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            m_ArrivalName.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            m_ArrivalName.rectTransform.sizeDelta = new Vector2(-Theme.spaceLg, 110f);
            m_ArrivalName.rectTransform.anchoredPosition = new Vector2(0f, -100f);

            var next = UIFactory.PrimaryButton(card.transform, "Next Landmark", Theme.accent, 460f);
            var nextRect = next.GetComponent<RectTransform>();
            nextRect.anchorMin = new Vector2(0.5f, 0f);
            nextRect.anchorMax = new Vector2(0.5f, 0f);
            nextRect.pivot = new Vector2(0.5f, 0f);
            nextRect.anchoredPosition = new Vector2(0f, Theme.spaceLg);
            next.onClick.AddListener(HideAll);
        }

        void BuildGpsPanel(RectTransform parent)
        {
            var card = UIFactory.Surface(parent, "Gps Panel", Theme.surfaceGlass, Theme.radiusPopup,
                new Vector2(640f, 560f));
            card.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_GpsPanel = card.rectTransform;
            m_GpsGroup = card.gameObject.AddComponent<CanvasGroup>();

            var badge = UIFactory.Surface(card.transform, "Warning", Theme.warning, Theme.radiusMedium,
                new Vector2(150f, 150f));
            badge.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            badge.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            badge.rectTransform.pivot = new Vector2(0.5f, 1f);
            badge.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceLg);

            var bang = UIFactory.Icon(badge.transform, "Bang", IconGraphic.IconType.Warning, 96f,
                new Color32(0x0B, 0x0F, 0x17, 0xFF));
            bang.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            bang.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bang.rectTransform.anchoredPosition = Vector2.zero;

            var title = UIFactory.Label(card.transform, "Title", "GPS Signal Weak", Theme.textHeadline,
                Theme.warning, TextAlignmentOptions.Center, FontWeight.Bold);
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(-Theme.spaceLg, 70f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -10f);

            m_GpsBody = UIFactory.Label(card.transform, "Body",
                "Guidance continues from camera tracking. Step into the open for a better fix.",
                Theme.textLabel, Theme.textSecondary, TextAlignmentOptions.Center);
            m_GpsBody.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            m_GpsBody.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            m_GpsBody.rectTransform.sizeDelta = new Vector2(-Theme.spaceLg * 2f, 130f);
            m_GpsBody.rectTransform.anchoredPosition = new Vector2(0f, -110f);

            var ok = UIFactory.PrimaryButton(card.transform, "OK", Theme.warning, 420f);
            var okRect = ok.GetComponent<RectTransform>();
            okRect.anchorMin = new Vector2(0.5f, 0f);
            okRect.anchorMax = new Vector2(0.5f, 0f);
            okRect.pivot = new Vector2(0.5f, 0f);
            okRect.anchoredPosition = new Vector2(0f, Theme.spaceLg);
            ok.onClick.AddListener(HideAll);

            // The OK label needs dark text on the amber fill to stay readable.
            ok.GetComponentInChildren<TMP_Text>().color = new Color32(0x0B, 0x0F, 0x17, 0xFF);
        }

        void BuildPausedPanel(RectTransform parent)
        {
            var card = UIFactory.Surface(parent, "Paused Panel", Theme.surfaceGlass, Theme.radiusPopup,
                new Vector2(640f, 660f));
            card.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_PausedPanel = card.rectTransform;
            m_PausedGroup = card.gameObject.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "Navigation Paused",
                Theme.textHeadline, Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            UIFactory.AnchorTop(title.rectTransform, 80f, Theme.spaceMd);
            title.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceLg);

            var ring = UIFactory.Rect(card.transform, "PauseRing", new Vector2(190f, 190f));
            ring.anchorMin = new Vector2(0.5f, 0.5f);
            ring.anchorMax = new Vector2(0.5f, 0.5f);
            ring.anchoredPosition = new Vector2(0f, 60f);
            m_PauseRingRect = ring;

            var ringGraphic = ring.gameObject.AddComponent<ProgressRingGraphic>();
            ringGraphic.color = Theme.accent;
            ringGraphic.Fill = 1f;
            ringGraphic.Thickness = 8f;
            ringGraphic.raycastTarget = false;

            var pauseGlyph = UIFactory.Icon(ring, "Glyph", IconGraphic.IconType.Pause, 90f, Theme.accent);
            pauseGlyph.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            pauseGlyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            pauseGlyph.rectTransform.anchoredPosition = Vector2.zero;

            var resume = UIFactory.PrimaryButton(card.transform, "Resume", Theme.accent, 460f);
            var resumeRect = resume.GetComponent<RectTransform>();
            resumeRect.anchorMin = new Vector2(0.5f, 0f);
            resumeRect.anchorMax = new Vector2(0.5f, 0f);
            resumeRect.pivot = new Vector2(0.5f, 0f);
            resumeRect.anchoredPosition = new Vector2(0f, Theme.spaceLg + Theme.buttonHeight + Theme.spaceSm);
            resume.onClick.AddListener(Resume);

            var end = UIFactory.SecondaryButton(card.transform, "End Navigation", 460f);
            var endRect = end.GetComponent<RectTransform>();
            endRect.anchorMin = new Vector2(0.5f, 0f);
            endRect.anchorMax = new Vector2(0.5f, 0f);
            endRect.pivot = new Vector2(0.5f, 0f);
            endRect.anchoredPosition = new Vector2(0f, Theme.spaceLg);
            end.onClick.AddListener(() =>
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu"));
        }

        /// <summary>
        /// "Exit app? Yes / No" — reached only from the hardware back button on the AR screen
        /// (see <see cref="BackStackManager"/>), never from a mockup button.
        /// </summary>
        void BuildExitConfirmPanel(RectTransform parent)
        {
            var card = UIFactory.Surface(parent, "Exit Confirm Panel", Theme.surfaceGlass, Theme.radiusPopup,
                new Vector2(560f, 380f));
            card.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_ExitConfirmPanel = card.rectTransform;
            m_ExitConfirmGroup = card.gameObject.AddComponent<CanvasGroup>();

            var title = UIFactory.Label(card.transform, "Title", "Exit App?", Theme.textHeadline,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            UIFactory.AnchorTop(title.rectTransform, 90f, Theme.spaceMd);
            title.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceLg);

            var body = UIFactory.Label(card.transform, "Body", "Your progress is saved and will be here"
                + " when you come back.", Theme.textLabel, Theme.textSecondary, TextAlignmentOptions.Center);
            body.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            body.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            body.rectTransform.sizeDelta = new Vector2(-Theme.spaceLg * 2f, 100f);
            body.rectTransform.anchoredPosition = new Vector2(0f, 0f);

            var yes = UIFactory.PrimaryButton(card.transform, "Yes", Theme.danger, 240f);
            var yesRect = yes.GetComponent<RectTransform>();
            yesRect.anchorMin = new Vector2(0.5f, 0f);
            yesRect.anchorMax = new Vector2(0.5f, 0f);
            yesRect.pivot = new Vector2(1f, 0f);
            yesRect.anchoredPosition = new Vector2(-Theme.spaceXs, Theme.spaceLg);
            yes.onClick.AddListener(ConfirmExit);

            var no = UIFactory.SecondaryButton(card.transform, "No", 240f);
            var noRect = no.GetComponent<RectTransform>();
            noRect.anchorMin = new Vector2(0.5f, 0f);
            noRect.anchorMax = new Vector2(0.5f, 0f);
            noRect.pivot = new Vector2(0f, 0f);
            noRect.anchoredPosition = new Vector2(Theme.spaceXs, Theme.spaceLg);
            no.onClick.AddListener(HideAll);
        }

        // -------------------------------------------------------------------------------
        // Behaviour
        // -------------------------------------------------------------------------------

        void OnEnable()
        {
            EventBus.Subscribe<TurnInstructionEvent>(OnTurn);
            EventBus.Subscribe<LandmarkTriggeredEvent>(OnLandmark);
            EventBus.Subscribe<DestinationReachedEvent>(OnDestination);
            EventBus.Subscribe<GpsHealthChangedEvent>(OnGps);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<TurnInstructionEvent>(OnTurn);
            EventBus.Unsubscribe<LandmarkTriggeredEvent>(OnLandmark);
            EventBus.Unsubscribe<DestinationReachedEvent>(OnDestination);
            EventBus.Unsubscribe<GpsHealthChangedEvent>(OnGps);
        }

        void OnTurn(TurnInstructionEvent evt)
        {
            // Straight-ahead reassurance belongs in the status banner, not a modal card.
            if (evt.direction is TurnDirection.Straight or TurnDirection.Arrive)
                return;

            if (IsPaused)
                return;

            m_TurnArrow.Facing = evt.direction is TurnDirection.Left or TurnDirection.SlightLeft
                or TurnDirection.SharpLeft
                ? ChevronGraphic.Direction.Left
                : ChevronGraphic.Direction.Right;

            // Bear/sharp variants reuse the same left/right chevron, tilted, rather than needing
            // a separate mesh per angle.
            var tilt = evt.direction switch
            {
                TurnDirection.SlightLeft => 30f,
                TurnDirection.SharpLeft => -30f,
                TurnDirection.SlightRight => -30f,
                TurnDirection.SharpRight => 30f,
                _ => 0f
            };
            m_TurnArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);

            m_TurnTitle.text = evt.direction switch
            {
                TurnDirection.SlightLeft => "Bear Left",
                TurnDirection.Left => "Turn Left",
                TurnDirection.SharpLeft => "Sharp Left",
                TurnDirection.SlightRight => "Bear Right",
                TurnDirection.Right => "Turn Right",
                _ => "Turn Right"
            };

            m_TurnDistance.text = $"in {Mathf.RoundToInt(evt.distanceToTurn)} m";

            // TurnInstructionEvent doesn't carry a target label, so fall back to the next
            // landmark along the route.
            var towards = m_Bootstrap?.Landmarks?.NextLandmark?.name;
            m_TurnTowards.text = string.IsNullOrEmpty(towards) ? string.Empty : $"towards {towards}";

            Show(Overlay.Turn, 6f);
        }

        void OnLandmark(LandmarkTriggeredEvent evt)
        {
            if (IsPaused || evt.landmark == null)
                return;

            m_ArrivalName.text = evt.landmark.name;
            TriggerHaptic();
            Show(Overlay.Arrival, 7f);
        }

        void OnDestination(DestinationReachedEvent evt)
        {
            m_ArrivalName.text = "Tirumala";
            TriggerHaptic();
            Show(Overlay.Arrival, 0f);
        }

        void OnGps(GpsHealthChangedEvent evt)
        {
            if (evt.health != GpsHealth.NoFix || IsPaused)
                return;

            TriggerHaptic();
            Show(Overlay.GpsWarning, 8f);
        }

        void TriggerHaptic()
        {
            var settings = m_Bootstrap?.Database?.Settings;

            if (settings != null && !settings.GetBool(SettingsKeys.HapticFeedback, true))
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        public void Pause()
        {
            IsPaused = true;
            Show(Overlay.Paused, 0f);
        }

        public void Resume()
        {
            IsPaused = false;
            HideAll();
        }

        public void TogglePause()
        {
            if (IsPaused)
                Resume();
            else
                Pause();
        }

        public void ShowExitConfirm() => Show(Overlay.ExitConfirm, 0f);

        /// <summary>Dismisses any currently-shown modal overlay without side effects — used by the
        /// hardware back button, which should close a card rather than trigger its own action.</summary>
        public void DismissTopOverlay() => HideAll();

        void ConfirmExit()
        {
            m_Bootstrap?.Database?.Flush();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Show(Overlay overlay, float autoHideSeconds)
        {
            m_Active = overlay;
            m_HideAt = autoHideSeconds > 0f ? Time.unscaledTime + autoHideSeconds : float.MaxValue;
            ApplyVisibility();
        }

        public void HideAll()
        {
            m_Active = Overlay.None;
            m_HideAt = float.MaxValue;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            if (m_TurnPanel == null)
                return;

            SetPanelActive(m_TurnPanel, m_TurnGroup, m_Active == Overlay.Turn);
            SetPanelActive(m_ArrivalPanel, m_ArrivalGroup, m_Active == Overlay.Arrival);
            SetPanelActive(m_GpsPanel, m_GpsGroup, m_Active == Overlay.GpsWarning);
            SetPanelActive(m_PausedPanel, m_PausedGroup, m_Active == Overlay.Paused);
            SetPanelActive(m_ExitConfirmPanel, m_ExitConfirmGroup, m_Active == Overlay.ExitConfirm);

            if (m_Active == Overlay.Arrival)
                m_Confetti?.Play();

            if (m_Active == Overlay.Paused)
            {
                m_PausePulse ??= UITween.Pulse(m_PauseRingRect, 0.94f, 1.06f, 1.1f);
            }
            else if (m_PausePulse != null)
            {
                UITween.StopPulse(m_PauseRingRect, m_PausePulse);
                m_PausePulse = null;
            }
        }

        static void SetPanelActive(RectTransform panel, CanvasGroup group, bool active)
        {
            if (panel == null)
                return;

            panel.gameObject.SetActive(active);

            if (!active)
                return;

            // Popup entrance per the design: fade + scale in from 0.9, not an instant pop.
            if (group != null)
                UITween.FadeCanvasGroup(group, 0f, 1f, 0.2f);

            UITween.ScaleIn(panel, 0.9f, 1f, 0.2f);
        }

        void Update()
        {
            if (m_Active == Overlay.None || Time.unscaledTime < m_HideAt)
                return;

            HideAll();
        }
    }
}
