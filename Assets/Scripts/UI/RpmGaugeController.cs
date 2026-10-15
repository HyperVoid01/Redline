using UnityEngine;

public class RpmGaugeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EngineAudioDriver engineDriver;
    [SerializeField] private Transform needle;

    [Header("Needle Angles (degrees)")]
    [SerializeField] private float zeroRpmAngle = 0f;
    [SerializeField] private float maxRpmAngle = -270f;

    [Header("RPM")]
    [Tooltip("RPM at the far end of the gauge face (e.g. 8000 for a gauge marked 0-8).")]
    [SerializeField] private float maxRpm = 8000f;

    [Header("Needle Behaviour")]
    [SerializeField] private Vector3 rotationAxis = Vector3.forward; // local axis the needle spins around
    [SerializeField] private float smoothing = 12f;        // higher = snappier

    [Header("Redline Shake (optional)")]
    [SerializeField] private bool shakeAtRedline = true;
    [SerializeField] private float shakeStartFraction = 0.95f; // fraction of redline where shaking begins
    [SerializeField] private float shakeAmount = 1.5f;         // degrees
    [SerializeField] private float shakeSpeed = 40f;

    private float currentRpm;
    private float displayedAngle;
    private Quaternion needleStartRotation;

    private void Awake()
    {
        if (needle == null)
        {
            Debug.LogError("RpmGaugeController: Needle is not assigned.", this);
            enabled = false;
            return;
        }

        needleStartRotation = needle.localRotation;
        displayedAngle = zeroRpmAngle;
        ApplyNeedleRotation(0f);
    }

    private void Update()
    {
        if (engineDriver != null)
        {
            currentRpm = engineDriver.Rpm;
        }

        float t = Mathf.Clamp01(currentRpm / maxRpm);
        float targetAngle = Mathf.Lerp(zeroRpmAngle, maxRpmAngle, t);

        // Frame-rate independent smoothing
        displayedAngle = Mathf.Lerp(displayedAngle, targetAngle, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

        float shake = 0f;
        if (shakeAtRedline && engineDriver != null)
        {
            float redline = engineDriver.RedlineRpm;
            float start = redline * shakeStartFraction;
            if (currentRpm > start)
            {
                float intensity = Mathf.InverseLerp(start, redline, currentRpm);
                shake = (Mathf.PerlinNoise(Time.time * shakeSpeed, 0f) - 0.5f) * 2f * shakeAmount * intensity;
            }
        }

        ApplyNeedleRotation(shake);
    }

    private void ApplyNeedleRotation(float extraAngle)
    {
        needle.localRotation = needleStartRotation * Quaternion.AngleAxis(displayedAngle + extraAngle, rotationAxis);
    }

    /// <summary>
    /// Call this instead if you don't want to read from the EngineAudioDriver.
    /// </summary>
    public void SetRpm(float rpm)
    {
        currentRpm = rpm;
    }
}