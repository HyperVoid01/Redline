using UnityEngine;

/// <summary>
/// Put this on a trigger collider inside the finish prefab, spanning the width of the road.
/// Crossing it wins the game and sets off the celebration particles.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FinishTrigger : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    [Header("Celebration")]
    [Tooltip("Confetti, sparks or streamers that play when the player crosses the line. Turn Play On Awake off on them. " +
             "If left empty, the particle systems under this object are used.")]
    [SerializeField] private ParticleSystem[] celebrationParticles;

    private bool triggered;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;

        // Inside the prefab, pick up every particle system on it.
        celebrationParticles = transform.root.GetComponentsInChildren<ParticleSystem>(true);
    }

    private void Awake()
    {
        if (celebrationParticles == null || celebrationParticles.Length == 0)
            celebrationParticles = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered)
        {
            return;
        }

        // Use the Rigidbody's object so any child collider on the car counts.
        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        if (!root.CompareTag(playerTag))
        {
            return;
        }

        triggered = true;

        PlayCelebration();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.WinGame();
        }
    }

    private void PlayCelebration()
    {
        if (celebrationParticles == null)
            return;

        for (int i = 0; i < celebrationParticles.Length; i++)
        {
            ParticleSystem system = celebrationParticles[i];
            if (system == null)
                continue;

            // Unscaled time, so the confetti still falls during the slow motion dip and once the game freezes.
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = true;

            system.gameObject.SetActive(true);
            system.Play(true);
        }
    }
}