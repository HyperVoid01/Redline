using UnityEngine;

/// <summary>
/// A projectile fired by EnemyGun. Put this on the root of the bullet prefab (a TrailRenderer on the
/// same object or a child gives it its trail). Don't give the bullet prefab any colliders.
///
/// It doesn't rely on Rigidbody collisions or triggers, which miss fast objects. Every frame it sweeps a small
/// sphere along the distance it is about to travel and hits whatever is in the way, so it can't
/// skip over the player's car however fast it moves. As a second safety net, it also checks whether it
/// ended the step inside a collider (sweeps can't see a collider they start inside).
/// </summary>
public class EnemyBullet : MonoBehaviour
{
    [Tooltip("Radius of the sweep. Slightly bigger than the visual makes hits feel fair.")]
    public float radius = 0.12f;
    
    [Tooltip("Distance to the player at which the near-miss whiz plays.")]
    public float whizDistance = 3f;

    bool whizzed;

    EnemyGun gun;
    Vector3 velocity;
    float damage;
    LayerMask mask;
    float age;
    float maxAge;
    bool flying;
    float releaseTime;

    TrailRenderer[] trails;
    Renderer[] visuals;
    float trailFadeTime;

    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    static readonly Collider[] overlapBuffer = new Collider[8];

    void Awake()
    {
        trails = GetComponentsInChildren<TrailRenderer>(true);

        // Everything that is visible except the trails, so the trail can fade out after impact.
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (!(all[i] is TrailRenderer))
                count++;
        }

        visuals = new Renderer[count];
        for (int i = 0, k = 0; i < all.Length; i++)
        {
            if (!(all[i] is TrailRenderer))
                visuals[k++] = all[i];
        }

        for (int i = 0; i < trails.Length; i++)
            trailFadeTime = Mathf.Max(trailFadeTime, trails[i].time);
    }

    public void Launch(EnemyGun owner, Vector3 position, Vector3 worldVelocity, float damageAmount, LayerMask hitMask, float lifetime)
    {
        gun = owner;
        velocity = worldVelocity;
        damage = damageAmount;
        mask = hitMask;
        maxAge = lifetime;
        age = 0f;
        flying = true;
        whizzed = false;

        Quaternion rotation = worldVelocity.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(worldVelocity) : Quaternion.identity;
        transform.SetPositionAndRotation(position, rotation);

        gameObject.SetActive(true);
        SetVisualsEnabled(true);

        // Clear AFTER moving, or the trail would streak from wherever this pooled bullet last was.
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
            trails[i].emitting = true;
        }
    }

    void Update()
    {
        if (gun == null)
            return;

        // After impact the bullet waits for its trail to fade, then goes back to the pool.
        if (!flying)
        {
            if (Time.time >= releaseTime)
                gun.ReleaseBullet(this);
            return;
        }

        // Cap the step so a lag spike doesn't turn into one huge jump.
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        age += dt;

        if (age > maxAge)
        {
            Finish(transform.position);
            return;
        }
        
        // Near miss: play the whiz once, the first time we pass close to the player.
        if (!whizzed)
            CheckWhiz();

        Vector3 start = transform.position;
        Vector3 step = velocity * dt;
        float distance = step.magnitude;

        if (distance < 1e-5f)
            return;

        Vector3 direction = step / distance;

        // 1) Sweep along the path for the nearest thing in the way.
        if (TrySweep(start, direction, distance, out RaycastHit hit))
        {
            Impact(hit.point, hit.normal, hit.collider);
            return;
        }

        Vector3 end = start + step;

        // 2) Safety net: did the bullet end up inside something solid?
        int overlaps = Physics.OverlapSphereNonAlloc(end, radius, overlapBuffer, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (gun.ShouldIgnore(overlapBuffer[i]))
                continue;

            Impact(end, -direction, overlapBuffer[i]);
            return;
        }

        transform.position = end;
    }
    
    void CheckWhiz()
    {
        // The gun already knows the player's car, so no scene search is needed.
        Transform playerTransform = gun != null ? gun.target : null;
        if (playerTransform == null)
            return;

        float sqrDist = (playerTransform.position - transform.position).sqrMagnitude;
        if (sqrDist > whizDistance * whizDistance)
            return;

        whizzed = true;

        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound(SoundID.BulletWhiz, transform.position);
    }

    bool TrySweep(Vector3 start, Vector3 direction, float distance, out RaycastHit best)
    {
        int count = Physics.SphereCastNonAlloc(start, radius, direction, hitBuffer, distance, mask, QueryTriggerInteraction.Ignore);

        best = default;
        float nearest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (gun.ShouldIgnore(hitBuffer[i].collider))
                continue;

            if (hitBuffer[i].distance < nearest)
            {
                nearest = hitBuffer[i].distance;
                best = hitBuffer[i];
                found = true;
            }
        }

        return found;
    }

    void Impact(Vector3 point, Vector3 normal, Collider collider)
    {
        PlayerHealth health = collider.GetComponentInParent<PlayerHealth>();
        if (health != null)
            health.TakeDamage(damage);
        
        SoundPlayer.Instance.PlaySound(SoundID.BulletImpact, transform.position);

        gun.SpawnHitEffect(point, normal, health != null);
        Finish(point);
    }

    void Finish(Vector3 point)
    {
        flying = false;
        transform.position = point;
        SetVisualsEnabled(false);

        // Stop adding to the trail but let what's already drawn fade out.
        for (int i = 0; i < trails.Length; i++)
            trails[i].emitting = false;

        releaseTime = Time.time + trailFadeTime + 0.05f;
    }

    void SetVisualsEnabled(bool on)
    {
        for (int i = 0; i < visuals.Length; i++)
            visuals[i].enabled = on;
    }
}