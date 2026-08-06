using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.GPS
{
    /// <summary>
    /// Wraps the compass, gyroscope and accelerometer behind one interface so the localisation
    /// engine can be unit-tested against a fake, and so the rest of the app never touches
    /// UnityEngine.Input directly.
    /// </summary>
    public interface IDeviceSensors
    {
        bool CompassAvailable { get; }
        bool GyroAvailable { get; }

        /// <summary>Filtered true-north heading in degrees [0,360).</summary>
        float HeadingDegrees { get; }

        /// <summary>0 = the compass readings disagree wildly, 1 = rock steady.</summary>
        float HeadingConfidence { get; }

        /// <summary>Device attitude from the gyroscope, in Unity world convention.</summary>
        Quaternion Attitude { get; }

        /// <summary>Gravity-compensated linear acceleration in device space, m/s².</summary>
        Vector3 LinearAcceleration { get; }

        /// <summary>True while the accelerometer suggests the pilgrim is walking rather than standing.</summary>
        bool IsMoving { get; }

        /// <summary>Pedometer-style step count derived from accelerometer peaks.</summary>
        int StepCount { get; }

        void Enable();
        void Disable();
        void Tick(float deltaTime);
    }

    /// <summary>Concrete <see cref="IDeviceSensors"/> backed by the device hardware.</summary>
    public sealed class DeviceSensorService : IDeviceSensors
    {
        readonly CircularMeanFilter m_HeadingFilter = new CircularMeanFilter(20);
        readonly MovingAverageFilter m_AccelerationMagnitude = new MovingAverageFilter(15);

        bool m_Enabled;

        // Step detection state.
        float m_StepCooldown;
        bool m_AbovePeak;

        /// <summary>Acceleration excursion (m/s²) that counts as a footfall. Tuned for stair climbing,
        /// where the vertical impulse is stronger than on flat ground.</summary>
        const float k_StepThreshold = 1.35f;
        const float k_StepReleaseThreshold = 0.55f;
        const float k_MinSecondsBetweenSteps = 0.28f;

        public bool CompassAvailable { get; private set; }
        public bool GyroAvailable { get; private set; }

        public float HeadingDegrees { get; private set; }
        public float HeadingConfidence => m_HeadingFilter.Consistency;
        public Quaternion Attitude { get; private set; } = Quaternion.identity;
        public Vector3 LinearAcceleration { get; private set; }
        public bool IsMoving { get; private set; }
        public int StepCount { get; private set; }

        public void Enable()
        {
            if (m_Enabled)
                return;

            Input.compass.enabled = true;
            CompassAvailable = true;

            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                Input.gyro.updateInterval = 1f / 60f;
                GyroAvailable = true;
            }
            else
            {
                GyroAvailable = false;
                Debug.LogWarning("[Sensors] No gyroscope on this device; attitude falls back to the AR camera pose.");
            }

            m_Enabled = true;
        }

        public void Disable()
        {
            if (!m_Enabled)
                return;

            Input.compass.enabled = false;

            if (GyroAvailable)
                Input.gyro.enabled = false;

            m_Enabled = false;
        }

        public void Tick(float deltaTime)
        {
            if (!m_Enabled || deltaTime <= 0f)
                return;

            UpdateHeading();
            UpdateAttitude();
            UpdateMotion(deltaTime);
        }

        void UpdateHeading()
        {
            if (!CompassAvailable)
                return;

            var raw = Input.compass.trueHeading;

            // A magnetometer that has never been calibrated reports headingAccuracy < 0.
            // Treat that as unusable rather than feeding garbage into the fusion filter.
            if (Input.compass.headingAccuracy < 0f)
                return;

            HeadingDegrees = m_HeadingFilter.Push(raw);
        }

        void UpdateAttitude()
        {
            if (!GyroAvailable)
                return;

            // Unity reports gyro attitude in a right-handed frame with the opposite Z convention,
            // so it must be converted before it can be compared with a Unity world rotation.
            var g = Input.gyro.attitude;
            Attitude = new Quaternion(g.x, g.y, -g.z, -g.w);
        }

        void UpdateMotion(float deltaTime)
        {
            // Subtracting gravity leaves the motion component. Input.acceleration is in g units.
            var acceleration = Input.acceleration * 9.81f;
            var gravity = GyroAvailable ? Input.gyro.gravity * 9.81f : Vector3.down * 9.81f;
            LinearAcceleration = acceleration - gravity;

            var magnitude = LinearAcceleration.magnitude;
            var smoothed = m_AccelerationMagnitude.Push(magnitude);
            IsMoving = smoothed > 0.4f;

            DetectStep(magnitude, deltaTime);
        }

        /// <summary>
        /// Peak-and-release step detector with a refractory period. A simple threshold crossing
        /// double-counts every footfall because the signal oscillates around the threshold, so a
        /// step is only committed once the signal has fallen back below the release level.
        /// </summary>
        void DetectStep(float magnitude, float deltaTime)
        {
            m_StepCooldown -= deltaTime;

            if (!m_AbovePeak)
            {
                if (magnitude > k_StepThreshold && m_StepCooldown <= 0f)
                {
                    m_AbovePeak = true;
                    StepCount++;
                    m_StepCooldown = k_MinSecondsBetweenSteps;
                }
            }
            else if (magnitude < k_StepReleaseThreshold)
            {
                m_AbovePeak = false;
            }
        }
    }
}
