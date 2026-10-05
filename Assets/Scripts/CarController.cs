using UnityEngine;

public class CarController : MonoBehaviour
{
    [SerializeField] private Rigidbody rigidBody;
    [SerializeField] private WheelCollider wheel1, wheel2, wheel3, wheel4;
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -0.5f, 0f);
    public float driveSpeed, steerSpeed;

    private float horizontalInput, verticalInput;

    private void Awake()
    {
        if (rigidBody == null)
        {
            rigidBody = GetComponent<Rigidbody>();
        }

        // Local-space offset in meters. Keep it low and near the middle of the car.
        rigidBody.centerOfMass = centerOfMass;
    }

    private void Update()
    {
        horizontalInput = Input.GetAxis("Horizontal");
        verticalInput = Input.GetAxis("Vertical");
    }

    private void FixedUpdate()
    {
        float motor = verticalInput * driveSpeed;

        wheel1.motorTorque = motor;
        wheel2.motorTorque = motor;
        wheel3.motorTorque = motor;
        wheel4.motorTorque = motor;

        float steer = steerSpeed * horizontalInput;
        wheel1.steerAngle = steer;
        wheel2.steerAngle = steer;
    }
}