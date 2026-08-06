using System;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TirumalaAR.UI
{
    /// <summary>
    /// The main navigation overlay (System 15).
    ///
    /// Purely a subscriber: it reads events off the bus and never queries the navigation systems
    /// directly, so adding or removing a readout can never affect navigation behaviour.
    /// Text is only rewritten when the displayed value actually changes — string allocation every
    /// frame across a dozen labels is a real cost on a mid-range Android device.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NavigationHUD : MonoBehaviour
    {
        [Header("Top panel")]
        [SerializeField] RectTransform m_CompassNeedle;
        [SerializeField] TMP_Text m_HeadingLabel;
        [SerializeField] TMP_Text m_SpeedLabel;
        [SerializeField] TMP_Text m_AltitudeLabel;

        [Header("Bottom panel")]
        [SerializeField] TMP_Text m_RemainingDistanceLabel;
        [SerializeField] TMP_Text m_EtaLabel;
        [SerializeField] TMP_Text m_NextLandmarkLabel;
        [SerializeField] TMP_Text m_GpsAccuracyLabel;
        [SerializeField] TMP_Text m_BatteryLabel;
        [SerializeField] TMP_Text m_StepCountLabel;
        [SerializeField] Slider m_ProgressBar;
        [SerializeField] TMP_Text m_ProgressLabel;

        [Header("Status")]
        [SerializeField] TMP_Text m_StatusBanner;
        [SerializeField] CanvasGroup m_StatusGroup;
        [SerializeField] Image m_GpsIndicator;
        [SerializeField] Image m_TrackingIndicator;

        [Header("Colours")]
        [SerializeField] Color m_Good = new Color(0.20f, 0.78f, 0.35f);
        [SerializeField] Color m_Warning = new Color(0.98f, 0.68f, 0.11f);
        [SerializeField] Color m_Bad = new Color(0.90f, 0.25f, 0.22f);

        [Header("Wiring")]
        [SerializeField] AppBootstrap m_Bootstrap;

        // Cached last-rendered values so labels are only rewritten on change.
        int m_LastRemainingMeters = -1;
        int m_LastEtaMinutes = -1;
        int m_LastProgressPercent = -1;
        int m_LastSteps = -1;
        int m_LastBatteryPercent = -1;
        int m_LastHeadingDegrees = -1;
        int m_LastSpeedDecimetres = -1;
        int m_LastAccuracyMeters = -1;
        string m_LastLandmarkName;
        string m_LastStatus;

        float m_BatteryTimer;
        float m_StatusHideAt;

        void Awake()
        {
            if (m_Bootstrap == null)
                m_Bootstrap = FindAnyObjectByType<AppBootstrap>();
        }

        void OnEnable()
        {
            EventBus.Subscribe<UserPoseUpdatedEvent>(OnPose);
            EventBus.Subscribe<RouteProgressEvent>(OnProgress);
            EventBus.Subscribe<GpsHealthChangedEvent>(OnGpsHealth);
            EventBus.Subscribe<TrackingStateChangedEvent>(OnTracking);
            EventBus.Subscribe<TurnInstructionEvent>(OnTurn);
            EventBus.Subscribe<LandmarkTriggeredEvent>(OnLandmark);
            EventBus.Subscribe<RelocalizedEvent>(OnRelocalized);
            EventBus.Subscribe<DestinationReachedEvent>(OnArrived);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<UserPoseUpdatedEvent>(OnPose);
            EventBus.Unsubscribe<RouteProgressEvent>(OnProgress);
            EventBus.Unsubscribe<GpsHealthChangedEvent>(OnGpsHealth);
            EventBus.Unsubscribe<TrackingStateChangedEvent>(OnTracking);
            EventBus.Unsubscribe<TurnInstructionEvent>(OnTurn);
            EventBus.Unsubscribe<LandmarkTriggeredEvent>(OnLandmark);
            EventBus.Unsubscribe<RelocalizedEvent>(OnRelocalized);
            EventBus.Unsubscribe<DestinationReachedEvent>(OnArrived);
        }

        void Update()
        {
            UpdateBattery();
            UpdateStatusFade();
            UpdateNextLandmark();

            // Before navigation starts the bootstrap owns the message shown to the user.
            if (m_Bootstrap != null && !m_Bootstrap.IsReady)
                ShowStatus(m_Bootstrap.StatusMessage, 0f);
        }

        // -------------------------------------------------------------------------------
        // Event handlers
        // -------------------------------------------------------------------------------

        void OnPose(UserPoseUpdatedEvent evt)
        {
            var heading = Mathf.RoundToInt(evt.headingDegrees);

            if (heading != m_LastHeadingDegrees)
            {
                m_LastHeadingDegrees = heading;

                if (m_HeadingLabel != null)
                    m_HeadingLabel.text = $"{heading:000}° {CompassPoint(evt.headingDegrees)}";
            }

            // The needle points at true north, so it rotates opposite to the device heading.
            if (m_CompassNeedle != null)
                m_CompassNeedle.localRotation = Quaternion.Euler(0f, 0f, evt.headingDegrees);

            var speedDecimetres = Mathf.RoundToInt(evt.speedMetersPerSecond * 10f);

            if (speedDecimetres != m_LastSpeedDecimetres && m_SpeedLabel != null)
            {
                m_LastSpeedDecimetres = speedDecimetres;
                m_SpeedLabel.text = $"{evt.speedMetersPerSecond * 3.6f:F1} km/h";
            }

            var accuracy = Mathf.RoundToInt(evt.horizontalAccuracy);

            if (accuracy != m_LastAccuracyMeters && m_GpsAccuracyLabel != null)
            {
                m_LastAccuracyMeters = accuracy;
                m_GpsAccuracyLabel.text = evt.horizontalAccuracy > 0f ? $"±{accuracy} m" : "no fix";
            }

            if (m_AltitudeLabel != null && m_Bootstrap?.Localization != null)
            {
                var geodetic = m_Bootstrap.Localization.Frame.IsInitialized
                    ? Utilities.GeoMath.EnuToGeodetic(evt.enuPosition, m_Bootstrap.Graph.Origin)
                    : default;

                m_AltitudeLabel.text = $"{geodetic.altitude:F0} m";
            }
        }

        void OnProgress(RouteProgressEvent evt)
        {
            var remaining = Mathf.RoundToInt(evt.remainingDistance);

            if (remaining != m_LastRemainingMeters && m_RemainingDistanceLabel != null)
            {
                m_LastRemainingMeters = remaining;
                m_RemainingDistanceLabel.text = FormatDistance(evt.remainingDistance);
            }

            var etaMinutes = Mathf.RoundToInt(evt.estimatedSecondsRemaining / 60f);

            if (etaMinutes != m_LastEtaMinutes && m_EtaLabel != null)
            {
                m_LastEtaMinutes = etaMinutes;
                m_EtaLabel.text = FormatDuration(evt.estimatedSecondsRemaining);
            }

            var percent = Mathf.RoundToInt(evt.progressNormalized * 100f);

            if (percent != m_LastProgressPercent)
            {
                m_LastProgressPercent = percent;

                if (m_ProgressBar != null)
                    m_ProgressBar.value = evt.progressNormalized;

                if (m_ProgressLabel != null)
                    m_ProgressLabel.text = $"{percent}%";
            }

            if (evt.estimatedSteps != m_LastSteps && m_StepCountLabel != null)
            {
                m_LastSteps = evt.estimatedSteps;
                m_StepCountLabel.text = $"{evt.estimatedSteps:N0} steps";
            }
        }

        void OnGpsHealth(GpsHealthChangedEvent evt)
        {
            if (m_GpsIndicator != null)
            {
                m_GpsIndicator.color = evt.health switch
                {
                    GpsHealth.Good => m_Good,
                    GpsHealth.Acceptable => m_Warning,
                    _ => m_Bad
                };
            }

            if (evt.health == GpsHealth.NoFix)
                ShowStatus("Satellite signal lost — still guiding from the camera.", 6f);
        }

        void OnTracking(TrackingStateChangedEvent evt)
        {
            if (m_TrackingIndicator != null)
            {
                m_TrackingIndicator.color = evt.health switch
                {
                    TrackingHealth.Tracking => m_Good,
                    TrackingHealth.Limited => m_Warning,
                    _ => m_Bad
                };
            }

            if (evt.health == TrackingHealth.NotTracking)
                ShowStatus("Move the phone slowly to re-establish tracking.", 0f);
            else if (evt.health == TrackingHealth.Tracking)
                HideStatus();
        }

        void OnTurn(TurnInstructionEvent evt)
        {
            if (evt.direction != TurnDirection.Straight)
                ShowStatus(evt.spokenText, 6f);
        }

        void OnLandmark(LandmarkTriggeredEvent evt) => ShowStatus(evt.landmark.name, 5f);

        void OnRelocalized(RelocalizedEvent evt)
        {
            if (evt.source != RelocalizationSource.TrackedImage)
                return;

            ShowStatus($"Position corrected at {evt.landmarkName} ({evt.correctionMagnitude:F0} m).", 4f);
        }

        void OnArrived(DestinationReachedEvent evt) =>
            ShowStatus($"You have arrived. {FormatDistance(evt.totalDistance)} in " +
                       $"{FormatDuration(evt.totalSeconds)}.", 0f);

        // -------------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------------

        void UpdateNextLandmark()
        {
            var landmarks = m_Bootstrap?.Landmarks;
            var next = landmarks?.NextLandmark;
            var name = next?.name;

            if (name == m_LastLandmarkName || m_NextLandmarkLabel == null)
                return;

            m_LastLandmarkName = name;

            m_NextLandmarkLabel.text = next == null
                ? "—"
                : $"{next.name}  ·  {FormatDistance(landmarks.DistanceToNextLandmark)}";
        }

        void UpdateBattery()
        {
            m_BatteryTimer += Time.deltaTime;

            if (m_BatteryTimer < 10f || m_BatteryLabel == null)
                return;

            m_BatteryTimer = 0f;

            var level = SystemInfo.batteryLevel;

            if (level < 0f)
            {
                m_BatteryLabel.text = "—";
                return;
            }

            var percent = Mathf.RoundToInt(level * 100f);

            if (percent == m_LastBatteryPercent)
                return;

            m_LastBatteryPercent = percent;
            m_BatteryLabel.text = $"{percent}%";
        }

        void ShowStatus(string message, float autoHideSeconds)
        {
            if (m_StatusBanner == null || string.IsNullOrEmpty(message))
                return;

            if (message != m_LastStatus)
            {
                m_LastStatus = message;
                m_StatusBanner.text = message;
            }

            if (m_StatusGroup != null)
                m_StatusGroup.alpha = 1f;

            m_StatusHideAt = autoHideSeconds > 0f ? Time.time + autoHideSeconds : float.MaxValue;
        }

        void HideStatus() => m_StatusHideAt = Time.time;

        void UpdateStatusFade()
        {
            if (m_StatusGroup == null || m_StatusHideAt == float.MaxValue)
                return;

            var remaining = m_StatusHideAt - Time.time;

            if (remaining > 0.5f)
                return;

            m_StatusGroup.alpha = Mathf.Clamp01(remaining / 0.5f);
        }

        public static string FormatDistance(float meters) =>
            meters >= 1000f ? $"{meters / 1000f:F2} km" : $"{Mathf.RoundToInt(meters)} m";

        public static string FormatDuration(float seconds)
        {
            if (seconds <= 0f || float.IsInfinity(seconds) || float.IsNaN(seconds))
                return "—";

            var span = TimeSpan.FromSeconds(seconds);

            return span.TotalHours >= 1d
                ? $"{(int)span.TotalHours} h {span.Minutes} min"
                : $"{Mathf.Max(1, (int)span.TotalMinutes)} min";
        }

        static string CompassPoint(float bearing)
        {
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            var index = Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8;
            return points[index];
        }
    }
}
