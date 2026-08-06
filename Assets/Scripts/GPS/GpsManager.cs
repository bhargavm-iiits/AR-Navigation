using System;
using System.Collections;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Utilities;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace TirumalaAR.GPS
{
    public struct GpsReading
    {
        public GeoCoordinate coordinate;
        public float horizontalAccuracy;
        public float verticalAccuracy;
        public double timestamp;
        public bool isValid;
    }

    public interface IGpsProvider
    {
        bool IsRunning { get; }
        bool HasFix { get; }
        GpsReading Latest { get; }
        GpsHealth Health { get; }

        /// <summary>Seconds since the last accepted fix. Grows when the sky view is blocked.</summary>
        float SecondsSinceFix { get; }

        event Action<GpsReading> FixReceived;
    }

    /// <summary>
    /// Owns the Android location service (System 3). Responsible for permission, start-up,
    /// polling, and rejecting fixes that are too poor to use.
    ///
    /// Raw fixes are published as-is; smoothing lives in <c>HybridLocalizationEngine</c> so that
    /// the research trace can record both the unfiltered and the fused track.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GpsManager : MonoBehaviour, IGpsProvider
    {
        [Header("Service configuration")]
        [Tooltip("Desired accuracy in metres passed to the location service.")]
        [SerializeField] float m_DesiredAccuracy = 5f;

        [Tooltip("Minimum movement in metres before the OS reports a new fix.")]
        [SerializeField] float m_UpdateDistance = 1f;

        [Tooltip("Seconds to wait for the first fix before giving up.")]
        [SerializeField] float m_InitializationTimeout = 25f;

        [Tooltip("Fixes worse than this are discarded outright — under tree cover they are pure noise.")]
        [SerializeField] float m_RejectAccuracyAbove = 60f;

        double m_LastTimestamp = -1d;
        float m_TimeSinceFix;
        GpsHealth m_Health = GpsHealth.NoFix;

        public bool IsRunning { get; private set; }
        public bool HasFix { get; private set; }
        public GpsReading Latest { get; private set; }
        public GpsHealth Health => m_Health;
        public float SecondsSinceFix => m_TimeSinceFix;

        /// <summary>True when the user permanently denied location access.</summary>
        public bool PermissionDenied { get; private set; }

        public event Action<GpsReading> FixReceived;

        public IEnumerator Initialize()
        {
            yield return RequestPermission();

            if (PermissionDenied)
            {
                Debug.LogError("[GPS] Location permission denied — navigation cannot start.");
                yield break;
            }

            if (!Input.location.isEnabledByUser)
            {
                Debug.LogError("[GPS] Location services are turned off in system settings.");
                yield break;
            }

            Input.location.Start(m_DesiredAccuracy, m_UpdateDistance);

            var elapsed = 0f;
            while (Input.location.status == LocationServiceStatus.Initializing && elapsed < m_InitializationTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                Debug.LogError($"[GPS] Location service failed to start (status: {Input.location.status}).");
                Input.location.Stop();
                yield break;
            }

            IsRunning = true;
            Debug.Log("[GPS] Location service running.");
        }

        IEnumerator RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                yield break;

            var granted = false;
            var responded = false;

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { granted = true;  responded = true; };
            callbacks.PermissionDenied  += _ => { granted = false; responded = true; };
            callbacks.PermissionDeniedAndDontAskAgain += _ => { granted = false; responded = true; };

            Permission.RequestUserPermission(Permission.FineLocation, callbacks);

            // The dialog is modal to the app, so a plain wait is correct here.
            var waited = 0f;
            while (!responded && waited < 60f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            PermissionDenied = !granted;
#else
            // The editor and desktop players have no runtime permission model.
            yield break;
#endif
        }

        void Update()
        {
            if (!IsRunning)
                return;

            m_TimeSinceFix += Time.deltaTime;

            if (Input.location.status != LocationServiceStatus.Running)
            {
                DemoteHealth();
                return;
            }

            var data = Input.location.lastData;

            // The service repeats the previous sample until a genuinely new one arrives.
            if (Math.Abs(data.timestamp - m_LastTimestamp) < 1e-6)
            {
                DemoteHealth();
                return;
            }

            m_LastTimestamp = data.timestamp;

            if (data.horizontalAccuracy <= 0f || data.horizontalAccuracy > m_RejectAccuracyAbove)
            {
                Debug.Log($"[GPS] Rejected a fix with {data.horizontalAccuracy:F0} m accuracy.");
                DemoteHealth();
                return;
            }

            var reading = new GpsReading
            {
                coordinate = new GeoCoordinate(data.latitude, data.longitude, data.altitude),
                horizontalAccuracy = data.horizontalAccuracy,
                verticalAccuracy = data.verticalAccuracy,
                timestamp = data.timestamp,
                isValid = true
            };

            if (!reading.coordinate.IsValid)
            {
                DemoteHealth();
                return;
            }

            Latest = reading;
            HasFix = true;
            m_TimeSinceFix = 0f;

            UpdateHealth(ClassifyAccuracy(reading.horizontalAccuracy));
            FixReceived?.Invoke(reading);
        }

        static GpsHealth ClassifyAccuracy(float accuracy) => accuracy switch
        {
            <= 8f => GpsHealth.Good,
            <= 20f => GpsHealth.Acceptable,
            _ => GpsHealth.Poor
        };

        /// <summary>Degrades the reported health the longer the app goes without a usable fix.</summary>
        void DemoteHealth()
        {
            if (m_TimeSinceFix > 20f)
                UpdateHealth(GpsHealth.NoFix);
            else if (m_TimeSinceFix > 8f && m_Health == GpsHealth.Good)
                UpdateHealth(GpsHealth.Acceptable);
        }

        void UpdateHealth(GpsHealth health)
        {
            if (health == m_Health)
                return;

            m_Health = health;

            if (health == GpsHealth.NoFix)
                HasFix = false;

            EventBus.Publish(new GpsHealthChangedEvent
            {
                health = health,
                horizontalAccuracy = Latest.horizontalAccuracy
            });
        }

        void OnDestroy() => Shutdown();

        public void Shutdown()
        {
            if (!IsRunning)
                return;

            Input.location.Stop();
            IsRunning = false;
            HasFix = false;
        }
    }
}
