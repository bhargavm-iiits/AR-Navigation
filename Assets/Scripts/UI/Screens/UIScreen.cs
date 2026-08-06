using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// Base class for a full-screen page.
    ///
    /// Screens build their own hierarchy rather than being authored as prefabs. That keeps the
    /// entire design in version-controlled text, means a layout change is a code review rather
    /// than a binary diff, and removes the class of bug where a generated scene loses a reference.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIScreen : MonoBehaviour
    {
        protected static UITheme Theme => UITheme.Current;

        RectTransform m_Rect;
        CanvasGroup m_Group;
        bool m_Built;

        public RectTransform Rect => m_Rect ??= GetComponent<RectTransform>();

        /// <summary>Title shown in the header, and the bottom-nav label.</summary>
        public abstract string Title { get; }

        /// <summary>Icon used on the bottom navigation bar.</summary>
        public abstract IconGraphic.IconType TabIcon { get; }

        /// <summary>Screens that render over the live camera must not paint an opaque background.</summary>
        public virtual bool IsTransparent => false;

        /// <summary>Whether this screen appears in the bottom navigation bar.</summary>
        public virtual bool ShowInNavBar => true;

        /// <summary>Content area, inset below the header and above the nav bar.</summary>
        protected RectTransform Body { get; private set; }

        public bool IsVisible { get; private set; }

        protected virtual void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (m_Built)
                return;

            m_Built = true;

            UIFactory.Stretch(Rect);
            m_Group = gameObject.AddComponent<CanvasGroup>();

            if (!IsTransparent)
            {
                var background = UIFactory.Rect(Rect, "Background");
                UIFactory.Stretch(background);

                var image = background.gameObject.AddComponent<Image>();
                image.color = Theme.background;
                image.raycastTarget = true;
            }

            // Everything sits inside the safe area so notches and the gesture bar never clip it.
            var safe = UIFactory.Rect(Rect, "Safe Area");
            safe.gameObject.AddComponent<SafeAreaFitter>();

            Body = UIFactory.Rect(safe, "Body");
            UIFactory.Stretch(Body);

            BuildContent(Body);
        }

        protected abstract void BuildContent(RectTransform body);

        public virtual void Show()
        {
            EnsureBuilt();
            IsVisible = true;
            gameObject.SetActive(true);

            if (m_Group == null)
                return;

            m_Group.alpha = 1f;
            m_Group.interactable = true;
            m_Group.blocksRaycasts = true;
        }

        public virtual void Hide()
        {
            IsVisible = false;

            if (m_Group != null)
            {
                m_Group.interactable = false;
                m_Group.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        // -------------------------------------------------------------------------------
        // Shared chrome
        // -------------------------------------------------------------------------------

        /// <summary>
        /// Standard page header: optional back button, title, optional trailing action.
        /// An optional leading icon (e.g. the temple glyph) can be placed before the title.
        /// Returns the content rect positioned below it.
        /// </summary>
        protected RectTransform BuildHeader(RectTransform body, string title,
            out Button backButton, out Button actionButton,
            IconGraphic.IconType? actionIcon = null, bool showBack = false,
            IconGraphic.IconType? leadingIcon = null)
        {
            const float headerHeight = 140f;
            const float leadingIconSize = 52f;

            var header = UIFactory.Rect(body, "Header");
            UIFactory.AnchorTop(header, headerHeight, Theme.spaceMd);

            backButton = null;
            actionButton = null;

            var titleInsetLeft = 0f;

            if (showBack)
            {
                backButton = UIFactory.BackButton(header, Theme.surface);
                var rect = backButton.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                titleInsetLeft = Theme.circleButton + Theme.spaceSm;
            }

            if (leadingIcon.HasValue)
            {
                var icon = UIFactory.Icon(header, "LeadingIcon", leadingIcon.Value,
                    leadingIconSize, Theme.templeGold);
                icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.anchoredPosition = new Vector2(titleInsetLeft, 0f);
                titleInsetLeft += leadingIconSize + Theme.spaceXs;
            }

            var titleLabel = UIFactory.Label(header, "Title", title, Theme.textTitle,
                Theme.textPrimary, TextAlignmentOptions.Left, FontWeight.Bold);
            titleLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            titleLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleLabel.rectTransform.offsetMin = new Vector2(titleInsetLeft, 0f);
            titleLabel.rectTransform.offsetMax = new Vector2(-(Theme.circleButton + Theme.spaceSm), 0f);

            if (actionIcon.HasValue)
            {
                actionButton = UIFactory.IconButton(header, "Action", actionIcon.Value, Theme.surface);
                var rect = actionButton.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
            }

            var content = UIFactory.Rect(body, "Content");
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(Theme.spaceMd, Theme.navBarHeight);
            content.offsetMax = new Vector2(-Theme.spaceMd, -headerHeight);

            return content;
        }
    }
}
