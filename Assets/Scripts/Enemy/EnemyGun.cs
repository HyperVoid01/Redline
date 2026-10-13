using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Guns on the enemy car. Fires bursts of real bullets (EnemyBullet) at the player when the player is in
/// range and in front of the enemy. Bullets are pooled, leave trails and spawn a hit particle on impact.
///
/// Because bullets take time to arrive, the gun leads its shots: it works out where the player will be
/// when the bullet gets there. Spread then makes some shots miss.
///
/// Put this on the enemy car root. Put the muzzle transforms just in front of the car, pointing forward.
/// If you don't assign a bullet prefab or hit effects, simple built-in ones are generated so it works
/// straight away; assign your own prefabs for the final look.
///
/// Audio (through SoundPlayer): the MachineGun sound plays once at the start of each burst, BulletImpact
/// plays on every hit, and BulletWhiz plays when a bullet is going to pass close to the player.
/// </summary>
public class EnemyGun : MonoBehaviour
{
    [Header("References")]
    public EnemyCarController car;         // optional: guns stay silent until the enemy has spawned
    public Transform target;               // the player car
    public PlayerHealth targetHealth;
    public Rigidbody targetBody;           // optional but recommended: the player's Rigidbody, for accurate lead
    public Transform[] muzzles;            // alternates between them; if empty, fires from this object

    [Header("Firing")]
    public float range = 70f;
    public float maxAimAngle = 45f;        // degrees between the enemy's forward and the player
    public float shotInterval = 0.1f;
    public int burstSize = 10;
    public float burstCooldown = 2.5f;
    public float initialDelay = 3f;        // seconds after spawning before the first burst
    public Vector3 aimOffset = new Vector3(0f, 0.8f, 0f);

    [Header("Bullets")]
    [Tooltip("Prefab with EnemyBullet on the root and a TrailRenderer. Leave empty to use a generated bullet.")]
    public EnemyBullet bulletPrefab;
    public float bulletSpeed = 110f;       // m/s relative to the enemy car
    public float bulletLifetime = 2f;
    public float damagePerHit = 3f;
    public float spreadDegrees = 1.5f;
    public bool leadTarget = true;
    public LayerMask hitMask = ~0;
    public int initialPoolSize = 30;

    [Header("Hit effects")]
    [Tooltip("Particle System prefab played where bullets hit scenery. Leave empty to use a generated spark burst.")]
    public ParticleSystem hitEffectPrefab;
    [Tooltip("Optional separate effect for hitting the player's car. Falls back to the one above.")]
    public ParticleSystem playerHitEffectPrefab;

    [Header("Generated defaults (only used when the prefabs above are empty)")]
    [Tooltip("Any unlit material (ideally additive/emissive). Empty uses Sprites/Default, which can be stripped from builds.")]
    public Material fallbackMaterial;
    public Color trailColor = new Color(1f, 0.8f, 0.3f);
    public float trailTime = 0.12f;
    public float trailWidth = 0.08f;
    public Color sceneryHitColor = new Color(0.9f, 0.8f, 0.55f);
    public Color playerHitColor = new Color(1f, 0.5f, 0.2f);

    [Header("Muzzle effects (all optional)")]
    public ParticleSystem[] muzzleFlashes; // matched to muzzles by index
    [Tooltip("Optional per-shot sound. Leave empty when using the MachineGun burst sound below, or shots will be doubled.")]
    public AudioSource shotSource;
    public AudioClip shotClip;

    [Header("Audio (SoundPlayer)")]
    [Tooltip("Plays the MachineGun sound once at the start of each burst. Make burstSize x shotInterval about the length of that clip.")]
    public bool playBurstSound = true;
    [Tooltip("Plays the BulletImpact sound at every bullet hit.")]
    public bool playHitSound = true;
    [Tooltip("Plays the BulletWhiz sound when a bullet will pass close to the player without hitting.")]
    public bool playWhizSound = true;
    [Tooltip("Closer than this and the bullet is treated as a hit, so no whiz.")]
    public float whizMinMiss = 1.5f;
    [Tooltip("Farther than this and the bullet is too far away to hear as a near miss.")]
    public float whizMaxMiss = 5f;
    [Tooltip("Minimum seconds between whizzes, so a burst doesn't become one long hiss.")]
    public float whizMinInterval = 0.15f;

