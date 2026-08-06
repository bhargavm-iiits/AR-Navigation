using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Framework
{
    /// <summary>
    /// Builds every widget in the design system from code.
    ///
    /// Screens are assembled by calling these rather than by authoring prefabs, for the same
    /// reason the scenes are generated: a screen described in code cannot silently lose an
    /// inspector reference, and re-running the generator after a change repairs the whole layout.
    ///
    /// All sizes are reference pixels against the theme's 1080x1920 canvas; the
    /// <see cref="ResponsiveUIScaler"/> converts to device pixels.
    /// </summary>
    public static class UIFactory
    {
        static UITheme Theme => UITheme.Current;

        // -----------------------------------------------------------------------------------
        // Primitives
        // -----------------------------------------------------------------------------------

        public static RectTransform Rect(Transform parent, string name, Vector2 size = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            if (size != default)
                rect.sizeDelta = size;

            return rect;
        }

        /// <summary>Stretches a RectTransform to fill its parent, with optional padding.</summary>
        public static RectTransform Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        /// <summary>Anchors to an edge: pins across one axis and fixes the size on the other.</summary>
        public static RectTransform AnchorTop(RectTransform rect, float height, float inset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(inset, -height);
            rect.offsetMax = new Vector2(-inset, 0f);
            return rect;
        }

        public static RectTransform AnchorBottom(RectTransform rect, float height, float inset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, height);
            return rect;
        }

        /// <summary>A rounded surface. The backbone of every card, pill and panel.</summary>
        public static RoundedRectGraphic Surface(Transform parent, string name, Color color,
            float radius, Vector2 size = default)
        {
            var rect = Rect(parent, name, size);
            var surface = rect.gameObject.AddComponent<RoundedRectGraphic>();
            surface.color = color;
            surface.Radius = radius;
            surface.raycastTarget = false;
            return surface;
        }

        public static RoundedRectGraphic Card(Transform parent, string name, Vector2 size = default) =>
            Surface(parent, name, Theme.surface, Theme.radiusLarge, size);

        public static RoundedRectGraphic Glass(Transform parent, string name, Vector2 size = default) =>
            Surface(parent, name, Theme.surfaceGlass, Theme.radiusLarge, size);

        public static RoundedRectGraphic Pill(Transform parent, string name, Color color, Vector2 size = default)
        {
            var pill = Surface(parent, name, color, Theme.radiusPill, size);
            pill.Pill = true;
            return pill;
        }

        public static TMP_Text Label(Transform parent, string name, string text, float size,
            Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontWeight weight = FontWeight.Regular)
        {
            var rect = Rect(parent, name);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.fontWeight = weight;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.textWrappingMode = TextWrappingModes.Normal;

            return label;
        }

        // -----------------------------------------------------------------------------------
        // Controls
        // -----------------------------------------------------------------------------------

        /// <summary>Filled primary button (Resume, Next Landmark, Listen, OK).</summary>
        public static Button PrimaryButton(Transform parent, string text, Color? tint = null,
            float width = 560f)
        {
            var background = Pill(parent, $"Button_{text}", tint ?? Theme.accent,
                new Vector2(width, Theme.buttonHeight));
            background.raycastTarget = true;

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ApplyButtonColors(button, tint ?? Theme.accent);

            var label = Label(background.transform, "Label", text, Theme.textBody,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.SemiBold);
            Stretch(label.rectTransform, Theme.spaceSm);

            return button;
        }

        /// <summary>Outlined secondary button (End Navigation).</summary>
        public static Button SecondaryButton(Transform parent, string text, float width = 560f)
        {
            var background = Pill(parent, $"Button_{text}", Theme.surfaceRaised,
                new Vector2(width, Theme.buttonHeight));
            background.raycastTarget = true;

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ApplyButtonColors(button, Theme.surfaceRaised);

            var label = Label(background.transform, "Label", text, Theme.textBody,
                Theme.textSecondary, TextAlignmentOptions.Center, FontWeight.Medium);
            Stretch(label.rectTransform, Theme.spaceSm);

            return button;
        }

        /// <summary>
        /// Circular glass button used by the AR view's left rail and the map's control stack.
        /// <paramref name="glyph"/> is a short string (the design uses two-letter tokens and
        /// simple symbols rather than an icon font, so nothing external is required).
        /// </summary>
        public static Button CircleButton(Transform parent, string name, string glyph, Color? tint = null)
        {
            var size = Theme.circleButton;
            var background = Pill(parent, $"Circle_{name}", tint ?? Theme.surfaceGlass,
                new Vector2(size, size));
            background.raycastTarget = true;

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ApplyButtonColors(button, tint ?? Theme.surfaceGlass);

            var label = Label(background.transform, "Glyph", glyph, Theme.textLabel,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.SemiBold);
            Stretch(label.rectTransform);

            return button;
        }

        /// <summary>Mesh-drawn icon. Preferred over text glyphs, which are font-dependent.</summary>
        public static IconGraphic Icon(Transform parent, string name, IconGraphic.IconType type,
            float size, Color? tint = null)
        {
            var rect = Rect(parent, name, new Vector2(size, size));
            var icon = rect.gameObject.AddComponent<IconGraphic>();
            icon.Icon = type;
            icon.color = tint ?? Theme.textPrimary;
            icon.raycastTarget = false;
            return icon;
        }

        /// <summary>Circular glass button carrying a vector icon.</summary>
        public static Button IconButton(Transform parent, string name, IconGraphic.IconType icon,
            Color? background = null, Color? iconTint = null)
        {
            var size = Theme.circleButton;
            var surface = Pill(parent, $"Circle_{name}", background ?? Theme.surfaceGlass,
                new Vector2(size, size));
            surface.raycastTarget = true;

            var button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            ApplyButtonColors(button, background ?? Theme.surfaceGlass);

            var glyph = Icon(surface.transform, "Icon", icon, size * 0.52f, iconTint);
            glyph.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            glyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            glyph.rectTransform.anchoredPosition = Vector2.zero;

            return button;
        }

        /// <summary>Circular back button carrying a left chevron.</summary>
        public static Button BackButton(Transform parent, Color? background = null)
        {
            var size = Theme.circleButton;
            var surface = Pill(parent, "Circle_Back", background ?? Theme.surfaceGlass,
                new Vector2(size, size));
            surface.raycastTarget = true;

            var button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            ApplyButtonColors(button, background ?? Theme.surfaceGlass);

            var chevronRect = Rect(surface.transform, "Chevron", new Vector2(size * 0.24f, size * 0.40f));
            chevronRect.anchorMin = new Vector2(0.5f, 0.5f);
            chevronRect.anchorMax = new Vector2(0.5f, 0.5f);
            chevronRect.anchoredPosition = new Vector2(-size * 0.04f, 0f);

            var chevron = chevronRect.gameObject.AddComponent<ChevronGraphic>();
            chevron.Facing = ChevronGraphic.Direction.Left;
            chevron.color = Theme.textPrimary;
            chevron.raycastTarget = false;

            return button;
        }

        static void ApplyButtonColors(Button button, Color baseColor)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // Every button in the design system presses with the same 0.96 -> 1.0 scale, so it
            // lives here once rather than at each of the ~15 call sites that build a button.
            button.gameObject.AddComponent<ButtonPressFeedback>();
        }

        /// <summary>Filter chip for the landmarks list (All / Temple / Water / Statue / Steps).</summary>
        public static Toggle Chip(Transform parent, string text, bool selected, ToggleGroup group)
        {
            var background = Pill(parent, $"Chip_{text}", selected ? Theme.accent : Theme.surfaceRaised,
                new Vector2(0f, 68f));
            background.raycastTarget = true;

            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 68f;
            layout.minWidth = 120f;

            var toggle = background.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.isOn = selected;

            // Assigning the group registers the toggle; calling RegisterToggle as well would
            // add it twice and break the mutual-exclusion logic.
            toggle.group = group;

            var label = Label(background.transform, "Label", text, Theme.textLabel,
                selected ? Theme.textPrimary : Theme.textSecondary,
                TextAlignmentOptions.Center, FontWeight.Medium);
            Stretch(label.rectTransform, Theme.spaceSm);

            var fitter = background.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Recolour on selection — a chip has no separate checkmark in this design.
            toggle.onValueChanged.AddListener(isOn =>
            {
                background.color = isOn ? Theme.accent : Theme.surfaceRaised;
                label.color = isOn ? Theme.textPrimary : Theme.textSecondary;
            });

            return toggle;
        }

        /// <summary>iOS-style switch used throughout Settings.</summary>
        public static Toggle Switch(Transform parent, bool isOn)
        {
            var track = Pill(parent, "Switch", isOn ? Theme.accent : Theme.surfaceRaised,
                new Vector2(104f, 60f));
            track.raycastTarget = true;

            var toggle = track.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.isOn = isOn;

            var knob = Pill(track.transform, "Knob", Color.white, new Vector2(48f, 48f));
            knob.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            knob.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            knob.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            knob.rectTransform.anchoredPosition = new Vector2(isOn ? 74f : 30f, 0f);

            toggle.onValueChanged.AddListener(value =>
            {
                track.color = value ? Theme.accent : Theme.surfaceRaised;
                knob.rectTransform.anchoredPosition = new Vector2(value ? 74f : 30f, 0f);
            });

            return toggle;
        }

        // -----------------------------------------------------------------------------------
        // Composite rows
        // -----------------------------------------------------------------------------------

        /// <summary>A "label ......... value" row, as used by the Progress stats block.</summary>
        public static TMP_Text StatRow(Transform parent, string caption, string value)
        {
            var row = Rect(parent, $"Stat_{caption}");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;

            var captionLabel = Label(row, "Caption", caption, Theme.textLabel, Theme.textSecondary);
            captionLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            captionLabel.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            captionLabel.rectTransform.offsetMin = Vector2.zero;
            captionLabel.rectTransform.offsetMax = Vector2.zero;
            captionLabel.alignment = TextAlignmentOptions.Left;

            var valueLabel = Label(row, "Value", value, Theme.textLabel, Theme.textPrimary,
                TextAlignmentOptions.Right, FontWeight.SemiBold);
            valueLabel.rectTransform.anchorMin = new Vector2(0.4f, 0f);
            valueLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueLabel.rectTransform.offsetMin = Vector2.zero;
            valueLabel.rectTransform.offsetMax = Vector2.zero;

            return valueLabel;
        }

        /// <summary>Settings row with a trailing switch.</summary>
        public static Toggle SettingsToggleRow(Transform parent, string caption, bool isOn)
        {
            var row = Card(parent, $"Row_{caption}");
            row.color = Theme.surface;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;

            var label = Label(row.transform, "Caption", caption, Theme.textBody, Theme.textPrimary);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.7f, 1f);
            label.rectTransform.offsetMin = new Vector2(Theme.spaceMd, 0f);
            label.rectTransform.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Left;

            var toggle = Switch(row.transform, isOn);
            toggle.GetComponent<RectTransform>().anchorMin = new Vector2(1f, 0.5f);
            toggle.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 0.5f);
            toggle.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
            toggle.GetComponent<RectTransform>().anchoredPosition = new Vector2(-Theme.spaceMd, 0f);

            return toggle;
        }

        /// <summary>Settings row with a trailing value and disclosure chevron.</summary>
        public static TMP_Text SettingsValueRow(Transform parent, string caption, string value)
        {
            var row = Card(parent, $"Row_{caption}");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;
            row.raycastTarget = true;
            row.gameObject.AddComponent<Button>().targetGraphic = row;

            var label = Label(row.transform, "Caption", caption, Theme.textBody, Theme.textPrimary);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            label.rectTransform.offsetMin = new Vector2(Theme.spaceMd, 0f);
            label.rectTransform.offsetMax = Vector2.zero;

            var valueLabel = Label(row.transform, "Value", value, Theme.textBody, Theme.textSecondary,
                TextAlignmentOptions.Right);
            valueLabel.rectTransform.anchorMin = new Vector2(0.4f, 0f);
            valueLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueLabel.rectTransform.offsetMin = Vector2.zero;
            valueLabel.rectTransform.offsetMax = new Vector2(-72f, 0f);

            var chevron = Rect(row.transform, "Chevron", new Vector2(20f, 32f));
            chevron.anchorMin = new Vector2(1f, 0.5f);
            chevron.anchorMax = new Vector2(1f, 0.5f);
            chevron.pivot = new Vector2(1f, 0.5f);
            chevron.anchoredPosition = new Vector2(-Theme.spaceMd, 0f);

            var glyph = chevron.gameObject.AddComponent<ChevronGraphic>();
            glyph.Facing = ChevronGraphic.Direction.Right;
            glyph.color = Theme.textTertiary;
            glyph.raycastTarget = false;

            return valueLabel;
        }

        // -----------------------------------------------------------------------------------
        // Layout helpers
        // -----------------------------------------------------------------------------------

        /// <summary>Vertical stack with consistent spacing and padding.</summary>
        public static VerticalLayoutGroup Column(RectTransform rect, float spacing, RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            return layout;
        }

        public static HorizontalLayoutGroup Row(RectTransform rect, float spacing, RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return layout;
        }

        /// <summary>
        /// Vertical scroll view with the content stacked and auto-sized. Used by the landmarks
        /// list and the settings page, both of which must scroll on a short screen and simply
        /// fit on a tall one.
        /// </summary>
        public static ScrollRect ScrollColumn(Transform parent, string name, float spacing,
            out RectTransform content, RectOffset padding = null)
        {
            var viewportRect = Rect(parent, name);
            Stretch(viewportRect);

            var scroll = viewportRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;

            var maskRect = Rect(viewportRect, "Viewport");
            Stretch(maskRect);

            // A RectMask2D clips without needing a mask texture or an extra draw call for stencil.
            maskRect.gameObject.AddComponent<RectMask2D>();

            content = Rect(maskRect, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            Column(content, spacing, padding);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = maskRect;
            scroll.content = content;

            return scroll;
        }

        /// <summary>Full-bleed backdrop for a modal, which also swallows taps behind it.</summary>
        public static Image Scrim(Transform parent, string name = "Scrim")
        {
            var rect = Rect(parent, name);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.45f);
            image.raycastTarget = true;

            return image;
        }

        /// <summary>Thumbnail slot. Falls back to a tinted rounded block when no sprite exists.</summary>
        public static Image Thumbnail(Transform parent, string name, float size, Sprite sprite = null)
        {
            var rect = Rect(parent, name, new Vector2(size, size));

            var placeholder = rect.gameObject.AddComponent<RoundedRectGraphic>();
            placeholder.color = Theme.surfaceRaised;
            placeholder.Radius = Theme.radiusMedium;
            placeholder.raycastTarget = false;

            var imageRect = Rect(rect, "Image");
            Stretch(imageRect);

            var image = imageRect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.preserveAspect = true;
            image.raycastTarget = false;

            return image;
        }
    }
}
