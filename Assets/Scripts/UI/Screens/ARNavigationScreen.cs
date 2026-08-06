using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Managers;
using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// The live AR view (screen 1) — the screen the pilgrim actually walks with.
    ///
    /// Transparent, because the camera feed renders behind it. Every element is either a glass
    /// pill or a glass card so the staircase stays readable underneath, and the bottom panel is
    /// deliberately shallow: on a 21:9 phone a taller panel would cover the arrows nearest the
    /// user, which are the ones that matter most.
    /// </summary>
    public sealed class ARNavigationScreen : UIScreen
    {
        public override string Title => "Navigate";
        public override IconGraphic.IconType TabIcon => IconGraphic.IconType.Navigate;
        public override bool IsTransparent => true;

        AppBootstrap m_Bootstrap;

        TMP_Text m_NextLandmarkName;
        TMP_Text m_NextLandmarkDistance;
        Image m_NextLandmarkThumb;
        RectTransform m_CompassNeedle;
        TMP_Text m_StepBadge;
        TMP_Text m_Distance;
        TMP_Text m_Eta;
        TMP_Text m_BottomLandmark;
        TMP_Text m_BottomLandmarkDistance;
        TMP_Text m_ProgressPercent;
        RectTransform m_ProgressFill;
        RoundedRectGraphic m_GpsDot;
        RoundedRectGraphic m_TrackingDot;
        RectTransform m_BottomPanelRect;

        string m_LastLandmarkName;
        float m_TargetProgress;
        bool m_BrightnessBoosted;

        protected override void BuildContent(RectTransform body)
        {
            m_Bootstrap = FindAnyObjectByType<AppBootstrap>();

            BuildTopBar(body);
            BuildLeftRail(body);
            BuildTempleButton(body);
            BuildStepBadge(body);
            BuildBottomPanel(body);
        }

        void BuildTempleButton(RectTransform body)
        {
            // Sits directly above the step badge, level with the compass at the top of the left
            // rail — the badge below it is pushed down to make room (see BuildStepBadge).
            var temple = UIFactory.IconButton(body, "Temple", IconGraphic.IconType.Temple,
                iconTint: Theme.templeGold);
            var rect = temple.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-Theme.spaceSm, -(Theme.circleButton + Theme.spaceLg));

            temple.onClick.AddListener(() => UIRoot.Instance?.ShowScreen<LandmarksScreen>());
        }

        void BuildTopBar(RectTransform body)
        {
            var bar = UIFactory.Rect(body, "Top Bar");
            UIFactory.AnchorTop(bar, Theme.circleButton + Theme.spaceSm, Theme.spaceSm);

            // Menu (left)
            var menu = UIFactory.IconButton(bar, "Menu", IconGraphic.IconType.Menu);
            var menuRect = menu.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0f, 0.5f);
            menuRect.anchorMax = new Vector2(0f, 0.5f);
            menuRect.pivot = new Vector2(0f, 0.5f);
            menuRect.anchoredPosition = Vector2.zero;
            menu.onClick.AddListener(() => UIRoot.Instance?.ShowScreen<SettingsScreen>());

            // Mute (right)
            var mute = UIFactory.IconButton(bar, "Mute", IconGraphic.IconType.Speaker);
            var muteRect = mute.GetComponent<RectTransform>();
            muteRect.anchorMin = new Vector2(1f, 0.5f);
            muteRect.anchorMax = new Vector2(1f, 0.5f);
            muteRect.pivot = new Vector2(1f, 0.5f);
            muteRect.anchoredPosition = Vector2.zero;

            var voice = FindAnyObjectByType<Audio.VoiceNavigationManager>();
            var speakerIcon = mute.GetComponentInChildren<IconGraphic>();

            mute.onClick.AddListener(() =>
            {
                if (voice == null)
                    return;

                voice.SetEnabled(!voice.IsEnabled);
                speakerIcon.Icon = voice.IsEnabled
                    ? IconGraphic.IconType.Speaker
                    : IconGraphic.IconType.SpeakerMuted;
            });

            // Centre pill: next landmark
            var pill = UIFactory.Pill(bar, "NextLandmarkPill", Theme.surfaceGlass,
                new Vector2(580f, Theme.circleButton));
            pill.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            pill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            pill.rectTransform.anchoredPosition = Vector2.zero;

            m_NextLandmarkThumb = UIFactory.Thumbnail(pill.transform, "Thumb", 72f);
            var thumbHost = (RectTransform)m_NextLandmarkThumb.transform.parent;
            thumbHost.anchorMin = new Vector2(0f, 0.5f);
            thumbHost.anchorMax = new Vector2(0f, 0.5f);
            thumbHost.pivot = new Vector2(0f, 0.5f);
            thumbHost.anchoredPosition = new Vector2(12f, 0f);

            m_NextLandmarkName = UIFactory.Label(pill.transform, "Name", "—", Theme.textLabel,
                Theme.textPrimary, TextAlignmentOptions.Left, FontWeight.SemiBold);
            m_NextLandmarkName.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            m_NextLandmarkName.rectTransform.anchorMax = new Vector2(1f, 1f);
            m_NextLandmarkName.rectTransform.offsetMin = new Vector2(96f, -6f);
            m_NextLandmarkName.rectTransform.offsetMax = new Vector2(-16f, -10f);

            m_NextLandmarkDistance = UIFactory.Label(pill.transform, "Distance", "", Theme.textCaption,
                Theme.textSecondary, TextAlignmentOptions.Left);
            m_NextLandmarkDistance.rectTransform.anchorMin = new Vector2(0f, 0f);
            m_NextLandmarkDistance.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            m_NextLandmarkDistance.rectTransform.offsetMin = new Vector2(96f, 10f);
            m_NextLandmarkDistance.rectTransform.offsetMax = new Vector2(-16f, 6f);
        }

        void BuildLeftRail(RectTransform body)
        {
            var rail = UIFactory.Rect(body, "Left Rail", new Vector2(Theme.circleButton, 460f));
            rail.anchorMin = new Vector2(0f, 1f);
            rail.anchorMax = new Vector2(0f, 1f);
            rail.pivot = new Vector2(0f, 1f);
            rail.anchoredPosition = new Vector2(Theme.spaceSm, -(Theme.circleButton + Theme.spaceLg));

            var column = UIFactory.Column(rail, Theme.spaceSm);
            column.childForceExpandWidth = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            // Compass rosette
            var compass = UIFactory.Pill(rail, "Compass", Theme.surfaceGlass,
                new Vector2(Theme.circleButton, Theme.circleButton));

            m_CompassNeedle = UIFactory.Rect(compass.transform, "Needle", new Vector2(8f, 54f));
            m_CompassNeedle.anchoredPosition = Vector2.zero;

            var needle = m_CompassNeedle.gameObject.AddComponent<ChevronGraphic>();
            needle.Facing = ChevronGraphic.Direction.Up;
            needle.color = Theme.danger;
            needle.raycastTarget = false;

            // Status dots double as the AR / GPS health indicators from the design's left rail.
            var arButton = UIFactory.CircleButton(rail, "AR", "AR", Theme.accent);
            m_TrackingDot = arButton.GetComponent<RoundedRectGraphic>();

            var autoButton = UIFactory.CircleButton(rail, "Auto", "Auto");
            m_GpsDot = autoButton.GetComponent<RoundedRectGraphic>();

            // Boosts screen brightness for outdoor glare on the staircase — the same setting the
            // Settings screen's "Auto Brightness" toggle controls.
            var lightButton = UIFactory.IconButton(rail, "Light", IconGraphic.IconType.Brightness);
            var lightGlyph = lightButton.GetComponentInChildren<IconGraphic>();
            lightButton.onClick.AddListener(() =>
            {
                m_BrightnessBoosted = !m_BrightnessBoosted;
                Screen.brightness = m_BrightnessBoosted ? 1f : -1f;
                lightGlyph.color = m_BrightnessBoosted ? Theme.warning : Theme.textPrimary;
            });
        }

        void BuildStepBadge(RectTransform body)
        {
            var badge = UIFactory.Surface(body, "StepBadge", Theme.surfaceGlass, Theme.radiusMedium,
                new Vector2(140f, 140f));
            badge.rectTransform.anchorMin = new Vector2(1f, 1f);
            badge.rectTransform.anchorMax = new Vector2(1f, 1f);
            badge.rectTransform.pivot = new Vector2(1f, 1f);
            badge.rectTransform.anchoredPosition = new Vector2(-Theme.spaceSm,
                -(Theme.circleButton + Theme.spaceLg + Theme.circleButton + Theme.spaceSm));

            m_StepBadge = UIFactory.Label(badge.transform, "Steps", "0", Theme.textHeadline,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            m_StepBadge.rectTransform.anchorMin = new Vector2(0f, 0.4f);
            m_StepBadge.rectTransform.anchorMax = new Vector2(1f, 1f);
            m_StepBadge.rectTransform.offsetMin = Vector2.zero;
            m_StepBadge.rectTransform.offsetMax = Vector2.zero;

            var caption = UIFactory.Label(badge.transform, "Caption", "Steps", Theme.textCaption,
                Theme.textSecondary, TextAlignmentOptions.Center);
            caption.rectTransform.anchorMin = new Vector2(0f, 0f);
            caption.rectTransform.anchorMax = new Vector2(1f, 0.42f);
            caption.rectTransform.offsetMin = Vector2.zero;
            caption.rectTransform.offsetMax = Vector2.zero;
        }

        void BuildBottomPanel(RectTransform body)
        {
            var panel = UIFactory.Surface(body, "Bottom Panel", Theme.surfaceGlass, Theme.radiusLarge);
            UIFactory.AnchorBottom(panel.rectTransform, 250f, Theme.spaceSm);
            panel.rectTransform.anchoredPosition = new Vector2(0f, Theme.navBarHeight + Theme.spaceXs);
            m_BottomPanelRect = panel.rectTransform;

            // Three stat columns
            var stats = UIFactory.Rect(panel.transform, "Stats");
            stats.anchorMin = new Vector2(0f, 0f);
            stats.anchorMax = new Vector2(1f, 1f);
            stats.offsetMin = new Vector2(Theme.spaceMd, 74f);
            stats.offsetMax = new Vector2(-Theme.spaceMd, -Theme.spaceSm);

            m_Distance = BuildStatColumn(stats, "Distance", "Remaining", 0f, 0.33f, out _);
            m_BottomLandmark = BuildStatColumn(stats, "Next Landmark", "", 0.33f, 0.67f,
                out m_BottomLandmarkDistance);
            m_Eta = BuildStatColumn(stats, "ETA", "Remaining", 0.67f, 1f, out _);

            // Progress bar
            var barCaption = UIFactory.Label(panel.transform, "ProgressCaption", "Progress",
                Theme.textCaption, Theme.textSecondary, TextAlignmentOptions.Left);
            barCaption.rectTransform.anchorMin = new Vector2(0f, 0f);
            barCaption.rectTransform.anchorMax = new Vector2(0.3f, 0f);
            barCaption.rectTransform.pivot = new Vector2(0f, 0f);
            barCaption.rectTransform.offsetMin = new Vector2(Theme.spaceMd, Theme.spaceSm);
            barCaption.rectTransform.offsetMax = new Vector2(0f, Theme.spaceSm + 44f);

            var track = UIFactory.Pill(panel.transform, "ProgressTrack", Theme.surfaceRaised);
            track.rectTransform.anchorMin = new Vector2(0.28f, 0f);
            track.rectTransform.anchorMax = new Vector2(0.84f, 0f);
            track.rectTransform.pivot = new Vector2(0f, 0f);
            track.rectTransform.offsetMin = new Vector2(0f, Theme.spaceSm + 14f);
            track.rectTransform.offsetMax = new Vector2(0f, Theme.spaceSm + 32f);

            var fill = UIFactory.Pill(track.transform, "ProgressFill", Theme.success);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            m_ProgressFill = fill.rectTransform;

            m_ProgressPercent = UIFactory.Label(panel.transform, "ProgressPercent", "0%",
                Theme.textCaption, Theme.textPrimary, TextAlignmentOptions.Right, FontWeight.SemiBold);
            m_ProgressPercent.rectTransform.anchorMin = new Vector2(0.84f, 0f);
            m_ProgressPercent.rectTransform.anchorMax = new Vector2(1f, 0f);
            m_ProgressPercent.rectTransform.pivot = new Vector2(1f, 0f);
            m_ProgressPercent.rectTransform.offsetMin = new Vector2(0f, Theme.spaceSm);
            m_ProgressPercent.rectTransform.offsetMax = new Vector2(-Theme.spaceMd, Theme.spaceSm + 44f);
        }

        TMP_Text BuildStatColumn(RectTransform parent, string caption, string footnote,
            float anchorMin, float anchorMax, out TMP_Text footnoteLabel)
        {
            var column = UIFactory.Rect(parent, $"Stat_{caption}");
            column.anchorMin = new Vector2(anchorMin, 0f);
            column.anchorMax = new Vector2(anchorMax, 1f);
            column.offsetMin = Vector2.zero;
            column.offsetMax = Vector2.zero;

            var captionLabel = UIFactory.Label(column, "Caption", caption, Theme.textCaption,
                Theme.textSecondary, TextAlignmentOptions.Center);
            captionLabel.rectTransform.anchorMin = new Vector2(0f, 0.66f);
            captionLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            captionLabel.rectTransform.offsetMin = Vector2.zero;
            captionLabel.rectTransform.offsetMax = Vector2.zero;

            var value = UIFactory.Label(column, "Value", "—", Theme.textHeadline,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            value.rectTransform.anchorMin = new Vector2(0f, 0.28f);
            value.rectTransform.anchorMax = new Vector2(1f, 0.7f);
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = Vector2.zero;

            footnoteLabel = UIFactory.Label(column, "Footnote", footnote, Theme.textCaption,
                Theme.textTertiary, TextAlignmentOptions.Center);
            footnoteLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            footnoteLabel.rectTransform.anchorMax = new Vector2(1f, 0.3f);
            footnoteLabel.rectTransform.offsetMin = Vector2.zero;
            footnoteLabel.rectTransform.offsetMax = Vector2.zero;

            return value;
        }

        void OnEnable()
        {
            EventBus.Subscribe<UserPoseUpdatedEvent>(OnPose);
            EventBus.Subscribe<RouteProgressEvent>(OnProgress);
            EventBus.Subscribe<GpsHealthChangedEvent>(OnGps);
            EventBus.Subscribe<TrackingStateChangedEvent>(OnTracking);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<UserPoseUpdatedEvent>(OnPose);
            EventBus.Unsubscribe<RouteProgressEvent>(OnProgress);
            EventBus.Unsubscribe<GpsHealthChangedEvent>(OnGps);
            EventBus.Unsubscribe<TrackingStateChangedEvent>(OnTracking);
        }

        void OnPose(UserPoseUpdatedEvent evt)
        {
            if (m_CompassNeedle != null)
                m_CompassNeedle.localRotation = Quaternion.Euler(0f, 0f, evt.headingDegrees);
        }

        void OnProgress(RouteProgressEvent evt)
        {
            if (m_Distance != null)
                m_Distance.text = NavigationHUD.FormatDistance(evt.remainingDistance);

            if (m_Eta != null)
                m_Eta.text = NavigationHUD.FormatDuration(evt.estimatedSecondsRemaining);

            if (m_StepBadge != null)
                m_StepBadge.text = evt.estimatedSteps.ToString("N0");

            if (m_ProgressPercent != null)
                m_ProgressPercent.text = $"{Mathf.RoundToInt(evt.progressNormalized * 100f)}%";

            m_TargetProgress = Mathf.Clamp01(evt.progressNormalized);
        }

        void OnGps(GpsHealthChangedEvent evt)
        {
            if (m_GpsDot == null)
                return;

            m_GpsDot.color = evt.health switch
            {
                GpsHealth.Good => Theme.success,
                GpsHealth.Acceptable => Theme.warning,
                _ => Theme.danger
            };
        }

        void OnTracking(TrackingStateChangedEvent evt)
        {
            if (m_TrackingDot == null)
                return;

            m_TrackingDot.color = evt.health switch
            {
                TrackingHealth.Tracking => Theme.accent,
                TrackingHealth.Limited => Theme.warning,
                _ => Theme.danger
            };
        }

        public override void Show()
        {
            base.Show();

            if (m_BottomPanelRect != null)
                UITween.SlideUp(m_BottomPanelRect, 60f, 0.35f);
        }

        void Update()
        {
            if (!IsVisible)
                return;

            if (m_ProgressFill != null)
            {
                var currentFill = m_ProgressFill.anchorMax.x;
                var smoothedFill = Mathf.Lerp(currentFill, m_TargetProgress, Time.unscaledDeltaTime * 4f);

                if (Mathf.Abs(smoothedFill - m_TargetProgress) < 0.001f)
                    smoothedFill = m_TargetProgress;

                m_ProgressFill.anchorMax = new Vector2(smoothedFill, 1f);
            }

            // While navigation has not initialised yet show the bootstrap status so the
            // pilgrim knows what the app is doing rather than seeing silent dashes.
            if (m_Bootstrap != null && !m_Bootstrap.IsReady)
            {
                if (m_NextLandmarkName != null)
                    m_NextLandmarkName.text = m_Bootstrap.StatusMessage;

                if (m_NextLandmarkDistance != null)
                    m_NextLandmarkDistance.text = string.Empty;

                return;
            }

            var landmarks = m_Bootstrap?.Landmarks;
            var next = landmarks?.NextLandmark;

            if (next == null)
            {
                if (m_LastLandmarkName == null)
                    return;

                m_LastLandmarkName = null;
                m_NextLandmarkName.text = "Follow the arrows";
                m_NextLandmarkDistance.text = "";
                m_BottomLandmark.text = "—";
                m_BottomLandmarkDistance.text = "";
                return;
            }

            var distanceText = NavigationHUD.FormatDistance(landmarks.DistanceToNextLandmark) + " ahead";

            m_NextLandmarkDistance.text = distanceText;
            m_BottomLandmarkDistance.text = distanceText;

            if (next.name == m_LastLandmarkName)
                return;

            m_LastLandmarkName = next.name;
            m_NextLandmarkName.text = next.name;
            m_BottomLandmark.text = next.name;
            m_BottomLandmark.fontSize = next.name.Length > 18 ? Theme.textBody : Theme.textHeadline;

            var sprite = Resources.Load<Sprite>($"Images/landmark_{next.id}");
            m_NextLandmarkThumb.sprite = sprite;
            m_NextLandmarkThumb.enabled = sprite != null;
        }
    }
}
