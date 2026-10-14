using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes parts of the car (bumpers, doors, bonnet, spoiler...) fall off as the car takes damage.
///
/// Put this on the player car root and drag the part GameObjects straight into the Parts list.
/// Nothing falls off until health drops to Start Falling At Health. The first part goes at that
/// point, and the rest are spread evenly between there and zero health. For example, with the
/// threshold at 75% and 5 parts, they go at 75%, 60%, 45%, 30% and 15% health.
///   Random Order on (default): each drop picks a random part that is still attached.
///   Random Order off: parts drop in the order they appear in the list.
///
/// A detached part gets a Rigidbody and collider, and is thrown off the car with the car's own velocity.
///
/// PlayerHealth calls UpdateFromHealth(current, max) after damage and RestoreParts() on reset.
/// </summary>
public class CarPartDetacher : MonoBehaviour
{
    class RuntimePart
    {
        public Transform transform;
        public bool detached;
        public Transform originalParent;
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
        public Vector3 originalLocalScale;
        public Rigidbody addedBody;
        public Collider addedCollider;
    }

    [Header("Parts")]
    [Tooltip("Drag the car's part GameObjects in here. Each one must be a child of the car.")]
    public GameObject[] parts;

    [Header("When parts fall off")]
    [Tooltip("No parts fall off until health drops to this fraction (1 = full health, 0.75 = 75%). The first part goes at this point.")]
    [Range(0.05f, 1f)] public float startFallingAtHealth = 0.75f;
    [Tooltip("On: a random attached part drops each time. Off: parts drop in list order.")]
    public bool randomOrder = true;
    [Tooltip("The car keeps this fraction of its parts even at zero health. 0 = every part can fall off.")]
    [Range(0f, 0.9f)] public float minPartsKept = 0f;

    [Header("Car")]
    [Tooltip("Used to pass the car's speed on to the parts. Found automatically if empty.")]
    public Rigidbody carBody;

    [Header("Throw")]
    [Tooltip("Mass of a loose part. Heavier parts fly less far.")]
    public float partMass = 20f;
    [Tooltip("Extra speed pushing the part away from the car centre, in metres per second.")]
    public float outwardSpeed = 4f;
    [Tooltip("Extra upward speed so the part pops off instead of just dropping.")]
    public float upwardSpeed = 3f;
    public float spinSpeed = 8f;

    [Header("Cleanup")]
    [Tooltip("Seconds the part stays in the world before it disappears. 0 = never.")]
    public float debrisLifetime = 10f;
    [Tooltip("Seconds the loose part ignores the car's colliders, so it doesn't jam against the car as it leaves.")]
    public float ignoreCarTime = 0.5f;

    readonly List<RuntimePart> runtimeParts = new List<RuntimePart>();
    readonly List<RuntimePart> scratch = new List<RuntimePart>();
    Collider[] carColliders;
    float lowestFractionSeen = 1f;

    void Awake()
    {
        if (carBody == null)
            carBody = GetComponentInChildren<Rigidbody>();

        carColliders = GetComponentsInChildren<Collider>();

        BuildRuntimeParts();
    }

    void BuildRuntimeParts()
    {
        runtimeParts.Clear();

        if (parts == null)
            return;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null)
                continue;

            Transform t = parts[i].transform;

