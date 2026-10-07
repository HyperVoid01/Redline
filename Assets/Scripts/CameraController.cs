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

    private Vector3 _localOffset;
    private Vector3 _smoothedForward;
    private Vector3 _velocity;

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
        transform.position = _target.position + Quaternion.LookRotation(_smoothedForward, Vector3.up) * _localOffset;
        transform.rotation = LookRotationAtTarget();
    }

    private void LateUpdate()
    {
        // Heading follows the car's yaw only, smoothed so spins don't whip the camera.
        Vector3 flatForward = FlatForward(_target);
        float t = 1f - Mathf.Exp(-headingSharpness * Time.deltaTime);
        _smoothedForward = Vector3.Slerp(_smoothedForward, flatForward, t);
        _smoothedForward.y = 0f;
        _smoothedForward.Normalize();

        Quaternion heading = Quaternion.LookRotation(_smoothedForward, Vector3.up);
        Vector3 desiredPosition = _target.position + heading * _localOffset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, positionSmoothTime);
        transform.rotation = LookRotationAtTarget();
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

    private Quaternion LookRotationAtTarget()
    {
        Vector3 lookPoint = _target.position + Vector3.up * lookAtHeight;
        Vector3 direction = lookPoint - transform.position;

        return direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : transform.rotation;
    }
}