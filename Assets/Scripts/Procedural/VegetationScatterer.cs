using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum VegetationMode
{
    Prefab,     // trees, bushes, ferns: real GameObjects (can have colliders and LODs)
    Instanced   // grass: GPU-instanced mesh, no GameObjects
}

[System.Serializable]
public class VegetationLayer
{
    public string name = "Trees";
    public VegetationMode mode = VegetationMode.Prefab;

    [Header("Prefab mode")]
    public GameObject[] prefabs;

    [Header("Instanced mode (material needs 'Enable GPU Instancing' ticked)")]
    public Mesh mesh;
    public Material material;
    public float drawDistance = 80f;

    [Header("Placement")]
    [Tooltip("Instances per 100 m of track, both sides combined.")]
    public float perHundredMeters = 20f;
    [Tooltip("Distance from the road edge.")]
    public float minDistFromRoad = 3f;
    public float maxDistFromRoad = 50f;
    [Tooltip("1 = even spread. Above 1 clusters towards the road.")]
    public float nearRoadBias = 1f;
    public float maxSlope = 35f;

    [Header("Variation")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.3f);
    public float yOffset = 0f;
    public bool alignToSurface = false;
    public float randomTilt = 0f;
}

/// <summary>
/// Scatters vegetation on the land strip. Works in chunks along the track:
/// distant chunks are switched off (prefabs) or not drawn (instanced grass).
/// </summary>
public class VegetationScatterer : MonoBehaviour
{
    public TerrainStripGenerator terrain;
    public Transform focus;                  // the player car; falls back to Camera.main
    public List<VegetationLayer> layers = new List<VegetationLayer>();

    [Header("Scatter")]
    public bool useRandomSeed = true;
    public int seed = 12345;
    public float rayHeight = 40f;
    public bool scatterOnStart = true;

    [Header("Chunking")]
    public float chunkLength = 40f;
    public float prefabDrawDistance = 200f;

    class Chunk
    {
        public GameObject root;
        public Vector3 sum;
        public int count;
        public readonly Dictionary<int, List<Matrix4x4>> instanced = new Dictionary<int, List<Matrix4x4>>();
        public Vector3 Center => count > 0 ? sum / count : root.transform.position;
    }

    readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
    readonly Matrix4x4[] batch = new Matrix4x4[1023];

    void Start()
    {
        if (scatterOnStart)
            Scatter();
    }

