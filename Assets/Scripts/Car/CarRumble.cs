using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarRumble : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Turns all controller rumble from this car on or off. The Rumble Strength in the settings menu still scales everything.")]
    [SerializeField] private bool enableRumble = true;

    // ------------------------------------------------------------------ Barrier hits

    [Header("Barrier Impacts")]
    [Tooltip("Thud when the car hits a barrier.")]
    [SerializeField] private bool barrierImpacts = true;
    [SerializeField] private LayerMask barrierLayers;
    [SerializeField] private float minImpactSpeed = 1.5f;     // m/s, anything slower is ignored
    [SerializeField] private float maxImpactSpeed = 15f;      // m/s, full-strength hit
    [Tooltip("Lowest strength for any real barrier hit, so even light hits are felt.")]
    [SerializeField, Range(0f, 1f)] private float minImpactStrength = 0.35f;
    [SerializeField] private float impactFadeTime = 0.45f;    // seconds for the thud to fade out

    [Header("Barrier Scrape")]
    [Tooltip("Grinding rumble while sliding along a barrier.")]
    [SerializeField] private bool barrierScrape = true;
    [SerializeField, Range(0f, 1f)] private float scrapeStrength = 0.35f;
    [SerializeField] private float minScrapeSpeed = 3f;       // m/s

    // ------------------------------------------------------------------ Speed

    [Header("Speed Shake")]
    [Tooltip("Constant buzz that grows with speed.")]
    [SerializeField] private bool speedShake = true;
    [SerializeField] private float shakeStartKmh = 60f;
    [SerializeField] private float shakeMaxKmh = 200f;
    [Tooltip("Buzz strength just after the start speed. Controller motors are often hard to feel below about 0.1 to 0.15.")]
    [SerializeField, Range(0f, 1f)] private float minSpeedShake = 0.15f;
    [Tooltip("Buzz strength at the max speed.")]
    [SerializeField, Range(0f, 1f)] private float maxSpeedShake = 0.45f;

    // ------------------------------------------------------------------ Damage

    [Header("Damage")]
    [Tooltip("Pulse each time the car takes damage (for example from bullets). Called by PlayerHealth.")]
    [SerializeField] private bool damageRumble = true;
    [Tooltip("A hit of this much damage (or more) gives the full damage strength.")]
    [SerializeField] private float damageForFullStrength = 25f;
    [Tooltip("Lowest strength for any hit, so even small hits are felt.")]
    [SerializeField, Range(0f, 1f)] private float minDamageStrength = 0.3f;
    [Tooltip("Highest strength a single hit can give.")]
    [SerializeField, Range(0f, 1f)] private float maxDamageStrength = 0.8f;
    [Tooltip("Seconds for the pulse to fade out.")]
    [SerializeField] private float damageFadeTime = 0.3f;

    // ------------------------------------------------------------------ Explosion

    [Header("Explosion")]
    [Tooltip("Big rumble when the car explodes. Called by PlayerHealth.")]
    [SerializeField] private bool explosionRumble = true;
    [SerializeField, Range(0f, 1f)] private float explosionStrength = 1f;
    [Tooltip("Seconds for the explosion rumble to fade out.")]
    [SerializeField] private float explosionFadeTime = 1.2f;
    [Tooltip("Stop the speed shake and scrape once the car has exploded, so only the explosion is felt.")]
    [SerializeField] private bool silenceOtherRumbleAfterExplosion = true;

    // ------------------------------------------------------------------ Finish

    [Header("Finish")]
    [Tooltip("Strong pulse when the car crosses the finish line.")]
    [SerializeField] private bool finishRumble = true;
    [SerializeField, Range(0f, 1f)] private float finishStrength = 0.9f;
    [Tooltip("Seconds for the finish rumble to fade out.")]
    [SerializeField] private float finishFadeTime = 1f;
    [Tooltip("Stop the speed shake and scrape after the finish line, so only the finish pulse is felt and the roll-out is calm.")]
    [SerializeField] private bool silenceOtherRumbleAfterFinish = true;

    // ------------------------------------------------------------------ Debug

    [Header("Debug")]
    [Tooltip("Shows an on-screen readout and logs collisions. Hold D-pad Up to force full rumble as a hardware test. Turn off when done.")]
    [SerializeField] private bool showDebug = true;

    private Rigidbody rb;
    private float impact;            // 0-1, decays over time
    private float damagePulse;       // 0-1, decays over time
    private float explosion;         // 0-1, decays over time
    private float finishPulse;       // 0-1, decays over time
    private bool exploded;
    private bool finished;
    private float lastScrapeTime = -1f;

    // Debug readout
    private string status = "";
    private float lastLow;
    private float lastHigh;
    private float lastSpeedKmh;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (barrierImpacts && barrierLayers.value == 0)
            Debug.LogWarning("CarRumble: Barrier Layers is empty, so barrier hits will not rumble.", this);
    }

    private void OnEnable()
    {
        GameManager.RaceFinished += OnRaceFinished;
    }

    // ---------------------------------------------------------------- Called from PlayerHealth

    /// <summary>A hit that took this much health off. Gives a short pulse.</summary>
    public void OnDamaged(float amount)
    {
        if (!enableRumble || !damageRumble || exploded || amount <= 0f)
            return;

        float t = damageForFullStrength > 0f ? Mathf.Clamp01(amount / damageForFullStrength) : 1f;
        float strength = Mathf.Lerp(minDamageStrength, maxDamageStrength, t);

        damagePulse = Mathf.Max(damagePulse, strength);

        if (showDebug)
            Debug.Log($"CarRumble damage {amount:F1}. Pulse strength: {strength:F2}");
    }

    /// <summary>The car exploded.</summary>
    public void OnExploded()
    {
        exploded = true;

        if (!enableRumble || !explosionRumble)
            return;

        explosion = explosionStrength;
        damagePulse = 0f;   // the explosion takes over from any damage pulse

        if (showDebug)
            Debug.Log($"CarRumble explosion. Strength: {explosionStrength:F2}");
    }

    // The car crossed the finish line (GameManager.RaceFinished).
    private void OnRaceFinished()
    {
        finished = true;

        if (!enableRumble || !finishRumble)
            return;

        finishPulse = finishStrength;
        damagePulse = 0f;

        if (showDebug)
            Debug.Log($"CarRumble finish. Strength: {finishStrength:F2}");
    }

    /// <summary>Call when the car is repaired or respawned.</summary>
    public void ResetState()
    {
        exploded = false;
        finished = false;
        explosion = 0f;
        finishPulse = 0f;
        damagePulse = 0f;
        impact = 0f;
        lastScrapeTime = -1f;
    }

    // ---------------------------------------------------------------- Update

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

        if (!enableRumble)
        {
            status = "Silent: Enable Rumble is off";
            Silence(pad);
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

        // Pulses fade out using unscaled time so they behave the same in slow-mo
        float dt = Time.unscaledDeltaTime;
        impact = Mathf.MoveTowards(impact, 0f, dt / Mathf.Max(0.01f, impactFadeTime));
        damagePulse = Mathf.MoveTowards(damagePulse, 0f, dt / Mathf.Max(0.01f, damageFadeTime));
        explosion = Mathf.MoveTowards(explosion, 0f, explosionStrength * dt / Mathf.Max(0.01f, explosionFadeTime));
        finishPulse = Mathf.MoveTowards(finishPulse, 0f, finishStrength * dt / Mathf.Max(0.01f, finishFadeTime));

        bool quietCar = (exploded && silenceOtherRumbleAfterExplosion) || (finished && silenceOtherRumbleAfterFinish);

        // Speed shake: starts at a level you can feel, then climbs toward the redline
        lastSpeedKmh = rb.linearVelocity.magnitude * 3.6f;
        float shakeLow = 0f;
        float shakeHigh = 0f;

        if (speedShake && !quietCar)
        {
            float speedT = Mathf.InverseLerp(shakeStartKmh, shakeMaxKmh, lastSpeedKmh);
            float shake = 0f;
            if (speedT > 0f)
            {
                float curved = Mathf.Pow(speedT, 0.7f);
                shake = Mathf.Lerp(minSpeedShake, maxSpeedShake, curved);
                shake *= Mathf.Clamp01(speedT * 8f);   // short ease-in so it doesn't pop on at the start speed
            }

            float noise = 0.8f + 0.2f * Mathf.PerlinNoise(Time.unscaledTime * 15f, 0f);
            shakeHigh = shake * noise;
            shakeLow = shake * 0.5f;
        }

        // Scrape: grinding rumble while in contact with a barrier
        bool scraping = barrierScrape && !quietCar && Time.unscaledTime - lastScrapeTime < 0.1f;
        float scrapeHigh = scraping ? scrapeStrength : 0f;
        float scrapeLow = scraping ? scrapeStrength * 0.6f : 0f;

        // Barrier impact (only ever set when Barrier Impacts is on)
        float impactLow = impact;
        float impactHigh = impact * 0.8f;

        // Damage pulse: a sharp jolt, so favour the high motor a little
        float damageLow = damagePulse * 0.7f;
        float damageHigh = damagePulse;

        // Explosion: heavy on the low motor, with a strong buzz on top
        float explosionLow = explosion;
        float explosionHigh = explosion * 0.8f;

        // Finish: both motors, so it reads as a bright, full pulse rather than a thud
        float finishLow = finishPulse * 0.8f;
        float finishHigh = finishPulse;

        // Low motor = heavy thud, high motor = fine buzz
        float low = Mathf.Clamp01(impactLow + shakeLow + scrapeLow + damageLow + explosionLow + finishLow) * strength;
        float high = Mathf.Clamp01(impactHigh + shakeHigh + scrapeHigh + damageHigh + explosionHigh + finishHigh) * strength;

        pad.SetMotorSpeeds(low, high);
        lastLow = low;
        lastHigh = high;
    }

    // Winning does not silence the rumble, so the finish pulse can play. It goes quiet once the game freezes.
    private static string GetSilentReason()
    {
        if (Time.timeScale == 0f)
            return "Time.timeScale is 0";

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            if (gm.IsPaused) return "game is paused";
            if (gm.IsFrozen) return "game is frozen after game over or win";
        }

        return null;
    }

    private void Silence(Gamepad pad)
    {
        pad.SetMotorSpeeds(0f, 0f);
        lastLow = 0f;
        lastHigh = 0f;
    }

    // ---------------------------------------------------------------- Collisions

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

        if (!enableRumble || !barrierImpacts) return;
        if (!isBarrier || hitStrength <= 0f) return;

        impact = Mathf.Max(impact, hitStrength);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!enableRumble || !barrierScrape) return;
        if (!IsBarrier(collision)) return;

        if (rb.linearVelocity.magnitude > minScrapeSpeed)
            lastScrapeTime = Time.unscaledTime;
    }

    private bool IsBarrier(Collision collision)
    {
        return (barrierLayers.value & (1 << collision.gameObject.layer)) != 0;
    }

    // ---------------------------------------------------------------- Debug and cleanup

    private void OnGUI()
    {
        if (!showDebug) return;

        Gamepad pad = Gamepad.current;
        string padName = pad != null ? pad.displayName : "NONE";

        GUI.Label(new Rect(10, 10, 700, 160),
            "Rumble debug\n" +
            $"Gamepad: {padName}\n" +
            $"Status: {status}\n" +
            $"Speed: {lastSpeedKmh:F0} km/h   Impact: {impact:F2}   Damage: {damagePulse:F2}   Explosion: {explosion:F2}   Finish: {finishPulse:F2}\n" +
            $"Motors: low {lastLow:F2}   high {lastHigh:F2}");
    }

    public void StopRumble()
    {
        impact = 0f;
        damagePulse = 0f;
        explosion = 0f;
        finishPulse = 0f;
        lastScrapeTime = -1f;
        if (Gamepad.current != null)
            Gamepad.current.SetMotorSpeeds(0f, 0f);
    }

    private void OnDisable()
    {
        GameManager.RaceFinished -= OnRaceFinished;
        StopRumble();
    }

    private void OnApplicationQuit() => StopRumble();
    private void OnApplicationPause(bool paused)
    {
        if (paused) StopRumble();
    }
}