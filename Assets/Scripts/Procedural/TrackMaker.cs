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

    // Remembers where a section started, so the last sections can be removed if the finish doesn't fit.
    private struct SectionMark
    {
        public int pointCount;
        public float yaw;
        public float generated;
    }

    [SerializeField] private TrackData trackData;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 12345;

    [Tooltip("Empty ground kept between the edges of two different parts of the road, including barriers. Raise it if tracks still crowd each other.")]
    [SerializeField] private float extraRoadGap = 12f;

    private const int MaxAttemptsPerSection = 8;
    private const int ElevationSmoothPasses = 4;

    private const string GatesRootName = "Gates";
    private const string BarriersRootName = "BarrierPieces";
    private const string LegacyBarriersName = "Barriers";       // from the old procedural barrier mesh
    private const string LegacyPlatformName = "FinishPlatform";  // from the old square finish platform
    private const string FinishRootName = "Finish";

    private SplineContainer container;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh roadMesh;
    private TrackSampler sampler;

    // Index of the spline knot where the finish line is. The runout continues after it.
    private int finishKnotIndex;

    public TerrainStripGenerator terrain;
    public VegetationScatterer scatterer;

    public bool IsGenerated { get; private set; }
    public int CurrentSeed { get; private set; }

    /// <summary>Full length of the spline, including the runout past the finish line.</summary>
    public float TrackLength { get; private set; }

    /// <summary>Normalised position (0 to 1) of the finish line. The road carries on past it.</summary>
    public float FinishT { get; private set; } = 1f;

    /// <summary>Approximate distance along the track to the finish line, in metres.</summary>
    public float FinishDistance => FinishT * TrackLength;

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

    /// <summary>World position of the finish line.</summary>
    public Vector3 FinishPosition
    {
        get { SampleAtT(FinishT, out Vector3 p, out _); return p; }
    }

    /// <summary>World position of the very end of the road, after the runout.</summary>
    public Vector3 EndPosition
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
        RemoveLegacyObjects();

        CurrentSeed = useRandomSeed ? System.Environment.TickCount : seed;
        System.Random rng = new System.Random(CurrentSeed);

        List<Vector3> points = GeneratePoints(rng);
        ApplyElevation(points, rng);
        BuildSpline(points);
        TrackLength = container.Spline.GetLength();

        // Where the finish line sits on the spline. Uses the same 0 to 1 scale as SampleAtT.
        FinishT = Mathf.Clamp01(SplineUtility.GetNormalizedInterpolation(
            container.Spline, finishKnotIndex, PathIndexUnit.Knot));

        BuildRoadMesh();
        SpawnBarriers();
        SpawnGates();
        SpawnFinish();

        IsGenerated = true;
        Debug.Log($"{name}: generated {TrackLength:0} m track (finish at {FinishDistance:0} m) with seed {CurrentSeed}.", this);
    }

    private void EnsureComponents()
    {
        if (container == null) container = GetComponent<SplineContainer>();
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
        if (sampler == null) sampler = GetComponent<TrackSampler>();

        if (roadMesh == null)
        {
            roadMesh = new Mesh { name = "Track Road" };
        }
    }

    /// <summary>
    /// Smallest allowed distance between the centre lines of two different parts of the road.
    /// It covers the road itself, the barriers on both sides, and the extra gap on top.
    /// </summary>
    private float GetClearance()
    {
        float barrierWidth = trackData.generateBarriers ? Mathf.Max(trackData.barrierSideOffset, 0f) * 2f : 0f;
        return trackData.roadWidth + barrierWidth + Mathf.Max(0f, extraRoadGap);
    }

    private List<Vector3> GeneratePoints(System.Random rng)
    {
        float step = trackData.knotSpacing;
        float clearance = GetClearance();

        // Points within this many knots of each other are neighbours along the road, not overlaps.
        int skip = Mathf.CeilToInt(clearance * 2f / step) + 1;

        List<Vector3> points = new List<Vector3> { Vector3.zero };
        List<SectionMark> marks = new List<SectionMark>();
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

                marks.Add(new SectionMark { pointCount = start, yaw = yaw, generated = generated });

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

        AppendFinish(points, marks, ref yaw, ref generated, step, skip, clearance);

        return points;
    }

    /// <summary>
    /// Adds the finish straight and the runout after it, and checks them like any other section.
    /// If they don't fit, the last section is removed and the finish is tried again from the one before.
    /// The finish line sits at the end of the finish straight. The runout carries on past it.
    /// </summary>
    private void AppendFinish(List<Vector3> points, List<SectionMark> marks, ref float yaw, ref float generated,
                              float step, int skip, float clearance)
    {
        int finishKnots = Mathf.Max(1, Mathf.CeilToInt(trackData.finishStraightLength / step));
        int runoutKnots = Mathf.CeilToInt(trackData.runoutLength / step);

        int removedSections = 0;

        while (true)
        {
            int start = points.Count;
            AppendArc(points, yaw, 0f, finishKnots + runoutKnots, step);

            bool clear = IsClear(points, start, skip, clearance);

            if (clear || marks.Count == 0)
            {
                if (!clear)
                {
                    Debug.LogWarning($"{name}: the finish straight overlaps the road and there is nothing left to remove.", this);
                }
                else if (removedSections > 0)
                {
                    Debug.Log($"{name}: removed {removedSections} section(s) at the end so the finish doesn't overlap the road.", this);
                }

                // The finish line is the last knot of the finish straight. Everything after it is runout.
                finishKnotIndex = points.Count - 1 - runoutKnots;
                return;
            }

            // Doesn't fit: drop the finish and the last section, then try again.
            points.RemoveRange(start, points.Count - start);

            SectionMark last = marks[marks.Count - 1];
            marks.RemoveAt(marks.Count - 1);
            points.RemoveRange(last.pointCount, points.Count - last.pointCount);
            yaw = last.yaw;
            generated = last.generated;
            removedSections++;
        }
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

    // ---------------------------------------------------------------- Elevation

    /// <summary>
    /// Adds hills to the flat path. Runs after the layout is decided, so the overlap check
    /// stays purely horizontal. The start straight stays flat, and the slope is limited
    /// and smoothed so the car can always drive it.
    /// </summary>
    private void ApplyElevation(List<Vector3> points, System.Random rng)
    {
        if (trackData.hillAmplitude <= 0.01f || points.Count < 3)
        {
            return;
        }

        int count = points.Count;
        float step = trackData.knotSpacing;
        float noiseOffset = RandomRange(rng, 0f, 1000f);
        float flatLength = trackData.startStraightLength;
        float easeLength = Mathf.Max(flatLength, 20f);
        float maxDelta = trackData.maxGradePercent * 0.01f * step;

        float[] heights = new float[count];

        for (int i = 0; i < count; i++)
        {
            float distance = i * step;
            float noise = Mathf.PerlinNoise(noiseOffset, distance / trackData.hillWavelength) * 2f - 1f;

            // Flat over the start straight, then eases into the hills.
            float ease = Mathf.SmoothStep(0f, 1f, (distance - flatLength) / easeLength);
            heights[i] = noise * trackData.hillAmplitude * ease;
        }

        // Limit the slope between neighbouring knots.
        for (int i = 1; i < count; i++)
        {
            heights[i] = Mathf.Clamp(heights[i], heights[i - 1] - maxDelta, heights[i - 1] + maxDelta);
        }

        // Smooth out the kinks the limiter leaves. Averaging never makes a slope steeper.
        float[] buffer = new float[count];
        for (int pass = 0; pass < ElevationSmoothPasses; pass++)
        {
            buffer[0] = heights[0];
            buffer[count - 1] = heights[count - 1];

            for (int i = 1; i < count - 1; i++)
            {
                buffer[i] = (heights[i - 1] + heights[i] + heights[i + 1]) / 3f;
            }

            (heights, buffer) = (buffer, heights);
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 p = points[i];
            points[i] = new Vector3(p.x, heights[i], p.z);
        }
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
            // The road is flat side to side (no banking), so right stays horizontal even on slopes.
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

        // Land and vegetation follow the whole spline, so they carry on through the runout too.
        terrain.Generate();
        sampler.Build();
        scatterer.Scatter();
    }

    // ---------------------------------------------------------------- Barriers

    private void SpawnBarriers()
    {
        ClearChildren(transform.Find(BarriersRootName));

        if (!trackData.generateBarriers)
        {
            return;
        }

        if (trackData.barrierPrefab == null)
        {
            Debug.LogWarning($"{name}: TrackData has no barrier prefab, so no barriers were placed.", this);
            return;
        }

        Transform root = GetOrCreateRoot(BarriersRootName);

        float length = trackData.barrierSegmentLength;
        float sideDistance = trackData.roadWidth * 0.5f + trackData.barrierSideOffset;
        int pieces = Mathf.FloorToInt(TrackLength / length);

        for (int side = -1; side <= 1; side += 2)
        {
            // The left side is turned around so the model faces the road from both sides.
            float yaw = trackData.barrierYawOffset + (side < 0 ? 180f : 0f);
            Quaternion modelOffset = Quaternion.Euler(0f, yaw, 0f);

            for (int i = 0; i < pieces; i++)
            {
                float tStart = i * length / TrackLength;
                float tEnd = (i + 1) * length / TrackLength;

                Vector3 start = EdgePoint(tStart, side, sideDistance);
                Vector3 end = EdgePoint(tEnd, side, sideDistance);

                // Each piece sits between two points on the edge, so it follows curves and slopes.
                Vector3 chord = end - start;
                if (chord.sqrMagnitude < 1e-4f)
                {
                    continue;
                }

                Vector3 centre = (start + end) * 0.5f + Vector3.up * trackData.barrierHeightOffset;
                Quaternion rotation = Quaternion.LookRotation(chord.normalized, Vector3.up) * modelOffset;

                Instantiate(trackData.barrierPrefab, centre, rotation, root);
            }
        }
    }

    private Vector3 EdgePoint(float t, int side, float sideDistance)
    {
        SampleAtT(t, out Vector3 position, out Vector3 forward);

        // Unity is left-handed: Cross(up, forward) points right.
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        return position + right * (side * sideDistance);
    }

    private void RemoveLegacyObjects()
    {
        // The old procedural barrier object, if it is still in the scene from the previous version.
        Transform legacy = transform.Find(LegacyBarriersName);
        if (legacy != null)
        {
            DestroySafe(legacy.gameObject);
        }

        // The old square finish platform and its FinishZone, if still in the scene.
        Transform platform = transform.Find(LegacyPlatformName);
        if (platform != null)
        {
            DestroySafe(platform.gameObject);
        }
    }

    // ---------------------------------------------------------------- Gates

    private void SpawnGates()
    {
        ClearChildren(transform.Find(GatesRootName));

        if (trackData.gatePrefab == null || trackData.gateSpacing <= 0f)
        {
            return;
        }

        Transform root = GetOrCreateRoot(GatesRootName);

        // Gates stop before the finish line, none in the runout.
        float lastGate = FinishDistance - 10f;

        for (float d = trackData.firstGateDistance; d < lastGate; d += trackData.gateSpacing)
        {
            SampleAtT(d / TrackLength, out Vector3 position, out Vector3 forward);
            Instantiate(trackData.gatePrefab, position, Quaternion.LookRotation(forward, Vector3.up), root);
        }
    }

    // ---------------------------------------------------------------- Finish

    private void SpawnFinish()
    {
        ClearChildren(transform.Find(FinishRootName));

        if (trackData.finishPrefab == null)
        {
            return;
        }

        Transform root = GetOrCreateRoot(FinishRootName);

        // On the road at the finish line, facing down the track.
        SampleAtT(FinishT, out Vector3 position, out Vector3 forward);
        GameObject finish = Instantiate(trackData.finishPrefab, position, Quaternion.LookRotation(forward, Vector3.up), root);

        if (finish.GetComponentInChildren<FinishTrigger>(true) == null)
        {
            Debug.LogWarning($"{name}: the finish prefab has no FinishTrigger, so crossing the line won't win the game. " +
                             "Add a trigger collider and a FinishTrigger to the prefab.", this);
        }
    }

    // ---------------------------------------------------------------- Helpers

    private Transform GetOrCreateRoot(string rootName)
    {
        Transform existing = transform.Find(rootName);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject(rootName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroySafe(root.GetChild(i).gameObject);
        }
    }

    // ---------------------------------------------------------------- Path API (for enemies)

    /// <summary>
    /// World position and forward direction at normalised position t (0 = start, 1 = end of the runout).
    /// Use FinishT for the finish line. Position includes height. Forward is kept horizontal.
    /// </summary>
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