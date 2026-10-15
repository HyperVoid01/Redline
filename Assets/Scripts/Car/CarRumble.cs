using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarRumble : MonoBehaviour
{
    [Header("Barrier Collisions")]
    [SerializeField] private LayerMask barrierLayers;
    [SerializeField] private float minImpactSpeed = 1.5f;     // m/s, anything slower is ignored
    [SerializeField] private float maxImpactSpeed = 15f;      // m/s, full-strength hit
    [Tooltip("Lowest strength for any real barrier hit, so even light hits are felt.")]
    [SerializeField, Range(0f, 1f)] private float minImpactStrength = 0.35f;
    [SerializeField] private float impactFadeTime = 0.45f;    // seconds for the thud to fade out
    [SerializeField, Range(0f, 1f)] private float scrapeStrength = 0.35f;  // rumble while grinding along a barrier
    [SerializeField] private float minScrapeSpeed = 3f;       // m/s

    [Header("Speed Shake")]
    [SerializeField] private float shakeStartKmh = 60f;
    [SerializeField] private float shakeMaxKmh = 200f;
    [Tooltip("Buzz strength just after the start speed. Controller motors are often hard to feel below about 0.1 to 0.15.")]
    [SerializeField, Range(0f, 1f)] private float minSpeedShake = 0.15f;
    [Tooltip("Buzz strength at the max speed.")]
    [SerializeField, Range(0f, 1f)] private float maxSpeedShake = 0.45f;

    [Header("Debug")]
    [Tooltip("Shows an on-screen readout and logs collisions. Hold D-pad Up to force full rumble as a hardware test. Turn off when done.")]
    [SerializeField] private bool showDebug = true;

    private Rigidbody rb;
    private float impact;            // 0-1, decays over time
    private float lastScrapeTime = -1f;

    // Debug readout
    private string status = "";
    private float lastLow;
    private float lastHigh;
    private float lastSpeedKmh;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (barrierLayers.value == 0)
            Debug.LogWarning("CarRumble: Barrier Layers is empty, so barrier hits will not rumble.", this);
    }

    private void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            status = "No Gamepad detected (Gamepad.current is null)";
            return;
        }

        // Hardware test: hold D-pad Up for full rumble, ignoring everything else
        if (showDebug && pad.dpad.up.isPressed)
        {
            pad.SetMotorSpeeds(1f, 1f);
            lastLow = 1f;
            lastHigh = 1f;
            status = "Debug test: D-pad Up held";
            return;
        }

        // Rumble strength from the settings menu. 1 if there is no SettingsManager in the scene.
        SettingsData settings = SettingsManager.Current;
        float strength = settings != null ? settings.rumbleStrength : 1f;

        if (strength <= 0f)
        {
            status = "Silent: Rumble Strength is 0 in settings";
            Silence(pad);
            return;
        }

        string silentReason = GetSilentReason();
        if (silentReason != null)
        {
            status = "Silent: " + silentReason + "   (strength " + strength.ToString("F2") + ")";
            Silence(pad);
            return;
        }

        status = "Running   (strength " + strength.ToString("F2") + ")";

        // Impact fades out using unscaled time so it behaves the same in slow-mo
        impact = Mathf.MoveTowards(impact, 0f, Time.unscaledDeltaTime / impactFadeTime);

        // Speed shake: starts at a level you can feel, then climbs toward the redline
        lastSpeedKmh = rb.linearVelocity.magnitude * 3.6f;
        float speedT = Mathf.InverseLerp(shakeStartKmh, shakeMaxKmh, lastSpeedKmh);
        float shake = 0f;
        if (speedT > 0f)
        {
            float curved = Mathf.Pow(speedT, 0.7f);
            shake = Mathf.Lerp(minSpeedShake, maxSpeedShake, curved);
            shake *= Mathf.Clamp01(speedT * 8f);   // short ease-in so it doesn't pop on at the start speed
        }

        float noise = 0.8f + 0.2f * Mathf.PerlinNoise(Time.unscaledTime * 15f, 0f);
        float shakeHigh = shake * noise;
        float shakeLow = shake * 0.5f;

        // Scrape: grinding rumble while in contact with a barrier
        bool scraping = Time.unscaledTime - lastScrapeTime < 0.1f;
        float scrapeHigh = scraping ? scrapeStrength : 0f;
        float scrapeLow = scraping ? scrapeStrength * 0.6f : 0f;

        // Low motor = heavy thud, high motor = fine buzz
        float low = Mathf.Clamp01(impact + shakeLow + scrapeLow) * strength;
        float high = Mathf.Clamp01(impact * 0.8f + shakeHigh + scrapeHigh) * strength;

        pad.SetMotorSpeeds(low, high);
        lastLow = low;
        lastHigh = high;
    }

    private static string GetSilentReason()
    {
        if (Time.timeScale == 0f)
            return "Time.timeScale is 0";

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            if (gm.IsPaused) return "game is paused";
            if (gm.IsFrozen) return "game is frozen after game over or win";
            if (gm.IsWon) return "run is won";
        }

        return null;
    }

    private void Silence(Gamepad pad)
    {
        pad.SetMotorSpeeds(0f, 0f);
        lastLow = 0f;
        lastHigh = 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        bool isBarrier = IsBarrier(collision);
        float speed = collision.relativeVelocity.magnitude;

        // Light taps are ignored. Anything above the minimum is at least minImpactStrength,
        // and the square root makes medium hits feel strong instead of weak.
        float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed);
        float hitStrength = t > 0f ? Mathf.Lerp(minImpactStrength, 1f, Mathf.Sqrt(t)) : 0f;

        if (showDebug)
        {
            Debug.Log($"CarRumble hit '{collision.gameObject.name}' on layer '{LayerMask.LayerToName(collision.gameObject.layer)}' " +
                      $"at {speed:F1} m/s. Barrier: {isBarrier}. Impact strength: {hitStrength:F2}");
        }

        if (!isBarrier || hitStrength <= 0f) return;

        impact = Mathf.Max(impact, hitStrength);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!IsBarrier(collision)) return;

        if (rb.linearVelocity.magnitude > minScrapeSpeed)
            lastScrapeTime = Time.unscaledTime;
    }

    private bool IsBarrier(Collision collision)
    {
        return (barrierLayers.value & (1 << collision.gameObject.layer)) != 0;
    }

    private void OnGUI()
    {
        if (!showDebug) return;

        Gamepad pad = Gamepad.current;
        string padName = pad != null ? pad.displayName : "NONE";

        GUI.Label(new Rect(10, 10, 600, 140),
            "Rumble debug\n" +
            $"Gamepad: {padName}\n" +
            $"Status: {status}\n" +
            $"Speed: {lastSpeedKmh:F0} km/h   Impact: {impact:F2}\n" +
            $"Motors: low {lastLow:F2}   high {lastHigh:F2}");
    }

    public void StopRumble()
    {
        impact = 0f;
        lastScrapeTime = -1f;
        if (Gamepad.current != null)
            Gamepad.current.SetMotorSpeeds(0f, 0f);
    }

    private void OnDisable() => StopRumble();
    private void OnApplicationQuit() => StopRumble();
    private void OnApplicationPause(bool paused)
    {
        if (paused) StopRumble();
    }
}