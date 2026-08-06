using System.Collections.Generic;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Database;
using TirumalaAR.Database.Json;
using TirumalaAR.Managers;
using TirumalaAR.UI.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// Scrollable landmark list with category filters (screen 3 of the design).
    ///
    /// Rows are pooled and rebuilt only when the filter or the data changes, not per frame — the
    /// list is 41 entries today but the same code has to stay smooth if the dataset grows.
    /// </summary>
    public sealed class LandmarksScreen : UIScreen
    {
        sealed class Row
        {
            public RectTransform root;
            public TMP_Text index;
            public TMP_Text title;
            public TMP_Text description;
            public TMP_Text distance;
            public RoundedRectGraphic indexBadge;
            public RoundedRectGraphic visitedBadge;
            public IconGraphic visitedGlyph;
            public Image thumbnail;
            public RoundedRectGraphic thumbnailPlaceholder;
        }

        public override string Title => "Landmarks";
        public override IconGraphic.IconType TabIcon => IconGraphic.IconType.List;

        static readonly string[] k_Filters = { "All", "Temple", "Water", "Statue", "Steps" };

        AppBootstrap m_Bootstrap;

        // A short-lived local database opened directly from StreamingAssets so the landmark
        // list shows immediately — before GPS or AR has initialised. Once AppBootstrap is
        // ready its database takes over, giving live visited/distance state.
        IDatabase m_LocalDb;

        RectTransform m_ListContent;
        readonly List<Row> m_Rows = new List<Row>();
        readonly List<LandmarkData> m_Filtered = new List<LandmarkData>();

        string m_ActiveFilter = "All";
        float m_RefreshTimer;
        int m_LastVisitedCount = -1;

        protected override void BuildContent(RectTransform body)
        {
            m_Bootstrap = FindAnyObjectByType<AppBootstrap>();

            // Open a local read-only database so data is available immediately, even before
            // the navigation scene's AppBootstrap has finished its async initialisation.
            m_LocalDb = new JsonDatabase();

            var content = BuildHeader(body, Title, out _, out _, IconGraphic.IconType.Search,
                leadingIcon: IconGraphic.IconType.Temple);

            // Filter chips
            var chipBar = UIFactory.Rect(content, "Filters");
            UIFactory.AnchorTop(chipBar, 76f);

            var chipRow = UIFactory.Row(chipBar, Theme.spaceXs);
            chipRow.childAlignment = TextAnchor.MiddleLeft;

            var group = chipBar.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            foreach (var filter in k_Filters)
            {
                var captured = filter;
                var chip = UIFactory.Chip(chipBar, filter, filter == m_ActiveFilter, group);

                chip.onValueChanged.AddListener(isOn =>
                {
                    if (!isOn)
                        return;

                    m_ActiveFilter = captured;
                    Refresh(true);
                });
            }

            // Scrolling list
            var listHost = UIFactory.Rect(content, "List");
            listHost.anchorMin = Vector2.zero;
            listHost.anchorMax = Vector2.one;
            listHost.offsetMin = Vector2.zero;
            listHost.offsetMax = new Vector2(0f, -(76f + Theme.spaceSm));

            UIFactory.ScrollColumn(listHost, "Scroll", Theme.spaceSm, out m_ListContent,
                new RectOffset(0, 0, 0, (int)Theme.spaceLg));
        }

        public override void Show()
        {
            base.Show();
            Refresh(true);
        }

        void Update()
        {
            if (!IsVisible)
                return;

            // Visit state changes while the user is walking; poll cheaply rather than wiring a
            // dedicated event, since this screen is usually not even visible.
            m_RefreshTimer += Time.unscaledDeltaTime;

            if (m_RefreshTimer < 1f)
                return;

            m_RefreshTimer = 0f;

            var visited = m_Bootstrap?.Landmarks?.VisitedCount ?? 0;

            if (visited == m_LastVisitedCount)
                return;

            m_LastVisitedCount = visited;
            Refresh(false);
        }

        void Refresh(bool rebuildRows)
        {
            // Prefer the live Bootstrap database (has visited state + route distances).
            // Fall back to our local DB so the list is populated immediately on app start.
            var repository = m_Bootstrap?.Database?.Landmarks ?? m_LocalDb?.Landmarks;
            var all = repository?.GetAll();

            m_Filtered.Clear();

            if (all != null)
            {
                foreach (var landmark in all)
                {
                    if (Matches(landmark, m_ActiveFilter))
                        m_Filtered.Add(landmark);
                }

                // Order by position along the route so the list reads as the walk itself.
                m_Filtered.Sort((a, b) => a.routeDistance.CompareTo(b.routeDistance));
            }

            if (rebuildRows || m_Rows.Count != m_Filtered.Count)
                EnsureRowCount(m_Filtered.Count);

            var userDistance = m_Bootstrap?.Progress?.CompletedDistance ?? 0f;

            for (var i = 0; i < m_Rows.Count; i++)
            {
                var row = m_Rows[i];

                if (i >= m_Filtered.Count)
                {
                    row.root.gameObject.SetActive(false);
                    continue;
                }

                Bind(row, m_Filtered[i], i + 1, userDistance);
            }
        }

        static bool Matches(LandmarkData landmark, string filter) => filter switch
        {
            "All" => true,
            "Temple" => landmark.Category == LandmarkCategory.Temple,
            "Water" => landmark.Category == LandmarkCategory.WaterPoint,
            "Statue" => landmark.Category == LandmarkCategory.Statue,
            "Steps" => landmark.Category == LandmarkCategory.Steps,
            _ => true
        };

        void EnsureRowCount(int count)
        {
            while (m_Rows.Count < count)
                m_Rows.Add(CreateRow(m_ListContent));

            for (var i = 0; i < m_Rows.Count; i++)
                m_Rows[i].root.gameObject.SetActive(i < count);
        }

        Row CreateRow(RectTransform parent)
        {
            var card = UIFactory.Card(parent, "LandmarkRow");
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 200f;

            var row = new Row { root = card.rectTransform };

            row.thumbnail = UIFactory.Thumbnail(card.transform, "Thumb", 140f);
            var thumbRect = row.thumbnail.transform.parent as RectTransform;
            row.thumbnailPlaceholder = thumbRect.GetComponent<RoundedRectGraphic>();
            thumbRect.anchorMin = new Vector2(0f, 0.5f);
            thumbRect.anchorMax = new Vector2(0f, 0.5f);
            thumbRect.pivot = new Vector2(0f, 0.5f);
            thumbRect.anchoredPosition = new Vector2(Theme.spaceSm, 0f);

            const float textLeft = 140f + 28f;

            row.indexBadge = UIFactory.Pill(card.transform, "IndexBadge", Theme.success,
                new Vector2(46f, 46f));
            row.indexBadge.rectTransform.anchorMin = new Vector2(0f, 1f);
            row.indexBadge.rectTransform.anchorMax = new Vector2(0f, 1f);
            row.indexBadge.rectTransform.pivot = new Vector2(0f, 1f);
            row.indexBadge.rectTransform.anchoredPosition = new Vector2(textLeft, -Theme.spaceSm - 4f);

            row.index = UIFactory.Label(row.indexBadge.transform, "Index", "1", Theme.textCaption,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            UIFactory.Stretch(row.index.rectTransform);

            row.title = UIFactory.Label(card.transform, "Title", "", Theme.textBody,
                Theme.textPrimary, TextAlignmentOptions.TopLeft, FontWeight.SemiBold);
            row.title.rectTransform.anchorMin = new Vector2(0f, 1f);
            row.title.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.title.rectTransform.pivot = new Vector2(0f, 1f);
            row.title.rectTransform.offsetMin = new Vector2(textLeft + 60f, -80f);
            row.title.rectTransform.offsetMax = new Vector2(-190f, -Theme.spaceSm);

            row.description = UIFactory.Label(card.transform, "Description", "", Theme.textCaption,
                Theme.textSecondary, TextAlignmentOptions.TopLeft);
            row.description.rectTransform.anchorMin = new Vector2(0f, 0f);
            row.description.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.description.rectTransform.offsetMin = new Vector2(textLeft, Theme.spaceSm);
            row.description.rectTransform.offsetMax = new Vector2(-Theme.spaceSm, -96f);

            row.distance = UIFactory.Label(card.transform, "Distance", "", Theme.textLabel,
                Theme.textSecondary, TextAlignmentOptions.TopRight, FontWeight.Medium);
            row.distance.rectTransform.anchorMin = new Vector2(1f, 1f);
            row.distance.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.distance.rectTransform.pivot = new Vector2(1f, 1f);
            row.distance.rectTransform.sizeDelta = new Vector2(180f, 44f);
            row.distance.rectTransform.anchoredPosition = new Vector2(-Theme.spaceSm, -Theme.spaceSm);

            row.visitedBadge = UIFactory.Pill(card.transform, "Visited", Theme.success,
                new Vector2(48f, 48f));
            row.visitedBadge.rectTransform.anchorMin = new Vector2(1f, 0f);
            row.visitedBadge.rectTransform.anchorMax = new Vector2(1f, 0f);
            row.visitedBadge.rectTransform.pivot = new Vector2(1f, 0f);
            row.visitedBadge.rectTransform.anchoredPosition = new Vector2(-Theme.spaceSm, Theme.spaceSm);

            row.visitedGlyph = UIFactory.Icon(row.visitedBadge.transform, "Check",
                IconGraphic.IconType.Check, 30f);
            row.visitedGlyph.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            row.visitedGlyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            row.visitedGlyph.rectTransform.anchoredPosition = Vector2.zero;

            return row;
        }

        void Bind(Row row, LandmarkData landmark, int displayIndex, float userDistance)
        {
            row.root.gameObject.SetActive(true);

            row.index.text = displayIndex.ToString();
            row.title.text = landmark.name;
            row.description.text = landmark.voiceText;

            var delta = landmark.routeDistance - userDistance;
            row.distance.text = delta <= 0f
                ? "passed"
                : NavigationHUD.FormatDistance(delta);

            // The numbered badge is always green, matching the design — only the bottom-right
            // check/ring communicates visited state.
            row.indexBadge.color = Theme.success;

            row.visitedBadge.color = landmark.visited ? Theme.success : Theme.border;
            row.visitedGlyph.enabled = landmark.visited;

            var sprite = Resources.Load<Sprite>($"Images/landmark_{landmark.id}");
            row.thumbnail.sprite = sprite;
            row.thumbnail.enabled = sprite != null;

            // No landmark photographs ship with the project yet — tint the placeholder by
            // category so the list still reads as differentiated rather than a flat grey column.
            if (row.thumbnailPlaceholder != null)
            {
                row.thumbnailPlaceholder.color =
                    sprite != null ? Theme.surfaceRaised : CategoryTint(landmark.Category);
            }
        }

        void OnDestroy()
        {
            m_LocalDb?.Dispose();
            m_LocalDb = null;
        }

        static Color CategoryTint(LandmarkCategory category)
        {
            var accent = category switch
            {
                LandmarkCategory.Temple => Theme.templeGold,
                LandmarkCategory.WaterPoint => Theme.accentNav,
                LandmarkCategory.Statue => Theme.textTertiary,
                LandmarkCategory.Steps => Theme.success,
                _ => Theme.textTertiary
            };

            return Color.Lerp(Theme.surfaceRaised, accent, 0.35f);
        }
    }
}
