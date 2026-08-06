using UnityEngine;

namespace TirumalaAR.Data
{
    // ---------------------------------------------------------------------------------------
    // Every cross-system message travels through EventBus as one of these structs. They are
    // structs so publishing does not allocate.
    // ---------------------------------------------------------------------------------------

    public enum GpsHealth { NoFix, Poor, Acceptable, Good }

    public enum TrackingHealth { NotTracking, Limited, Tracking }

    public enum RelocalizationSource { None, Gps, TrackedImage, RouteSnap }

    /// <summary>Raised whenever the fused localisation engine produces a new pose.</summary>
    public struct UserPoseUpdatedEvent
    {
        public Vector3 enuPosition;
        public float headingDegrees;
        public float speedMetersPerSecond;
        public float horizontalAccuracy;
        public GpsHealth gpsHealth;
        public RelocalizationSource lastCorrection;
    }

    /// <summary>Raised each time the user advances to a new nearest waypoint.</summary>
    public struct RouteProgressEvent
    {
        public int currentWaypointId;
        public float completedDistance;
        public float remainingDistance;
        public float progressNormalized;
        public float estimatedSecondsRemaining;
        public float currentSpeed;
        public int estimatedSteps;
        public int visitedLandmarkCount;
    }

    public struct LandmarkTriggeredEvent
    {
        public LandmarkData landmark;
        public float distanceToUser;
    }

    public struct TurnInstructionEvent
    {
        public TurnDirection direction;
        public float distanceToTurn;
        public string spokenText;
    }

    public enum TurnDirection { Straight, SlightLeft, Left, SharpLeft, SlightRight, Right, SharpRight, Arrive }

    public struct TrackingStateChangedEvent
    {
        public TrackingHealth health;
        public string reason;
    }

    public struct GpsHealthChangedEvent
    {
        public GpsHealth health;
        public float horizontalAccuracy;
    }

    /// <summary>Raised when a reference image re-anchors the AR origin against the geodetic frame.</summary>
    public struct RelocalizedEvent
    {
        public RelocalizationSource source;
        public string landmarkName;
        public float correctionMagnitude;
        public float headingCorrectionDegrees;
    }

    public struct DestinationReachedEvent
    {
        public float totalDistance;
        public float totalSeconds;
    }

    public struct NavigationStateChangedEvent
    {
        public string stateName;
    }
}
