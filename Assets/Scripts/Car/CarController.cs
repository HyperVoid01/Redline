using UnityEngine;

public class CarController : MonoBehaviour
{
    [SerializeField] private CarData carData;
    [SerializeField] private Rigidbody rigidBody;

    [Tooltip("wheel1 and wheel2 are the front (steering) wheels. wheel3 and wheel4 are the rear wheels.")]
    [SerializeField] private WheelCollider wheel1, wheel2, wheel3, wheel4;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -0.5f, 0f);

    [Tooltip("Tick on the player's car only. Enemy cars should leave this off.")]
    [SerializeField] private bool updatesHud = true;

    [Tooltip("Tick on the player's car only. Enemy cars should leave this off and set their inputs from AI.")]
    [SerializeField] private bool playerControlled = true;

    private const float MsToKmh = 3.6f;

    private float horizontalInput, verticalInput;
    private bool handbrake;
    private int lastDisplayedSpeed = -1;

    // Drift state
    private WheelFrictionCurve baseRearSideways;
    private float rearGripLoss; // 0 = full grip, 1 = fully sliding
    private float smoothedSteer;

    /// <summary>Signed speed along the car's forward axis in km/h (negative when reversing).</summary>
    public float ForwardSpeedKmh { get; private set; }

    /// <summary>Absolute speed in km/h, regardless of direction.</summary>
    public float SpeedKmh => Mathf.Abs(ForwardSpeedKmh);

    /// <summary>Top speed from CarData in km/h.</summary>
    public float TopSpeedKmh => carData != null ? carData.topSpeed : 0f;

    public float Throttle => Mathf.Abs(verticalInput);

    /// <summary>Angle in degrees between where the car points and where it is travelling.</summary>
    public float DriftAngle { get; private set; }

    /// <summary>True while the car is sliding sideways at speed.</summary>
    public bool IsDrifting =>
        carData != null && SpeedKmh > 10f && Mathf.Abs(DriftAngle) > carData.driftAngleThreshold;

    /// <summary>Lets AI drivers set inputs on cars that are not player controlled.</summary>
    public void SetInputs(float steer, float throttle, bool useHandbrake)
    {
        horizontalInput = Mathf.Clamp(steer, -1f, 1f);
        verticalInput = Mathf.Clamp(throttle, -1f, 1f);
        handbrake = useHandbrake;
    }

    private void Awake()
    {
        if (rigidBody == null)
        {
            rigidBody = GetComponent<Rigidbody>();
        }

        if (carData == null)
        {
            Debug.LogError($"{name}: CarData is not assigned.", this);
            enabled = false;
            return;
        }

        // Local-space offset in meters. Keep it low and near the middle of the car.
        rigidBody.centerOfMass = centerOfMass;

        // Remember the rear wheels' normal grip so the drift can blend away from it and back.
        baseRearSideways = wheel3.sidewaysFriction;
    }

    private void Update()
    {
        if (playerControlled && InputManager.Instance != null)
        {
            horizontalInput = InputManager.Instance.Steer;
            verticalInput = InputManager.Instance.Throttle;
            handbrake = InputManager.Instance.Handbrake;
        }

        UpdateHud();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Signed speed along the car's forward axis, in km/h.
        ForwardSpeedKmh = Vector3.Dot(rigidBody.linearVelocity, transform.forward) * MsToKmh;

        bool hasInput = Mathf.Abs(verticalInput) > 0.01f;
        bool movingMeaningfully = Mathf.Abs(ForwardSpeedKmh) > 1f;
        bool inputOpposesMotion = hasInput && movingMeaningfully
                                  && Mathf.Sign(verticalInput) != Mathf.Sign(ForwardSpeedKmh);

        // Drive torque, cut off once top speed is reached in the direction of input.
        float motor = verticalInput * carData.driveSpeed;
        bool atTopSpeed = Mathf.Abs(ForwardSpeedKmh) >= carData.topSpeed && !inputOpposesMotion;
        if (atTopSpeed || inputOpposesMotion)
        {
            motor = 0f;
        }

        // Split the same total torque between the front and rear axles.
        float frontMotor = motor * 2f * (1f - carData.rearDriveShare);
        float rearMotor = motor * 2f * carData.rearDriveShare;

        // Normal braking uses all wheels. The handbrake only locks the rear wheels,
        // which is what makes the tail swing out.
        float frontBrake = inputOpposesMotion ? carData.brakeForce : 0f;
        float rearBrake = Mathf.Max(
            inputOpposesMotion ? carData.brakeForce : 0f,
            handbrake ? carData.handbrakeForce : 0f);

        UpdateRearGrip(dt);
        ApplyDrive(frontMotor, rearMotor, frontBrake, rearBrake);
        ApplySteering(CalculateSteer(dt));
    }

    private void UpdateHud()
    {
        if (!updatesHud || UIManager.Instance == null)
        {
            return;
        }

        int speed = Mathf.RoundToInt(SpeedKmh);

        // Only touch the text when the displayed number actually changes.
        if (speed != lastDisplayedSpeed)
        {
            lastDisplayedSpeed = speed;
            UIManager.Instance.UpdateSpeed(speed);
        }
    }

    // ---------------------------------------------------------------- Drift

    private void UpdateRearGrip(float dt)
    {
        // Grip drops fast when the handbrake is pulled and returns slowly,
        // so the slide carries on for a moment after release.
        float target = handbrake ? 1f : 0f;
        float rate = handbrake ? carData.gripLossSpeed : carData.gripRecoverySpeed;
        rearGripLoss = Mathf.MoveTowards(rearGripLoss, target, rate * dt);

        // WheelFrictionCurve is a struct, so copy it, change it, and assign it back.
        WheelFrictionCurve friction = baseRearSideways;
        friction.stiffness = Mathf.Lerp(baseRearSideways.stiffness, carData.driftSidewaysStiffness, rearGripLoss);
        wheel3.sidewaysFriction = friction;
        wheel4.sidewaysFriction = friction;
    }

    private float CalculateSteer(float dt)
    {
        // Smooth the input so keyboard taps don't snap the wheels straight to full lock.
        smoothedSteer = Mathf.Lerp(smoothedSteer, horizontalInput, 1f - Mathf.Exp(-carData.steerSmoothing * dt));

        // Less steering lock at high speed.
        float speed01 = carData.topSpeed > 0f ? Mathf.Clamp01(SpeedKmh / carData.topSpeed) : 0f;
        float maxSteer = carData.steeringAngle * Mathf.Lerp(1f, carData.highSpeedSteerFactor, speed01);
        float steer = smoothedSteer * maxSteer;

        // Slip angle: positive when the car is moving to the right of where it points.
        Vector3 velocity = Vector3.ProjectOnPlane(rigidBody.linearVelocity, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (velocity.magnitude > 3f && ForwardSpeedKmh > 0f)
        {
            DriftAngle = Vector3.SignedAngle(forward, velocity, Vector3.up);

            // Point the front wheels toward the direction of travel, which is counter-steering.
            steer += DriftAngle * carData.counterSteerAssist;
        }
        else
        {
            DriftAngle = 0f;
        }

        return Mathf.Clamp(steer, -carData.steeringAngle, carData.steeringAngle);
    }

    // ---------------------------------------------------------------- Wheels

    private void ApplyDrive(float frontMotor, float rearMotor, float frontBrake, float rearBrake)
    {
        wheel1.motorTorque = frontMotor;
        wheel2.motorTorque = frontMotor;
        wheel3.motorTorque = rearMotor;
        wheel4.motorTorque = rearMotor;

        wheel1.brakeTorque = frontBrake;
        wheel2.brakeTorque = frontBrake;
        wheel3.brakeTorque = rearBrake;
        wheel4.brakeTorque = rearBrake;
    }

    private void ApplySteering(float steer)
    {
        wheel1.steerAngle = steer;
        wheel2.steerAngle = steer;
    }
}