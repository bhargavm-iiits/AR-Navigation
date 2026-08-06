using System.Collections.Generic;
using TirumalaAR.Core;
using TirumalaAR.Data;
using TirumalaAR.Database;
using UnityEngine;

namespace TirumalaAR.Audio
{
    public enum VoicePriority { Low, Normal, High }

    /// <summary>
    /// Offline voice guidance (System 10).
    ///
    /// Two delivery paths, tried in order:
    ///   1. a pre-recorded clip named by the landmark's <c>audio</c> field, loaded from Resources;
    ///   2. the Android platform text-to-speech engine, which is on-device and needs no network.
    ///
    /// Announcements are queued rather than played over each other, and a short-term memory of
    /// what was recently said suppresses repeats — the single most common complaint about
    /// navigation audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceNavigationManager : MonoBehaviour
    {
        struct Cue
        {
            public string text;
            public AudioClip clip;
            public VoicePriority priority;
            public string dedupeKey;
        }

        [Header("Audio")]
        [SerializeField] AudioSource m_AudioSource;
        [SerializeField, Range(0f, 1f)] float m_Volume = 1f;

        [Header("Behaviour")]
        [Tooltip("Seconds before the same announcement may be repeated.")]
        [SerializeField] float m_DedupeSeconds = 45f;

        [Tooltip("Longest a queued cue may wait before it is considered stale and dropped.")]
        [SerializeField] float m_MaxQueueAgeSeconds = 20f;

        readonly Queue<Cue> m_Queue = new Queue<Cue>();
        readonly Dictionary<string, float> m_LastSpoken = new Dictionary<string, float>();
        readonly Dictionary<string, AudioClip> m_ClipCache = new Dictionary<string, AudioClip>();

        AndroidTextToSpeech m_Tts;
        ISettingsRepository m_Settings;
        float m_BusyUntil;
        float m_QueuedAt;

        public bool IsEnabled { get; private set; } = true;
        public bool TtsAvailable => m_Tts is { IsReady: true };

        public void Configure(ISettingsRepository settings)
        {
            m_Settings = settings;

            if (m_Settings != null)
            {
                IsEnabled = m_Settings.GetBool(SettingsKeys.VoiceEnabled, true);
                m_Volume = m_Settings.GetFloat(SettingsKeys.VoiceVolume, 1f);
            }

            if (m_AudioSource == null)
            {
                m_AudioSource = gameObject.AddComponent<AudioSource>();
                m_AudioSource.playOnAwake = false;
                m_AudioSource.spatialBlend = 0f; // guidance is a voice in your ear, not in the world
            }

            m_AudioSource.volume = m_Volume;

            m_Tts = new AndroidTextToSpeech();
            m_Tts.Initialize();
        }

        void OnEnable()
        {
            EventBus.Subscribe<TurnInstructionEvent>(OnTurnInstruction);
            EventBus.Subscribe<LandmarkTriggeredEvent>(OnLandmarkTriggered);
            EventBus.Subscribe<DestinationReachedEvent>(OnDestinationReached);
            EventBus.Subscribe<GpsHealthChangedEvent>(OnGpsHealthChanged);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<TurnInstructionEvent>(OnTurnInstruction);
            EventBus.Unsubscribe<LandmarkTriggeredEvent>(OnLandmarkTriggered);
            EventBus.Unsubscribe<DestinationReachedEvent>(OnDestinationReached);
            EventBus.Unsubscribe<GpsHealthChangedEvent>(OnGpsHealthChanged);
        }

        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
            m_Settings?.Set(SettingsKeys.VoiceEnabled, enabled);

            if (enabled)
                return;

            m_Queue.Clear();
            Stop();
        }

        public void SetVolume(float volume)
        {
            m_Volume = Mathf.Clamp01(volume);

            if (m_AudioSource != null)
                m_AudioSource.volume = m_Volume;

            m_Settings?.Set(SettingsKeys.VoiceVolume, m_Volume);
        }

        // -------------------------------------------------------------------------------
        // Event handlers
        // -------------------------------------------------------------------------------

        void OnTurnInstruction(TurnInstructionEvent evt) =>
            Speak(evt.spokenText, null, VoicePriority.Normal, $"turn:{evt.direction}");

