using UnityEngine;

public class CarController : MonoBehaviour
{
    [SerializeField] private CarData carData;
    [SerializeField] private Rigidbody rigidBody;
    [SerializeField] private WheelCollider wheel1, wheel2, wheel3, wheel4;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -0.5f, 0f);

    [Tooltip("Tick on the player's car only. Enemy cars should leave this off.")]
    [SerializeField] private bool updatesHud = true;

    private const float MsToKmh = 3.6f;

    private float horizontalInput, verticalInput;
    private bool handbrake;
    private int lastDisplayedSpeed = -1;

    /// <summary>Signed speed along the car's forward axis in km/h (negative when reversing).</summary>
    public float ForwardSpeedKmh { get; private set; }

    /// <summary>Absolute speed in km/h, regardless of direction.</summary>
    public float SpeedKmh => Mathf.Abs(ForwardSpeedKmh);

    /// <summary>Top speed from CarData in km/h.</summary>
    public float TopSpeedKmh => carData != null ? carData.topSpeed : 0f;

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
    }

    private void Update()
    {
        horizontalInput = Input.GetAxis("Horizontal");
        verticalInput = Input.GetAxis("Vertical");
        handbrake = Input.GetKey(KeyCode.Space);

        UpdateHud();
    }

    private void FixedUpdate()
    {
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

        // Brake when pressing against the direction of travel, or on handbrake.
        float brake = (inputOpposesMotion || handbrake) ? carData.brakeForce : 0f;

        ApplyDrive(motor, brake);
        ApplySteering(carData.steeringAngle * horizontalInput);
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

    private void ApplyDrive(float motor, float brake)
    {
        wheel1.motorTorque = motor;
        wheel2.motorTorque = motor;
        wheel3.motorTorque = motor;
        wheel4.motorTorque = motor;

        wheel1.brakeTorque = brake;
        wheel2.brakeTorque = brake;
        wheel3.brakeTorque = brake;
        wheel4.brakeTorque = brake;
    }

    private void ApplySteering(float steer)
    {
        wheel1.steerAngle = steer;
        wheel2.steerAngle = steer;
    }
}