    [ContextMenu("Scatter (play mode)")]
    public void Scatter()
    {
        if (terrain == null)
        {
            Debug.LogError("VegetationScatterer: assign the TerrainStripGenerator.", this);
            return;
        }

        Clear();
        terrain.EnsureGenerated();

        if (!terrain.HasGenerated)
        {
            Debug.LogWarning("VegetationScatterer: the terrain hasn't generated, so nothing was scattered. " +
                             "Generate the terrain after the track is built, then call Scatter().", this);
            return;
        }

        Physics.SyncTransforms();

        System.Random rng = new System.Random(useRandomSeed ? Random.Range(0, int.MaxValue) : seed);

        float length = terrain.TrackLength;
        float roadHalf = terrain.roadWidth * 0.5f;

        for (int li = 0; li < layers.Count; li++)
        {
            VegetationLayer layer = layers[li];
            if (!LayerIsValid(layer))
                continue;

            int count = Mathf.RoundToInt(length / 100f * layer.perHundredMeters);

            for (int n = 0; n < count; n++)
            {
                float d = (float)rng.NextDouble() * length;
                terrain.SampleCenter(d, out Vector3 center, out Vector3 right);

                float side = rng.Next(2) == 0 ? -1f : 1f;
                float u = Mathf.Pow((float)rng.NextDouble(), Mathf.Max(0.01f, layer.nearRoadBias));
                float lateral = Mathf.Lerp(layer.minDistFromRoad, layer.maxDistFromRoad, u);

                Vector3 xz = center + right * (side * (roadHalf + lateral));
                Ray ray = new Ray(new Vector3(xz.x, center.y + rayHeight, xz.z), Vector3.down);

                // Collider.Raycast only tests the terrain, so roads and barriers can't be hit by mistake.
                if (!terrain.Collider.Raycast(ray, out RaycastHit hit, rayHeight * 3f))
                    continue;

                if (Vector3.Angle(hit.normal, Vector3.up) > layer.maxSlope)
                    continue;

                // Where the track bends back on itself, this spot may be near a different
                // road section. Reject anything too close to ANY road.
                if (terrain.DistanceToRoad(hit.point) < roadHalf + layer.minDistFromRoad)
                    continue;

                float scale = Mathf.Lerp(layer.scaleRange.x, layer.scaleRange.y, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;
                float tiltX = ((float)rng.NextDouble() * 2f - 1f) * layer.randomTilt;
                float tiltZ = ((float)rng.NextDouble() * 2f - 1f) * layer.randomTilt;

                Quaternion surface = layer.alignToSurface
                    ? Quaternion.FromToRotation(Vector3.up, hit.normal)
                    : Quaternion.identity;
                Quaternion rotation = surface * Quaternion.Euler(tiltX, yaw, tiltZ);

                Vector3 position = hit.point + Vector3.up * layer.yOffset;

                Chunk chunk = GetChunk(Mathf.FloorToInt(d / chunkLength));
                chunk.sum += position;
                chunk.count++;

                if (layer.mode == VegetationMode.Prefab)
                {
                    GameObject prefab = layer.prefabs[rng.Next(layer.prefabs.Length)];
                    GameObject go = Instantiate(prefab, position, rotation, chunk.root.transform);
                    go.transform.localScale = prefab.transform.localScale * scale;
                }
                else
                {
                    if (!chunk.instanced.TryGetValue(li, out List<Matrix4x4> list))
                    {
                        list = new List<Matrix4x4>();
                        chunk.instanced[li] = list;
                    }
                    list.Add(Matrix4x4.TRS(position, rotation, Vector3.one * scale));
                }
            }
        }
    }

    public void Clear()
    {
        foreach (Chunk chunk in chunks.Values)
        {
            if (chunk.root != null)
                Destroy(chunk.root);
        }
        chunks.Clear();
    }

    void Update()
    {
        if (chunks.Count == 0)
            return;

        Vector3 focusPos;
        if (focus != null) focusPos = focus.position;
        else if (Camera.main != null) focusPos = Camera.main.transform.position;
        else return;

        foreach (Chunk chunk in chunks.Values)
        {
            float dist = Vector3.Distance(focusPos, chunk.Center);

            bool prefabsOn = dist < prefabDrawDistance;
            if (chunk.root.activeSelf != prefabsOn)
                chunk.root.SetActive(prefabsOn);

            foreach (KeyValuePair<int, List<Matrix4x4>> pair in chunk.instanced)
            {
                VegetationLayer layer = layers[pair.Key];
                if (dist > layer.drawDistance)
                    continue;

                List<Matrix4x4> list = pair.Value;
                for (int start = 0; start < list.Count; start += batch.Length)
                {
                    int n = Mathf.Min(batch.Length, list.Count - start);
                    list.CopyTo(start, batch, 0, n);
                    Graphics.DrawMeshInstanced(layer.mesh, 0, layer.material, batch, n,
                        null, ShadowCastingMode.Off, false);
                }
            }
        }
    }

    Chunk GetChunk(int index)
    {
        if (!chunks.TryGetValue(index, out Chunk chunk))
        {
            chunk = new Chunk();
            chunk.root = new GameObject("VegetationChunk " + index);
            chunk.root.transform.SetParent(transform, false);
            chunks[index] = chunk;
        }
        return chunk;
    }

    bool LayerIsValid(VegetationLayer layer)
    {
        if (layer.mode == VegetationMode.Prefab)
        {
            if (layer.prefabs != null && layer.prefabs.Length > 0)
                return true;
            Debug.LogWarning("Vegetation layer '" + layer.name + "' has no prefabs.", this);
            return false;
        }

        if (layer.mesh != null && layer.material != null)
            return true;
        Debug.LogWarning("Vegetation layer '" + layer.name + "' needs a mesh and material.", this);
        return false;
    }
}