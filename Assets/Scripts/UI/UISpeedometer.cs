using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dial speedometer for a Screen Space - Overlay canvas.
/// Put it on the dial's root UI object. The needle is a child RectTransform whose pivot sits at the dial's centre.
/// The speed comes from the player's Rigidbody. If none is assigned, it uses the one UIManager was given by CarSpawner,
/// so it works with a car that spawns at runtime.
/// </summary>
public class UISpeedometer : MonoBehaviour
{
    private const float MsToKmh = 3.6f;
    private const float KmhToMph = 0.621371f;

    [Header("References")]
    [Tooltip("Leave empty to use the player car from UIManager (set by CarSpawner).")]
    [SerializeField] private Rigidbody carRigidbody;
    [Tooltip("The needle image. Its pivot must be at the point it spins around (the dial centre).")]
    [SerializeField] private RectTransform needle;
    [Tooltip("Optional. A number readout, for example '120 km/h'.")]
    [SerializeField] private TMP_Text speedText;
    [Tooltip("Optional. An Image set to Filled that grows with speed (a bar around the dial).")]
    [SerializeField] private Image fillImage;

    [Header("Needle Angles (degrees)")]
    [Tooltip("Needle angle at 0 speed. On a UI Z rotation, positive is counter-clockwise.")]
    [SerializeField] private float zeroSpeedAngle = 135f;
    [Tooltip("Needle angle at max speed. 135 to -135 sweeps 270 degrees clockwise.")]
    [SerializeField] private float maxSpeedAngle = -135f;

    [Header("Speed")]
    [Tooltip("Speed at the end of the dial, always in km/h. It is converted when the unit is mph.")]
    [SerializeField] private float maxSpeedKmh = 220f;
    [Tooltip("Follow the km/h / mph choice from the settings menu.")]
    [SerializeField] private bool useSettingsUnit = true;
    [Tooltip("Used only when 'Use Settings Unit' is off. false = mph.")]
    [SerializeField] private bool useKmh = true;

    [Header("Needle Behaviour")]
    [Tooltip("Higher = snappier.")]
    [SerializeField] private float smoothing = 8f;

    private float currentSpeedKmh;
    private float displayedAngle;
    private bool externalSpeed;
    private int lastShownSpeed = -1;
    private bool lastShownMph;

    private void Awake()
    {
        if (needle == null)
        {
            Debug.LogError("UISpeedometer: Needle is not assigned.", this);
            enabled = false;
            return;
        }

        displayedAngle = zeroSpeedAngle;
        ApplyNeedleRotation();
        ApplyFill(0f);
    }

    private void OnEnable()
    {
        SettingsManager.Changed += OnSettingsChanged;
    }

    private void OnDisable()
    {
        SettingsManager.Changed -= OnSettingsChanged;
    }

    // Force the number to redraw in the new unit.
    private void OnSettingsChanged()
    {
        lastShownSpeed = -1;
    }

    private void Update()
    {
        if (!externalSpeed)
        {
            Rigidbody body = carRigidbody != null ? carRigidbody : GetPlayerBody();

            if (body != null)
                currentSpeedKmh = body.linearVelocity.magnitude * MsToKmh;   // use .velocity before Unity 6
        }

        float t = Mathf.Clamp01(currentSpeedKmh / Mathf.Max(1f, maxSpeedKmh));
        float targetAngle = Mathf.Lerp(zeroSpeedAngle, maxSpeedAngle, t);

        // Frame-rate independent smoothing.
        displayedAngle = Mathf.Lerp(displayedAngle, targetAngle, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));

        ApplyNeedleRotation();
        ApplyFill(Mathf.InverseLerp(zeroSpeedAngle, maxSpeedAngle, displayedAngle));
        UpdateText();
    }

    private static Rigidbody GetPlayerBody()
    {
        return UIManager.Instance != null ? UIManager.Instance.PlayerBody : null;
    }

    private bool UseMph()
    {
        if (useSettingsUnit)
        {
            SettingsData settings = SettingsManager.Current;
            return settings != null && settings.speedUnit == SpeedUnit.Mph;
        }

        return !useKmh;
    }

    private void ApplyNeedleRotation()
    {
        // UI rotates around Z. The needle's pivot is the centre of the dial.
        needle.localRotation = Quaternion.Euler(0f, 0f, displayedAngle);
    }

    private void ApplyFill(float amount)
    {
        if (fillImage != null)
            fillImage.fillAmount = Mathf.Clamp01(amount);
    }

    // Only touches the text when the number or the unit changes, so TMP isn't rebuilt every frame.
    private void UpdateText()
    {
        if (speedText == null)
            return;

        bool mph = UseMph();
        int shown = Mathf.RoundToInt(mph ? currentSpeedKmh * KmhToMph : currentSpeedKmh);

        if (shown == lastShownSpeed && mph == lastShownMph)
            return;

        lastShownSpeed = shown;
        lastShownMph = mph;
        speedText.text = shown + (mph ? " mph" : " km/h");
    }

    /// <summary>
    /// Call this if you don't want to read from a Rigidbody (for example your car controller computes its own speed).
    /// Takes the speed in km/h. After the first call the Rigidbody is no longer read.
    /// </summary>
    public void SetSpeed(float speedKmh)
    {
        externalSpeed = true;
        currentSpeedKmh = speedKmh;
    }

    /// <summary>Gives the dial a car directly. Not needed if UIManager already has the player car.</summary>
    public void SetCar(Rigidbody body)
    {
        carRigidbody = body;
        externalSpeed = false;
    }
}