    class EffectPool
    {
        public ParticleSystem prefab;
        public readonly Queue<ParticleSystem> free = new Queue<ParticleSystem>();
        public float duration;
    }

    struct ActiveEffect
    {
        public ParticleSystem system;
        public EffectPool pool;
        public float endTime;
    }

    readonly Queue<EnemyBullet> bulletPool = new Queue<EnemyBullet>();
    readonly List<ActiveEffect> activeEffects = new List<ActiveEffect>();

    EffectPool sceneryPool;
    EffectPool playerPool;
    Transform bulletRoot;
    Transform effectRoot;
    Material runtimeMaterial;

    int burstLeft;
    int muzzleIndex;
    float nextShotTime;
    float cooldownUntil;
    float activeSince = -1f;

    Vector3 lastShooterPos;
    Vector3 shooterVelocity;
    Vector3 lastTargetPos;
    Vector3 targetVelocity;
    bool trackingStarted;

    // Audio state
    AudioSource burstAudio;
    float burstAudioEndTime;
    readonly List<float> pendingWhizTimes = new List<float>();
    float nextWhizTime;

    void Start()
    {
        bulletRoot = new GameObject("EnemyBullets").transform;
        effectRoot = new GameObject("EnemyHitEffects").transform;

        if (hitEffectPrefab == null)
            hitEffectPrefab = BuildDefaultHitEffect(sceneryHitColor);
        if (playerHitEffectPrefab == null)
            playerHitEffectPrefab = hitEffectPrefab == null ? null : BuildDefaultHitEffect(playerHitColor);

        sceneryPool = new EffectPool { prefab = hitEffectPrefab };
        playerPool = new EffectPool { prefab = playerHitEffectPrefab };

        for (int i = 0; i < initialPoolSize; i++)
            bulletPool.Enqueue(CreateBullet());
    }

    void OnDestroy()
    {
        StopBurstAudio();

        if (bulletRoot != null)
            Destroy(bulletRoot.gameObject);
        if (effectRoot != null)
            Destroy(effectRoot.gameObject);
    }

    void Update()
    {
        UpdateVelocities();
        RecycleEffects();
        UpdateAudio();

        if (target == null || (targetHealth != null && targetHealth.IsDead))
            return;

        if (car != null)
        {
            if (!car.IsSpawned)
            {
                activeSince = -1f;
                burstLeft = 0;
                return;
            }

            if (activeSince < 0f)
                activeSince = Time.time;
        }
        else if (activeSince < 0f)
        {
            activeSince = Time.time;
        }

        if (Time.time < activeSince + initialDelay)
            return;

        Vector3 aimPoint = target.position + aimOffset;
        Vector3 toTarget = aimPoint - transform.position;

        if (toTarget.magnitude > range || Vector3.Angle(transform.forward, toTarget) > maxAimAngle)
            return;

        if (burstLeft <= 0)
        {
            if (Time.time < cooldownUntil)
                return;
            burstLeft = burstSize;
            StartBurstAudio();
        }

        if (Time.time < nextShotTime)
            return;

        Fire(aimPoint);
        burstLeft--;
        nextShotTime = Time.time + shotInterval;

        if (burstLeft <= 0)
            cooldownUntil = Time.time + burstCooldown;
    }

    // ---------- Firing ----------

    void Fire(Vector3 aimPoint)
    {
        int muzzleCount = Mathf.Max(1, muzzles != null ? muzzles.Length : 0);
        int index = muzzleIndex % muzzleCount;
        muzzleIndex++;

        Transform muzzle = (muzzles != null && muzzles.Length > 0) ? muzzles[index] : transform;
        Vector3 origin = muzzle.position;

        // Work relative to the enemy car: bullets inherit its velocity, so what matters is where the
        // player is and how fast it is moving relative to us.
        Vector3 relativePosition = aimPoint - origin;
        Vector3 relativeVelocity = targetVelocity - shooterVelocity;
        Vector3 direction = relativePosition.normalized;

        if (leadTarget)
        {
            float time = InterceptTime(relativePosition, relativeVelocity, bulletSpeed);
            if (time > 0f && time < bulletLifetime)
                direction = (relativePosition + relativeVelocity * time).normalized;
        }

        // Random spread inside a cone around the aim direction.
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 up = Vector3.Cross(direction, right);
        Vector2 offset = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
        direction = (direction + right * offset.x + up * offset.y).normalized;

        Vector3 bulletVelocity = shooterVelocity + direction * bulletSpeed;

        EnemyBullet bullet = bulletPool.Count > 0 ? bulletPool.Dequeue() : CreateBullet();
        bullet.Launch(this, origin, bulletVelocity, damagePerHit, hitMask, bulletLifetime);

        ScheduleWhiz(origin, bulletVelocity, aimPoint);

        if (muzzleFlashes != null && index < muzzleFlashes.Length && muzzleFlashes[index] != null)
            muzzleFlashes[index].Play();

        if (shotSource != null && shotClip != null)
            shotSource.PlayOneShot(shotClip);
    }

