using UnityEngine;

/// <summary>
/// Rotates a steering wheel model to match the car's actual front wheel steering,
/// including the automatic counter-steer during drifts.
/// </summary>
public class SteeringWheelController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Found automatically on the car if left empty.")]
    [SerializeField] private CarController car;
    [Tooltip("The object to rotate. Leave empty to use this object.")]
    [SerializeField] private Transform steeringWheel;

    [Header("Rotation")]
    [Tooltip("Local axis the wheel spins around (along the steering column). Try Z first, then X or Y if it spins the wrong way.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.forward;
    [Tooltip("Degrees the wheel turns at full steering lock. 450 = 900 degrees lock to lock, like a road car.")]
    [SerializeField] private float maxWheelRotation = 450f;
    [Tooltip("Tick if the wheel turns the opposite way to the car.")]
    [SerializeField] private bool invertDirection = false;
    [Tooltip("Higher = snappier. 0 = no smoothing.")]
    [SerializeField] private float smoothing = 15f;

    private Quaternion restRotation;
    private Vector3 axis;
    private float currentAngle;

    private void Awake()
    {
        if (steeringWheel == null)
        {
            steeringWheel = transform;
        }

        if (car == null)
        {
            car = GetComponentInParent<CarController>();
        }

        if (car == null)
        {
            Debug.LogError($"{name}: no CarController found. Assign one or place this under the car.", this);
            enabled = false;
            return;
        }

        axis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.forward;

        // The wheel's pose in the prefab is its straight-ahead position.
        restRotation = steeringWheel.localRotation;
    }

    private void OnEnable()
    {
        // Start from the car's current steering so the wheel doesn't swing in from zero.
        if (car != null)
        {
            currentAngle = TargetAngle();
        }
    }

    // Visuals run in LateUpdate, after the car has been updated.
    private void LateUpdate()
    {
        float target = TargetAngle();

        currentAngle = smoothing > 0f
            ? Mathf.Lerp(currentAngle, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime))
            : target;

        steeringWheel.localRotation = restRotation * Quaternion.AngleAxis(currentAngle, axis);
    }

    private float TargetAngle()
    {
        float angle = car.SteerNormalized * maxWheelRotation;
        return invertDirection ? -angle : angle;
    }
}