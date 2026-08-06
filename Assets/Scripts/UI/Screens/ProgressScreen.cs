using System.Collections.Generic;
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
    /// Progress page (screen 4): completion ring, distance/time stats and the elevation profile.
    /// </summary>
    public sealed class ProgressScreen : UIScreen
    {
        public override string Title => "Your Progress";
        public override IconGraphic.IconType TabIcon => IconGraphic.IconType.Chart;

        AppBootstrap m_Bootstrap;

        ProgressRingGraphic m_Ring;
        TMP_Text m_RingPercent;
        TMP_Text m_TotalDistance;
        TMP_Text m_Completed;
        TMP_Text m_Remaining;
        TMP_Text m_Eta;
        TMP_Text m_Steps;
        SparklineGraphic m_Elevation;
        TMP_Text m_ElevationMax;
        TMP_Text m_ElevationEnd;

        bool m_ElevationBuilt;
        float m_TargetFill;

        protected override void BuildContent(RectTransform body)
        {
            m_Bootstrap = FindAnyObjectByType<AppBootstrap>();

            var content = BuildHeader(body, Title, out _, out _,
                leadingIcon: IconGraphic.IconType.Chart);

            UIFactory.ScrollColumn(content, "Scroll", Theme.spaceMd, out var column,
                new RectOffset(0, 0, 0, (int)Theme.spaceLg));

            BuildRingCard(column);
            BuildStatsCard(column);
            BuildElevationCard(column);
        }

        void BuildRingCard(RectTransform column)
        {
            var card = UIFactory.Card(column, "RingCard");
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 500f;

            var ringRect = UIFactory.Rect(card.transform, "Ring", new Vector2(380f, 380f));
            ringRect.anchorMin = new Vector2(0.5f, 0.5f);
            ringRect.anchorMax = new Vector2(0.5f, 0.5f);
            ringRect.anchoredPosition = Vector2.zero;

            m_Ring = ringRect.gameObject.AddComponent<ProgressRingGraphic>();
            m_Ring.color = Theme.success;
            m_Ring.Thickness = 24f;
            m_Ring.Fill = 0f;
            m_Ring.raycastTarget = false;

            m_RingPercent = UIFactory.Label(ringRect, "Percent", "0%", Theme.textDisplay,
                Theme.textPrimary, TextAlignmentOptions.Center, FontWeight.Bold);
            m_RingPercent.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            m_RingPercent.rectTransform.sizeDelta = new Vector2(340f, 90f);

            var caption = UIFactory.Label(ringRect, "Caption", "Completed", Theme.textBody,
                Theme.textSecondary, TextAlignmentOptions.Center);
            caption.rectTransform.anchoredPosition = new Vector2(0f, -52f);
            caption.rectTransform.sizeDelta = new Vector2(340f, 48f);
        }

        void BuildStatsCard(RectTransform column)
        {
            var card = UIFactory.Card(column, "StatsCard");
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 420f;

            var inner = UIFactory.Rect(card.transform, "Rows");
            UIFactory.Stretch(inner, Theme.spaceMd);
            UIFactory.Column(inner, Theme.spaceXs);

            m_TotalDistance = UIFactory.StatRow(inner, "Total Distance", "—");
            m_Completed = UIFactory.StatRow(inner, "Completed", "—");
            m_Remaining = UIFactory.StatRow(inner, "Remaining", "—");
            m_Eta = UIFactory.StatRow(inner, "ETA", "—");
            m_Steps = UIFactory.StatRow(inner, "Steps Climbed", "—");
        }

        void BuildElevationCard(RectTransform column)
        {
            var card = UIFactory.Card(column, "ElevationCard");
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 420f;

            var title = UIFactory.Label(card.transform, "Title", "Elevation Profile",
                Theme.textLabel, Theme.textSecondary, TextAlignmentOptions.Left, FontWeight.Medium);
            UIFactory.AnchorTop(title.rectTransform, 56f, Theme.spaceMd);

            var chart = UIFactory.Rect(card.transform, "Chart");
            chart.anchorMin = Vector2.zero;
            chart.anchorMax = Vector2.one;
            chart.offsetMin = new Vector2(Theme.spaceMd, 64f);
            chart.offsetMax = new Vector2(-Theme.spaceMd, -70f);

            m_Elevation = chart.gameObject.AddComponent<SparklineGraphic>();
            m_Elevation.color = Theme.success;
            m_Elevation.raycastTarget = false;

            m_ElevationMax = UIFactory.Label(card.transform, "AxisMax", "", Theme.textCaption,
                Theme.textTertiary, TextAlignmentOptions.Left);
            m_ElevationMax.rectTransform.anchorMin = new Vector2(0f, 0f);
            m_ElevationMax.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            m_ElevationMax.rectTransform.pivot = new Vector2(0f, 0f);
            m_ElevationMax.rectTransform.offsetMin = new Vector2(Theme.spaceMd, Theme.spaceXs);
            m_ElevationMax.rectTransform.offsetMax = new Vector2(0f, 48f + Theme.spaceXs);

            m_ElevationEnd = UIFactory.Label(card.transform, "AxisEnd", "", Theme.textCaption,
                Theme.textTertiary, TextAlignmentOptions.Right);
            m_ElevationEnd.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            m_ElevationEnd.rectTransform.anchorMax = new Vector2(1f, 0f);
            m_ElevationEnd.rectTransform.pivot = new Vector2(1f, 0f);
            m_ElevationEnd.rectTransform.offsetMin = new Vector2(0f, Theme.spaceXs);
            m_ElevationEnd.rectTransform.offsetMax = new Vector2(-Theme.spaceMd, 48f + Theme.spaceXs);
        }

        void OnEnable() => EventBus.Subscribe<RouteProgressEvent>(OnProgress);
        void OnDisable() => EventBus.Unsubscribe<RouteProgressEvent>(OnProgress);

        void Update()
        {
            if (!IsVisible || m_Ring == null)
                return;

            var smoothed = Mathf.Lerp(m_Ring.Fill, m_TargetFill, Time.unscaledDeltaTime * 3f);

            if (Mathf.Abs(smoothed - m_TargetFill) < 0.001f)
                smoothed = m_TargetFill;

            m_Ring.Fill = smoothed;
            m_RingPercent.text = $"{Mathf.RoundToInt(smoothed * 100f)}%";
        }

        public override void Show()
        {
            base.Show();
            BuildElevationSeries();
            PullCurrentValues();
        }

        void PullCurrentValues()
        {
            var progress = m_Bootstrap?.Progress;

            if (progress == null)
                return;

            OnProgress(new RouteProgressEvent
            {
                completedDistance = progress.CompletedDistance,
                remainingDistance = progress.RemainingDistance,
                progressNormalized = progress.ProgressNormalized,
                estimatedSecondsRemaining = progress.EstimatedSecondsRemaining,
                estimatedSteps = progress.StepCount,
                visitedLandmarkCount = m_Bootstrap?.Landmarks?.VisitedCount ?? 0
            });
        }

        void OnProgress(RouteProgressEvent evt)
        {
            if (m_Ring == null)
                return;

            m_TargetFill = Mathf.Clamp01(evt.progressNormalized);

            var total = m_Bootstrap?.Graph?.TotalDistance ?? 0f;

            m_TotalDistance.text = NavigationHUD.FormatDistance(total);
            m_Completed.text = NavigationHUD.FormatDistance(evt.completedDistance);
            m_Remaining.text = NavigationHUD.FormatDistance(evt.remainingDistance);
            m_Eta.text = NavigationHUD.FormatDuration(evt.estimatedSecondsRemaining);
            m_Steps.text = evt.estimatedSteps > 0 ? $"~{evt.estimatedSteps:N0}" : "—";
        }

        /// <summary>
        /// Builds the elevation series from the route.
        ///
        /// The supplied GeoJSON carries no elevation values, so if every waypoint reports zero
        /// the chart says so rather than drawing a flat line that looks like real data — the
        /// Alipiri walk climbs roughly 500 m and a flat profile would be actively misleading.
        /// </summary>
        void BuildElevationSeries()
        {
            if (m_ElevationBuilt || m_Elevation == null)
                return;

            var graph = m_Bootstrap?.Graph;

            if (graph == null || !graph.IsReady)
                return;

            m_ElevationBuilt = true;

            const int samples = 96;
            var waypoints = graph.Waypoints;
            var values = new List<float>(samples);

            var min = float.MaxValue;
            var max = float.MinValue;

            for (var i = 0; i < samples; i++)
            {
                var index = Mathf.Clamp(
                    Mathf.RoundToInt(i / (float)(samples - 1) * (waypoints.Count - 1)),
                    0, waypoints.Count - 1);

                var elevation = waypoints[index].elevation;
                values.Add(elevation);

                min = Mathf.Min(min, elevation);
                max = Mathf.Max(max, elevation);
            }

            var range = max - min;

            if (range < 1f)
            {
                m_Elevation.SetValues(null);
                m_ElevationMax.text = "No elevation data in the route file";
                m_ElevationEnd.text = NavigationHUD.FormatDistance(graph.TotalDistance);
                return;
            }

            for (var i = 0; i < values.Count; i++)
                values[i] = Mathf.InverseLerp(min, max, values[i]);

            m_Elevation.SetValues(values);
            m_ElevationMax.text = $"{min:F0} m to {max:F0} m";
            m_ElevationEnd.text = NavigationHUD.FormatDistance(graph.TotalDistance);
        }
    }
}
