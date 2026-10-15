using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _cameraPosition; // child of the car, defines the offset
    [SerializeField] private Transform _target;         // the car; auto-found from the Rigidbody if empty

    [Header("Follow")]
    [SerializeField] private float positionSmoothTime = 0.12f;
    [SerializeField] private float headingSharpness = 4f;   // lower = lazier turning
    [SerializeField] private float lookAtHeight = 1f;       // how far above the car's origin to aim

    [Header("Shake")]
    [SerializeField] private float maxShakeAngle = 3f;
    [Tooltip("Metres of camera movement at full shake.")]
    [SerializeField] private float maxShakeOffset = 0.35f;
    [SerializeField] private float shakeFrequency = 20f;
    [Tooltip("How fast the shake dies down, per second.")]
    [SerializeField] private float traumaDecay = 1.2f;

    private Vector3 _localOffset;
    private Vector3 _smoothedForward;
    private Vector3 _smoothedPosition; // the follow position, without shake
    private Vector3 _velocity;
    private float _trauma;

    private void Start()
    {
        if (_target == null)
        {
            Rigidbody body = GetComponentInParent<Rigidbody>();
            if (body != null)
            {
                _target = body.transform;
            }
        }

        if (_target == null || _cameraPosition == null)
        {
            Debug.LogError($"{name}: assign a target and camera position.", this);
            enabled = false;
            return;
        }

        // Record the offset while still parented, then detach.
        _localOffset = _target.InverseTransformPoint(_cameraPosition.position);
        _smoothedForward = FlatForward(_target);

        transform.parent = null;

        // Snap into place on the first frame.
        _smoothedPosition = _target.position + Quaternion.LookRotation(_smoothedForward, Vector3.up) * _localOffset;
        transform.position = _smoothedPosition;
        transform.rotation = LookRotationAtTarget(_smoothedPosition);
    }

    private void LateUpdate()
    {
        // Paused: hold the view exactly where it is.
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
        {
            return;
        }

        // Heading follows the car's yaw only, smoothed so spins don't whip the camera.
        Vector3 flatForward = FlatForward(_target);
        float t = 1f - Mathf.Exp(-headingSharpness * Time.deltaTime);
        _smoothedForward = Vector3.Slerp(_smoothedForward, flatForward, t);
        _smoothedForward.y = 0f;
        _smoothedForward.Normalize();

        Quaternion heading = Quaternion.LookRotation(_smoothedForward, Vector3.up);
        Vector3 desiredPosition = _target.position + heading * _localOffset;

        _smoothedPosition = Vector3.SmoothDamp(_smoothedPosition, desiredPosition, ref _velocity, positionSmoothTime);

        Quaternion baseRotation = LookRotationAtTarget(_smoothedPosition);

        // Shake is added on top and never fed back into the follow smoothing.
        // Unscaled time, so a shake that is still running when the game freezes dies out
        // instead of leaving the camera stuck at an angle.
        _trauma = Mathf.MoveTowards(_trauma, 0f, traumaDecay * Time.unscaledDeltaTime);
        float shake = _trauma * _trauma;
        float shakeTime = Time.unscaledTime * shakeFrequency;

        Vector3 shakeRot = NoiseVector(shakeTime, 0f) * (shake * maxShakeAngle);
        Vector3 shakePos = NoiseVector(shakeTime, 10f) * (shake * maxShakeOffset);

        transform.position = _smoothedPosition + shakePos;
        transform.rotation = baseRotation * Quaternion.Euler(shakeRot);
    }

    /// <summary>Adds screen shake, for example when the player dies or is hit. Amount is 0 to 1.</summary>
    public void AddShake(float amount)
    {
        _trauma = Mathf.Clamp01(_trauma + amount);
    }

    private Vector3 FlatForward(Transform source)
    {
        Vector3 flat = Vector3.ProjectOnPlane(source.forward, Vector3.up);

        // If the car is pointing nearly straight up or down, keep the last good heading.
        if (flat.sqrMagnitude < 0.01f)
        {
            return _smoothedForward.sqrMagnitude > 0f ? _smoothedForward : Vector3.forward;
        }

        return flat.normalized;
    }

    private Quaternion LookRotationAtTarget(Vector3 fromPosition)
    {
        Vector3 lookPoint = _target.position + Vector3.up * lookAtHeight;
        Vector3 direction = lookPoint - fromPosition;

        return direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : transform.rotation;
    }

    private static Vector3 NoiseVector(float time, float seed)
    {
        return new Vector3(Noise(time, seed), Noise(time, seed + 3.1f), Noise(time, seed + 6.2f));
    }

    private static float Noise(float time, float seed)
    {
        return Mathf.PerlinNoise(time, seed) * 2f - 1f;
    }
}