    /// <summary>
    /// Time for a bullet of the given speed to reach a target at relative position p moving with relative
    /// velocity v. Solves |p + v t| = speed * t. Returns -1 if it can't be reached.
    /// </summary>
    static float InterceptTime(Vector3 p, Vector3 v, float speed)
    {
        float a = Vector3.Dot(v, v) - speed * speed;
        float b = 2f * Vector3.Dot(p, v);
        float c = Vector3.Dot(p, p);

        if (Mathf.Abs(a) < 1e-4f)
            return b < 0f ? -c / b : -1f;

        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
            return -1f;

        float root = Mathf.Sqrt(discriminant);
        float t1 = (-b - root) / (2f * a);
        float t2 = (-b + root) / (2f * a);

        float best = float.MaxValue;
        if (t1 > 0f) best = t1;
        if (t2 > 0f && t2 < best) best = t2;
        return best == float.MaxValue ? -1f : best;
    }

    void UpdateVelocities()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        Vector3 shooterPos = transform.position;
        Vector3 targetPos = target != null ? target.position : Vector3.zero;

        if (!trackingStarted)
        {
            lastShooterPos = shooterPos;
            lastTargetPos = targetPos;
            trackingStarted = true;
            return;
        }

        // Smoothed so teleports and interpolation jitter don't throw the aim.
        Vector3 rawShooter = (shooterPos - lastShooterPos) / dt;
        shooterVelocity = Vector3.Lerp(shooterVelocity, rawShooter, 0.3f);
        lastShooterPos = shooterPos;

