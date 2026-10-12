using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Puts the player's car and the enemy cars on the track at the start.
///
/// It waits until TrackSampler has been built, so it doesn't matter whether it starts before or after
/// TrackMaker. If the track is rebuilt later (a new Build()), everything is placed again.
/// Call SpawnAll() yourself to restart a run.
///
/// Player: assign a scene car to playerCar (it gets moved to the start), or a prefab to playerPrefab
/// (it gets instantiated there). Enemies: assign an enemyPrefab and a count, or put scene enemies in
/// sceneEnemies. Enemies are wired up to the player automatically. So are UIManager (speedometer)
/// and the vegetation scatterer (which uses the car to decide which vegetation to draw).
/// </summary>
public class CarSpawner : MonoBehaviour
{
    [Header("Track")]
    public TrackSampler sampler;

    [Header("Player")]
    [Tooltip("Optional. Instantiated at the start if set; otherwise the scene car below is moved there.")]
    public GameObject playerPrefab;
    public GameObject playerCar;
    [Tooltip("Metres along the track. Raised automatically so every enemy fits behind the player.")]
    public float playerStartDistance = 40f;
    public float playerLateralOffset = 0f;
    [Tooltip("Height above the road, so the wheels and suspension start clear of the ground.")]
    public float spawnHeight = 0.6f;
    [Tooltip("Fires after the player is placed. Use it to point the camera at the car when it comes from a prefab.")]
    public UnityEvent<Transform> onPlayerSpawned;

    [Header("Scene objects that follow the player")]
    [Tooltip("Gets the player car as its focus. Optional: if empty, the spawner looks for one in the scene.")]
    public VegetationScatterer vegetation;

    [Header("Enemies")]
    [Tooltip("Optional. Instantiated enemyCount times. If empty, the scene enemies below are used.")]
    public EnemyCarController enemyPrefab;
    public int enemyCount = 1;
    public EnemyCarController[] sceneEnemies;
    [Tooltip("Extra metres between each enemy and the one in front of it.")]
    public float enemySpacing = 18f;
    [Tooltip("Enemies alternate left and right of the road centre by this much.")]
    public float laneSpacing = 2.2f;

    [Header("Timing")]
    public bool spawnOnStart = true;
    public bool respawnWhenTrackRebuilt = true;

    public Transform PlayerRoot { get { return playerRoot; } }
    public IReadOnlyList<EnemyCarController> Enemies { get { return enemies; } }

    Transform playerRoot;
    Rigidbody playerBody;
    TrackProgress playerProgress;
    PlayerHealth playerHealth;

    readonly List<EnemyCarController> enemies = new List<EnemyCarController>();
    int spawnedVersion = -1;

    IEnumerator Start()
    {
        if (!spawnOnStart)
            yield break;

        while (sampler == null || !sampler.IsBuilt)
            yield return null;

        SpawnAll();
    }

    void Update()
    {
        if (respawnWhenTrackRebuilt && spawnedVersion >= 0 && sampler != null && sampler.IsBuilt && sampler.Version != spawnedVersion)
            SpawnAll();
    }

    [ContextMenu("Spawn all (play mode)")]
    public void SpawnAll()
    {
        if (sampler == null || !sampler.IsBuilt)
        {
            Debug.LogWarning("CarSpawner: the track isn't built yet. Call sampler.Build() first.", this);
            return;
        }

        if (!FindPlayer())
            return;

        ConfigureEnemies();

        // Make room behind the player for every enemy.
        float farthestGap = 0f;
        for (int i = 0; i < enemies.Count; i++)
            farthestGap = Mathf.Max(farthestGap, enemies[i].StartGap);

        float playerDistance = Mathf.Max(playerStartDistance, farthestGap + 5f);
        playerDistance = Mathf.Min(playerDistance, sampler.Length - 5f);

        PlacePlayer(playerDistance);

        for (int i = 0; i < enemies.Count; i++)
            enemies[i].SpawnAt(Mathf.Max(0f, playerDistance - enemies[i].StartGap));

        spawnedVersion = sampler.Version;

        HandPlayerToSceneObjects();
        onPlayerSpawned?.Invoke(playerRoot);
    }

