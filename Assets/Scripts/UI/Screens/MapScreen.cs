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
    /// Full-screen offline map (screen 2). Draws the same route geometry the navigation uses —
    /// there are no map tiles and no network, so what you see is the surveyed path itself.
    /// </summary>
    public sealed class MapScreen : UIScreen
    {
        public override string Title => "Map";
        public override IconGraphic.IconType TabIcon => IconGraphic.IconType.Map;

        AppBootstrap m_Bootstrap;
        MiniMapController m_Map;
        TMP_Text m_Subtitle;
        TMP_Text m_NextName;
        TMP_Text m_NextDistance;
        TMP_Text m_ModeGlyph;
        RectTransform m_NextCardRect;

        protected override void BuildContent(RectTransform body)
        {
            m_Bootstrap = FindAnyObjectByType<AppBootstrap>();

            BuildMapSurface(body);
            BuildHeaderCard(body);
            BuildControlStack(body);
            BuildNextCard(body);
        }

        void BuildMapSurface(RectTransform body)
        {
            var host = UIFactory.Rect(body, "Map Surface");
            UIFactory.Stretch(host);

            // Satellite background — a pre-baked aerial photograph bundled with the app,
            // so there is no network dependency and no tile stitching to manage.
            var bgSat = host.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            var satTexture = Resources.Load<Texture2D>("Images/map_satellite");
            if (satTexture != null)
            {
                bgSat.texture = satTexture;
                bgSat.color = new Color(1f, 1f, 1f, 0.85f);
            }
            else
            {
                // Fallback: dark terrain plate when the texture hasn't been imported yet.
                bgSat.color = new Color32(0x0A, 0x14, 0x0E, 0xFF);
            }
            bgSat.raycastTarget = false;

            // Dark semi-transparent overlay so the blue route and white UI stay readable
            // against the bright satellite imagery.
            var overlay = UIFactory.Rect(host, "Overlay");
            UIFactory.Stretch(overlay);
            var overlayImg = overlay.gameObject.AddComponent<UnityEngine.UI.Image>();
            overlayImg.color = new Color(0f, 0f, 0.04f, 0.42f);
            overlayImg.raycastTarget = false;

            // Route polyline drawn on top
            var mapRect = UIFactory.Rect(host, "Route");
            UIFactory.Stretch(mapRect);

            m_Map = mapRect.gameObject.AddComponent<MiniMapController>();
            m_Map.color = Theme.accentNav;
            m_Map.Zoom = 900f;

            // Markers
            // User: bright blue circle with navigation arrow inside
            var user = UIFactory.Pill(mapRect, "UserMarker", Theme.accent, new Vector2(56f, 56f));
            var userIcon = UIFactory.Icon(user.transform, "Arrow",
                IconGraphic.IconType.Navigate, 32f, Color.white);
            userIcon.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            userIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            userIcon.rectTransform.anchoredPosition = Vector2.zero;

            // Destination: gold temple marker
            var destination = UIFactory.Pill(mapRect, "DestinationMarker",
                Theme.templeGold, new Vector2(48f, 48f));
            var destIcon = UIFactory.Icon(destination.transform, "Temple",
                IconGraphic.IconType.Temple, 28f, Color.white);
            destIcon.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            destIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            destIcon.rectTransform.anchoredPosition = Vector2.zero;

            // North indicator (invisible — used by MiniMapController for rotation)
            var north = UIFactory.Rect(mapRect, "NorthIndicator", new Vector2(1f, 1f));

            m_Map.AssignMarkers(user.rectTransform, destination.rectTransform, north);
        }

        void BuildHeaderCard(RectTransform body)
        {
            var card = UIFactory.Surface(body, "Header Card", Theme.surfaceGlass, Theme.radiusLarge,
                new Vector2(0f, 150f));
            UIFactory.AnchorTop(card.rectTransform, 150f, Theme.circleButton + Theme.spaceMd);
            card.rectTransform.anchoredPosition = new Vector2(0f, -Theme.spaceSm);

            var title = UIFactory.Label(card.transform, "Title", "Alipiri to Tirumala",
                Theme.textHeadline, Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            title.rectTransform.anchorMin = new Vector2(0f, 0.45f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(Theme.spaceSm, 0f);
            title.rectTransform.offsetMax = new Vector2(-Theme.spaceSm, -Theme.spaceXs);

            m_Subtitle = UIFactory.Label(card.transform, "Subtitle", "—", Theme.textCaption,
                Theme.textSecondary, TextAlignmentOptions.Center);
            m_Subtitle.rectTransform.anchorMin = new Vector2(0f, 0f);
            m_Subtitle.rectTransform.anchorMax = new Vector2(1f, 0.45f);
            m_Subtitle.rectTransform.offsetMin = new Vector2(Theme.spaceSm, Theme.spaceXs);
            m_Subtitle.rectTransform.offsetMax = new Vector2(-Theme.spaceSm, 0f);

            var back = UIFactory.BackButton(body);
            var backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 1f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = new Vector2(Theme.spaceSm, -Theme.spaceSm);
            back.onClick.AddListener(() => UIRoot.Instance?.ShowScreen<ARNavigationScreen>());

            var settings = UIFactory.IconButton(body, "Settings", IconGraphic.IconType.Gear);
            var settingsRect = settings.GetComponent<RectTransform>();
            settingsRect.anchorMin = new Vector2(1f, 1f);
            settingsRect.anchorMax = new Vector2(1f, 1f);
            settingsRect.pivot = new Vector2(1f, 1f);
            settingsRect.anchoredPosition = new Vector2(-Theme.spaceSm, -Theme.spaceSm);
            settings.onClick.AddListener(() => UIRoot.Instance?.ShowScreen<SettingsScreen>());
        }

        void BuildControlStack(RectTransform body)
        {
            var stack = UIFactory.Rect(body, "Controls", new Vector2(Theme.circleButton, 340f));
            stack.anchorMin = new Vector2(1f, 0.5f);
            stack.anchorMax = new Vector2(1f, 0.5f);
            stack.pivot = new Vector2(1f, 0.5f);
            stack.anchoredPosition = new Vector2(-Theme.spaceSm, 0f);

            var column = UIFactory.Column(stack, Theme.spaceSm);
            column.childControlWidth = false;
            column.childControlHeight = false;
            column.childForceExpandWidth = false;

            var recenter = UIFactory.IconButton(stack, "Recenter", IconGraphic.IconType.Target);
            recenter.onClick.AddListener(() => m_Map.Zoom = 400f);

            // Zoom stays a text button: "2D"/"ALL"/"1x" are ASCII, so they are font-safe, and the
            // current scale is clearer as a word than as a symbol.
            var zoomMode = UIFactory.CircleButton(stack, "Zoom", "2D");
            m_ModeGlyph = zoomMode.GetComponentInChildren<TMP_Text>();
            zoomMode.onClick.AddListener(CycleZoom);

            var northUp = UIFactory.IconButton(stack, "NorthUp", IconGraphic.IconType.North);
            var northIcon = northUp.GetComponentInChildren<IconGraphic>();
            northUp.onClick.AddListener(() =>
            {
                m_Map.RotateWithHeading = !m_Map.RotateWithHeading;
                northIcon.color = m_Map.RotateWithHeading ? Theme.textPrimary : Theme.accent;
            });
        }

        void CycleZoom()
        {
            // Three useful scales: the next few hundred metres, the current section, the whole walk.
            var zoom = m_Map.Zoom;

            var next = zoom < 500f ? 900f
                : zoom < 1500f ? 8000f
                : 300f;

            m_Map.Zoom = next;
            m_ModeGlyph.text = next >= 8000f ? "ALL" : next >= 900f ? "2D" : "1x";
        }

        void BuildNextCard(RectTransform body)
        {
            var card = UIFactory.Surface(body, "Next Card", Theme.surfaceGlass, Theme.radiusLarge);
            UIFactory.AnchorBottom(card.rectTransform, 140f, Theme.spaceSm);
            card.rectTransform.anchoredPosition = new Vector2(0f, Theme.navBarHeight + Theme.spaceXs);
            card.raycastTarget = true;
            m_NextCardRect = card.rectTransform;

            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = card;
            button.onClick.AddListener(() => UIRoot.Instance?.ShowScreen<LandmarksScreen>());

            var caption = UIFactory.Label(card.transform, "Caption", "Next Landmark:", Theme.textCaption,
                Theme.textSecondary, TextAlignmentOptions.Left);
            caption.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            caption.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            caption.rectTransform.offsetMin = new Vector2(Theme.spaceMd + 110f, 0f);
            caption.rectTransform.offsetMax = new Vector2(0f, -Theme.spaceXs);

            var nextThumb = UIFactory.Thumbnail(card.transform, "NextThumb", 96f);
            var nextThumbRect = nextThumb.transform.parent as RectTransform;
            nextThumbRect.anchorMin = new Vector2(0f, 0.5f);
            nextThumbRect.anchorMax = new Vector2(0f, 0.5f);
            nextThumbRect.pivot = new Vector2(0f, 0.5f);
            nextThumbRect.anchoredPosition = new Vector2(Theme.spaceSm, 0f);

            m_NextName = UIFactory.Label(card.transform, "Name", "—", Theme.textBody,
                Theme.textPrimary, TextAlignmentOptions.Left, FontWeight.SemiBold);
            m_NextName.rectTransform.anchorMin = new Vector2(0f, 0f);
            m_NextName.rectTransform.anchorMax = new Vector2(0.7f, 0.55f);
            m_NextName.rectTransform.offsetMin = new Vector2(Theme.spaceMd + 110f, Theme.spaceXs);
            m_NextName.rectTransform.offsetMax = Vector2.zero;

            m_NextDistance = UIFactory.Label(card.transform, "Distance", "", Theme.textLabel,
                Theme.textSecondary, TextAlignmentOptions.Right);
            m_NextDistance.rectTransform.anchorMin = new Vector2(0.6f, 0f);
            m_NextDistance.rectTransform.anchorMax = new Vector2(1f, 1f);
            m_NextDistance.rectTransform.offsetMin = Vector2.zero;
            m_NextDistance.rectTransform.offsetMax = new Vector2(-(Theme.spaceMd + 30f), 0f);

            var chevron = UIFactory.Rect(card.transform, "Chevron", new Vector2(18f, 30f));
            chevron.anchorMin = new Vector2(1f, 0.5f);
            chevron.anchorMax = new Vector2(1f, 0.5f);
            chevron.pivot = new Vector2(1f, 0.5f);
            chevron.anchoredPosition = new Vector2(-Theme.spaceMd, 0f);

            var glyph = chevron.gameObject.AddComponent<ChevronGraphic>();
            glyph.Facing = ChevronGraphic.Direction.Right;
            glyph.color = Theme.textTertiary;
            glyph.raycastTarget = false;
        }

        public override void Show()
        {
            base.Show();

            if (m_NextCardRect != null)
                UITween.SlideUp(m_NextCardRect, 50f, 0.35f);
        }

        void Update()
        {
            if (!IsVisible)
                return;

            var progress = m_Bootstrap?.Progress;

            if (progress != null && m_Subtitle != null)
            {
                var total = m_Bootstrap?.Graph?.TotalDistance ?? 0f;
                m_Subtitle.text = $"Distance {NavigationHUD.FormatDistance(total)} · ETA " +
                                  $"{NavigationHUD.FormatDuration(progress.EstimatedSecondsRemaining)}";
            }

            var landmarks = m_Bootstrap?.Landmarks;
            var next = landmarks?.NextLandmark;

            if (next == null)
            {
                m_NextName.text = "—";
                m_NextDistance.text = "";
                return;
            }

            m_NextName.text = next.name;
            m_NextDistance.text = NavigationHUD.FormatDistance(landmarks.DistanceToNextLandmark) + " ahead";
        }
    }
}