        void OnLandmarkTriggered(LandmarkTriggeredEvent evt)
        {
            var landmark = evt.landmark;

            if (landmark == null)
                return;

            var clip = LoadClip(landmark.audio);
            var text = string.IsNullOrWhiteSpace(landmark.voiceText)
                ? $"You are approaching {landmark.name}."
                : landmark.voiceText;

            Speak(text, clip, VoicePriority.High, $"landmark:{landmark.id}");
        }

        void OnDestinationReached(DestinationReachedEvent evt) =>
            Speak("You have reached the Tirumala temple. Your Alipiri walk is complete.",
                null, VoicePriority.High, "arrived");

        void OnGpsHealthChanged(GpsHealthChangedEvent evt)
        {
            if (evt.health != GpsHealth.NoFix)
                return;

            Speak("Satellite signal lost. Guidance will continue using camera tracking.",
                null, VoicePriority.Low, "gps:lost");
        }

        // -------------------------------------------------------------------------------
        // Queue
        // -------------------------------------------------------------------------------

        public void Speak(string text, AudioClip clip, VoicePriority priority, string dedupeKey)
        {
            if (!IsEnabled || (string.IsNullOrWhiteSpace(text) && clip == null))
                return;

            var key = dedupeKey ?? text;

            if (m_LastSpoken.TryGetValue(key, out var last) && Time.time - last < m_DedupeSeconds)
                return;

            // A high-priority cue (a landmark the pilgrim is standing at) jumps a queue of
            // routine turn instructions, which would otherwise be stale by the time they play.
            if (priority == VoicePriority.High && m_Queue.Count > 0)
            {
                var retained = new Queue<Cue>();

                foreach (var queued in m_Queue)
                {
                    if (queued.priority == VoicePriority.High)
                        retained.Enqueue(queued);
                }

                m_Queue.Clear();

                foreach (var queued in retained)
                    m_Queue.Enqueue(queued);
            }

            m_LastSpoken[key] = Time.time;

            if (m_Queue.Count == 0)
                m_QueuedAt = Time.time;

            m_Queue.Enqueue(new Cue { text = text, clip = clip, priority = priority, dedupeKey = key });
        }

        void Update()
        {
            if (!IsEnabled || m_Queue.Count == 0 || Time.time < m_BusyUntil)
                return;

            if (m_AudioSource != null && m_AudioSource.isPlaying)
                return;

            if (m_Tts is { IsSpeaking: true })
                return;

            // Drop cues that have been waiting so long they no longer describe the situation.
            if (Time.time - m_QueuedAt > m_MaxQueueAgeSeconds && m_Queue.Peek().priority == VoicePriority.Low)
            {
                m_Queue.Dequeue();
                return;
            }

            Play(m_Queue.Dequeue());
            m_QueuedAt = Time.time;
        }

        void Play(Cue cue)
        {
            if (cue.clip != null && m_AudioSource != null)
            {
                m_AudioSource.clip = cue.clip;
                m_AudioSource.volume = m_Volume;
                m_AudioSource.Play();
                m_BusyUntil = Time.time + cue.clip.length;
                return;
            }

            if (m_Tts is { IsReady: true })
            {
                m_Tts.Speak(cue.text);

                // Rough duration guard so the queue does not stall if the TTS status is unreliable.
                m_BusyUntil = Time.time + Mathf.Clamp(cue.text.Length * 0.06f, 1.5f, 12f);
                return;
            }

            // No clip and no TTS: the text still reaches the UI banner via the event that
            // produced it, so guidance is not lost — only the audio is.
            Debug.Log($"[Voice] (silent) {cue.text}");
        }

        AudioClip LoadClip(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (m_ClipCache.TryGetValue(path, out var cached))
                return cached;

            // Resources.Load wants an extension-less path relative to a Resources folder.
            var resourcePath = path;
            var dot = resourcePath.LastIndexOf('.');

            if (dot > 0)
                resourcePath = resourcePath[..dot];

            var clip = Resources.Load<AudioClip>(resourcePath);
            m_ClipCache[path] = clip;

            if (clip == null)
                Debug.Log($"[Voice] No recorded clip at Resources/{resourcePath}; falling back to speech synthesis.");

            return clip;
        }

        public void Stop()
        {
            if (m_AudioSource != null)
                m_AudioSource.Stop();

            m_Tts?.Stop();
            m_BusyUntil = 0f;
        }

        void OnDestroy()
        {
            m_Tts?.Dispose();
            m_Tts = null;
        }
    }
}
