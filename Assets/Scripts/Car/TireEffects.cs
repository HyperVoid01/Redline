using System.Collections.Generic;
using UnityEngine;

public class TireEffects : MonoBehaviour
{
    private class WheelState
    {
        public WheelCollider collider;
        public ParticleSystem smoke;
        public TrailRenderer trail; // the skid mark currently being drawn, null if none
        public bool skidding;
    }

    private struct PendingRelease
    {
        public TrailRenderer trail;
        public float releaseTime;
    }

    [Header("Wheels")]
    [Tooltip("Leave empty to use every WheelCollider under this object.")]
    [SerializeField] private WheelCollider[] wheels;

    [Header("Slip")]
    [Tooltip("Slip needed to start skidding. WheelCollider slip is roughly 0 (gripping) to 1+ (sliding).")]
    [SerializeField] private float startSlip = 0.4f;
    [Tooltip("Slip below which a skid ends. Keep this under Start Slip so marks don't flicker on and off.")]
    [SerializeField] private float stopSlip = 0.25f;
    [Tooltip("No effects below this speed (m/s), where slip readings are noisy.")]
    [SerializeField] private float minSpeed = 3f;
    [Tooltip("Logs the highest slip value once a second, to help tune the thresholds.")]
    [SerializeField] private bool logPeakSlip;

    [Header("Smoke")]
    [Tooltip("Use a URP Particles Lit material so headlights light the smoke. An unlit material will glow at night.")]
    [SerializeField] private Material smokeMaterial;
    [SerializeField] private Color smokeColor = new Color(0.65f, 0.65f, 0.65f, 0.5f);
    [SerializeField] private float maxSmokeRate = 40f;
    [SerializeField] private float smokeStartSize = 0.8f;
    [SerializeField] private float smokeLifetime = 1.5f;

    [Header("Tire Marks")]
    [Tooltip("Transparent material. Setting its Render Face to Both makes marks visible from either side.")]
    [SerializeField] private Material markMaterial;
    [SerializeField] private float markWidth = 0.25f;
    [SerializeField, Range(0f, 1f)] private float markOpacity = 0.7f;
    [Tooltip("How long a mark stays on the road, in seconds.")]
    [SerializeField] private float markLifetime = 30f;
    [Tooltip("The most skid marks that can exist at once. When it is reached, the oldest finished mark is reused for the new skid.")]
    [SerializeField] private int maxMarks = 40;
    [Tooltip("Lift above the road surface to stop the mark flickering against it.")]
    [SerializeField] private float markLift = 0.04f;
    [Tooltip("If marks only show from below the road, toggle this, or set the mark material's Render Face to Both.")]
    [SerializeField] private bool flipMarkFacing = true;
    [Tooltip("Only these layers receive marks. Smoke still appears on other surfaces.")]
    [SerializeField] private LayerMask markSurfaces = ~0;

    private WheelState[] states;
    private Transform markRoot;

    private readonly Stack<TrailRenderer> pool = new Stack<TrailRenderer>();
    // Oldest first, because marks are added in the order they finish.
    private readonly List<PendingRelease> pendingReleases = new List<PendingRelease>();
    private int totalMarks; // every mark created so far: drawing, fading and pooled

    private Vector3 lastPosition;
    private float nextLogTime;
    private float loggedPeak;

    private void Awake()
    {
        if (wheels == null || wheels.Length == 0)
        {
            wheels = GetComponentsInChildren<WheelCollider>();
        }

        if (wheels.Length == 0)
        {
            Debug.LogError($"{name}: no WheelColliders found.", this);
            enabled = false;
            return;
        }

        if (smokeMaterial == null)
        {
            Debug.LogWarning($"{name}: no smoke material assigned, the smoke may render pink.", this);
        }

        if (markMaterial == null)
        {
            Debug.LogWarning($"{name}: no tire mark material assigned, the marks may render pink.", this);
        }

        // Every wheel can be drawing a mark at once. The cap needs room for those plus one fading mark to reuse.
        maxMarks = Mathf.Max(maxMarks, wheels.Length + 1);

        // Marks live in the world, not under the car, so they stay put as the car drives away.
        markRoot = new GameObject($"{name} Tire Marks").transform;

        states = new WheelState[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            states[i] = new WheelState
            {
                collider = wheels[i],
                smoke = CreateSmoke(wheels[i].name)
            };
        }

        lastPosition = transform.position;
    }

