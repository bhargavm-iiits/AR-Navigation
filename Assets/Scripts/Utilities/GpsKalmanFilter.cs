using UnityEngine;

namespace TirumalaAR.Utilities
{
    /// <summary>
    /// Constant-velocity Kalman filter over the 2D ENU plane (state = [east, north, vEast, vNorth]).
    /// Raw Android GPS on a forested hillside jitters by 5-15 m between samples; feeding that
    /// straight into the arrow renderer makes arrows visibly teleport. The filter trades a small
    /// amount of lag for a position that moves smoothly at walking pace.
    ///
    /// The measurement covariance is driven by the reported horizontal accuracy, so a poor fix is
    /// automatically trusted less than a good one.
    /// </summary>
    public sealed class GpsKalmanFilter
    {
        // State vector.
        double m_E, m_N, m_Ve, m_Vn;

        // Covariance matrix P, stored as a dense 4x4 (small enough that structure isn't worth it).
        readonly double[,] m_P = new double[4, 4];

        /// <summary>Process noise: expected unmodelled acceleration in m/s^2. Walking is well under 1.</summary>
        public double ProcessNoiseAcceleration { get; set; } = 0.35;

        /// <summary>Floor applied to the reported accuracy so an over-optimistic fix cannot dominate.</summary>
        public double MinimumMeasurementSigma { get; set; } = 2.5;

        public bool IsInitialized { get; private set; }

        public Vector2 Position => new Vector2((float)m_E, (float)m_N);
        public Vector2 Velocity => new Vector2((float)m_Ve, (float)m_Vn);
        public float Speed => Mathf.Sqrt((float)(m_Ve * m_Ve + m_Vn * m_Vn));

        /// <summary>Current 1-sigma position uncertainty in metres, averaged over both axes.</summary>
        public float PositionSigma => Mathf.Sqrt((float)(0.5 * (m_P[0, 0] + m_P[1, 1])));

        public void Reset(double east, double north, double accuracyMeters)
        {
            m_E = east;
            m_N = north;
            m_Ve = 0.0;
            m_Vn = 0.0;

            var sigma = Mathd.Max(accuracyMeters, MinimumMeasurementSigma);
            var variance = sigma * sigma;

            System.Array.Clear(m_P, 0, m_P.Length);
            m_P[0, 0] = variance;
            m_P[1, 1] = variance;
            m_P[2, 2] = 4.0;   // 2 m/s initial velocity uncertainty
            m_P[3, 3] = 4.0;

            IsInitialized = true;
        }

        /// <summary>Advances the state by <paramref name="dt"/> seconds with no measurement.</summary>
        public void Predict(double dt)
        {
            if (!IsInitialized || dt <= 0.0)
                return;

            dt = Mathd.Min(dt, 5.0); // A huge gap (app backgrounded) must not blow up the covariance.

            // x = F x
            m_E += m_Ve * dt;
            m_N += m_Vn * dt;

            // P = F P F^T + Q, expanded by hand for the constant-velocity model.
            var dt2 = dt * dt;
            var dt3 = dt2 * dt;
            var dt4 = dt2 * dt2;
            var q = ProcessNoiseAcceleration * ProcessNoiseAcceleration;

            // East block (indices 0 = position, 2 = velocity).
            var p00 = m_P[0, 0] + dt * (m_P[0, 2] + m_P[2, 0]) + dt2 * m_P[2, 2] + 0.25 * dt4 * q;
            var p02 = m_P[0, 2] + dt * m_P[2, 2] + 0.5 * dt3 * q;
            var p22 = m_P[2, 2] + dt2 * q;

            // North block (indices 1 = position, 3 = velocity).
            var p11 = m_P[1, 1] + dt * (m_P[1, 3] + m_P[3, 1]) + dt2 * m_P[3, 3] + 0.25 * dt4 * q;
            var p13 = m_P[1, 3] + dt * m_P[3, 3] + 0.5 * dt3 * q;
            var p33 = m_P[3, 3] + dt2 * q;

            m_P[0, 0] = p00; m_P[0, 2] = p02; m_P[2, 0] = p02; m_P[2, 2] = p22;
            m_P[1, 1] = p11; m_P[1, 3] = p13; m_P[3, 1] = p13; m_P[3, 3] = p33;
        }

        /// <summary>
        /// Folds in a GPS fix. East and north are ENU metres; accuracy is the reported horizontal
        /// accuracy in metres (Android's 68% confidence radius).
        /// </summary>
        public void Update(double east, double north, double accuracyMeters)
        {
            if (!IsInitialized)
            {
                Reset(east, north, accuracyMeters);
                return;
            }

            var sigma = Mathd.Max(accuracyMeters, MinimumMeasurementSigma);
            var r = sigma * sigma;

            UpdateAxis(0, 2, east, r);
            UpdateAxis(1, 3, north, r);
        }

        /// <summary>
        /// Scalar Kalman update for one axis. Position and velocity on a given axis are only
        /// coupled to each other in the constant-velocity model, so the 4x4 update decomposes
        /// into two independent 2x2 updates — much cheaper and numerically better behaved.
        /// </summary>
        void UpdateAxis(int posIndex, int velIndex, double measurement, double r)
        {
            var pp = m_P[posIndex, posIndex];
            var pv = m_P[posIndex, velIndex];
            var vv = m_P[velIndex, velIndex];

            var s = pp + r;                       // innovation covariance
            if (s < 1e-12)
                return;

            var kPos = pp / s;                    // Kalman gain, position
            var kVel = pv / s;                    // Kalman gain, velocity

            var current = posIndex == 0 ? m_E : m_N;
            var innovation = measurement - current;

            if (posIndex == 0)
            {
                m_E += kPos * innovation;
                m_Ve += kVel * innovation;
            }
            else
            {
                m_N += kPos * innovation;
                m_Vn += kVel * innovation;
            }

            // P = (I - K H) P
            m_P[posIndex, posIndex] = pp - kPos * pp;
            m_P[posIndex, velIndex] = pv - kPos * pv;
            m_P[velIndex, posIndex] = pv - kVel * pp;
            m_P[velIndex, velIndex] = vv - kVel * pv;
        }

        /// <summary>
        /// Applies an external position correction (e.g. a landmark image re-localisation) without
        /// discarding the velocity estimate. <paramref name="confidenceSigma"/> is how much the
        /// correction is trusted, in metres.
        /// </summary>
        public void ApplyCorrection(double east, double north, double confidenceSigma)
        {
            if (!IsInitialized)
            {
                Reset(east, north, confidenceSigma);
                return;
            }

            var r = confidenceSigma * confidenceSigma;
            UpdateAxis(0, 2, east, r);
            UpdateAxis(1, 3, north, r);
        }
    }

    /// <summary>Double-precision equivalents of the Mathf helpers this project needs.</summary>
    internal static class Mathd
    {
        public static double Max(double a, double b) => a > b ? a : b;
        public static double Min(double a, double b) => a < b ? a : b;
        public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