            RuntimePart p = new RuntimePart();
            p.transform = t;
            p.originalParent = t.parent;
            p.originalLocalPosition = t.localPosition;
            p.originalLocalRotation = t.localRotation;
            p.originalLocalScale = t.localScale;
            runtimeParts.Add(p);
        }
    }

    // ---------- Called from PlayerHealth ----------

    public void UpdateFromHealth(float currentHealth, float maxHealth)
    {
        if (maxHealth <= 0f)
            return;

        UpdateFromFraction(Mathf.Clamp01(currentHealth / maxHealth));
    }

    public void UpdateFromFraction(float healthFraction)
    {
        // Health going back up to full means the car was reset.
        if (healthFraction >= 0.999f && lowestFractionSeen < 0.999f)
        {
            RestoreParts();
            return;
        }

        lowestFractionSeen = Mathf.Min(lowestFractionSeen, healthFraction);

        int total = runtimeParts.Count;
        if (total == 0)
            return;

        // Still above the threshold: the car is only scratched.
        if (healthFraction > startFallingAtHealth)
            return;

        int allowedToLose = total - Mathf.CeilToInt(total * minPartsKept);

        // The first part goes exactly at the threshold, the rest are spaced evenly down to zero health.
        float progress = (startFallingAtHealth - healthFraction) / startFallingAtHealth;
        int shouldBeGone = 1 + Mathf.FloorToInt(progress * total + 0.0001f);
        shouldBeGone = Mathf.Clamp(shouldBeGone, 0, allowedToLose);

        int gone = CountDetached();
        while (gone < shouldBeGone)
        {
            if (!DetachNext())
                break;

            gone++;
        }
    }

    int CountDetached()
    {
        int count = 0;
        for (int i = 0; i < runtimeParts.Count; i++)
        {
            if (runtimeParts[i].detached)
                count++;
        }
        return count;
    }

    /// <summary>Knocks off one part that is still attached (random or next in the list). Handy for testing or for big hits.</summary>
    [ContextMenu("Detach one part (play mode)")]
    public void DetachOne()
    {
        DetachNext();
    }

    bool DetachNext()
    {
        scratch.Clear();
        for (int i = 0; i < runtimeParts.Count; i++)
        {
            if (!runtimeParts[i].detached)
                scratch.Add(runtimeParts[i]);
        }

        if (scratch.Count == 0)
            return false;

        Detach(randomOrder ? scratch[Random.Range(0, scratch.Count)] : scratch[0]);
        return true;
    }

    // ---------- Detach ----------

    void Detach(RuntimePart p)
    {
        p.detached = true;

        Transform t = p.transform;
        Vector3 awayFromCar = t.position - transform.position;
        awayFromCar.y = 0f;
        awayFromCar = awayFromCar.sqrMagnitude > 1e-4f ? awayFromCar.normalized : transform.right;

        t.SetParent(null, true);

        // Make sure there is a collider the part can land on.
        Collider[] existing = t.GetComponentsInChildren<Collider>(true);
        if (existing.Length == 0)
        {
            p.addedCollider = AddBoundsCollider(t);
        }
        else
        {
            for (int i = 0; i < existing.Length; i++)
            {
                MeshCollider mc = existing[i] as MeshCollider;
                if (mc != null)
                    mc.convex = true; // non-convex mesh colliders aren't allowed on moving bodies

                existing[i].enabled = true;
            }
        }

        Rigidbody rb = t.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = t.gameObject.AddComponent<Rigidbody>();
            p.addedBody = rb;
        }

        rb.isKinematic = false;
        rb.mass = Mathf.Max(0.1f, partMass);

        Vector3 carVelocity = carBody != null ? carBody.linearVelocity : Vector3.zero;
        rb.linearVelocity = carVelocity + awayFromCar * outwardSpeed + Vector3.up * upwardSpeed;
        rb.angularVelocity = Random.onUnitSphere * spinSpeed;

        if (ignoreCarTime > 0f)
            StartCoroutine(IgnoreCarTemporarily(t.GetComponentsInChildren<Collider>(), ignoreCarTime));

        if (debrisLifetime > 0f)
            StartCoroutine(HideAfter(p, debrisLifetime));
    }

    Collider AddBoundsCollider(Transform t)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return null;

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

        BoxCollider box = t.gameObject.AddComponent<BoxCollider>();
        box.center = t.InverseTransformPoint(worldBounds.center);

        Vector3 scale = t.lossyScale;
        box.size = new Vector3(
            worldBounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            worldBounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            worldBounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

        return box;
    }

    IEnumerator IgnoreCarTemporarily(Collider[] partColliders, float seconds)
    {
        SetIgnore(partColliders, true);
        yield return new WaitForSeconds(seconds);
        SetIgnore(partColliders, false);
    }

    void SetIgnore(Collider[] partColliders, bool ignore)
    {
        for (int i = 0; i < partColliders.Length; i++)
        {
            if (partColliders[i] == null)
                continue;

            for (int j = 0; j < carColliders.Length; j++)
            {
                if (carColliders[j] != null && carColliders[j] != partColliders[i])
                    Physics.IgnoreCollision(partColliders[i], carColliders[j], ignore);
            }
        }
    }

    IEnumerator HideAfter(RuntimePart p, float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (p.detached && p.transform != null)
            p.transform.gameObject.SetActive(false);
    }

    // ---------- Restore ----------

    /// <summary>Puts every part back on the car. Call this when the car is reset or a run restarts.</summary>
    [ContextMenu("Restore parts (play mode)")]
    public void RestoreParts()
    {
        StopAllCoroutines();
        lowestFractionSeen = 1f;

        for (int i = 0; i < runtimeParts.Count; i++)
        {
            RuntimePart p = runtimeParts[i];
            if (p.transform == null || !p.detached)
                continue;

            Transform t = p.transform;

            if (p.addedBody != null)
            {
                p.addedBody.isKinematic = true;
                Destroy(p.addedBody);
                p.addedBody = null;
            }
            else
            {
                Rigidbody rb = t.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            if (p.addedCollider != null)
            {
                Destroy(p.addedCollider);
                p.addedCollider = null;
            }

            t.gameObject.SetActive(true);
            t.SetParent(p.originalParent, false);
            t.localPosition = p.originalLocalPosition;
            t.localRotation = p.originalLocalRotation;
            t.localScale = p.originalLocalScale;

            p.detached = false;
        }

        Physics.SyncTransforms();
    }
}