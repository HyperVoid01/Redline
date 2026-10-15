using UnityEngine;

[RequireComponent(typeof(Camera))]
public class InteriorCameraController : MonoBehaviour
{
    // A simple damped spring, used for head movement and head tilt.
    private struct Spring
    {
        public Vector3 value;
        public Vector3 velocity;

        public void Step(Vector3 target, float stiffness, float damping, float dt)
        {
            Vector3 force = (target - value) * stiffness - velocity * damping;
            velocity += force * dt;
            value += velocity * dt;
        }
    }

    [Header("References")]
    [Tooltip("Found automatically on the car if left empty.")]
    [SerializeField] private Rigidbody carBody;

    [Header("Mouse Look")]
    [Tooltip("Degrees of head turn per unit of mouse movement.")]
    [SerializeField] private float sensitivity = 2f;
    [Tooltip("Degrees per second of head turn at full stick deflection.")]
    [SerializeField] private float stickSensitivity = 120f;
    [SerializeField] private float maxYaw = 120f;
    [SerializeField] private float maxLookUp = 60f;
    [SerializeField] private float maxLookDown = 45f;

    [Header("Head Movement (car-local)")]
    [Tooltip("Metres of head movement per m/s² of acceleration. x = sideways, y = vertical, z = forward/back. Flip a sign if a direction feels backwards.")]
    [SerializeField] private Vector3 shiftPerAccel = new Vector3(0.008f, 0.004f, 0.01f);
    [SerializeField] private Vector3 maxShift = new Vector3(0.12f, 0.06f, 0.15f);

    [Header("Head Tilt")]
    [Tooltip("Degrees of nod per m/s² of braking or acceleration.")]
    [SerializeField] private float pitchPerAccel = 0.5f;
    [Tooltip("Degrees of lean per m/s² of cornering.")]
    [SerializeField] private float rollPerAccel = 0.6f;
    [SerializeField] private float maxTilt = 6f;

    [Header("Spring")]
    [Tooltip("Higher = snappier head. Lower = floatier.")]
    [SerializeField] private float springStiffness = 80f;
    [Tooltip("Higher = less wobble after a movement.")]
    [SerializeField] private float springDamping = 12f;
    [Tooltip("How quickly acceleration readings are smoothed. Higher = twitchier.")]
    [SerializeField] private float accelSmoothing = 8f;
    [Tooltip("Acceleration above this (m/s²) is ignored for sway, so crashes don't throw the head across the cabin.")]
    [SerializeField] private float maxAccelInput = 30f;

    [Header("Impact Shake")]
    [Tooltip("A sudden acceleration above this (m/s²) counts as a crash and shakes the view.")]
    [SerializeField] private float impactAccel = 25f;
    [SerializeField] private float maxShakeAngle = 4f;
    [SerializeField] private float maxShakeOffset = 0.05f;
    [SerializeField] private float shakeFrequency = 18f;
    [Tooltip("How fast the shake dies down, per second.")]
    [SerializeField] private float traumaDecay = 1.5f;

    [Header("Speed Effects")]
    [Tooltip("Speed (m/s) at which speed effects reach full strength.")]
    [SerializeField] private float maxEffectSpeed = 30f;
    [SerializeField] private float vibrationAngle = 0.15f;
    [SerializeField] private float vibrationFrequency = 22f;
    [Tooltip("Extra field of view at full speed. 0 turns it off.")]
    [SerializeField] private float speedFovBoost = 8f;

    private const float MaxStepTime = 1f / 30f;

    private Camera cam;
    private Transform frame; // the car-side parent. Sway is measured in its axes.

    private Vector3 restPosition;
    private Quaternion restRotation;
    private float baseFov;
    private float currentFov;

    private float yaw;
    private float pitch; // positive = looking down, like Unity's X rotation

    private Spring headSpring;
    private Spring tiltSpring;
    private Vector3 lastVelocity;
    private Vector3 smoothedAccel;
    private float speed;
    private float trauma;

    private bool initialised;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        frame = transform.parent;

        if (carBody == null)
        {
            carBody = GetComponentInParent<Rigidbody>();
        }

        if (frame == null || carBody == null)
        {
            Debug.LogError($"{name}: place this camera as a child of the car, which needs a Rigidbody.", this);
            enabled = false;
            return;
        }

        // The camera's pose in the car prefab is the resting head position.
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        baseFov = cam.fieldOfView;
        currentFov = baseFov;

