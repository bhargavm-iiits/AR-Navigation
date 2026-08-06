using TirumalaAR.Data;
using TirumalaAR.Utilities;
using UnityEngine;

namespace TirumalaAR.Localization
{
    /// <summary>
    /// The rigid transform between AR session space (Unity world metres, arbitrary origin and
    /// arbitrary yaw decided by ARCore at session start) and the geodetic ENU frame the route
    /// lives in.
    ///
    /// This single object is what makes the hybrid approach work. ARCore's visual-inertial
    /// odometry is locally excellent but globally meaningless — it has no idea where north is or
    /// where on Earth it started. GPS is globally meaningful but locally terrible. Keeping the
    /// two in separate frames joined by one correctable transform means:
    ///
    ///   * arrows are placed from AR-tracked geometry, so they never jitter with GPS noise;
    ///   * the route stays geo-referenced, so it never drifts away from the real staircase;
    ///   * a correction (from GPS consensus or a recognised landmark) adjusts the *frame*, not
    ///     the arrows, so the whole world shifts coherently instead of individual arrows sliding.
    ///
    /// Yaw-only rotation is deliberate: ARCore already gravity-aligns its Y axis far better than
    /// any magnetometer or GPS altitude could, so the only unknown rotational degree of freedom
    /// is heading.
    /// </summary>
    public sealed class GeoAnchorFrame
    {
        /// <summary>Rotation from Unity world to ENU about the up axis, in degrees.</summary>
        public float YawDegrees { get; private set; }

        /// <summary>ENU coordinate that Unity world origin maps to.</summary>
        public Vector3 EnuOffset { get; private set; }

        /// <summary>Geodetic origin of the ENU frame (the start of the route).</summary>
        public GeoCoordinate Origin { get; private set; }

        public bool IsInitialized { get; private set; }

        /// <summary>How many times the frame has been corrected. Reported in the debug overlay.</summary>
        public int CorrectionCount { get; private set; }

        /// <summary>Total metres of accumulated positional correction — the drift the system absorbed.</summary>
        public float AccumulatedPositionCorrection { get; private set; }

        public float AccumulatedYawCorrection { get; private set; }

        Quaternion m_UnityToEnuRotation = Quaternion.identity;
        Quaternion m_EnuToUnityRotation = Quaternion.identity;

        public void Initialize(GeoCoordinate origin, Vector3 unityPosition, Vector3 enuPosition, float yawDegrees)
        {
            Origin = origin;
            SetYaw(yawDegrees);
            EnuOffset = enuPosition - m_UnityToEnuRotation * unityPosition;
            IsInitialized = true;
        }

        void SetYaw(float yawDegrees)
        {
            YawDegrees = Mathf.Repeat(yawDegrees, 360f);
            m_UnityToEnuRotation = Quaternion.Euler(0f, YawDegrees, 0f);
            m_EnuToUnityRotation = Quaternion.Inverse(m_UnityToEnuRotation);
        }

        public Vector3 UnityToEnu(Vector3 unityPosition) => m_UnityToEnuRotation * unityPosition + EnuOffset;

        public Vector3 EnuToUnity(Vector3 enuPosition) => m_EnuToUnityRotation * (enuPosition - EnuOffset);

        public Vector3 UnityToEnuDirection(Vector3 unityDirection) => m_UnityToEnuRotation * unityDirection;

        public Vector3 EnuToUnityDirection(Vector3 enuDirection) => m_EnuToUnityRotation * enuDirection;

        /// <summary>Converts a Unity-space forward vector into a true-north compass bearing.</summary>
        public float UnityDirectionToBearing(Vector3 unityDirection) =>
            GeoMath.EnuDirectionToBearing(UnityToEnuDirection(unityDirection));

        public GeoCoordinate UnityToGeodetic(Vector3 unityPosition) =>
            GeoMath.EnuToGeodetic(UnityToEnu(unityPosition), Origin);

        /// <summary>
        /// Nudges the frame so that <paramref name="unityPosition"/> maps to
        /// <paramref name="targetEnu"/>. <paramref name="weight"/> in [0,1] controls how much of
        /// the error is absorbed now — a low weight bleeds GPS corrections in gradually so the
        /// world does not visibly lurch, while a landmark detection can use 1.0 because it is
        /// trustworthy enough to snap.
        /// </summary>
        public void CorrectPosition(Vector3 unityPosition, Vector3 targetEnu, float weight)
        {
            if (!IsInitialized)
                return;

            weight = Mathf.Clamp01(weight);

            var currentEnu = UnityToEnu(unityPosition);
            var error = targetEnu - currentEnu;

            // Height is left to the AR ground raycasts; folding GPS altitude in here would push
            // arrows below or above the actual steps.
            error.y = 0f;

            var applied = error * weight;
            EnuOffset += applied;

            AccumulatedPositionCorrection += applied.magnitude;
            CorrectionCount++;
        }

        /// <summary>
        /// Rotates the frame so that <paramref name="unityDirection"/> reads as
        /// <paramref name="targetBearing"/>. Rotation is applied about
        /// <paramref name="pivotUnityPosition"/> so the user's own position does not slide while
        /// the heading is fixed.
        /// </summary>
        public void CorrectHeading(Vector3 unityDirection, float targetBearing, Vector3 pivotUnityPosition, float weight)
        {
            if (!IsInitialized)
                return;

            weight = Mathf.Clamp01(weight);

            var currentBearing = UnityDirectionToBearing(unityDirection);
            var delta = GeoMath.DeltaAngle(currentBearing, targetBearing) * weight;

            if (Mathf.Abs(delta) < 0.01f)
                return;

            var pivotEnuBefore = UnityToEnu(pivotUnityPosition);
            SetYaw(YawDegrees + delta);

            // Re-derive the offset so the pivot lands where it did before the rotation.
            EnuOffset = pivotEnuBefore - m_UnityToEnuRotation * pivotUnityPosition;

            AccumulatedYawCorrection += Mathf.Abs(delta);
            CorrectionCount++;
        }

        /// <summary>
        /// Hard re-alignment from a recognised landmark: both position and heading are set
        /// outright. Used when a reference image gives an unambiguous six-degree-of-freedom fix.
        /// </summary>
        public void Realign(Vector3 unityPosition, Vector3 unityForward, Vector3 targetEnu, float targetBearing)
        {
            if (!IsInitialized)
            {
                Initialize(Origin, unityPosition, targetEnu, targetBearing);
                return;
            }

            var beforeEnu = UnityToEnu(unityPosition);

            var currentBearing = UnityDirectionToBearing(unityForward);
            SetYaw(YawDegrees + GeoMath.DeltaAngle(currentBearing, targetBearing));

            var flatTarget = new Vector3(targetEnu.x, UnityToEnu(unityPosition).y, targetEnu.z);
            EnuOffset += flatTarget - UnityToEnu(unityPosition);

            AccumulatedPositionCorrection += Vector3.Distance(
                new Vector3(beforeEnu.x, 0f, beforeEnu.z),
                new Vector3(targetEnu.x, 0f, targetEnu.z));
            CorrectionCount++;
        }

        public void Reset()
        {
            IsInitialized = false;
            CorrectionCount = 0;
            AccumulatedPositionCorrection = 0f;
            AccumulatedYawCorrection = 0f;
            EnuOffset = Vector3.zero;
            SetYaw(0f);
        }
    }
}
