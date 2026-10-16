using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

/// <summary>
/// Builds a strip of land down both sides of a spline road.
/// Replaces the flat grid MeshGenerator: the grid can't follow a winding road,
/// so this builds rows along the spline and columns outward from the road edge.
/// Land height follows the road's height, so hills in the track carry over.
///
/// Where the track bends back near itself, every land vertex checks its distance to
/// the NEAREST road section (not just its own). Land is flattened and pushed under
/// any road it would otherwise cover, and strips that fall inside another section's
/// territory are dropped, so strips no longer pile on top of each other or the road.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainStripGenerator : MonoBehaviour
{
    [Header("Track")]
    public SplineContainer track;          // keep this container at scale 1
    public int splineIndex = 0;
    public float roadWidth = 8f;

    [Header("Land Strip")]
    public float landWidth = 60f;          // how far land extends from each road edge
    public float edgeOverlap = 1f;         // tucks the land edge under the road to hide gaps
    public float edgeDrop = 0.15f;         // land sits slightly below the road to avoid z-fighting
    public float rowSpacing = 2f;          // metres between rows along the road
    public int sideColumns = 20;           // columns per side, outward from the road

    [Header("Shape")]
    public float hillHeight = 6f;          // replaces yMultiplier
    public float noiseScale = 0.03f;       // replaces sizeSpread (world-space, so much smaller)
    public float flatBlendDistance = 8f;   // land is flat near ANY road and rises to full hills by this distance
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Look")]
    public Gradient gradient;              // vertex colour by height (needs a shader that reads vertex colours)
    public float uvScale = 0.1f;           // world-space UVs so a ground texture tiles

    [HideInInspector] public float minTerrainHeight;
    [HideInInspector] public float maxTerrainHeight;

    [Tooltip("Turn this off if TrackMaker builds the spline at runtime, and call Generate() once the track exists.")]
    public bool generateOnStart = true;

    [Header("Collider (coarser than the visible mesh)")]
    [Tooltip("The physics mesh uses every Nth row along the road. 1 = same as the visible mesh. 2 = a quarter of the triangles (with the column step).")]
    [Min(1)] public int colliderRowStep = 2;
    [Tooltip("The physics mesh uses every Nth column across the land. The outermost column is always kept.")]
    [Min(1)] public int colliderColumnStep = 2;

    public float TrackLength { get; private set; }
    public MeshCollider Collider { get; private set; }
    public bool HasGenerated { get; private set; }

    Mesh mesh;
    Mesh colliderMesh;
    float noiseOffsetX, noiseOffsetZ;
    float savedRoadHalf;

    // Kept after generation so the scatterer can ask "how far is this point from any road?"
    Vector3[] roadCenters;
    Dictionary<Vector2Int, List<int>> roadGrid;
    float gridCellSize;

    void Start()
    {
        if (generateOnStart)
            EnsureGenerated();
    }

    public void EnsureGenerated()
    {
        if (!HasGenerated)
            Generate();
    }

    /// <summary>Samples the road centre at a distance along the spline.</summary>
    public void SampleCenter(float distance, out Vector3 position, out Vector3 flatRight)
    {
        Spline spline = track.Splines[splineIndex];
        float t = SplineUtility.GetNormalizedInterpolation(spline, distance, PathIndexUnit.Distance);
        track.Evaluate(splineIndex, t, out float3 p, out float3 tangent, out float3 up);

        position = p;

        Vector3 forward = new Vector3(tangent.x, 0f, tangent.z);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        forward.Normalize();

        // Unity is left-handed: Cross(up, forward) points to the right of travel.
        flatRight = Vector3.Cross(Vector3.up, forward);
    }

    /// <summary>Horizontal distance from a world point to the nearest road centreline (any section).</summary>
    public float DistanceToRoad(Vector3 worldPos)
    {
        if (!HasGenerated || roadGrid == null)
            return float.MaxValue;

        return NearestRoadDistance(worldPos.x, worldPos.z, out int _);
    }

    public void Generate()
    {
        Collider = GetComponent<MeshCollider>();
        HasGenerated = false;

        // ---- Validate the track before building anything ----
        if (track == null)
        {
            Debug.LogError("TerrainStripGenerator: assign the track SplineContainer.", this);
            return;
        }

        if (splineIndex < 0 || splineIndex >= track.Splines.Count)
        {
            Debug.LogError("TerrainStripGenerator: splineIndex " + splineIndex + " doesn't exist on the container.", this);
            return;
        }

        if (track.Splines[splineIndex].Count < 2)
        {
            Debug.LogWarning("TerrainStripGenerator: the spline has fewer than 2 knots. " +
                             "If TrackMaker builds it at runtime, untick generateOnStart and call Generate() after TrackMaker finishes.", this);
            return;
        }

        if (rowSpacing < 0.1f || sideColumns < 1)
        {
            Debug.LogError("TerrainStripGenerator: rowSpacing must be at least 0.1 and sideColumns at least 1.", this);
            return;
        }

        TrackLength = track.CalculateLength(splineIndex);

        if (float.IsNaN(TrackLength) || TrackLength < rowSpacing * 2f)
        {
            Debug.LogWarning("TerrainStripGenerator: the track is too short or not built yet (length " + TrackLength + "). " +
                             "Untick generateOnStart and call Generate() after TrackMaker finishes.", this);
            return;
        }

        System.Random rng = new System.Random(useRandomSeed ? UnityEngine.Random.Range(0, int.MaxValue) : seed);
        noiseOffsetX = (float)rng.NextDouble() * 9999f;
        noiseOffsetZ = (float)rng.NextDouble() * 9999f;

        int rows = Mathf.Max(2, Mathf.CeilToInt(TrackLength / rowSpacing) + 1);
        int cols = sideColumns + 1;
        int vertsPerSide = rows * cols;

        // Sample the road centre once per row.
        Vector3[] centers = new Vector3[rows];
        Vector3[] rights = new Vector3[rows];
        for (int r = 0; r < rows; r++)
        {
            float d = TrackLength * r / (rows - 1);
            SampleCenter(d, out centers[r], out rights[r]);

            if (!IsFinite(centers[r]) || !IsFinite(rights[r]))
            {
                Debug.LogError("TerrainStripGenerator: the spline returned an invalid point at distance " + d +
                               ". Check the track for NaN knots or a zero-length spline.", this);
                return;
            }
        }

        float roadHalf = roadWidth * 0.5f;
        savedRoadHalf = roadHalf;
        float totalWidth = edgeOverlap + landWidth;
        float colStep = totalWidth / sideColumns;

        // Spatial grid of road samples, so each vertex can find the nearest road quickly.
        roadCenters = centers;
        BuildRoadGrid(roadHalf + totalWidth);

        // A vertex is "owned" by its own row unless a different road sample is clearly closer.
        float ownershipTolerance = rowSpacing;

        Vector3[] vertices = new Vector3[vertsPerSide * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        float[] worldHeights = new float[vertices.Length];
        bool[] owned = new bool[vertices.Length];

        minTerrainHeight = float.MaxValue;
        maxTerrainHeight = float.MinValue;

        for (int s = 0; s < 2; s++)
        {
            float sign = s == 0 ? -1f : 1f;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // Distance from the road edge (negative = tucked under the road).
                    float fromEdge = -edgeOverlap + c * colStep;
                    float ownDist = roadHalf + fromEdge;

                    Vector3 world = centers[r] + rights[r] * (sign * ownDist);

                    // Distance to the nearest road of ANY section, and which row that is.
                    float nearestDist = NearestRoadDistance(world.x, world.z, out int nearestRow);

                    // Height is built from the nearest road, so land never rises over another road
                    // and overlapping strips agree on their height.
                    float edgeDist = Mathf.Max(0f, nearestDist - roadHalf);
                    float blend = Mathf.SmoothStep(0f, 1f, edgeDist / Mathf.Max(0.01f, flatBlendDistance));
                    float noise = Noise(world.x, world.z);                 // 0..1
                    world.y = centers[nearestRow].y - edgeDrop + (noise - 0.5f) * 2f * hillHeight * blend;

                    int i = s * vertsPerSide + r * cols + c;
                    vertices[i] = transform.InverseTransformPoint(world);
                    uvs[i] = new Vector2(world.x, world.z) * uvScale;
                    worldHeights[i] = world.y;
                    owned[i] = nearestDist >= ownDist - ownershipTolerance;

                    if (world.y > maxTerrainHeight) maxTerrainHeight = world.y;
                    if (world.y < minTerrainHeight) minTerrainHeight = world.y;
                }
            }
        }

        // Vertex colours need min/max, so they're a second pass.
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = gradient != null
                ? gradient.Evaluate(Mathf.InverseLerp(minTerrainHeight, maxTerrainHeight, worldHeights[i]))
                : Color.white;
        }

        // Triangles. The right side uses the same winding as your original grid;
        // the left side runs the opposite direction, so its winding is flipped.
        // Skipped: zero-area triangles (PhysX refuses to cook them) and triangles that sit
        // entirely inside another road section's territory.
        List<int> triangles = new List<int>((rows - 1) * (cols - 1) * 12);

        for (int s = 0; s < 2; s++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int i0 = s * vertsPerSide + r * cols + c;         // this row, this column
                    int i1 = i0 + cols;                               // next row, this column
                    int i2 = i0 + 1;                                  // this row, next column
                    int i3 = i1 + 1;                                  // next row, next column

                    if (s == 1)
                    {
                        AddTriangle(triangles, vertices, owned, i0, i1, i2);
                        AddTriangle(triangles, vertices, owned, i2, i1, i3);
                    }
                    else
                    {
                        AddTriangle(triangles, vertices, owned, i0, i2, i1);
                        AddTriangle(triangles, vertices, owned, i2, i3, i1);
                    }
                }
            }
        }

        if (triangles.Count == 0)
        {
            Debug.LogError("TerrainStripGenerator: no valid triangles were generated. Check roadWidth, landWidth and the track.", this);
            return;
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "TerrainStrip" };
            mesh.indexFormat = IndexFormat.UInt32;   // long tracks exceed 65k vertices
        }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        // The collider doesn't need the visible mesh's detail, and baking a smaller mesh is much faster.
        BuildColliderMesh(vertices, owned, rows, cols, vertsPerSide);

        Collider.sharedMesh = null;      // forces a rebake when regenerating
        Collider.sharedMesh = colliderMesh;

        HasGenerated = true;
    }

    // ---------- Coarse collider ----------

    // Keeps every Nth row and column of the same grid (always including the last of each),
    // so the physics surface follows the visible land with far fewer triangles.
    void BuildColliderMesh(Vector3[] vertices, bool[] owned, int rows, int cols, int vertsPerSide)
    {
        int rowStep = Mathf.Max(1, colliderRowStep);
        int colStep = Mathf.Max(1, colliderColumnStep);

        List<int> keptRows = new List<int>();
        for (int r = 0; r < rows; r += rowStep) keptRows.Add(r);
        if (keptRows[keptRows.Count - 1] != rows - 1) keptRows.Add(rows - 1);

        List<int> keptCols = new List<int>();
        for (int c = 0; c < cols; c += colStep) keptCols.Add(c);
        if (keptCols[keptCols.Count - 1] != cols - 1) keptCols.Add(cols - 1);

        int cRows = keptRows.Count;
        int cCols = keptCols.Count;
        int cPerSide = cRows * cCols;

        Vector3[] cVerts = new Vector3[cPerSide * 2];
        bool[] cOwned = new bool[cVerts.Length];

        for (int s = 0; s < 2; s++)
        {
            for (int r = 0; r < cRows; r++)
            {
                for (int c = 0; c < cCols; c++)
                {
                    int source = s * vertsPerSide + keptRows[r] * cols + keptCols[c];
                    int target = s * cPerSide + r * cCols + c;
                    cVerts[target] = vertices[source];
                    cOwned[target] = owned[source];
                }
            }
        }

        List<int> tris = new List<int>((cRows - 1) * (cCols - 1) * 12);

        for (int s = 0; s < 2; s++)
        {
            for (int r = 0; r < cRows - 1; r++)
            {
                for (int c = 0; c < cCols - 1; c++)
                {
                    int i0 = s * cPerSide + r * cCols + c;
                    int i1 = i0 + cCols;
                    int i2 = i0 + 1;
                    int i3 = i1 + 1;

                    // Same winding as the visible mesh.
                    if (s == 1)
                    {
                        AddTriangle(tris, cVerts, cOwned, i0, i1, i2);
                        AddTriangle(tris, cVerts, cOwned, i2, i1, i3);
                    }
                    else
                    {
                        AddTriangle(tris, cVerts, cOwned, i0, i2, i1);
                        AddTriangle(tris, cVerts, cOwned, i2, i3, i1);
                    }
                }
            }
        }

        if (colliderMesh == null)
        {
            colliderMesh = new Mesh { name = "TerrainStripCollider" };
            colliderMesh.indexFormat = IndexFormat.UInt32;
        }

        colliderMesh.Clear();
        colliderMesh.vertices = cVerts;
        colliderMesh.triangles = tris.ToArray();
        colliderMesh.RecalculateBounds();
    }

    /// <summary>
    /// Height of the visible land at a world X/Z, worked out from the same formula the mesh is built from.
    /// The collider is coarser than the mesh, so use this (not a raycast hit's Y) to place things on the ground.
    /// </summary>
    public bool SampleSurfaceHeight(float x, float z, out float height)
    {
        height = 0f;

        if (!HasGenerated || roadGrid == null)
            return false;

        float nearestDist = NearestRoadDistance(x, z, out int nearestRow);
        if (nearestDist == float.MaxValue)
            return false;

        float edgeDist = Mathf.Max(0f, nearestDist - savedRoadHalf);
        float blend = Mathf.SmoothStep(0f, 1f, edgeDist / Mathf.Max(0.01f, flatBlendDistance));
        float noise = Noise(x, z);
        height = roadCenters[nearestRow].y - edgeDrop + (noise - 0.5f) * 2f * hillHeight * blend;
        return true;
    }

    // ---------- Nearest-road lookup ----------

    void BuildRoadGrid(float cellSize)
    {
        gridCellSize = Mathf.Max(1f, cellSize);
        roadGrid = new Dictionary<Vector2Int, List<int>>();

        for (int r = 0; r < roadCenters.Length; r++)
        {
            Vector2Int key = new Vector2Int(
                Mathf.FloorToInt(roadCenters[r].x / gridCellSize),
                Mathf.FloorToInt(roadCenters[r].z / gridCellSize));

            if (!roadGrid.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                roadGrid[key] = list;
            }
            list.Add(r);
        }
    }

    float NearestRoadDistance(float x, float z, out int nearestRow)
    {
        int cx = Mathf.FloorToInt(x / gridCellSize);
        int cz = Mathf.FloorToInt(z / gridCellSize);

        float bestSqr = float.MaxValue;
        nearestRow = 0;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!roadGrid.TryGetValue(new Vector2Int(cx + dx, cz + dz), out List<int> list))
                    continue;

                for (int k = 0; k < list.Count; k++)
                {
                    int r = list[k];
                    float ddx = x - roadCenters[r].x;
                    float ddz = z - roadCenters[r].z;
                    float sqr = ddx * ddx + ddz * ddz;

                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        nearestRow = r;
                    }
                }
            }
        }

        return bestSqr == float.MaxValue ? float.MaxValue : Mathf.Sqrt(bestSqr);
    }

    // ---------- Helpers ----------

    static void AddTriangle(List<int> list, Vector3[] verts, bool[] owned, int a, int b, int c)
    {
        // Entirely inside another section's territory: that section's own strip covers it.
        if (!owned[a] && !owned[b] && !owned[c])
            return;

        Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (cross.sqrMagnitude < 1e-8f)
            return;

        list.Add(a);
        list.Add(b);
        list.Add(c);
    }

    static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    // Two octaves of Perlin noise in world space, so both sides of the road agree.
    float Noise(float x, float z)
    {
        float n1 = Mathf.PerlinNoise(x * noiseScale + noiseOffsetX, z * noiseScale + noiseOffsetZ);
        float n2 = Mathf.PerlinNoise(x * noiseScale * 2.3f + noiseOffsetX, z * noiseScale * 2.3f + noiseOffsetZ);
        return n1 * 0.7f + n2 * 0.3f;
    }
}