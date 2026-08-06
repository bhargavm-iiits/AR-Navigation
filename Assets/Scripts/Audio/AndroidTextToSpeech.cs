using System;
using UnityEngine;

namespace TirumalaAR.Audio
{
    /// <summary>
    /// Thin JNI wrapper over Android's on-device <c>android.speech.tts.TextToSpeech</c>.
    ///
    /// This is what makes voice guidance genuinely offline without shipping a recording for every
    /// possible instruction: the Android TTS engine synthesises locally once a language's voice
    /// data is installed. If it is unavailable the wrapper reports not-ready and the voice manager
    /// silently degrades to on-screen text only.
    /// </summary>
    public sealed class AndroidTextToSpeech : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject m_TextToSpeech;
        AndroidJavaObject m_Activity;

        const int k_QueueFlush = 0;
        const int k_QueueAdd = 1;
#endif

        public bool IsReady { get; private set; }

        public bool IsSpeaking
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    return IsReady && m_TextToSpeech != null && m_TextToSpeech.Call<bool>("isSpeaking");
                }
                catch (Exception)
                {
                    return false;
                }
#else
                return false;
#endif
            }
        }

        public void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                m_Activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                // The listener fires on the Java side once the engine has finished loading.
                var listener = new InitListener(this);
                m_TextToSpeech = new AndroidJavaObject("android.speech.tts.TextToSpeech", m_Activity, listener);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TTS] Could not start Android text to speech: {e.Message}");
                IsReady = false;
            }
#else
            // The editor has no Android TTS; the voice manager falls back to recorded clips.
            IsReady = false;
#endif
        }

        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsReady || m_TextToSpeech == null)
                return;

            try
            {
                // speak(CharSequence, int queueMode, Bundle params, String utteranceId)
                m_TextToSpeech.Call<int>("speak", text, k_QueueFlush, null, "tirumala-nav");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TTS] speak() failed: {e.Message}");
            }
#else
            Debug.Log($"[TTS] {text}");
#endif
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                m_TextToSpeech?.Call<int>("stop");
            }
            catch (Exception)
            {
                // Stopping an already-stopped engine is not an error worth surfacing.
            }
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                m_TextToSpeech?.Call("shutdown");
                m_TextToSpeech?.Dispose();
                m_Activity?.Dispose();
            }
            catch (Exception)
            {
                // Shutting down during app teardown can race with the JVM; nothing to recover.
            }
            finally
            {
                m_TextToSpeech = null;
                m_Activity = null;
            }
#endif
            IsReady = false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Implements android.speech.tts.TextToSpeech$OnInitListener.</summary>
        sealed class InitListener : AndroidJavaProxy
        {
            readonly AndroidTextToSpeech m_Owner;

            public InitListener(AndroidTextToSpeech owner)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                m_Owner = owner;
            }

            // Called by Android. SUCCESS == 0.
            public void onInit(int status)
            {
                m_Owner.IsReady = status == 0;

                if (!m_Owner.IsReady)
                {
                    Debug.LogWarning($"[TTS] Engine initialisation returned status {status}.");
                    return;
                }

                try
                {
                    // Indian English where available; the engine falls back on its own if not.
                    using var locale = new AndroidJavaObject("java.util.Locale", "en", "IN");
                    m_Owner.m_TextToSpeech?.Call<int>("setLanguage", locale);
                    m_Owner.m_TextToSpeech?.Call<int>("setSpeechRate", 0.95f);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TTS] Could not set locale: {e.Message}");
                }
            }
        }
#endif
    }
}
