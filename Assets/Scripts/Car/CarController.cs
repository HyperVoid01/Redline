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

    [Tooltip("Tick on the player's car only. Enemy cars should leave this off and set their inputs from AI. " +
             "The steering settings (response and assist) only apply to a player controlled car.")]
    [SerializeField] private bool playerControlled = true;

    [Header("Steering feel (player only)")]
    [Tooltip("Makes small stick movements steer less, so barely turning really is barely turning. Full lock is unchanged.")]
    [SerializeField] private bool useSteerCurve = true;
    [Tooltip("1 = linear (old behaviour). 1.5 to 2 = much finer control near the centre. Keyboard is unaffected, it is always full or nothing.")]
    [SerializeField, Range(1f, 3f)] private float steerCurvePower = 1.6f;

    [Header("Stability (stops the rear stepping out)")]
    [Tooltip("Multiplies the rear wheels' sideways grip (outside of handbrake drifts). Above 1 makes the car understeer a little instead of sliding. " +
             "1 = old behaviour. Try 1.2 to 1.6.")]
    [SerializeField, Range(0.5f, 3f)] private float rearGripMultiplier = 1.3f;

    [Tooltip("Cuts the drive torque on an axle while its wheels are slipping, so the tyres keep their grip. Not active while the handbrake is held. " +
             "This is the car's own switch. On the player's car the Traction Control toggle in the settings menu must also be on.")]
    [SerializeField] private bool tractionControl = true;
    [Tooltip("Wheel slip (WheelHit slip, 0 = full grip) where the torque starts being cut.")]
    [SerializeField, Range(0.05f, 1.5f)] private float tractionStartSlip = 0.4f;
    [Tooltip("Wheel slip where the torque is cut as far as it goes.")]
    [SerializeField, Range(0.1f, 2f)] private float tractionFullSlip = 0.9f;
    [Tooltip("Fraction of the torque kept at full slip. 0 = cut it completely, 1 = no cut.")]
    [SerializeField, Range(0f, 1f)] private float tractionMinTorque = 0.25f;

    [Header("Taillights")]
    [Tooltip("Renderers that use the emissive taillight material. Leave empty to disable the emissive effect.")]
    [SerializeField] private Renderer[] taillightRenderers;
    [Tooltip("Which material slot on those renderers is the taillight material (0 = first slot).")]
    [SerializeField] private int taillightMaterialIndex = 0;
    [Tooltip("The emission already set on the taillight material is used as the idle (not braking) look. " +
             "This colour is only used as a fallback if that material's emission is black.")]
    [SerializeField, ColorUsage(false, true)] private Color taillightFallbackColor = Color.red;
    [Tooltip("While braking, the material's own emission is multiplied by this. 1 = no change, 4 = four times brighter. " +
             "Pair with Bloom in the post-processing volume for a visible glow.")]
    [SerializeField, Min(1f)] private float taillightBrakeMultiplier = 4f;
    [Tooltip("Optional real lights (small red point lights behind the car). They are only enabled while braking. " +
             "Best on the player's car only, to keep the light count low.")]
    [SerializeField] private Light[] brakeLights;

    [Header("Death")]
    [Tooltip("Ignore the player's driving input once the car's health reaches 0, so the wreck can't be driven during the explosion.")]
    [SerializeField] private bool disableInputWhenDead = true;
    [Tooltip("Found automatically on this car if left empty. Only used on the player's car.")]
    [SerializeField] private PlayerHealth health;

    [Header("Finish (player only)")]
    [Tooltip("Ignore the player's driving input once the finish line is crossed, so the car just rolls on.")]
    [SerializeField] private bool disableInputOnFinish = true;
    [Tooltip("Brake gently after the finish line so the car slows down by itself.")]
    [SerializeField] private bool autoBrakeOnFinish = true;
    [Tooltip("Braking after the finish as a fraction of CarData.brakeForce. Raise it if the car doesn't stop within the runout, lower it for a longer roll.")]
    [SerializeField, Range(0f, 1f)] private float finishBrakeFraction = 0.5f;

    private const float MsToKmh = 3.6f;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private float horizontalInput, verticalInput;
    private bool handbrake;
    private int lastDisplayedSpeed = -1;

    // Drift state
    private WheelFrictionCurve baseRearSideways;
    private float rearGripLoss; // 0 = full grip, 1 = fully sliding
    private float appliedRearStiffness = float.NaN; // last value written to the rear wheels

    // Wheel ground hits, read once per frame and shared with the audio and tyre effects.
    private readonly WheelHit[] frameHits = new WheelHit[4];
    private readonly bool[] frameGrounded = new bool[4];
    private int frameHitsStamp = -1;

    private float smoothedSteer;

    // Taillight state
    private Material[] taillightMaterials;
    private Color[] taillightIdleColors;
    private bool taillightsBraking;
    private bool taillightsInitialised;

    /// <summary>Signed speed along the car's forward axis in km/h (negative when reversing).</summary>
    public float ForwardSpeedKmh { get; private set; }

    /// <summary>Absolute speed in km/h, regardless of direction.</summary>
    public float SpeedKmh => Mathf.Abs(ForwardSpeedKmh);

    /// <summary>Top speed from CarData in km/h.</summary>
    public float TopSpeedKmh => carData != null ? carData.topSpeed : 0f;

    public float Throttle => Mathf.Abs(verticalInput);

    /// <summary>Angle in degrees between where the car points and where it is travelling.</summary>
    public float DriftAngle { get; private set; }

    /// <summary>Steering angle actually applied to the front wheels, in degrees (positive = right).</summary>
    public float SteerAngle { get; private set; }

    /// <summary>True while any brake (pedal, handbrake or finish auto brake) is being applied.</summary>
    public bool IsBraking { get; private set; }

    /// <summary>Applied steering as a fraction of full lock, from -1 to 1.</summary>
    public float SteerNormalized =>
        carData != null && carData.steeringAngle > 0f
            ? Mathf.Clamp(SteerAngle / carData.steeringAngle, -1f, 1f)
            : 0f;

    /// <summary>True while the car is sliding sideways at speed.</summary>
    public bool IsDrifting =>
        carData != null && SpeedKmh > 10f && Mathf.Abs(DriftAngle) > carData.driftAngleThreshold;

    /// <summary>True when traction control is actually in effect: the car's own toggle, and the player's setting for the player's car.</summary>
    public bool TractionControlActive
    {
        get
        {
            if (!tractionControl)
                return false;

            // AI cars ignore the player's setting.
            if (!playerControlled)
                return true;

            SettingsData settings = SettingsManager.Current;
            return settings == null || settings.tractionControl;
        }
    }

    // True once the player has crossed the finish line.
    private static bool RaceWon
    {
        get { return GameManager.Instance != null && GameManager.Instance.IsWon; }
    }

    /// <summary>True on the car that pushes its speed to the HUD (the player's car).</summary>
    public bool UpdatesHud => updatesHud;

    /// <summary>
    /// The wheel's ground hit for this frame. The first call each frame reads the four wheels; later calls reuse it,
    /// so the audio and tyre effects don't each query the physics engine again. Use it from Update or LateUpdate only
    /// (FixedUpdate runs before the physics step, so it would be a step behind).
    /// </summary>
    public bool TryGetWheelHit(WheelCollider wheel, out WheelHit hit)
    {
        int index = wheel == wheel1 ? 0 : wheel == wheel2 ? 1 : wheel == wheel3 ? 2 : wheel == wheel4 ? 3 : -1;

        if (index < 0)
            return wheel.GetGroundHit(out hit);

        if (frameHitsStamp != Time.frameCount)
        {
            frameGrounded[0] = wheel1.GetGroundHit(out frameHits[0]);
            frameGrounded[1] = wheel2.GetGroundHit(out frameHits[1]);
            frameGrounded[2] = wheel3.GetGroundHit(out frameHits[2]);
            frameGrounded[3] = wheel4.GetGroundHit(out frameHits[3]);
            frameHitsStamp = Time.frameCount;
        }

        hit = frameHits[index];
        return frameGrounded[index];
    }

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

        if (playerControlled && health == null)
        {
            health = GetComponentInParent<PlayerHealth>();
            if (health == null)
                health = GetComponentInChildren<PlayerHealth>();
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

        SetupTaillights();
    }

    private void OnDestroy()
    {
        // The taillight materials are instances created in SetupTaillights, so clean them up.
        if (taillightMaterials == null)
            return;

        for (int i = 0; i < taillightMaterials.Length; i++)
        {
            if (taillightMaterials[i] != null)
                Destroy(taillightMaterials[i]);
        }
    }

    private void Update()
    {
        if (playerControlled && InputManager.Instance != null)
        {
            bool dead = disableInputWhenDead && health != null && health.IsDead;
            bool finished = disableInputOnFinish && RaceWon;

            if (dead || finished)
            {
                // Wrecked or finished: no steering, throttle or handbrake. The car just rolls on.
                horizontalInput = 0f;
                verticalInput = 0f;
                handbrake = false;
            }
            else
            {
                horizontalInput = ShapeSteerInput(InputManager.Instance.Steer);
                verticalInput = InputManager.Instance.Throttle;
                handbrake = InputManager.Instance.Handbrake;
            }
        }

        UpdateHud();
    }

    // Eases small stick deflections so they steer gently, while full deflection still gives full lock.
    private float ShapeSteerInput(float raw)
    {
        if (!useSteerCurve || steerCurvePower <= 1f)
            return raw;

        return Mathf.Sign(raw) * Mathf.Pow(Mathf.Abs(raw), steerCurvePower);
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

        // Traction control: ease off an axle's torque while its tyres are slipping.
        if (!handbrake && TractionControlActive)
        {
            frontMotor *= TractionScale(wheel1, wheel2);
            rearMotor *= TractionScale(wheel3, wheel4);
        }

        // Normal braking uses all wheels. The handbrake only locks the rear wheels,
        // which is what makes the tail swing out.
        float frontBrake = inputOpposesMotion ? carData.brakeForce : 0f;
        float rearBrake = Mathf.Max(
            inputOpposesMotion ? carData.brakeForce : 0f,
            handbrake ? carData.handbrakeForce : 0f);

        // After the finish line: slow down by itself, gently, on all four wheels.
        if (playerControlled && autoBrakeOnFinish && RaceWon)
        {
            float finishBrake = carData.brakeForce * finishBrakeFraction;
            frontBrake = Mathf.Max(frontBrake, finishBrake);
            rearBrake = Mathf.Max(rearBrake, finishBrake);
        }

        // Brake lights follow whatever brake torque is actually being applied.
        IsBraking = frontBrake > 0.01f || rearBrake > 0.01f;
        UpdateTaillights(IsBraking);

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
        // UIManager takes km/h and converts it to the player's chosen unit.
        if (speed != lastDisplayedSpeed)
        {
            lastDisplayedSpeed = speed;
            UIManager.Instance.UpdateSpeed(speed);
        }
    }

    // ---------------------------------------------------------------- Taillights

    // Creates one material instance per renderer (once, at startup) so each car's lights
    // change independently and the shared material asset is never modified.
    private void SetupTaillights()
    {
        if (taillightRenderers != null && taillightRenderers.Length > 0)
        {
            taillightMaterials = new Material[taillightRenderers.Length];
            taillightIdleColors = new Color[taillightRenderers.Length];

            for (int i = 0; i < taillightRenderers.Length; i++)
            {
                Renderer r = taillightRenderers[i];
                if (r == null)
                    continue;

                Material[] mats = r.materials; // instances
                int index = Mathf.Clamp(taillightMaterialIndex, 0, mats.Length - 1);
                Material mat = mats[index];

                // Emission only shows if the keyword is on.
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                r.materials = mats;
                taillightMaterials[i] = r.materials[index];

                // Read the material's current emission and keep it as the idle look.
                Color idle = taillightMaterials[i].GetColor(EmissionColorId);
                if (idle.maxColorComponent <= 0.001f)
                    idle = taillightFallbackColor;

                taillightIdleColors[i] = idle;
            }
        }

        taillightsInitialised = false;
        UpdateTaillights(false);
    }

    private void UpdateTaillights(bool braking)
    {
        // Only do work when the state actually changes.
        if (taillightsInitialised && braking == taillightsBraking)
            return;

        taillightsInitialised = true;
        taillightsBraking = braking;

        if (taillightMaterials != null)
        {
            float scale = braking ? taillightBrakeMultiplier : 1f;

            for (int i = 0; i < taillightMaterials.Length; i++)
            {
                if (taillightMaterials[i] != null)
                    taillightMaterials[i].SetColor(EmissionColorId, taillightIdleColors[i] * scale);
            }
        }

        if (brakeLights != null)
        {
            for (int i = 0; i < brakeLights.Length; i++)
            {
                if (brakeLights[i] != null)
                    brakeLights[i].enabled = braking;
            }
        }
    }

    // ---------------------------------------------------------------- Settings

    // The steering settings are the player's choice, so AI cars ignore them.
    private float SteeringResponse
    {
        get
        {
            SettingsData settings = SettingsManager.Current;
            return playerControlled && settings != null ? settings.steeringResponse : 1f;
        }
    }

    private float SteeringAssist
    {
        get
        {
            SettingsData settings = SettingsManager.Current;
            return playerControlled && settings != null ? settings.steeringAssist : 1f;
        }
    }

    // ---------------------------------------------------------------- Traction

    // 1 = full torque, down to tractionMinTorque when the worse of the two wheels is slipping hard.
    private float TractionScale(WheelCollider a, WheelCollider b)
    {
        float slip = Mathf.Max(WheelSlip(a), WheelSlip(b));
        float t = Mathf.InverseLerp(tractionStartSlip, Mathf.Max(tractionStartSlip + 0.01f, tractionFullSlip), slip);
        return Mathf.Lerp(1f, tractionMinTorque, t);
    }

    private static float WheelSlip(WheelCollider wheel)
    {
        if (!wheel.GetGroundHit(out WheelHit hit))
            return 0f;

        return Mathf.Max(Mathf.Abs(hit.sidewaysSlip), Mathf.Abs(hit.forwardSlip));
    }

    // ---------------------------------------------------------------- Drift

    private void UpdateRearGrip(float dt)
    {
        // Grip drops fast when the handbrake is pulled and returns slowly,
        // so the slide carries on for a moment after release.
        float target = handbrake ? 1f : 0f;
        float rate = handbrake ? carData.gripLossSpeed : carData.gripRecoverySpeed;
        rearGripLoss = Mathf.MoveTowards(rearGripLoss, target, rate * dt);

        // Normal rear grip, optionally boosted so the rear is harder to break loose than the front.
        float normalStiffness = baseRearSideways.stiffness * rearGripMultiplier;

        // WheelFrictionCurve is a struct, so copy it, change it, and assign it back.
        float stiffness = Mathf.Lerp(normalStiffness, carData.driftSidewaysStiffness, rearGripLoss);

        // Assigning friction makes the physics engine update the wheels, so only do it when the value moved.
        if (!float.IsNaN(appliedRearStiffness) && Mathf.Approximately(stiffness, appliedRearStiffness))
            return;

        appliedRearStiffness = stiffness;

        WheelFrictionCurve friction = baseRearSideways;
        friction.stiffness = stiffness;
        wheel3.sidewaysFriction = friction;
        wheel4.sidewaysFriction = friction;
    }

    private float CalculateSteer(float dt)
    {
        // Smooth the input so keyboard taps don't snap the wheels straight to full lock.
        float smoothing = carData.steerSmoothing * SteeringResponse;
        smoothedSteer = Mathf.Lerp(smoothedSteer, horizontalInput, 1f - Mathf.Exp(-smoothing * dt));

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
            steer += DriftAngle * carData.counterSteerAssist * SteeringAssist;
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
        SteerAngle = steer;
        wheel1.steerAngle = steer;
        wheel2.steerAngle = steer;
    }
}