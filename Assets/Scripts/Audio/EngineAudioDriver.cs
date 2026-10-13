using UnityEngine;

[RequireComponent(typeof(CarController))]
public class EngineAudioDriver : MonoBehaviour
{
    [SerializeField] private SampleEngine engine;
    [SerializeField] private float idleRpm = 900f;
    [SerializeField] private float redlineRpm = 6500f;

    [Header("Gearbox")]
    [Tooltip("Off = one smooth RPM curve across the whole speed range, with no shifts at all.")]
    [SerializeField] private bool useGears = true;
    [SerializeField, Min(1)] private int gearCount = 4;
    [Tooltip("Shift up when RPM passes this fraction of the redline.")]
    [SerializeField, Range(0.5f, 1f)] private float upshiftPoint = 0.85f;
    [Tooltip("Shift down when RPM falls below this fraction of the redline. Kept below the RPM an upshift lands on, so it can't flip-flop.")]
    [SerializeField, Range(0.1f, 0.6f)] private float downshiftPoint = 0.3f;
    [Tooltip("Minimum seconds between any two shifts.")]
    [SerializeField] private float minShiftInterval = 0.8f;
    [Tooltip("How long the throttle lift lasts during a shift.")]
    [SerializeField] private float shiftDuration = 0.15f;
    [Tooltip("Throttle ceiling during a shift. 0 = full lift-off, 1 = no lift.")]
    [SerializeField, Range(0f, 1f)] private float shiftThrottle = 0.4f;

    [Header("Smoothing")]
    [SerializeField] private float rpmSmoothing = 0.1f;
    [Tooltip("Filters bumps and wheel jitter out of the speed used for shifting.")]
    [SerializeField] private float speedSmoothing = 0.15f;

    [Header("Debug")]
    [SerializeField] private bool logShifts;

    private CarController car;
    private int gear = 1;
    private float shiftTimer;
    private float sinceLastShift = 999f;
    private float rpmVel;
    private float speedVel;
    private float smoothedRpm;
    private float smoothedSpeed;

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
        smoothedSpeed = Mathf.SmoothDamp(smoothedSpeed, Mathf.Abs(car.SpeedKmh), ref speedVel, speedSmoothing);

        float rpmNorm;
        float throttle = car.Throttle;

        if (useGears)
        {
            gear = Mathf.Clamp(gear, 1, gearCount);
            sinceLastShift += Time.deltaTime;
            if (shiftTimer > 0f) shiftTimer -= Time.deltaTime;

            rpmNorm = RpmNormInGear(gear, smoothedSpeed, topSpeed);

            // Never let the downshift point sit above the RPM a 1->2 upshift lands on.
            float downPoint = Mathf.Min(downshiftPoint, upshiftPoint * 0.5f * 0.9f);

            if (sinceLastShift >= minShiftInterval)
            {
                if (gear < gearCount && rpmNorm > upshiftPoint) Shift(+1, topSpeed);
                else if (gear > 1 && rpmNorm < downPoint) Shift(-1, topSpeed);
            }

            // Brief throttle lift for the shift; the RPM itself moves through the smoothing.
            if (shiftTimer > 0f) throttle = Mathf.Min(throttle, shiftThrottle);
        }
        else
        {
            gear = 1;
            float norm = Mathf.Clamp01(smoothedSpeed / topSpeed);
            rpmNorm = Mathf.Pow(norm, 0.7f);
        }

        float targetRpm = Mathf.Lerp(idleRpm, redlineRpm, Mathf.Clamp01(rpmNorm));
        smoothedRpm = Mathf.SmoothDamp(smoothedRpm, targetRpm, ref rpmVel, rpmSmoothing);

        engine.SetState(smoothedRpm, throttle);
    }

    // Speed at which a gear hits the redline: the top gear tops out at the car's top speed.
    private float GearTopSpeed(int g, float topSpeed) => topSpeed * g / gearCount;

    private float RpmNormInGear(int g, float speed, float topSpeed)
    {
        return speed / Mathf.Max(1f, GearTopSpeed(g, topSpeed));
    }

    private void Shift(int dir, float topSpeed)
    {
        gear += dir;
        shiftTimer = shiftDuration;
        sinceLastShift = 0f;

        if (logShifts)
            Debug.Log($"{name}: shift to gear {gear} at {smoothedSpeed:0} km/h (top {topSpeed:0})", this);
    }
}

// using UnityEngine;
//
// [RequireComponent(typeof(CarController))]
// public class EngineAudioDriver : MonoBehaviour
// {
//     [SerializeField] private SampleEngine engine;          // changed type
//     [SerializeField] private float idleRpm = 900f;
//     [SerializeField] private float redlineRpm = 6500f;
//     [SerializeField] private int gearCount = 5;
//     [SerializeField] private float shiftDuration = 0.25f;
//     [SerializeField] private float rpmSmoothing = 0.08f;
//
//     private CarController car;
//     private int gear = 1;
//     private float shiftTimer;
//     private float rpmVel;
//     private float smoothedRpm;
//     
//     public float Rpm => smoothedRpm;
//     public float IdleRpm => idleRpm;
//     public float RedlineRpm => redlineRpm;
//     public int Gear => gear;
//
//     private void Awake()
//     {
//         car = GetComponent<CarController>();
//         smoothedRpm = idleRpm;
//     }
//
//     private void Update()
//     {
//         float topSpeed = Mathf.Max(1f, car.TopSpeedKmh);
//         float speed = car.SpeedKmh;
//         float perGear = topSpeed / gearCount;
//
//         if (shiftTimer > 0f)
//         {
//             shiftTimer -= Time.deltaTime;
//         }
//         else
//         {
//             if (gear < gearCount && speed > perGear * gear * 0.95f) { gear++; shiftTimer = shiftDuration; }
//             else if (gear > 1 && speed < perGear * (gear - 1) * 0.6f) { gear--; shiftTimer = shiftDuration; }
//         }
//
//         float gearMin = perGear * (gear - 1);
//         float t = Mathf.InverseLerp(gearMin * 0.6f, perGear * gear, speed);
//         float targetRpm = Mathf.Lerp(idleRpm, redlineRpm, t);
//
//         float throttle = car.Throttle;
//         if (shiftTimer > 0f)
//         {
//             targetRpm = Mathf.Lerp(targetRpm, idleRpm * 1.5f, 0.6f);
//             throttle = 0f;
//         }
//
//         smoothedRpm = Mathf.SmoothDamp(smoothedRpm, targetRpm, ref rpmVel, rpmSmoothing);
//
//         engine.SetState(smoothedRpm, throttle);            // changed call
//     }
// }