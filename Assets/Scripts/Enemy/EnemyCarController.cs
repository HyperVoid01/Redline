using UnityEngine;

/// <summary>
/// Enemy car that chases the player down the track.
/// It doesn't use WheelColliders or NavMesh: it slides along the spline by distance, so it can't get
/// stuck on bends. Its speed rubber-bands against the player's speed to hold a target gap behind
/// the player, and that gap slowly shrinks so the pressure builds.
///
/// With spawnImmediately on (the default) it appears at the start, startTargetGap metres behind the player.
/// CarSpawner places it explicitly and gives each enemy its own slot, lane and weave phase,
/// so several enemies don't stack on top of each other.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyCarController : MonoBehaviour
{
    [Header("References")]
    public TrackSampler sampler;
    public TrackProgress playerProgress;
    public Rigidbody playerBody;

    [Header("Spawn")]
    [Tooltip("Appear at the start, behind the player, as soon as the track and player are ready. " +
             "Turn off to appear at the track start later, once the player is spawnGap along.")]
    public bool spawnImmediately = true;
    public float spawnGap = 45f;           // only used when spawnImmediately is off
    public bool hideUntilSpawned = true;

    [Header("Slot (set by CarSpawner so several enemies don't stack)")]
    [Tooltip("Added to the start, minimum and hard-minimum gaps. 0 for the lead enemy.")]
    public float slotOffset = 0f;
    [Tooltip("Sideways offset from the road centre that the weave is centred on.")]
    public float laneOffset = 0f;
    [Tooltip("Shifts the weave so enemies don't move in sync.")]
    public float phaseOffset = 0f;

    [Header("Chase")]
    public float startTargetGap = 30f;     // metres behind the player the enemy aims to sit at first
    public float minTargetGap = 12f;       // the gap never shrinks below this
    public float gapShrinkRate = 0.15f;    // metres per second that the target gap closes
    public float hardMinGap = 7f;          // never closer than this, so it can't overtake or ram the player
    public float catchUpGain = 0.6f;       // extra m/s of speed per metre the enemy is behind its target gap
    public float minSpeed = 5f;            // m/s, the enemy always keeps creeping forward
    public float maxSpeed = 60f;           // m/s
    public float acceleration = 25f;       // m/s per second

    [Header("Weave")]
    public float weaveAmplitude = 2.5f;    // metres either side of the lane
    public float weaveFrequency = 0.3f;    // cycles per second
    public float maxLateralOffset = 3f;    // keep below half the road width minus half the car width
    public float lateralSmoothing = 3f;

    [Header("Visuals")]
    public float heightOffset = 0f;
    public Transform[] wheels;
    public float wheelRadius = 0.33f;

    public bool IsSpawned { get; private set; }
    public float Distance { get; private set; }
    public float Speed { get; private set; }
    public float GapToPlayer { get { return playerProgress != null ? playerProgress.Distance - Distance : 0f; } }

    /// <summary>How far behind the player this enemy starts, including its slot.</summary>
    public float StartGap { get { return startTargetGap + slotOffset; } }

    Rigidbody body;
    Renderer[] renderers;
    float targetGap;
    float lateral;
    float elapsed;
    int lastVersion = -1;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(!hideUntilSpawned);
    }

    void FixedUpdate()
    {
        if (sampler == null || !sampler.IsBuilt || playerProgress == null || !playerProgress.IsReady)
            return;

        // The track was rebuilt: go back to waiting to spawn.
        if (lastVersion != sampler.Version)
        {
            lastVersion = sampler.Version;
            ResetEnemy();
        }

        float dt = Time.fixedDeltaTime;
        float playerDist = playerProgress.Distance;

        if (!IsSpawned)
        {
            if (spawnImmediately)
            {
                Spawn(playerDist - StartGap);
            }
            else
            {
                if (playerDist < spawnGap)
                    return;

                Spawn(playerDist - spawnGap);
            }
        }

        elapsed += dt;
        targetGap = Mathf.Max(minTargetGap + slotOffset, targetGap - gapShrinkRate * dt);

        // ---- Rubber-banded speed ----
        float playerSpeed = playerBody != null ? playerBody.linearVelocity.magnitude : 0f;
        float gap = playerDist - Distance;

        float desired = playerSpeed + (gap - targetGap) * catchUpGain;
        desired = Mathf.Clamp(desired, minSpeed, maxSpeed);
        Speed = Mathf.MoveTowards(Speed, desired, acceleration * dt);

        Distance += Speed * dt;

        // Never get closer than the hard minimum gap: no overtaking, no ramming.
        float maxDistance = playerDist - (hardMinGap + slotOffset);
        if (Distance > maxDistance)
        {
            Distance = maxDistance;
            Speed = Mathf.Min(Speed, playerSpeed);
        }
        Distance = Mathf.Clamp(Distance, 0f, sampler.Length);

        // ---- Weaving around the lane ----
        float phase = elapsed * weaveFrequency * Mathf.PI * 2f + phaseOffset;
        float weave = Mathf.Sin(phase) * weaveAmplitude + Mathf.Sin(phase * 0.37f + 1.3f) * weaveAmplitude * 0.25f;
        float targetLateral = Mathf.Clamp(laneOffset + weave, -maxLateralOffset, maxLateralOffset);

        float previousLateral = lateral;
        lateral = Mathf.Lerp(lateral, targetLateral, 1f - Mathf.Exp(-lateralSmoothing * dt));
        float lateralVelocity = (lateral - previousLateral) / dt;

        // ---- Pose on the track ----
        ApplyPose(Distance, lateralVelocity, true);
    }

    void Update()
    {
        // Spin the wheels to match speed (assumes each wheel turns around its local X axis).
        if (!IsSpawned || wheels == null || wheelRadius <= 0f)
            return;

        float degrees = Speed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] != null)
                wheels[i].Rotate(Vector3.forward, degrees, Space.Self);
        }
    }

    /// <summary>Places the enemy on the track at a distance and starts the chase. Used by CarSpawner.</summary>
    public void SpawnAt(float distance)
    {
        if (sampler == null || !sampler.IsBuilt)
        {
            Debug.LogWarning("EnemyCarController: the track isn't built yet, so the enemy can't spawn.", this);
            return;
        }

        lastVersion = sampler.Version;   // so the "track was rebuilt" check doesn't undo this spawn
        Spawn(distance);
    }

    void Spawn(float distance)
    {
        IsSpawned = true;
        elapsed = 0f;
        lateral = Mathf.Clamp(laneOffset, -maxLateralOffset, maxLateralOffset);
        targetGap = StartGap;
        Distance = Mathf.Clamp(distance, 0f, sampler.Length);
        Speed = playerBody != null ? playerBody.linearVelocity.magnitude : 0f;

        ApplyPose(Distance, 0f, false);
        SetVisible(true);
    }

    public void ResetEnemy()
    {
        IsSpawned = false;
        Speed = 0f;
        SetVisible(!hideUntilSpawned);
    }

    void ApplyPose(float distance, float lateralVelocity, bool smooth)
    {
        sampler.Sample(distance, out Vector3 centre, out Vector3 forward);

        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
        if (flatForward.sqrMagnitude < 1e-6f)
            flatForward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, flatForward.normalized);

        Vector3 position = centre + right * lateral + Vector3.up * heightOffset;
        Vector3 look = forward + right * (lateralVelocity * 0.15f);
        Quaternion rotation = Quaternion.LookRotation(look, Vector3.up);

        if (smooth)
        {
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }
        else
        {
            // Teleport without interpolating from wherever the car was before.
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    void SetVisible(bool visible)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }
}