        if (targetBody != null)
        {
            targetVelocity = targetBody.linearVelocity;
        }
        else
        {
            Vector3 rawTarget = (targetPos - lastTargetPos) / dt;
            targetVelocity = Vector3.Lerp(targetVelocity, rawTarget, 0.3f);
        }
        lastTargetPos = targetPos;
    }

    // ---------- Audio ----------

    // The MachineGun clip is a whole burst, so it plays once per burst rather than once per shot.
    // It is started as a loop only so it follows the car, then switched to play once and removed
    // when it has finished.
    void StartBurstAudio()
    {
        if (!playBurstSound || SoundPlayer.Instance == null)
            return;

        StopBurstAudio();

        burstAudio = SoundPlayer.Instance.PlayLoop(SoundID.MachineGun, transform);

        if (burstAudio == null)
            return;

        burstAudio.loop = false;
        burstAudioEndTime = Time.time + burstAudio.clip.length + 0.1f;
    }

    void StopBurstAudio()
    {
        if (burstAudio != null && SoundPlayer.Instance != null)
            SoundPlayer.Instance.StopLoop(burstAudio);

        burstAudio = null;
    }

    void UpdateAudio()
    {
        if (burstAudio != null && Time.time >= burstAudioEndTime)
            StopBurstAudio();

        if (pendingWhizTimes.Count == 0)
            return;

        for (int i = pendingWhizTimes.Count - 1; i >= 0; i--)
        {
            if (Time.time < pendingWhizTimes[i])
                continue;

            pendingWhizTimes.RemoveAt(i);

            if (target != null && SoundPlayer.Instance != null)
            {
                // Slightly off to one side of the player, where the bullet actually passes.
                Vector3 position = target.position + Random.onUnitSphere * 1.5f;
                SoundPlayer.Instance.PlaySound(SoundID.BulletWhiz, position);
            }
        }
    }

    // Works out, at the moment a bullet is fired, whether it will pass close to the player
    // without hitting, and if so queues a whiz for when it gets there.
    void ScheduleWhiz(Vector3 origin, Vector3 bulletVelocity, Vector3 aimPoint)
    {
        if (!playWhizSound || Time.time < nextWhizTime)
            return;

        Vector3 relativePosition = aimPoint - origin;
        Vector3 relativeVelocity = targetVelocity - bulletVelocity;
        float speedSquared = Vector3.Dot(relativeVelocity, relativeVelocity);

        if (speedSquared < 1e-4f)
            return;

        // Time of closest approach between the bullet and the player.
        float time = -Vector3.Dot(relativePosition, relativeVelocity) / speedSquared;

        if (time <= 0f || time > bulletLifetime)
            return;

        float missDistance = (relativePosition + relativeVelocity * time).magnitude;

        if (missDistance < whizMinMiss || missDistance > whizMaxMiss)
            return;

        pendingWhizTimes.Add(Time.time + time);
        nextWhizTime = Time.time + whizMinInterval;
    }

    // ---------- Called by EnemyBullet ----------

    /// <summary>True for the enemy's own colliders and for bullets, so bullets never hit those.</summary>
    public bool ShouldIgnore(Collider collider)
    {
        return collider.transform.IsChildOf(transform) || collider.GetComponentInParent<EnemyBullet>() != null;
    }

    public void ReleaseBullet(EnemyBullet bullet)
    {
        bullet.gameObject.SetActive(false);
        bulletPool.Enqueue(bullet);
    }

    public void SpawnHitEffect(Vector3 point, Vector3 normal, bool onPlayer)
    {
        if (playHitSound && SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound(SoundID.BulletImpact, point);

        EffectPool pool = onPlayer && playerPool != null && playerPool.prefab != null ? playerPool : sceneryPool;
        if (pool == null || pool.prefab == null)
            return;

        ParticleSystem system;
        if (pool.free.Count > 0)
        {
            system = pool.free.Dequeue();
        }
        else
        {
            system = Instantiate(pool.prefab, effectRoot);

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;

            if (pool.duration <= 0f)
                pool.duration = main.duration + main.startLifetime.constantMax + 0.1f;
        }

        Quaternion rotation = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        system.transform.SetPositionAndRotation(point, rotation);
        system.gameObject.SetActive(true);
        system.Clear();
        system.Play();

        activeEffects.Add(new ActiveEffect
        {
            system = system,
            pool = pool,
            endTime = Time.time + Mathf.Max(0.2f, pool.duration)
        });
    }

    void RecycleEffects()
    {
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            ActiveEffect effect = activeEffects[i];
            if (Time.time < effect.endTime)
                continue;

            effect.system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.system.gameObject.SetActive(false);
            effect.pool.free.Enqueue(effect.system);
            activeEffects.RemoveAt(i);
        }
    }

    // ---------- Pool and default asset creation ----------

    EnemyBullet CreateBullet()
    {
        EnemyBullet bullet;

        if (bulletPrefab != null)
        {
            bullet = Instantiate(bulletPrefab, bulletRoot);
        }
        else
        {
            GameObject go = new GameObject("Bullet (generated)");
            go.transform.SetParent(bulletRoot, false);

            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.startWidth = trailWidth;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            trail.startColor = trailColor;
            trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;

            Material material = GetFallbackMaterial();
            if (material != null)
                trail.sharedMaterial = material;

            bullet = go.AddComponent<EnemyBullet>();
        }

        bullet.gameObject.SetActive(false);
        return bullet;
    }

    ParticleSystem BuildDefaultHitEffect(Color color)
    {
        GameObject go = new GameObject("HitEffect (generated)");
        go.transform.SetParent(transform, false);   // moved under the effect root when instances are made

        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // modules can't change while playing

        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor = color;
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 32;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.02f;

        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        ParticleSystemRenderer particleRenderer = go.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.05f;
        particleRenderer.lengthScale = 2f;
        Material material = GetFallbackMaterial();
        if (material != null)
            particleRenderer.sharedMaterial = material;

        go.SetActive(false);
        return system;
    }

    Material GetFallbackMaterial()
    {
        if (fallbackMaterial != null)
            return fallbackMaterial;

        if (runtimeMaterial == null)
        {
            // Shader.Find can be stripped from a build if nothing else references the shader,
            // so assign fallbackMaterial for builds.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                runtimeMaterial = new Material(shader);
        }

        return runtimeMaterial;
    }
}