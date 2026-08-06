using System.Text;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Database;
using TirumalaAR.Managers;
using TirumalaAR.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace TirumalaAR.UI
{
    /// <summary>
    /// Diagnostic overlay (System 19). Also the data-collection surface for the research write-up:
    /// it surfaces the GPS/AR residual, the accumulated drift the frame has absorbed, and how many
    /// times each correction source has fired.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] AppBootstrap m_Bootstrap;
        [SerializeField] GameObject m_Panel;
        [SerializeField] TMP_Text m_Readout;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] ARPointCloudManager m_PointCloudManager;

        [Header("Behaviour")]
        [SerializeField] float m_RefreshInterval = 0.25f;

        readonly StringBuilder m_Builder = new StringBuilder(1024);

        float m_Timer;
        float m_FpsAccumulator;
        int m_FpsFrames;
        float m_Fps;

        int m_RelocalizationCount;
        float m_TotalDriftRemoved;
        string m_LastRelocalizationName = "—";

        public bool IsVisible { get; private set; }

        void Awake()
        {
            if (m_Bootstrap == null)
                m_Bootstrap = FindAnyObjectByType<AppBootstrap>();

            var enabledByDefault = m_Bootstrap?.Database?.Settings.GetBool(SettingsKeys.DebugMode, false) ?? false;
            SetVisible(enabledByDefault);
        }

        void OnEnable() => EventBus.Subscribe<RelocalizedEvent>(OnRelocalized);
        void OnDisable() => EventBus.Unsubscribe<RelocalizedEvent>(OnRelocalized);

        void OnRelocalized(RelocalizedEvent evt)
        {
            if (evt.source != RelocalizationSource.TrackedImage)
                return;

            m_RelocalizationCount++;
            m_TotalDriftRemoved += evt.correctionMagnitude;
            m_LastRelocalizationName = evt.landmarkName;
        }

        public void Toggle() => SetVisible(!IsVisible);

        public void SetVisible(bool visible)
        {
            IsVisible = visible;

            if (m_Panel != null)
                m_Panel.SetActive(visible);

            m_Bootstrap?.Database?.Settings.Set(SettingsKeys.DebugMode, visible);

            // Feature points and plane meshes are only rendered while debugging — they cost real
            // fill rate on a mid-range phone.
            if (m_PointCloudManager != null)
                m_PointCloudManager.enabled = visible;

            if (m_PlaneManager == null)
                return;

            foreach (var plane in m_PlaneManager.trackables)
            {
                if (plane == null)
                    continue;

                var renderer = plane.GetComponent<MeshRenderer>();

                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        void Update()
        {
            m_FpsAccumulator += Time.unscaledDeltaTime;
            m_FpsFrames++;

            if (!IsVisible || m_Readout == null)
                return;

            m_Timer += Time.unscaledDeltaTime;

            if (m_Timer < m_RefreshInterval)
                return;

            if (m_FpsAccumulator > 0f)
                m_Fps = m_FpsFrames / m_FpsAccumulator;

            m_FpsAccumulator = 0f;
            m_FpsFrames = 0;
            m_Timer = 0f;

            m_Readout.text = BuildReadout();
        }

        string BuildReadout()
        {
            m_Builder.Clear();

            var bootstrap = m_Bootstrap;

            if (bootstrap == null)
                return "No AppBootstrap in the scene.";

            m_Builder.AppendLine($"<b>State</b>  {bootstrap.CurrentStateName}   {m_Fps:F0} fps");

            var localization = bootstrap.Localization;

            if (localization is { IsInitialized: true })
            {
                var frame = localization.Frame;
                var geodetic = GeoMath.EnuToGeodetic(localization.EnuPosition, bootstrap.Graph.Origin);

                m_Builder.AppendLine();
                m_Builder.AppendLine("<b>Pose</b>");
                m_Builder.AppendLine($"  {geodetic}");
                m_Builder.AppendLine($"  ENU  E{localization.EnuPosition.x:F1}  N{localization.EnuPosition.z:F1}");
                m_Builder.AppendLine($"  heading {localization.HeadingDegrees:F0}°   " +
                                     $"speed {localization.SpeedMetersPerSecond:F2} m/s");
                m_Builder.AppendLine($"  last correction  {localization.LastCorrection}");

                m_Builder.AppendLine();
                m_Builder.AppendLine("<b>Frame</b>");
                m_Builder.AppendLine($"  yaw {frame.YawDegrees:F1}°   corrections {frame.CorrectionCount}");
                m_Builder.AppendLine($"  absorbed drift  {frame.AccumulatedPositionCorrection:F1} m / " +
                                     $"{frame.AccumulatedYawCorrection:F0}°");
                m_Builder.AppendLine($"  GPS residual    {localization.GpsResidualMeters:F1} m");

                var projection = localization.Projection;

                m_Builder.AppendLine();
                m_Builder.AppendLine("<b>Route</b>");

                if (projection.isValid)
                {
                    m_Builder.AppendLine($"  waypoint {projection.waypointId} / {bootstrap.Graph.Waypoints.Count}");
                    m_Builder.AppendLine($"  along {projection.distanceAlongRoute:F0} m   " +
                                         $"lateral {projection.lateralOffset:F1} m");
                }
                else
                {
                    m_Builder.AppendLine("  off route");
                }
            }
            else
            {
                m_Builder.AppendLine();
                m_Builder.AppendLine("<b>Pose</b>  not yet localised");
            }

            m_Builder.AppendLine();
            m_Builder.AppendLine("<b>AR</b>");
            m_Builder.AppendLine($"  tracking  {bootstrap.ArSession?.Health}");
            m_Builder.AppendLine($"  planes {bootstrap.Ground?.TrackedPlaneCount ?? 0}   " +
                                 $"slope {bootstrap.Ground?.SurfaceSlopeDegrees ?? 0f:F0}°   " +
                                 $"cam height {bootstrap.Ground?.CameraHeightAboveGround ?? 0f:F2} m");

            m_Builder.AppendLine();
            m_Builder.AppendLine("<b>Re-localisation</b>");
            m_Builder.AppendLine($"  landmark fixes  {m_RelocalizationCount}");
            m_Builder.AppendLine($"  drift removed   {m_TotalDriftRemoved:F1} m");
            m_Builder.AppendLine($"  last            {m_LastRelocalizationName}");
            m_Builder.AppendLine($"  image tracking  " +
                                 $"{(bootstrap.Relocalizer?.IsAvailable == true ? "on" : "unavailable")}");

            var progress = bootstrap.Progress;

            if (progress != null)
            {
                m_Builder.AppendLine();
                m_Builder.AppendLine("<b>Progress</b>");
                m_Builder.AppendLine($"  {progress.CompletedDistance:F0} m done, " +
                                     $"{progress.RemainingDistance:F0} m left ({progress.ProgressNormalized:P0})");
                m_Builder.AppendLine($"  steps {progress.StepCount}   " +
                                     $"landmarks {bootstrap.Landmarks?.VisitedCount ?? 0}");
            }

            if (bootstrap.DataWarnings.Count > 0)
            {
                m_Builder.AppendLine();
                m_Builder.AppendLine($"<b>Data warnings</b> ({bootstrap.DataWarnings.Count})");

                var shown = 0;

                foreach (var warning in bootstrap.DataWarnings)
                {
                    if (shown++ >= 4)
                        break;

                    m_Builder.AppendLine($"  • {warning}");
                }
            }

            return m_Builder.ToString();
        }
    }
}
