using System.Collections;
using TirumalaAR.Core;
using TirumalaAR.Data;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace TirumalaAR.AR
{
    /// <summary>
    /// Owns the AR session lifecycle and republishes tracking health on the event bus.
    /// Everything else in the AR layer assumes a running, tracking session and asks this object
    /// rather than poking at ARSession statics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARSessionBootstrapper : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] ARSession m_Session;
        [SerializeField] XROrigin m_Origin;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] ARAnchorManager m_AnchorManager;
        [SerializeField] ARCameraManager m_CameraManager;
        [SerializeField] AROcclusionManager m_OcclusionManager;

        [Header("Behaviour")]
        [Tooltip("Seconds of continuous limited tracking before recovery is triggered.")]
        [SerializeField] float m_LostTrackingGrace = 3f;

        float m_LimitedFor;
        TrackingHealth m_Health = TrackingHealth.NotTracking;

        public ARSession Session => m_Session;
        public XROrigin Origin => m_Origin;
        public ARPlaneManager PlaneManager => m_PlaneManager;
        public ARRaycastManager RaycastManager => m_RaycastManager;
        public ARAnchorManager AnchorManager => m_AnchorManager;
        public ARCameraManager CameraManager => m_CameraManager;
        public AROcclusionManager OcclusionManager => m_OcclusionManager;

        public Camera ArCamera => m_Origin != null ? m_Origin.Camera : null;
        public Transform ArCameraTransform => ArCamera != null ? ArCamera.transform : null;
        public TrackingHealth Health => m_Health;

        /// <summary>True once the device has reported that AR is supported and the session is live.</summary>
        public bool IsReady { get; private set; }

        public bool IsSupported { get; private set; }

        void Reset()
        {
            m_Session = FindAnyObjectByType<ARSession>();
            m_Origin = FindAnyObjectByType<XROrigin>();
            m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();
            m_AnchorManager = FindAnyObjectByType<ARAnchorManager>();
            m_CameraManager = FindAnyObjectByType<ARCameraManager>();
            m_OcclusionManager = FindAnyObjectByType<AROcclusionManager>();
        }

        public IEnumerator Initialize()
        {
            if (m_Session == null)
                m_Session = FindAnyObjectByType<ARSession>();

            if (m_Session == null)
            {
                Debug.LogError("[AR] No ARSession in the scene — AR navigation cannot start.");
                yield break;
            }

            yield return RequestCameraPermission();

            if (CameraPermissionDenied)
            {
                Debug.LogError("[AR] Camera permission denied — the AR view cannot be shown.");
                yield break;
            }

            if ((ARSession.state == ARSessionState.None) || (ARSession.state == ARSessionState.CheckingAvailability))
                yield return ARSession.CheckAvailability();

            if (ARSession.state == ARSessionState.NeedsInstall)
                yield return ARSession.Install();

            IsSupported = ARSession.state != ARSessionState.Unsupported;

            if (!IsSupported)
            {
                Debug.LogError("[AR] ARCore is not supported or not installed on this device.");
                yield break;
            }

            m_Session.enabled = true;

            // Horizontal planes are what the arrows land on. Vertical planes cost tracking budget
            // and are of no use on a staircase, so they stay off.
            if (m_PlaneManager != null)
                m_PlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

            // Environment depth gives real occlusion: arrows behind a step edge get hidden, which
            // is the single biggest cue that they are physically on the staircase.
            if (m_OcclusionManager != null)
            {
                m_OcclusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Medium;
                m_OcclusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            }

            while (ARSession.state is ARSessionState.SessionInitializing or ARSessionState.Ready)
                yield return null;

            IsReady = ARSession.state == ARSessionState.SessionTracking;
            Debug.Log($"[AR] Session state: {ARSession.state}");
        }

        /// <summary>True when the user refused camera access.</summary>
        public bool CameraPermissionDenied { get; private set; }

        /// <summary>
        /// Requests CAMERA explicitly rather than letting ARCore prompt implicitly.
        ///
        /// ARCore does ask on its own, but it does so during session start, which means a denial
        /// surfaces as a generic "session unsupported" state that is indistinguishable from an
        /// incompatible device. Asking first makes the difference reportable to the user.
        /// </summary>
        IEnumerator RequestCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Camera))
                yield break;

            var responded = false;
            var granted = false;

            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => { granted = true;  responded = true; };
            callbacks.PermissionDenied  += _ => { granted = false; responded = true; };
            callbacks.PermissionDeniedAndDontAskAgain += _ => { granted = false; responded = true; };

            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Camera, callbacks);

            var waited = 0f;
            while (!responded && waited < 60f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            CameraPermissionDenied = !granted;
#else
            yield break;
#endif
        }

        void OnEnable() => ARSession.stateChanged += OnSessionStateChanged;
        void OnDisable() => ARSession.stateChanged -= OnSessionStateChanged;

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            IsReady = args.state == ARSessionState.SessionTracking;

            if (args.state != ARSessionState.SessionTracking)
                SetHealth(TrackingHealth.NotTracking, args.state.ToString());
        }

        void Update()
        {
            if (m_Session == null || ARSession.state != ARSessionState.SessionTracking)
                return;

            switch (ARSession.notTrackingReason)
            {
                case NotTrackingReason.None:
                    m_LimitedFor = 0f;
                    SetHealth(TrackingHealth.Tracking, null);
                    break;

                case NotTrackingReason.Initializing:
                case NotTrackingReason.Relocalizing:
                    m_LimitedFor += Time.deltaTime;
                    SetHealth(TrackingHealth.Limited, ARSession.notTrackingReason.ToString());
                    break;

                default:
                    m_LimitedFor += Time.deltaTime;
                    SetHealth(m_LimitedFor > m_LostTrackingGrace ? TrackingHealth.NotTracking : TrackingHealth.Limited,
                        ARSession.notTrackingReason.ToString());
                    break;
            }
        }

        void SetHealth(TrackingHealth health, string reason)
        {
            if (health == m_Health)
                return;

            m_Health = health;
            EventBus.Publish(new TrackingStateChangedEvent { health = health, reason = reason });
        }

        /// <summary>Hard session reset, used when tracking cannot recover on its own.</summary>
        public void ResetSession()
        {
            if (m_Session == null)
                return;

            Debug.LogWarning("[AR] Resetting the AR session to recover tracking.");
            m_Session.Reset();
            m_LimitedFor = 0f;
        }
    }
}
