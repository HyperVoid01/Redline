using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class TrackMaker : MonoBehaviour
{
    public static TrackMaker Instance;

    private enum SectionType { Straight, Bend, Sharp }

    [SerializeField] private TrackData trackData;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 12345;

    private const int MaxAttemptsPerSection = 8;
    private const float ClearanceFactor = 1.5f;

    private SplineContainer container;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh roadMesh;
    private Transform gatesRoot;

    public bool IsGenerated { get; private set; }
    public int CurrentSeed { get; private set; }
    public float TrackLength { get; private set; }
    public SplineContainer Container => container;

    /// <summary>World position of the start of the track.</summary>
    public Vector3 StartPosition
    {
        get { SampleAtT(0f, out Vector3 p, out _); return p; }
    }

    /// <summary>World rotation facing down the track at the start.</summary>
    public Quaternion StartRotation
    {
        get { SampleAtT(0f, out _, out Vector3 f); return Quaternion.LookRotation(f, Vector3.up); }
    }

    /// <summary>World position of the end of the track.</summary>
    public Vector3 FinishPosition
    {
        get { SampleAtT(1f, out Vector3 p, out _); return p; }
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureComponents();
    }

    private void Start()
    {
        if (generateOnStart)
        {
            Generate();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (roadMesh != null)
        {
            DestroySafe(roadMesh);
        }
    }

    // ---------------------------------------------------------------- Generation

    [ContextMenu("Generate Track")]
    public void Generate()
    {
        if (trackData == null)
        {
            Debug.LogError($"{name}: TrackData is not assigned.", this);
            return;
        }

        EnsureComponents();

        CurrentSeed = useRandomSeed ? System.Environment.TickCount : seed;
        System.Random rng = new System.Random(CurrentSeed);

        List<Vector3> points = GeneratePoints(rng);
        BuildSpline(points);
        TrackLength = container.Spline.GetLength();

        BuildRoadMesh();
        SpawnGates();

        IsGenerated = true;
        Debug.Log($"{name}: generated {TrackLength:0} m track with seed {CurrentSeed}.", this);
    }

    private void EnsureComponents()
    {
        if (container == null) container = GetComponent<SplineContainer>();
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();

        if (roadMesh == null)
        {
            roadMesh = new Mesh { name = "Track Road" };
        }
    }

    private List<Vector3> GeneratePoints(System.Random rng)
    {
        float step = trackData.knotSpacing;
        float clearance = trackData.roadWidth * ClearanceFactor;

        // Points within this many knots of each other are neighbours along the road, not overlaps.
        int skip = Mathf.CeilToInt(clearance * 2f / step) + 1;

        List<Vector3> points = new List<Vector3> { Vector3.zero };
        float yaw = 0f; // 0 = +Z. Because it starts at 0, it is also the total drift from the start heading.
        float generated = 0f;

        // Straight runway so the car starts aligned with the road.
        int startKnots = Mathf.CeilToInt(trackData.startStraightLength / step);
        AppendArc(points, yaw, 0f, startKnots, step);
        generated += startKnots * step;

        while (generated < trackData.totalLength)
        {
            bool placed = false;

            for (int attempt = 0; attempt < MaxAttemptsPerSection && !placed; attempt++)
            {
                PlanSection(rng, yaw, step, out int knotCount, out float yawPerKnot);

                int start = points.Count;
                AppendArc(points, yaw, yawPerKnot, knotCount, step);

                if (!IsClear(points, start, skip, clearance))
                {
                    points.RemoveRange(start, points.Count - start);
                    continue;
                }

                yaw += yawPerKnot * knotCount;
                generated += knotCount * step;
                placed = true;
            }

            if (!placed)
            {
                Debug.LogWarning($"{name}: couldn't place another section without overlapping the road. Track ends early at {generated:0} m.", this);
                break;
            }
        }

        int finishKnots = Mathf.CeilToInt(trackData.finishStraightLength / step);
        AppendArc(points, yaw, 0f, finishKnots, step);

        return points;
    }

    private void PlanSection(System.Random rng, float currentYaw, float step, out int knotCount, out float yawPerKnot)
    {
        SectionType type = PickSection(rng);

        if (type == SectionType.Straight)
        {
            float length = RandomRange(rng, trackData.straightLength.x, trackData.straightLength.y);
            knotCount = Mathf.Max(1, Mathf.CeilToInt(length / step));
            yawPerKnot = 0f;
            return;
        }

        bool sharp = type == SectionType.Sharp;
        Vector2 angleRange = sharp ? trackData.sharpAngle : trackData.bendAngle;
        Vector2 radiusRange = sharp ? trackData.sharpRadius : trackData.bendRadius;

        float angle = RandomRange(rng, angleRange.x, angleRange.y);
        float radius = RandomRange(rng, radiusRange.x, radiusRange.y);

        // Random direction, flipped if it would swing the track too far from its starting heading.
        float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
        if (Mathf.Abs(currentYaw + sign * angle) > trackData.maxHeadingDrift)
        {
            sign = -sign;
        }

        float arcLength = radius * angle * Mathf.Deg2Rad;
        knotCount = Mathf.Max(2, Mathf.CeilToInt(arcLength / step));
        yawPerKnot = sign * angle / knotCount;
    }

    private SectionType PickSection(System.Random rng)
    {
        float total = trackData.straightWeight + trackData.bendWeight + trackData.sharpTurnWeight;
        float roll = RandomRange(rng, 0f, total);

        if (roll < trackData.straightWeight) return SectionType.Straight;
        if (roll < trackData.straightWeight + trackData.bendWeight) return SectionType.Bend;
        return SectionType.Sharp;
    }

    private static void AppendArc(List<Vector3> points, float startYaw, float yawPerKnot, int knotCount, float step)
    {
        Vector3 position = points[points.Count - 1];
        float heading = startYaw;

        for (int i = 0; i < knotCount; i++)
        {
            // Move along the heading at the middle of the step so arcs come out at the right radius.
            float mid = heading + yawPerKnot * 0.5f;
            position += HeadingToDirection(mid) * step;
            points.Add(position);
            heading += yawPerKnot;
        }
    }

    private static bool IsClear(List<Vector3> points, int startIndex, int skip, float clearance)
    {
        float minSqr = clearance * clearance;

        for (int n = startIndex; n < points.Count; n++)
        {
            for (int m = 0; m < n - skip; m++)
            {
                if ((points[n] - points[m]).sqrMagnitude < minSqr)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Vector3 HeadingToDirection(float yawDegrees)
    {
        float rad = yawDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    private static float RandomRange(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    private void BuildSpline(List<Vector3> points)
    {
        Spline spline = container.Spline;
        spline.Clear();

        foreach (Vector3 point in points)
        {
            spline.Add(new BezierKnot(point), TangentMode.AutoSmooth);
        }

        spline.Closed = false;
    }

    // ---------------------------------------------------------------- Road mesh

    private void BuildRoadMesh()
    {
        Spline spline = container.Spline;
        float halfWidth = trackData.roadWidth * 0.5f;
        int count = Mathf.Max(2, Mathf.CeilToInt(TrackLength / trackData.sampleSpacing) + 1);

        Vector3[] vertices = new Vector3[count * 2];
        Vector3[] normals = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        int[] triangles = new int[(count - 1) * 6];

        Vector3 previousPosition = Vector3.zero;
        Vector3 lastForward = Vector3.forward;
        float distance = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            SplineUtility.Evaluate(spline, t, out float3 p, out float3 tangent, out _);

            Vector3 position = p;
            Vector3 forward = new Vector3(tangent.x, 0f, tangent.z);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : lastForward;
            lastForward = forward;

            if (i > 0)
            {
                distance += Vector3.Distance(position, previousPosition);
            }
            previousPosition = position;

            // Unity is left-handed: Cross(up, forward) points right.
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            int v = i * 2;
            vertices[v] = position - right * halfWidth;
            vertices[v + 1] = position + right * halfWidth;
            normals[v] = Vector3.up;
            normals[v + 1] = Vector3.up;

            float vCoord = distance / trackData.uvTileLength;
            uvs[v] = new Vector2(0f, vCoord);
            uvs[v + 1] = new Vector2(1f, vCoord);

            if (i < count - 1)
            {
                int tri = i * 6;
                triangles[tri] = v;
                triangles[tri + 1] = v + 2;
                triangles[tri + 2] = v + 1;
                triangles[tri + 3] = v + 1;
                triangles[tri + 4] = v + 2;
                triangles[tri + 5] = v + 3;
            }
        }

        roadMesh.Clear();
        roadMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        roadMesh.vertices = vertices;
        roadMesh.normals = normals;
        roadMesh.uv = uvs;
        roadMesh.triangles = triangles;
        roadMesh.RecalculateBounds();

        meshFilter.sharedMesh = roadMesh;

        if (trackData.roadMaterial != null)
        {
            meshRenderer.sharedMaterial = trackData.roadMaterial;
        }
        else
        {
            Debug.LogWarning($"{name}: TrackData has no road material, the road will render pink.", this);
        }

        // Bake the collider once per generation, never per frame.
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = roadMesh;
    }

    // ---------------------------------------------------------------- Gates

    private void SpawnGates()
    {
        ClearGates();

        if (trackData.gatePrefab == null || trackData.gateSpacing <= 0f)
        {
            return;
        }

        if (gatesRoot == null)
        {
            Transform existing = transform.Find("Gates");
            if (existing != null)
            {
                gatesRoot = existing;
            }
            else
            {
                GameObject root = new GameObject("Gates");
                root.transform.SetParent(transform, false);
                gatesRoot = root.transform;
            }
        }

        for (float d = trackData.firstGateDistance; d < TrackLength - 10f; d += trackData.gateSpacing)
        {
            SampleAtT(d / TrackLength, out Vector3 position, out Vector3 forward);
            Instantiate(trackData.gatePrefab, position, Quaternion.LookRotation(forward, Vector3.up), gatesRoot);
        }
    }

    private void ClearGates()
    {
        if (gatesRoot == null)
        {
            gatesRoot = transform.Find("Gates");
        }

        if (gatesRoot == null)
        {
            return;
        }

        for (int i = gatesRoot.childCount - 1; i >= 0; i--)
        {
            DestroySafe(gatesRoot.GetChild(i).gameObject);
        }
    }

    // ---------------------------------------------------------------- Path API (for enemies)

    /// <summary>World position and flat forward direction at normalised position t (0 = start, 1 = end).</summary>
    public void SampleAtT(float t, out Vector3 position, out Vector3 forward)
    {
        EnsureComponents();

        SplineUtility.Evaluate(container.Spline, Mathf.Clamp01(t), out float3 p, out float3 tangent, out _);

        position = transform.TransformPoint((Vector3)p);

        Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
        if (flat.sqrMagnitude < 1e-6f)
        {
            flat = Vector3.forward;
        }
        forward = transform.TransformDirection(flat.normalized);
    }

    /// <summary>Normalised position (0 to 1) of the closest point on the track to a world position.</summary>
    public float GetNearestT(Vector3 worldPosition)
    {
        EnsureComponents();

        Vector3 local = transform.InverseTransformPoint(worldPosition);
        SplineUtility.GetNearestPoint(container.Spline, (float3)local, out _, out float t, 16, 3);
        return t;
    }

    /// <summary>The t value a number of metres further along the track (approximate, clamped to the end).</summary>
    public float GetTAhead(float t, float metres)
    {
        return Mathf.Clamp01(t + metres / Mathf.Max(TrackLength, 0.01f));
    }

    /// <summary>World point a number of metres ahead of t, handy as a chase target.</summary>
    public Vector3 GetPointAhead(float t, float metres)
    {
        SampleAtT(GetTAhead(t, metres), out Vector3 position, out _);
        return position;
    }

    private static void DestroySafe(Object target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}