    private void OnDestroy()
    {
        // Takes every mark with it, whether drawing, fading or pooled.
        if (markRoot != null)
        {
            Destroy(markRoot.gameObject);
        }
    }

    private void LateUpdate()
    {
        // Speed from position change so this works whether or not the Rigidbody is interpolated.
        float deltaTime = Time.deltaTime;
        float speed = deltaTime > 0f ? (transform.position - lastPosition).magnitude / deltaTime : 0f;
        lastPosition = transform.position;

        float peakSlip = 0f;

        foreach (WheelState state in states)
        {
            bool grounded = state.collider.GetGroundHit(out WheelHit hit);
            float slip = grounded ? Mathf.Max(Mathf.Abs(hit.sidewaysSlip), Mathf.Abs(hit.forwardSlip)) : 0f;
            peakSlip = Mathf.Max(peakSlip, slip);

            // Two thresholds, so a skid doesn't flicker on and off around a single value.
            if (!state.skidding)
            {
                state.skidding = grounded && speed > minSpeed && slip > startSlip;
            }
            else
            {
                state.skidding = grounded && speed > minSpeed * 0.5f && slip > stopSlip;
            }

            UpdateSmoke(state, hit, slip);
            UpdateMark(state, hit, grounded);
        }

        ReleaseFinishedMarks();

        if (logPeakSlip)
        {
            loggedPeak = Mathf.Max(loggedPeak, peakSlip);
            if (Time.time >= nextLogTime)
            {
                Debug.Log($"{name}: peak slip {loggedPeak:0.00}, marks {totalMarks}/{maxMarks}", this);
                loggedPeak = 0f;
                nextLogTime = Time.time + 1f;
            }
        }
    }

    // ---------------------------------------------------------------- Smoke

