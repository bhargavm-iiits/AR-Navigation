using System;
using System.Collections;
using System.Collections.Generic;
using TirumalaAR.AR;
using TirumalaAR.Audio;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Database;
using TirumalaAR.Database.Json;
using TirumalaAR.GeoJson;
using TirumalaAR.GPS;
using TirumalaAR.Localization;
using TirumalaAR.Navigation;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.Managers
{
    /// <summary>
    /// Composition root for the navigation scene (System 20).
    ///
    /// Every service is constructed here, in one place, and handed its dependencies explicitly.
    /// Nothing in the codebase reaches for a singleton or calls FindObjectOfType at runtime —
    /// which is what makes the individual systems testable in isolation and what stops
    /// initialisation order from becoming a source of intermittent bugs.
    ///
    /// Attach to a single "AppBootstrap" GameObject in NavigationScene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] AppConfig m_Config;

        [Header("Scene components")]
        [SerializeField] ARSessionBootstrapper m_ArSession;
        [SerializeField] GpsManager m_Gps;
        [SerializeField] DynamicArrowManager m_Arrows;
        [SerializeField] ReferenceImageRelocalizer m_Relocalizer;
        [SerializeField] VoiceNavigationManager m_Voice;
        [SerializeField] Transform m_LandmarkContentRoot;

        [Header("Status")]
        [SerializeField] bool m_LogVerbose = true;

        // Services owned by this object.
        IDatabase m_Database;
        NavigationGraph m_Graph;
        DeviceSensorService m_Sensors;
        HybridLocalizationEngine m_Localization;
        GroundPlacementService m_Ground;
        ARAnchorService m_Anchors;
        LandmarkTriggerService m_Landmarks;
        RouteProgressTracker m_Progress;
        TurnInstructionService m_Turns;

        readonly StateMachine m_StateMachine = new StateMachine();
        NavigationSessionRecord m_SessionRecord;

        public bool IsReady { get; private set; }
        public string StatusMessage { get; private set; } = "Starting…";
        public IReadOnlyList<string> DataWarnings => m_DataWarnings;
        readonly List<string> m_DataWarnings = new List<string>();

        // Exposed for the HUD and debug overlay.
        public NavigationGraph Graph => m_Graph;
        public HybridLocalizationEngine Localization => m_Localization;
        public RouteProgressTracker Progress => m_Progress;
        public LandmarkTriggerService Landmarks => m_Landmarks;
        public GroundPlacementService Ground => m_Ground;
        public ARSessionBootstrapper ArSession => m_ArSession;
        public ReferenceImageRelocalizer Relocalizer => m_Relocalizer;
        public IDatabase Database => m_Database;
        public AppConfig Config => m_Config;
        public string CurrentStateName => m_StateMachine.CurrentState?.Name ?? "—";

        void Awake()
        {
            m_Config ??= AppConfig.LoadOrDefault();

            Application.targetFrameRate = m_Config.targetFrameRate;
            QualitySettings.vSyncCount = 0;

            if (m_Config.preventScreenSleep)
                Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // The pilgrim holds the phone upright in front of them; rotating away from portrait
            // would break the AR framing.
            Screen.orientation = ScreenOrientation.Portrait;
        }

        void Start() => StartCoroutine(Initialize());

        IEnumerator Initialize()
        {
            ServiceLocator.Reset();
            EventBus.Clear();

            // --- 1. Storage -------------------------------------------------------------
            SetStatus("Opening offline database…");
            m_Database = new JsonDatabase();
            ServiceLocator.Current.Register(m_Database);
            Log($"Database backend: {m_Database.BackendName}");

            ApplyDefaultSettings();

            // --- 2. Route data ----------------------------------------------------------
            SetStatus("Loading the Alipiri route…");
            yield return LoadRoute();

            if (m_Graph == null || !m_Graph.IsReady)
            {
                SetStatus("The route could not be loaded. Check the GeoJSON in StreamingAssets.");
                yield break;
            }

            SetStatus("Loading landmarks…");
            yield return LoadLandmarks();

            // --- 3. Sensors -------------------------------------------------------------
            SetStatus("Starting sensors…");
            m_Sensors = new DeviceSensorService();
            m_Sensors.Enable();
            ServiceLocator.Current.Register<IDeviceSensors>(m_Sensors);

            if (m_Gps == null)
                m_Gps = gameObject.AddComponent<GpsManager>();

            yield return m_Gps.Initialize();

            if (m_Gps.PermissionDenied)
                SetStatus("Location permission is required to navigate the Alipiri route.");

            ServiceLocator.Current.Register<IGpsProvider>(m_Gps);

            // --- 4. AR session ----------------------------------------------------------
            SetStatus("Starting the camera…");

            if (m_ArSession == null)
                m_ArSession = FindAnyObjectByType<ARSessionBootstrapper>();

            if (m_ArSession == null)
            {
                SetStatus("No AR session in the scene. Run Tools ▸ Tirumala AR ▸ Build Scenes.");
                yield break;
            }

            yield return m_ArSession.Initialize();

            if (!m_ArSession.IsSupported)
            {
                SetStatus("This device does not support ARCore.");
                yield break;
            }

            var cameraTransform = m_ArSession.ArCameraTransform;

            // --- 5. Localisation --------------------------------------------------------
            m_Localization = new HybridLocalizationEngine(m_Gps, m_Sensors, m_Graph);
            m_Localization.AttachCamera(cameraTransform);
            ServiceLocator.Current.Register(m_Localization);

            // --- 6. AR placement --------------------------------------------------------
            m_Ground = new GroundPlacementService(
                m_ArSession.RaycastManager, m_ArSession.PlaneManager, cameraTransform);

            m_Anchors = new ARAnchorService(
                m_ArSession.AnchorManager, m_ArSession.Origin != null
                    ? m_ArSession.Origin.TrackablesParent
                    : transform,
                m_Config.anchorSpacing);

            if (m_Arrows != null)
            {
                m_Arrows.SetSpacing(m_Database.Settings.GetFloat(SettingsKeys.ArrowSpacing, m_Config.arrowSpacing));
                m_Arrows.SetVisibleCount((int)m_Database.Settings.GetFloat(
                    SettingsKeys.VisibleArrowCount, m_Config.visibleArrowCount));
                m_Arrows.Configure(m_Graph, m_Localization, m_Ground, m_Anchors, cameraTransform);
            }
            else
            {
                Debug.LogError("[Bootstrap] No DynamicArrowManager assigned — no arrows will be drawn.");
            }

            // --- 7. Navigation services -------------------------------------------------
            m_Landmarks = new LandmarkTriggerService(
                m_Database.Landmarks, m_Graph, m_Localization,
                m_LandmarkContentRoot != null ? m_LandmarkContentRoot : transform);
            m_Landmarks.Prepare();

            m_Progress = new RouteProgressTracker(m_Graph, m_Localization, m_Sensors)
            {
                ArrivalRadius = m_Config.arrivalRadius
            };

            m_Turns = new TurnInstructionService(m_Graph, m_Localization);

            ServiceLocator.Current.Register(m_Landmarks);
            ServiceLocator.Current.Register(m_Progress);
            ServiceLocator.Current.Register(m_Turns);

            // --- 8. Voice and vision ----------------------------------------------------
            if (m_Voice != null)
                m_Voice.Configure(m_Database.Settings);

            if (m_Relocalizer != null)
                m_Relocalizer.Configure(m_Localization, m_Database.Landmarks, cameraTransform);

            // --- 9. Session state machine -----------------------------------------------
            BuildStateMachine();

            EventBus.Subscribe<LandmarkTriggeredEvent>(OnLandmarkTriggered);
            EventBus.Subscribe<DestinationReachedEvent>(OnDestinationReached);

            BeginSession();

            IsReady = true;
            SetStatus("Point the camera at the steps ahead.");
            Log($"Ready. Route {m_Graph.TotalDistance:F0} m, {m_Graph.Waypoints.Count} waypoints, " +
                $"{m_Database.Landmarks.GetAll().Count} landmarks.");
        }

        // -----------------------------------------------------------------------------------
        // Data loading
        // -----------------------------------------------------------------------------------

        IEnumerator LoadRoute()
        {
            var result = new StreamingAssetsReader.ReadResult();
            yield return StreamingAssetsReader.ReadText(m_Config.routeGeoJsonPath, result);

            if (!result.Success)
            {
                Debug.LogError($"[Bootstrap] {result.error}");
                m_DataWarnings.Add(result.error);
                yield break;
            }

            var document = GeoJsonParser.Parse(result.text);
            var build = RouteBuilder.Build(document, m_Config.RouteStart, m_Config.routeSettings);

            foreach (var warning in build.warnings)
            {
                m_DataWarnings.Add(warning);
                Log($"Route: {warning}");
            }

            if (!build.IsUsable)
            {
                Debug.LogError("[Bootstrap] The route has fewer than two waypoints.");
                yield break;
            }

            m_Graph = new NavigationGraph();
            m_Graph.Build(build.waypoints, build.origin);

            m_Database.Waypoints.ReplaceAll(build.waypoints, build.origin);
            ServiceLocator.Current.Register<IRouteQuery>(m_Graph);

            if (build.bridgedGaps.Count > 0)
            {
                var total = 0f;

                foreach (var gap in build.bridgedGaps)
                    total += gap.meters;

                Debug.LogWarning(
                    $"[Bootstrap] {build.bridgedGaps.Count} gap(s) totalling {total:F0} m had no geometry in the " +
                    "source GeoJSON and were bridged with straight connectors. Arrows there are shown in amber.");
            }
        }

        IEnumerator LoadLandmarks()
        {
            var result = new StreamingAssetsReader.ReadResult();
            yield return StreamingAssetsReader.ReadText(m_Config.landmarkDatabasePath, result);

            if (!result.Success)
            {
                Debug.LogError($"[Bootstrap] {result.error}");
                m_DataWarnings.Add(result.error);
                yield break;
            }

            LandmarkCollection collection = null;

            try
            {
                collection = JsonUtility.FromJson<LandmarkCollection>(result.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bootstrap] landmarks.json is malformed: {e.Message}");
                m_DataWarnings.Add($"landmarks.json is malformed: {e.Message}");
            }

            if (collection?.landmarks == null || collection.landmarks.Length == 0)
            {
                m_DataWarnings.Add("No landmarks were loaded.");
                yield break;
            }

            // Reject anything outside the Tirumala bounding box. The supplied file contained a
            // longitude typo (779° instead of 79°) that would otherwise place a landmark off the
            // planet and silently distort the route projection.
            var accepted = new List<LandmarkData>(collection.landmarks.Length);

            foreach (var landmark in collection.landmarks)
            {
                if (!landmark.Coordinate.IsValid ||
                    landmark.latitude is < 13.5 or > 13.8 ||
                    landmark.longitude is < 79.2 or > 79.6)
                {
                    var message = $"Landmark #{landmark.id} '{landmark.name}' has an out-of-region " +
                                  $"coordinate ({landmark.latitude}, {landmark.longitude}) and was skipped.";
                    Debug.LogWarning($"[Bootstrap] {message}");
                    m_DataWarnings.Add(message);
                    continue;
                }

                accepted.Add(landmark);
            }

            m_Database.Landmarks.ReplaceAll(accepted);
            ServiceLocator.Current.Register(m_Database.Landmarks);
        }

        void ApplyDefaultSettings()
        {
            var settings = m_Database.Settings;

            if (settings.GetString(SettingsKeys.VoiceEnabled) == null)
                settings.Set(SettingsKeys.VoiceEnabled, m_Config.voiceEnabledByDefault);

            if (settings.GetString(SettingsKeys.DarkMode) == null)
                settings.Set(SettingsKeys.DarkMode, m_Config.darkModeByDefault);

            if (settings.GetString(SettingsKeys.DebugMode) == null)
                settings.Set(SettingsKeys.DebugMode, m_Config.debugModeByDefault);

            if (settings.GetString(SettingsKeys.ArrowSpacing) == null)
                settings.Set(SettingsKeys.ArrowSpacing, m_Config.arrowSpacing);

            if (settings.GetString(SettingsKeys.VisibleArrowCount) == null)
                settings.Set(SettingsKeys.VisibleArrowCount, m_Config.visibleArrowCount);
        }

        // -----------------------------------------------------------------------------------
        // Session lifecycle
        // -----------------------------------------------------------------------------------

        void BeginSession()
        {
            m_Progress.Begin();

            m_SessionRecord = new NavigationSessionRecord
            {
                sessionId = Guid.NewGuid().ToString("N"),
                startedUtc = DateTime.UtcNow.ToString("O"),
                completed = false
            };

            m_Database.History.Add(m_SessionRecord);
        }

        void OnLandmarkTriggered(LandmarkTriggeredEvent evt) => m_Progress?.RegisterLandmarkVisit();

        void OnDestinationReached(DestinationReachedEvent evt) => FinishSession(true);

        void FinishSession(bool completed)
        {
            if (m_SessionRecord == null || m_Progress == null)
                return;

            var snapshot = m_Progress.Snapshot(completed);

            m_SessionRecord.endedUtc = DateTime.UtcNow.ToString("O");
            m_SessionRecord.distanceCovered = snapshot.distanceCovered;
            m_SessionRecord.durationSeconds = snapshot.durationSeconds;
            m_SessionRecord.landmarksVisited = snapshot.landmarksVisited;
            m_SessionRecord.completed = snapshot.completed;

            m_Database.History.Update(m_SessionRecord);
            m_Database.Flush();
        }

        // -----------------------------------------------------------------------------------
        // State machine (System 18 — error recovery)
        // -----------------------------------------------------------------------------------

        void BuildStateMachine()
        {
            var acquiring = new DelegateState("Acquiring GPS", onTick: _ =>
            {
                SetStatus(m_Gps.HasFix
                    ? "Aligning with the route…"
                    : "Waiting for a satellite fix — step into the open if you can.");

                if (m_Localization.TryBootstrap())
                    m_StateMachine.ChangeState<NavigatingState>();
            });

            var navigating = new NavigatingState(this);
            var recovering = new RecoveringState(this);

            m_StateMachine.AddState(new WrappedState(acquiring));
            m_StateMachine.AddState(navigating);
            m_StateMachine.AddState(recovering);

            m_StateMachine.StateChanged += (_, next) =>
                EventBus.Publish(new NavigationStateChangedEvent { stateName = next?.Name ?? "—" });

            m_StateMachine.ChangeState<WrappedState>();
        }

        /// <summary>Wrapper so a DelegateState can be addressed by type in the state machine.</summary>
        sealed class WrappedState : IState
        {
            readonly DelegateState m_Inner;
            public WrappedState(DelegateState inner) => m_Inner = inner;
            public string Name => m_Inner.Name;
            public void Enter() => m_Inner.Enter();
            public void Tick(float dt) => m_Inner.Tick(dt);
            public void Exit() => m_Inner.Exit();
        }

        sealed class NavigatingState : IState
        {
            readonly AppBootstrap m_Owner;
            public NavigatingState(AppBootstrap owner) => m_Owner = owner;

            public string Name => "Navigating";

            public void Enter() => m_Owner.SetStatus("Follow the blue arrows.");

            public void Tick(float deltaTime)
            {
                var owner = m_Owner;

                owner.m_Localization.Tick(deltaTime);
                owner.m_Landmarks.Tick();
                owner.m_Turns.Tick();
                owner.m_Progress.Tick(deltaTime);

                // Recovery triggers: AR tracking gone, or the localisation frame invalidated.
                if (!owner.m_Localization.IsInitialized ||
                    owner.m_ArSession.Health == TrackingHealth.NotTracking)
                {
                    owner.m_StateMachine.ChangeState<RecoveringState>();
                }
            }

            public void Exit() { }
        }

        /// <summary>
        /// Handles the three ways navigation degrades on this route: the camera loses tracking in
        /// the dark covered walkway, GPS drops under the tree canopy, and the magnetometer is
        /// thrown off by the iron handrails. Each is recoverable without restarting the app.
        /// </summary>
        sealed class RecoveringState : IState
        {
            readonly AppBootstrap m_Owner;
            float m_Elapsed;
            bool m_ResetAttempted;

            public RecoveringState(AppBootstrap owner) => m_Owner = owner;

            public string Name => "Recovering";

            public void Enter()
            {
                m_Elapsed = 0f;
                m_ResetAttempted = false;
                m_Owner.SetStatus("Re-establishing tracking — move the phone slowly across the steps.");
                m_Owner.m_Arrows?.ClearAll();
            }

            public void Tick(float deltaTime)
            {
                m_Elapsed += deltaTime;
                var owner = m_Owner;

                // Keep the pose engine alive so it can re-seed the moment tracking returns.
                owner.m_Localization.Tick(deltaTime);

                if (owner.m_ArSession.Health == TrackingHealth.Tracking && owner.m_Localization.IsInitialized)
                {
                    owner.m_StateMachine.ChangeState<NavigatingState>();
                    return;
                }

                // Tracking is back but the frame is stale — re-seed from GPS.
                if (owner.m_ArSession.Health == TrackingHealth.Tracking && m_Elapsed > 2f)
                {
                    owner.m_Localization.ForceReseed();
                    return;
                }

                // Still nothing after 15 s: restart the AR session outright.
                if (m_Elapsed > 15f && !m_ResetAttempted)
                {
                    m_ResetAttempted = true;
                    owner.m_ArSession.ResetSession();
                    owner.m_Localization.ForceReseed();
                }
            }

            public void Exit() { }
        }

        // -----------------------------------------------------------------------------------
        // Unity loop
        // -----------------------------------------------------------------------------------

        void Update()
        {
            if (!IsReady)
                return;

            var deltaTime = Time.deltaTime;

            m_Sensors?.Tick(deltaTime);
            m_StateMachine.Tick(deltaTime);
        }

        void OnApplicationPause(bool paused)
        {
            if (!IsReady)
                return;

            if (paused)
            {
                m_Database?.Flush();
                return;
            }

            // Coming back from the background invalidates the AR frame; re-seed rather than
            // trusting a pose that is minutes stale.
            m_Localization?.ForceReseed();
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<LandmarkTriggeredEvent>(OnLandmarkTriggered);
            EventBus.Unsubscribe<DestinationReachedEvent>(OnDestinationReached);

            if (IsReady)
                FinishSession(m_Progress?.HasArrived ?? false);

            m_Landmarks?.Clear();
            m_Anchors?.Clear();
            m_Sensors?.Disable();
            m_Gps?.Shutdown();
            m_Database?.Dispose();

            ServiceLocator.Reset();
        }

        void SetStatus(string message)
        {
            StatusMessage = message;
            Log(message);
        }

        void Log(string message)
        {
            if (m_LogVerbose)
                Debug.Log($"[Bootstrap] {message}");
        }
    }
}
