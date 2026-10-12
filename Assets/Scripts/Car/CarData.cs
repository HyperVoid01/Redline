using UnityEngine;

[CreateAssetMenu(fileName = "NewCarData", menuName = "Scriptable Objects/Car Data")]
public class CarData : ScriptableObject
{
    [Header("Driving")]
    public float driveSpeed = 1500;
    public float topSpeed = 60;
    public float steeringAngle = 60;
    public float brakeForce = 10;

    [Header("Drive Layout")]
    [Tooltip("0 = front-wheel drive, 0.5 = all-wheel drive, 1 = rear-wheel drive.")]
    [Range(0f, 1f)] public float rearDriveShare = 0.8f;

    [Header("Drift")]
    [Tooltip("Brake torque on the rear wheels while the handbrake is held.")]
    public float handbrakeForce = 3000f;
    [Tooltip("Rear sideways grip while the handbrake is held. Wheel default is 1, lower = slidier.")]
    public float driftSidewaysStiffness = 0.55f;
    [Tooltip("How fast rear grip drops when the handbrake is pulled (per second).")]
    public float gripLossSpeed = 12f;
    [Tooltip("How fast rear grip returns after release (per second). Lower = longer slides.")]
    public float gripRecoverySpeed = 2.5f;
    [Tooltip("How quickly steering input is smoothed. Higher = snappier.")]
    public float steerSmoothing = 8f;
    [Tooltip("Steering angle multiplier at top speed. Lower = calmer steering at speed.")]
    [Range(0.1f, 1f)] public float highSpeedSteerFactor = 0.45f;
    [Tooltip("Auto counter-steer strength. 0 = off.")]
    public float counterSteerAssist = 0.5f;
    [Tooltip("Slip angle in degrees above which the car counts as drifting.")]
    public float driftAngleThreshold = 12f;
}