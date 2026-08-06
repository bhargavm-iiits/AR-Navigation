using System.Collections.Generic;
using TirumalaAR.Audio;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.UI.Framework;
using TirumalaAR.UI.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI
{
    /// <summary>
    /// The landmark detail card (screen 6): photo, name, category, description and a "Listen"
    /// button, shown when the pilgrim passes a named waypoint.
    ///
    /// Built procedurally through <see cref="UIFactory"/> like every other screen in the app —
    /// it previously expected a hand-authored, inspector-wired prefab that was never actually
    /// added to the generated scene, so it never showed up at all. Queued so that clustered
    /// landmarks (four sit within 30 m of Padalu Mandapam) surface one at a time, and held back
    /// while <see cref="NavigationOverlays"/> has its own arrival celebration on screen so the
    /// two modals never compete for the centre of the canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LandmarkPopup : MonoBehaviour
    {
        static UITheme Theme => UITheme.Current;

        const float VisibleSeconds = 8f;
        const float ImageHeight = 280f;

        RectTransform m_Panel;
        CanvasGroup m_Group;
        Image m_ThumbImage;
        RoundedRectGraphic m_ThumbPlaceholder;
        TMP_Text m_TitleLabel;
        TMP_Text m_TypeLabel;
        TMP_Text m_BodyLabel;
        Button m_ListenButton;

        NavigationOverlays m_Overlays;
        VoiceNavigationManager m_Voice;

        readonly Queue<LandmarkData> m_Pending = new Queue<LandmarkData>();
        readonly Dictionary<int, Sprite> m_ImageCache = new Dictionary<int, Sprite>();

        LandmarkData m_Current;
        float m_HideAt;
        bool m_Showing;

        public void Build(RectTransform parent)
        {
            m_Overlays = FindAnyObjectByType<NavigationOverlays>();
            m_Voice = FindAnyObjectByType<VoiceNavigationManager>();

            var safe = UIFactory.Rect(parent, "Landmark Popup Safe Area");
            UIFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var card = UIFactory.Surface(safe, "Landmark Popup", Theme.surfaceGlass, Theme.radiusPopup,
                new Vector2(640f, 720f));
            card.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_Panel = card.rectTransform;
            m_Group = card.gameObject.AddComponent<CanvasGroup>();

            var imageRect = UIFactory.Rect(card.transform, "Image");
            UIFactory.AnchorTop(imageRect, ImageHeight, 0f);

            m_ThumbPlaceholder = imageRect.gameObject.AddComponent<RoundedRectGraphic>();
            m_ThumbPlaceholder.color = Theme.surfaceRaised;
            m_ThumbPlaceholder.Radius = Theme.radiusPopup;
            m_ThumbPlaceholder.raycastTarget = false;

            var imageMask = UIFactory.Rect(imageRect, "Mask");
            UIFactory.Stretch(imageMask);
            imageMask.gameObject.AddComponent<RectMask2D>();

            var imageInner = UIFactory.Rect(imageMask, "Sprite");
            UIFactory.Stretch(imageInner);
            m_ThumbImage = imageInner.gameObject.AddComponent<Image>();
            m_ThumbImage.preserveAspect = false;
            m_ThumbImage.raycastTarget = false;
            m_ThumbImage.enabled = false;

            var close = UIFactory.IconButton(card.transform, "Close", IconGraphic.IconType.Close,
                Theme.surfaceGlass);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-Theme.spaceSm, -Theme.spaceSm);
            close.onClick.AddListener(Dismiss);

            m_TitleLabel = UIFactory.Label(card.transform, "Title", "", Theme.textTitle,
                Theme.textPrimary, TextAlignmentOptions.Left, FontWeight.Bold);
            UIFactory.AnchorTop(m_TitleLabel.rectTransform, 56f, Theme.spaceMd);
            m_TitleLabel.rectTransform.anchoredPosition = new Vector2(0f, -(ImageHeight + Theme.spaceSm));

            m_TypeLabel = UIFactory.Label(card.transform, "Type", "", Theme.textLabel,
                Theme.textSecondary, TextAlignmentOptions.Left);
            UIFactory.AnchorTop(m_TypeLabel.rectTransform, 40f, Theme.spaceMd);
            m_TypeLabel.rectTransform.anchoredPosition =
                new Vector2(0f, -(ImageHeight + Theme.spaceSm + 56f));

            m_BodyLabel = UIFactory.Label(card.transform, "Body", "", Theme.textBody,
                Theme.textSecondary, TextAlignmentOptions.TopLeft);
            m_BodyLabel.textWrappingMode = TextWrappingModes.Normal;
            m_BodyLabel.overflowMode = TextOverflowModes.Truncate;
            m_BodyLabel.rectTransform.anchorMin = Vector2.zero;
            m_BodyLabel.rectTransform.anchorMax = Vector2.one;
            m_BodyLabel.rectTransform.offsetMin =
                new Vector2(Theme.spaceMd, Theme.buttonHeight + Theme.spaceLg * 1.5f);
            m_BodyLabel.rectTransform.offsetMax =
                new Vector2(-Theme.spaceMd, -(ImageHeight + Theme.spaceSm + 56f + 40f + Theme.spaceSm));

            m_ListenButton = UIFactory.PrimaryButton(card.transform, "Listen", Theme.accent, 460f);
            var listenRect = m_ListenButton.GetComponent<RectTransform>();
            listenRect.anchorMin = new Vector2(0.5f, 0f);
            listenRect.anchorMax = new Vector2(0.5f, 0f);
            listenRect.pivot = new Vector2(0.5f, 0f);
            listenRect.anchoredPosition = new Vector2(0f, Theme.spaceLg);
            m_ListenButton.onClick.AddListener(Listen);

            m_Group.alpha = 0f;
            m_Group.interactable = false;
            m_Group.blocksRaycasts = false;
            card.gameObject.SetActive(false);
        }

        void OnEnable() => EventBus.Subscribe<LandmarkTriggeredEvent>(OnLandmarkTriggered);
        void OnDisable() => EventBus.Unsubscribe<LandmarkTriggeredEvent>(OnLandmarkTriggered);

        void OnLandmarkTriggered(LandmarkTriggeredEvent evt)
        {
            if (evt.landmark != null)
                m_Pending.Enqueue(evt.landmark);
        }

        void Update()
        {
            // Wait out any arrival celebration already on screen so the two modals never overlap.
            if (!m_Showing && m_Pending.Count > 0 && (m_Overlays == null || !m_Overlays.HasBlockingOverlay))
            {
                Show(m_Pending.Dequeue());
                return;
            }

            if (!m_Showing)
                return;

            if (Time.unscaledTime >= m_HideAt)
                Dismiss();
        }

        void Show(LandmarkData landmark)
        {
            if (m_Panel == null)
                return;

            m_Current = landmark;

            m_TitleLabel.text = landmark.name;
            m_TypeLabel.text = landmark.type;
            m_BodyLabel.text = landmark.voiceText;

            var sprite = LoadThumbnail(landmark);
            m_ThumbImage.sprite = sprite;
            m_ThumbImage.enabled = sprite != null;
            m_ThumbPlaceholder.color = sprite != null ? Theme.surfaceRaised : Theme.templeGold;

            m_Panel.gameObject.SetActive(true);
            m_Group.interactable = true;
            m_Group.blocksRaycasts = true;
            UITween.FadeCanvasGroup(m_Group, 0f, 1f, 0.2f);
            UITween.ScaleIn(m_Panel, 0.9f, 1f, 0.2f);

            m_HideAt = Time.unscaledTime + VisibleSeconds;
            m_Showing = true;
        }

        /// <summary>Landmark photographs live in Resources/Images, named by landmark id.</summary>
        Sprite LoadThumbnail(LandmarkData landmark)
        {
            if (m_ImageCache.TryGetValue(landmark.id, out var cached))
                return cached;

            var sprite = Resources.Load<Sprite>($"Images/landmark_{landmark.id}");
            m_ImageCache[landmark.id] = sprite;
            return sprite;
        }

        void Listen()
        {
            if (m_Current == null || m_Voice == null)
                return;

            m_Voice.Speak(m_Current.voiceText, null, VoicePriority.High, m_Current.id.ToString());
        }

        public void Dismiss()
        {
            m_Showing = false;
            m_Current = null;

            if (m_Group == null)
                return;

            m_Group.interactable = false;
            m_Group.blocksRaycasts = false;
            m_Group.alpha = 0f;
            m_Panel.gameObject.SetActive(false);
        }

        public bool IsShowing => m_Showing;
    }
}
