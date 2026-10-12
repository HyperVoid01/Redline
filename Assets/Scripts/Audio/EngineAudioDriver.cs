using UnityEngine;

[RequireComponent(typeof(CarController))]
public class EngineAudioDriver : MonoBehaviour
{
    [SerializeField] private SampleEngine engine;          // changed type
    [SerializeField] private float idleRpm = 900f;
    [SerializeField] private float redlineRpm = 6500f;
    [SerializeField] private int gearCount = 5;
    [SerializeField] private float shiftDuration = 0.25f;
    [SerializeField] private float rpmSmoothing = 0.08f;

    private CarController car;
    private int gear = 1;
    private float shiftTimer;
    private float rpmVel;
    private float smoothedRpm;
    
    public float Rpm => smoothedRpm;
    public float IdleRpm => idleRpm;
    public float RedlineRpm => redlineRpm;
    public int Gear => gear;

    private void Awake()
    {
        car = GetComponent<CarController>();
        smoothedRpm = idleRpm;
    }

    private void Update()
    {
        float topSpeed = Mathf.Max(1f, car.TopSpeedKmh);
        float speed = car.SpeedKmh;
        float perGear = topSpeed / gearCount;

        if (shiftTimer > 0f)
        {
            shiftTimer -= Time.deltaTime;
        }
        else
        {
            if (gear < gearCount && speed > perGear * gear * 0.95f) { gear++; shiftTimer = shiftDuration; }
            else if (gear > 1 && speed < perGear * (gear - 1) * 0.6f) { gear--; shiftTimer = shiftDuration; }
        }

        float gearMin = perGear * (gear - 1);
        float t = Mathf.InverseLerp(gearMin * 0.6f, perGear * gear, speed);
        float targetRpm = Mathf.Lerp(idleRpm, redlineRpm, t);

        float throttle = car.Throttle;
        if (shiftTimer > 0f)
        {
            targetRpm = Mathf.Lerp(targetRpm, idleRpm * 1.5f, 0.6f);
            throttle = 0f;
        }

        smoothedRpm = Mathf.SmoothDamp(smoothedRpm, targetRpm, ref rpmVel, rpmSmoothing);

        engine.SetState(smoothedRpm, throttle);            // changed call
    }
}