        initialised = true;
    }

    private void OnEnable()
    {
        if (!initialised)
        {
            return;
        }

        // Start clean each time the camera is switched on, so stale values don't cause a jolt.
        headSpring = new Spring();
        tiltSpring = new Spring();
        smoothedAccel = Vector3.zero;
        trauma = 0f;
        lastVelocity = GetVelocity();
    }

    private void FixedUpdate()
    {
        // Measure the car's acceleration at the physics rate, where the velocity actually changes.
        Vector3 velocity = GetVelocity();
        float dt = Time.fixedDeltaTime;

        Vector3 worldAccel = (velocity - lastVelocity) / dt;
        lastVelocity = velocity;
        speed = velocity.magnitude;

        // A big sudden spike is a crash, not driving.
        float spike = worldAccel.magnitude;
        if (spike > impactAccel)
        {
            float strength = 0.25f + 0.75f * Mathf.InverseLerp(impactAccel, impactAccel * 3f, spike);
            trauma = Mathf.Max(trauma, strength);
        }

        // Into the car's own axes: x = sideways, y = up, z = forward.
        Vector3 localAccel = frame.InverseTransformDirection(worldAccel);
        localAccel = Vector3.ClampMagnitude(localAccel, maxAccelInput);

        float blend = 1f - Mathf.Exp(-accelSmoothing * dt);
        smoothedAccel = Vector3.Lerp(smoothedAccel, localAccel, blend);
    }

    // Everything visual happens in LateUpdate, after the car has moved.
    private void LateUpdate()
    {
        GameManager gm = GameManager.Instance;

        // Paused: hold the view exactly where it is.
        if (gm != null && gm.IsPaused)
        {
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, MaxStepTime);
        bool gameOver = gm != null && gm.IsGameOver;

        if (!gameOver)
        {
            UpdateLook(dt);
        }

        UpdateHead(dt);
        UpdateFov(dt);
    }

    // ---------------------------------------------------------------- Look

    private void UpdateLook(float dt)
    {
        InputManager input = InputManager.Instance;
        if (input == null)
        {
            return;
        }

        if (input.Recenter)
        {
            float blend = 1f - Mathf.Exp(-8f * dt);
            yaw = Mathf.Lerp(yaw, 0f, blend);
            pitch = Mathf.Lerp(pitch, 0f, blend);
            return;
        }

        // Mouse: movement this frame, so no dt.
        Vector2 mouse = input.LookMouse;
        yaw += mouse.x * sensitivity;
        pitch -= mouse.y * sensitivity;

        // Stick: how far it's held, so scale by time.
        Vector2 stick = input.LookStick;
        yaw += stick.x * stickSensitivity * dt;
        pitch -= stick.y * stickSensitivity * dt;

        yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        pitch = Mathf.Clamp(pitch, -maxLookUp, maxLookDown);
    }

    // ---------------------------------------------------------------- Driving feel

    private void UpdateHead(float dt)
    {
        Vector3 a = smoothedAccel;

        // The head lags behind the car: braking throws it forward, cornering throws it outward.
        Vector3 shiftTarget = new Vector3(
            Mathf.Clamp(-a.x * shiftPerAccel.x, -maxShift.x, maxShift.x),
            Mathf.Clamp(-a.y * shiftPerAccel.y, -maxShift.y, maxShift.y),
            Mathf.Clamp(-a.z * shiftPerAccel.z, -maxShift.z, maxShift.z));

        // x = nod (braking nods forward), z = lean (cornering leans outward).
        Vector3 tiltTarget = new Vector3(
            Mathf.Clamp(-a.z * pitchPerAccel, -maxTilt, maxTilt),
            0f,
            Mathf.Clamp(a.x * rollPerAccel, -maxTilt, maxTilt));

        headSpring.Step(shiftTarget, springStiffness, springDamping, dt);
        tiltSpring.Step(tiltTarget, springStiffness, springDamping, dt);

        // Crash shake. Squaring the trauma makes small hits subtle and big ones violent.
        trauma = Mathf.MoveTowards(trauma, 0f, traumaDecay * dt);
        float shake = trauma * trauma;
        float shakeTime = Time.time * shakeFrequency;
        Vector3 shakeRot = NoiseVector(shakeTime, 0f) * (shake * maxShakeAngle);
        Vector3 shakePos = NoiseVector(shakeTime, 10f) * (shake * maxShakeOffset);

        // Faint constant road vibration that grows with speed.
        float speed01 = Mathf.Clamp01(speed / maxEffectSpeed);
        Vector3 vibration = NoiseVector(Time.time * vibrationFrequency, 20f) * (speed01 * speed01 * vibrationAngle);

        transform.localPosition = restPosition + headSpring.value + shakePos;

        // Sway is applied in the car's frame. Mouse look turns the head within it.
        Quaternion sway = Quaternion.Euler(tiltSpring.value.x, 0f, tiltSpring.value.z);
        Quaternion look = Quaternion.Euler(
            pitch + shakeRot.x + vibration.x,
            yaw + shakeRot.y + vibration.y,
            shakeRot.z + vibration.z);

        transform.localRotation = sway * restRotation * look;
    }

    private void UpdateFov(float dt)
    {
        float speed01 = Mathf.Clamp01(speed / maxEffectSpeed);
        float target = baseFov + speedFovBoost * speed01;

        currentFov = Mathf.Lerp(currentFov, target, 1f - Mathf.Exp(-3f * dt));
        cam.fieldOfView = currentFov;
    }

    /// <summary>Adds screen shake from outside, for example when the player is hit by gunfire. Amount is 0 to 1.</summary>
    public void AddShake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    // ---------------------------------------------------------------- Helpers

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return carBody.linearVelocity;
#else
        return carBody.velocity;
#endif
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