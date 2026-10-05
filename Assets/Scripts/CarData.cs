using UnityEngine;

[CreateAssetMenu(fileName = "NewCarData", menuName = "Scriptable Objects/Car Data")]
public class CarData : ScriptableObject
{
    [Header("Driving")]
    public float driveSpeed = 1500;
    public float topSpeed = 60;
    public float steeringAngle = 60;
    public float brakeForce = 10;
    
    [Header("Drifting")]
    public float driftGrip = 0.5f;
    public float driftSpeedThreshold = 10;
    public float driftSteeringAngleThreshold = 30;
}