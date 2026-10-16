using UnityEngine;

/// <summary>
/// Car sound effects: tire skid, wind, speed chime, gear shift, impacts and barrier scrape.
/// The engine itself is still handled by EngineAudioDriver + SampleEngine.
/// Put this on the same GameObject as CarController and the Rigidbody.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarAudio : MonoBehaviour
{
    [Header("Role")]
    [Tooltip("Tick on the player's car. Leave off on enemy cars (no wind, gear shift or speed chime).")]
    [SerializeField] private bool isPlayer = true;
    [Tooltip("Enemy cars only. Tick if the enemy has no EngineAudioDriver/SampleEngine of its own.")]
    [SerializeField] private bool enemyEngineLoop = false;

    [Header("Tire Skid")]
    [Tooltip("Wheel slip where the squeal starts to fade in.")]
    [SerializeField] private float skidSlipStart = 0.3f;
    [Tooltip("Wheel slip where the squeal reaches full volume.")]
    [SerializeField] private float skidSlipFull = 0.8f;
    [Tooltip("How much wheelspin / braking slip counts compared with sideways slip.")]
    [SerializeField, Range(0f, 1f)] private float forwardSlipWeight = 0.5f;
    [SerializeField] private float minSkidSpeedKmh = 8f;
    [SerializeField, Range(0f, 1f)] private float skidMaxVolume = 1f;
    [SerializeField] private float skidAttack = 14f;
    [SerializeField] private float skidRelease = 7f;

    [Header("Wind (player only)")]
    [SerializeField, Range(0f, 1f)] private float windMaxVolume = 0.6f;

    [Header("Speed Chime (player only)")]
    [Tooltip("Chime starts at this fraction of the car's top speed.")]
    [SerializeField, Range(0.3f, 1f)] private float chimeStartFraction = 0.85f;
    [Tooltip("Seconds between chimes just past the start speed.")]
    [SerializeField] private float chimeSlowInterval = 0.9f;
    [Tooltip("Seconds between chimes at top speed.")]
    [SerializeField] private float chimeFastInterval = 0.25f;

    [Header("Impacts")]
    [Tooltip("Closing speed for a light hit (CarImpact sound).")]
    [SerializeField] private float lightImpactKmh = 8f;
    [Tooltip("Closing speed for a heavy hit (Crash sound).")]
    [SerializeField] private float heavyImpactKmh = 40f;
    [SerializeField] private float impactCooldown = 0.2f;
    [Tooltip("Hits from rigidbodies lighter than this (bullets, debris) are ignored here.")]
    [SerializeField] private float minOtherMass = 20f;
    [Tooltip("Contacts whose surface normal points up more than this count as ground, not a wall.")]
    [SerializeField, Range(0f, 1f)] private float groundNormalY = 0.7f;

    [Header("Barrier Scrape")]
    [Tooltip("Car speed where the scrape reaches full volume.")]
    [SerializeField] private float scrapeFullSpeedKmh = 60f;
    [SerializeField, Range(0f, 1f)] private float scrapeMaxVolume = 1f;
    [SerializeField] private float scrapeAttack = 20f;
    [SerializeField] private float scrapeRelease = 8f;

    private const float MsToKmh = 3.6f;

    private CarController car;
    private EngineAudioDriver engineDriver;
    private WheelCollider[] wheels;

    private AudioSource skidLoop;
    private AudioSource windLoop;
    private AudioSource scrapeLoop;
    private AudioSource enemyEngineSource;
    private bool loopsCreated;

    private float skidLevel;
    private float scrapeLevel;
    private float lastScrapeContactTime = -999f;
    private float nextImpactTime;
    private float nextChimeTime;
    private int lastGear;

    /// <summary>0-1 amount the tires are currently sliding. Handy for tire smoke and skid marks.</summary>
    public float SkidLevel => skidLevel;

    private void Awake()
    {
        car = GetComponent<CarController>();
        engineDriver = GetComponent<EngineAudioDriver>();
        wheels = GetComponentsInChildren<WheelCollider>();
        lastGear = engineDriver != null ? engineDriver.Gear : 1;
    }

    private void OnDisable()
    {
        StopAudio();
    }

    private void Update()
    {
        SoundPlayer player = SoundPlayer.Instance;

        if (player == null)
            return;

        GameManager gm = GameManager.Instance;

        if (gm != null)
        {
            // Game over / win froze the game: kill every loop and don't restart them.
            if (gm.IsFrozen)
            {
                if (loopsCreated)
                    StopAudio();

                return;
            }

            // Paused: AudioListener.pause already holds the sound, nothing to update.
            if (gm.IsPaused)
                return;
        }

        // Created here rather than in Start so the car works no matter
        // which object's Awake/Start runs first (the player car is spawned at runtime).
        EnsureLoops(player);

        float dt = Time.deltaTime;

        UpdateSkid(player, dt);
        UpdateScrape(player, dt);

        if (isPlayer)
        {
            UpdateWind(player);
            UpdateGearShift(player);
            UpdateSpeedChime(player);
        }
        else if (enemyEngineLoop)
        {
            UpdateEnemyEngine(player);
        }
    }

    // ---------------------------------------------------------------- Setup

    private void EnsureLoops(SoundPlayer player)
    {
        if (loopsCreated)
            return;

        loopsCreated = true;

        skidLoop = player.PlayLoop(SoundID.TireSkid, transform);
        player.SetLoopVolume(skidLoop, 0f);

        scrapeLoop = player.PlayLoop(SoundID.BarrierScrape, transform);
        player.SetLoopVolume(scrapeLoop, 0f);

        if (isPlayer)
        {
            windLoop = player.PlayLoop(SoundID.Wind, transform);
            player.SetLoopVolume(windLoop, 0f);
        }
        else if (enemyEngineLoop)
        {
            enemyEngineSource = player.PlayLoop(SoundID.EnemyCarEngine, transform);
        }
    }

    /// <summary>Stops every loop this car started. Called automatically when the component is disabled.</summary>
    public void StopAudio()
    {
        SoundPlayer player = SoundPlayer.Instance;

        if (player != null)
        {
            player.StopLoop(skidLoop);
            player.StopLoop(windLoop);
            player.StopLoop(scrapeLoop);
            player.StopLoop(enemyEngineSource);
        }

        skidLoop = null;
        windLoop = null;
        scrapeLoop = null;
        enemyEngineSource = null;
        loopsCreated = false;

        skidLevel = 0f;
        scrapeLevel = 0f;
    }

    // ---------------------------------------------------------------- Loops

    private void UpdateSkid(SoundPlayer player, float dt)
    {
        float target = 0f;

        if (car.SpeedKmh > minSkidSpeedKmh)
            target = Mathf.InverseLerp(skidSlipStart, skidSlipFull, GetWorstSlip());

        skidLevel = Smooth(skidLevel, target, skidAttack, skidRelease, dt);

        player.SetLoopVolume(skidLoop, skidLevel * skidMaxVolume);
        player.SetLoopPitch(skidLoop, Mathf.Lerp(0.9f, 1.15f, skidLevel));
    }

    private float GetWorstSlip()
    {
        float worst = 0f;

        foreach (WheelCollider wheel in wheels)
        {
            WheelHit hit;
            bool grounded = car != null
                ? car.TryGetWheelHit(wheel, out hit)
                : wheel.GetGroundHit(out hit);

            if (!grounded)
                continue;

            float slip = Mathf.Max(
                Mathf.Abs(hit.sidewaysSlip),
                Mathf.Abs(hit.forwardSlip) * forwardSlipWeight);

            worst = Mathf.Max(worst, slip);
        }

        return worst;
    }

    private void UpdateScrape(SoundPlayer player, float dt)
    {
        // OnCollisionStay refreshes the timer every physics step while touching a wall.
        bool touching = Time.time - lastScrapeContactTime < 0.1f;
        float speed01 = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, scrapeFullSpeedKmh));
        float target = touching ? speed01 : 0f;

        scrapeLevel = Smooth(scrapeLevel, target, scrapeAttack, scrapeRelease, dt);

        player.SetLoopVolume(scrapeLoop, scrapeLevel * scrapeMaxVolume);
        player.SetLoopPitch(scrapeLoop, Mathf.Lerp(0.85f, 1.15f, speed01));
    }

    private void UpdateWind(SoundPlayer player)
    {
        float speed01 = Speed01();

        // Squared so wind stays quiet at low speed and builds near the top.
        player.SetLoopVolume(windLoop, speed01 * speed01 * windMaxVolume);
        player.SetLoopPitch(windLoop, Mathf.Lerp(0.8f, 1.3f, speed01));
    }

    private void UpdateEnemyEngine(SoundPlayer player)
    {
        float speed01 = Speed01();

        player.SetLoopVolume(enemyEngineSource, Mathf.Lerp(0.5f, 1f, speed01));
        player.SetLoopPitch(enemyEngineSource, Mathf.Lerp(0.8f, 1.6f, speed01));
    }

    // ---------------------------------------------------------------- One-shots

    private void UpdateGearShift(SoundPlayer player)
    {
        if (engineDriver == null)
            return;

        int gear = engineDriver.Gear;

        if (gear == lastGear)
            return;

        lastGear = gear;
        player.PlaySound2D(SoundID.GearShift);
    }

    private void UpdateSpeedChime(SoundPlayer player)
    {
        float top = car.TopSpeedKmh;
        float start = top * chimeStartFraction;

        if (top <= 0f || car.ForwardSpeedKmh < start)
        {
            // Reset so the chime sounds immediately next time the limit is passed.
            nextChimeTime = 0f;
            return;
        }

        if (Time.time < nextChimeTime)
            return;

        // Chimes come faster the closer the car gets to top speed.
        float t = Mathf.InverseLerp(start, top, car.ForwardSpeedKmh);

        player.PlaySound2D(SoundID.SpeedChime);
        nextChimeTime = Time.time + Mathf.Lerp(chimeSlowInterval, chimeFastInterval, t);
    }

    // ---------------------------------------------------------------- Collisions

    private void OnCollisionEnter(Collision collision)
    {
        SoundPlayer player = SoundPlayer.Instance;

        if (player == null || Time.time < nextImpactTime)
            return;

        if (collision.contactCount == 0 || IsLightObject(collision))
            return;

        ContactPoint contact = collision.GetContact(0);

        if (contact.normal.y > groundNormalY)
            return;

        float impactKmh = collision.relativeVelocity.magnitude * MsToKmh;

        SoundID id;

        if (impactKmh >= heavyImpactKmh)
            id = SoundID.Crash;
        else if (impactKmh >= lightImpactKmh)
            id = SoundID.CarImpact;
        else
            return;

        player.PlaySound(id, contact.point);
        nextImpactTime = Time.time + impactCooldown;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.contactCount == 0 || IsLightObject(collision))
            return;

        if (collision.GetContact(0).normal.y > groundNormalY)
            return;

        lastScrapeContactTime = Time.time;
    }

    // Bullets and debris have their own impact sounds, so skip them here.
    private bool IsLightObject(Collision collision)
    {
        return collision.rigidbody != null && collision.rigidbody.mass < minOtherMass;
    }

    // ---------------------------------------------------------------- Helpers

    private float Speed01()
    {
        float top = car.TopSpeedKmh;
        return top > 0f ? Mathf.Clamp01(car.SpeedKmh / top) : 0f;
    }

    // Fast rise, slower fall.
    private static float Smooth(float current, float target, float attack, float release, float dt)
    {
        float rate = target > current ? attack : release;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
    }
}