    private ParticleSystem CreateSmoke(string wheelName)
    {
        GameObject smokeObject = new GameObject($"Tire Smoke ({wheelName})");
        smokeObject.transform.SetParent(transform, false);

        ParticleSystem smoke = smokeObject.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = smoke.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime * 0.6f, smokeLifetime);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeStartSize * 0.7f, smokeStartSize);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startColor = smokeColor;
        main.gravityModifier = -0.05f; // drifts slightly upward
        main.maxParticles = 150;

        ParticleSystem.EmissionModule emission = smoke.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        // Puffs grow as they age.
        ParticleSystem.SizeOverLifetimeModule size = smoke.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1f));

        // Fade in quickly, then fade out.
        ParticleSystem.ColorOverLifetimeModule color = smoke.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystemRenderer particleRenderer = smokeObject.GetComponent<ParticleSystemRenderer>();
        if (smokeMaterial != null)
        {
            particleRenderer.sharedMaterial = smokeMaterial;
        }
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        smoke.Play();
        return smoke;
    }

    private void UpdateSmoke(WheelState state, WheelHit hit, float slip)
    {
        ParticleSystem.EmissionModule emission = state.smoke.emission;

        if (!state.skidding)
        {
            emission.rateOverTime = 0f;
            return;
        }

        state.smoke.transform.position = ContactPoint(state.collider, hit);

        // Harder slides make more smoke, with a floor so a light skid still shows.
        float amount = Mathf.InverseLerp(stopSlip, 1f, slip);
        emission.rateOverTime = maxSmokeRate * Mathf.Max(0.3f, amount);
    }

    // ---------------------------------------------------------------- Tire marks

    private void UpdateMark(WheelState state, WheelHit hit, bool grounded)
    {
        bool wantMark = state.skidding && grounded && SurfaceAllowsMarks(hit);

        if (wantMark)
        {
            Vector3 point = ContactPoint(state.collider, hit) + hit.normal * markLift;

            // The ribbon's Z axis runs along the surface normal, so it lies flat on the road.
            Vector3 facing = flipMarkFacing ? -hit.normal : hit.normal;
            Quaternion rotation = Quaternion.LookRotation(facing);

            if (state.trail == null)
            {
                // Can be null if every mark is still being drawn. It just tries again next frame.
                state.trail = BeginMark(point, rotation);
            }
            else
            {
                state.trail.transform.SetPositionAndRotation(point, rotation);
            }
        }
        else if (state.trail != null)
        {
            EndMark(state);
        }
    }

    private bool SurfaceAllowsMarks(WheelHit hit)
    {
        if (hit.collider == null)
        {
            return false;
        }

        int layerBit = 1 << hit.collider.gameObject.layer;
        return (markSurfaces.value & layerBit) != 0;
    }

    private TrailRenderer AcquireTrail()
    {
        // 1. Reuse a mark that has already faded and been pooled.
        if (pool.Count > 0)
        {
            return pool.Pop();
        }

        // 2. Still under the cap: make a new one.
        if (totalMarks < maxMarks)
        {
            totalMarks++;
            return CreateTrail();
        }

        // 3. At the cap: take over the oldest finished mark, even though it hasn't fully faded.
        if (pendingReleases.Count > 0)
        {
            TrailRenderer oldest = pendingReleases[0].trail;
            pendingReleases.RemoveAt(0);
            return oldest;
        }

        // Every mark is still being drawn.
        return null;
    }

    private TrailRenderer BeginMark(Vector3 point, Quaternion rotation)
    {
        TrailRenderer trail = AcquireTrail();
        if (trail == null)
        {
            return null;
        }

        trail.gameObject.SetActive(true);
        trail.transform.SetPositionAndRotation(point, rotation);

        // Clear after moving, so a reused trail doesn't draw a line from where it last ended.
        trail.Clear();
        trail.emitting = true;
        return trail;
    }

    private void EndMark(WheelState state)
    {
        TrailRenderer trail = state.trail;
        state.trail = null;

        // Stop drawing, but leave the mark on the road until it has fully faded.
        trail.emitting = false;
        pendingReleases.Add(new PendingRelease
        {
            trail = trail,
            releaseTime = Time.time + trail.time
        });
    }

    private void ReleaseFinishedMarks()
    {
        // Oldest first, so we can stop at the first one that is still fading.
        while (pendingReleases.Count > 0 && Time.time >= pendingReleases[0].releaseTime)
        {
            TrailRenderer trail = pendingReleases[0].trail;
            pendingReleases.RemoveAt(0);

            trail.gameObject.SetActive(false);
            pool.Push(trail);
        }
    }

    private TrailRenderer CreateTrail()
    {
        GameObject markObject = new GameObject("Skid Mark");
        markObject.transform.SetParent(markRoot, false);

        TrailRenderer trail = markObject.AddComponent<TrailRenderer>();
        trail.time = markLifetime;
        trail.startWidth = markWidth;
        trail.endWidth = markWidth;
        trail.minVertexDistance = 0.15f;
        trail.alignment = LineAlignment.TransformZ;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.generateLightingData = true; // lets Lit and Simple Lit materials work on trails
        trail.emitting = false;

        if (markMaterial != null)
        {
            trail.sharedMaterial = markMaterial;
        }

        // Black rubber, fading out at the oldest end.
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(markOpacity, 0f), new GradientAlphaKey(markOpacity, 0.8f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;

        return trail;
    }

    // ---------------------------------------------------------------- Helpers

    private static Vector3 ContactPoint(WheelCollider wheel, WheelHit hit)
    {
        // Where the wheel meets the road, from the wheel's currently drawn position...
        wheel.GetWorldPose(out Vector3 wheelCentre, out _);
        Vector3 point = wheelCentre - hit.normal * wheel.radius;

        // ...then snapped onto the ground plane the physics reported, so it can never sit below the road.
        float offsetFromGround = Vector3.Dot(hit.point - point, hit.normal);
        return point + hit.normal * offsetFromGround;
    }
}