using System;
using System.Collections.Generic;
using TirumalaAR.Data;
using TirumalaAR.Database;
using TirumalaAR.Localization;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace TirumalaAR.AR
{
    /// <summary>Binds a reference image name to the landmark it depicts.</summary>
    [Serializable]
    public struct LandmarkImageBinding
    {
        [Tooltip("Must match the image name in the XRReferenceImageLibrary exactly.")]
        public string referenceImageName;

        [Tooltip("id from landmarks.json.")]
        public int landmarkId;

        [Tooltip("How far the recognised structure is allowed to move the pose, 0-1.")]
        [Range(0f, 1f)] public float correctionWeight;
    }

    /// <summary>
    /// Landmark-based visual re-localisation (System 5, and the drift-bounding half of the
    /// research requirement).
    ///
    /// ARCore's image tracking does the recognition; this component decides what a detection
    /// *means*. A tracked image is only accepted as a localisation fix when it is being actively
    /// tracked (not merely remembered), when it is close enough that its pose is trustworthy, and
    /// when the same landmark has not already corrected the pose in the last few seconds. Without
    /// those gates a single poster-like surface flickering in and out of detection would jerk the
    /// whole world back and forth.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReferenceImageRelocalizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ARTrackedImageManager m_TrackedImageManager;

        [Tooltip("Maps each reference image to the landmark it identifies.")]
        [SerializeField] List<LandmarkImageBinding> m_Bindings = new List<LandmarkImageBinding>();

        [Header("Acceptance gates")]
        [Tooltip("A detection further away than this has too poor a pose to trust.")]
        [SerializeField] float m_MaxAcceptDistance = 15f;

        [Tooltip("Seconds before the same landmark may correct the pose again.")]
        [SerializeField] float m_PerLandmarkCooldown = 30f;

        [Tooltip("Marker spawned at each recognised landmark, if any.")]
        [SerializeField] GameObject m_DetectionMarkerPrefab;

        HybridLocalizationEngine m_Localization;
        ILandmarkRepository m_Landmarks;
        Transform m_Camera;

        readonly Dictionary<string, LandmarkImageBinding> m_BindingsByName =
            new Dictionary<string, LandmarkImageBinding>(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<int, float> m_LastCorrectionTime = new Dictionary<int, float>();
        readonly Dictionary<TrackableId, GameObject> m_Markers = new Dictionary<TrackableId, GameObject>();

        public int RecognisedLandmarkCount { get; private set; }
        public string LastRecognisedName { get; private set; } = "—";
        public bool IsAvailable => m_TrackedImageManager != null && m_TrackedImageManager.enabled;

        public void Configure(HybridLocalizationEngine localization, ILandmarkRepository landmarks, Transform camera)
        {
            m_Localization = localization;
            m_Landmarks = landmarks;
            m_Camera = camera;

            m_BindingsByName.Clear();
            foreach (var binding in m_Bindings)
            {
                if (!string.IsNullOrWhiteSpace(binding.referenceImageName))
                    m_BindingsByName[binding.referenceImageName.Trim()] = binding;
            }

            if (m_TrackedImageManager == null)
                m_TrackedImageManager = FindAnyObjectByType<ARTrackedImageManager>();

            if (m_TrackedImageManager == null)
            {
                Debug.LogWarning(
                    "[Relocalizer] No ARTrackedImageManager in the scene. Navigation still works, " +
                    "but drift will only be bounded by GPS and route snapping.");
                return;
            }

            if (m_TrackedImageManager.referenceLibrary == null || m_TrackedImageManager.referenceLibrary.count == 0)
            {
                Debug.LogWarning(
                    "[Relocalizer] The reference image library is empty. Add landmark photographs to it " +
                    "to enable visual re-localisation.");
            }
        }

        void OnEnable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        void OnDisable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (var image in args.added)
                Evaluate(image);

            foreach (var image in args.updated)
                Evaluate(image);

            foreach (var removed in args.removed)
            {
                if (!m_Markers.Remove(removed.Key, out var marker))
                    continue;

                if (marker != null)
                    Destroy(marker);
            }
        }

        void Evaluate(ARTrackedImage image)
        {
            if (image == null || m_Localization == null || !m_Localization.IsInitialized)
                return;

            // Limited tracking means ARCore is extrapolating the image pose from memory rather
            // than seeing it. Those poses are not accurate enough to re-anchor a 7 km route.
            if (image.trackingState != TrackingState.Tracking)
                return;

            var imageName = image.referenceImage.name;

            if (string.IsNullOrEmpty(imageName) || !m_BindingsByName.TryGetValue(imageName, out var binding))
                return;

            var landmark = m_Landmarks?.GetById(binding.landmarkId);

            if (landmark == null)
            {
                Debug.LogWarning($"[Relocalizer] Image '{imageName}' maps to landmark id " +
                                 $"{binding.landmarkId}, which is not in the database.");
                return;
            }

            if (m_Camera != null)
            {
                var distance = Vector3.Distance(m_Camera.position, image.transform.position);

                if (distance > m_MaxAcceptDistance)
                    return;
            }

            if (m_LastCorrectionTime.TryGetValue(landmark.id, out var last) &&
                Time.time - last < m_PerLandmarkCooldown)
                return;

            m_LastCorrectionTime[landmark.id] = Time.time;

            var weight = binding.correctionWeight > 0f ? binding.correctionWeight : 0.85f;
            var observedPosition = m_Camera != null ? m_Camera.position : image.transform.position;

            m_Localization.RelocalizeFromLandmark(landmark, observedPosition, weight);

            RecognisedLandmarkCount++;
            LastRecognisedName = landmark.name;

            SpawnMarker(image, landmark);
        }

        void SpawnMarker(ARTrackedImage image, LandmarkData landmark)
        {
            if (m_DetectionMarkerPrefab == null || m_Markers.ContainsKey(image.trackableId))
                return;

            var marker = Instantiate(m_DetectionMarkerPrefab, image.transform);
            marker.name = $"Detected_{landmark.name}";
            m_Markers[image.trackableId] = marker;
        }

        /// <summary>
        /// Builds a reference library at runtime from textures in Resources/ReferenceImages.
        /// Only used when no authored <c>XRReferenceImageLibrary</c> is assigned — an authored
        /// library is preferable because Unity pre-computes the image quality offline.
        /// </summary>
        public void BuildRuntimeLibraryFromResources(float physicalWidthMeters = 1.5f)
        {
            if (m_TrackedImageManager == null)
                return;

            var textures = Resources.LoadAll<Texture2D>("ReferenceImages");

            if (textures == null || textures.Length == 0)
                return;

            if (m_TrackedImageManager.CreateRuntimeLibrary() is not MutableRuntimeReferenceImageLibrary library)
            {
                Debug.LogWarning("[Relocalizer] This device does not support runtime reference image libraries.");
                return;
            }

            foreach (var texture in textures)
            {
                if (!texture.isReadable)
                {
                    Debug.LogWarning($"[Relocalizer] '{texture.name}' is not marked Read/Write and was skipped.");
                    continue;
                }

                library.ScheduleAddImageWithValidationJob(texture, texture.name,
                    new Vector2(physicalWidthMeters, physicalWidthMeters * texture.height / texture.width).x);
            }

            m_TrackedImageManager.referenceLibrary = library;
            Debug.Log($"[Relocalizer] Built a runtime library with {textures.Length} landmark image(s).");
        }
    }
}