    // Scene objects can't hold a reference to a car that is spawned at runtime, so the spawner hands it over.
    void HandPlayerToSceneObjects()
    {
        // UIManager is a scene singleton, so it may not exist in every scene.
        if (UIManager.Instance != null)
            UIManager.Instance.SetPlayerCar(playerRoot);

        // The scatterer switches vegetation on and off by distance from its focus.
        if (vegetation == null)
            vegetation = FindFirstObjectByType<VegetationScatterer>();

        if (vegetation != null)
            vegetation.focus = playerRoot;
    }

    // ---------- Player ----------

    bool FindPlayer()
    {
        if (playerRoot != null)
            return true;

        GameObject car = playerCar;
        if (playerPrefab != null)
        {
            car = Instantiate(playerPrefab);
            playerCar = car;
        }

        if (car == null)
        {
            Debug.LogError("CarSpawner: assign playerCar (a scene car) or playerPrefab.", this);
            return false;
        }

        playerRoot = car.transform;
        playerBody = car.GetComponentInChildren<Rigidbody>();
        playerProgress = car.GetComponentInChildren<TrackProgress>();
        playerHealth = car.GetComponentInChildren<PlayerHealth>();

        if (playerProgress == null)
        {
            Debug.LogError("CarSpawner: the player car needs a TrackProgress component, because the enemies use it to find the player.", car);
            playerRoot = null;
            return false;
        }

        return true;
    }

    void PlacePlayer(float distance)
    {
        sampler.Sample(distance, out Vector3 centre, out Vector3 forward);

        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
        if (flatForward.sqrMagnitude < 1e-6f)
            flatForward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, flatForward.normalized);

        Vector3 position = centre + right * playerLateralOffset + Vector3.up * spawnHeight;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        // Stop any leftover motion from a previous run before moving the car.
        if (playerBody != null && !playerBody.isKinematic)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }

        playerRoot.SetPositionAndRotation(position, rotation);

        if (playerBody != null)
        {
            playerBody.position = position;
            playerBody.rotation = rotation;
        }

        Physics.SyncTransforms();

        playerProgress.sampler = sampler;
        playerProgress.ResetProgress();

        if (playerHealth != null)
            playerHealth.ResetHealth();
    }

    // ---------- Enemies ----------

    void ConfigureEnemies()
    {
        if (enemies.Count == 0)
        {
            if (enemyPrefab != null)
            {
                for (int i = 0; i < Mathf.Max(0, enemyCount); i++)
                    enemies.Add(Instantiate(enemyPrefab));
            }
            else if (sceneEnemies != null)
            {
                for (int i = 0; i < sceneEnemies.Length; i++)
                {
                    if (sceneEnemies[i] != null)
                        enemies.Add(sceneEnemies[i]);
                }
            }

            if (enemies.Count == 0)
                Debug.LogWarning("CarSpawner: no enemies to spawn. Assign enemyPrefab or sceneEnemies.", this);
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyCarController enemy = enemies[i];

            enemy.sampler = sampler;
            enemy.playerProgress = playerProgress;
            enemy.playerBody = playerBody;

            // Lead enemy sits straight behind the player; the rest fan out behind it.
            enemy.slotOffset = i * enemySpacing;
            enemy.laneOffset = i == 0 ? 0f : ((i % 2 == 1) ? -1f : 1f) * laneSpacing * ((i + 1) / 2);
            enemy.phaseOffset = Random.value * Mathf.PI * 2f;

            EnemyGun gun = enemy.GetComponentInChildren<EnemyGun>();
            if (gun != null)
            {
                gun.car = enemy;
                gun.target = playerRoot;
                gun.targetHealth = playerHealth;
                gun.targetBody = playerBody;
            }
        }
    }
}