using UnityEngine;

public class SpeedometerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody carRigidbody;
    [SerializeField] private Transform needle;

    [Header("Needle Angles (degrees)")]
    [SerializeField] private float zeroSpeedAngle = 0f;
    [SerializeField] private float maxSpeedAngle = -270f;

    [Header("Speed")]
    [SerializeField] private float maxSpeed = 220f;       // in km/h
    [SerializeField] private bool useKmh = true;          // false = mph

    [Header("Needle Behaviour")]
    [SerializeField] private Vector3 rotationAxis = Vector3.forward; // local axis the needle spins around
    [SerializeField] private float smoothing = 8f;        // higher = snappier

    private float currentSpeed;
    private float displayedAngle;
    private Quaternion needleStartRotation;

    private void Awake()
    {
        if (needle == null)
        {
            Debug.LogError("SpeedometerController: Needle is not assigned.", this);
            enabled = false;
            return;
        }

        needleStartRotation = needle.localRotation;
        displayedAngle = zeroSpeedAngle;
        ApplyNeedleRotation();
    }

    private void Update()
    {
        if (carRigidbody != null)
        {
            // Use carRigidbody.velocity instead on Unity versions before 6
            float metersPerSecond = carRigidbody.linearVelocity.magnitude;
            currentSpeed = metersPerSecond * (useKmh ? 3.6f : 2.23694f);
        }

        float t = Mathf.Clamp01(currentSpeed / maxSpeed);
        float targetAngle = Mathf.Lerp(zeroSpeedAngle, maxSpeedAngle, t);

        // Frame-rate independent smoothing
        displayedAngle = Mathf.Lerp(displayedAngle, targetAngle, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

        ApplyNeedleRotation();
    }

    private void ApplyNeedleRotation()
    {
        needle.localRotation = needleStartRotation * Quaternion.AngleAxis(displayedAngle, rotationAxis);
    }

    /// <summary>
    /// Call this instead if you don't want to read from a Rigidbody
    /// (e.g. your car controller computes its own speed).
    /// </summary>
    public void SetSpeed(float speed)
    {
        currentSpeed = speed;
    }
}