using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on the ROOT of the player car (the same object as its Rigidbody),
/// so bullets that hit any collider in the car find it via GetComponentInParent.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;

    [Tooltip("Makes car parts fall off as health drops. Found automatically on this car if left empty.")]
    public CarPartDetacher partDetacher;

    [Tooltip("Current health, max health. Hook a health bar up to this.")]
    public UnityEvent<float, float> onHealthChanged;
    public UnityEvent onDied;
    public ParticleSystem deathParticles;

    [Header("Damaged Particles")]
    [Tooltip("Looping smoke / fire. Plays once health falls to the threshold below. Turn Play On Awake off on it.")]
    public ParticleSystem damagedParticles;
    [Tooltip("Health fraction at or below which the damaged particles start. 0.4 = at 40% health.")]
    [Range(0f, 1f)] public float damagedThreshold = 0.4f;

    [Header("Death Shake")]
    [Tooltip("Camera shake strength on death, 0 to 1. Applies to the chase camera and the interior camera.")]
    [Range(0f, 1f)] public float deathShake = 1f;

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public float Normalized { get { return maxHealth > 0f ? Current / maxHealth : 0f; } }

    void Awake()
    {
        if (partDetacher == null)
            partDetacher = GetComponentInChildren<CarPartDetacher>();

        ResetHealth();
    }

    public void ResetHealth()
    {
        Current = maxHealth;
        IsDead = false;
        onHealthChanged?.Invoke(Current, maxHealth);

        // Put every fallen part back on the car.
        if (partDetacher != null)
            partDetacher.RestoreParts();

        // Healthy again: clear the smoke straight away.
        if (damagedParticles != null)
            damagedParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        Current = Mathf.Max(0f, Current - amount);
        onHealthChanged?.Invoke(Current, maxHealth);

        if (UIManager.Instance != null)
            UIManager.Instance.UpdateHealth(Current, maxHealth);

        // Knock off any parts whose health threshold has been reached.
        if (partDetacher != null)
            partDetacher.UpdateFromHealth(Current, maxHealth);

        UpdateDamagedParticles();

        if (Current <= 0f)
            Die();
    }

    void UpdateDamagedParticles()
    {
        if (damagedParticles == null)
            return;

        bool shouldPlay = !IsDead && Current > 0f && Normalized <= damagedThreshold;

        if (shouldPlay && !damagedParticles.isPlaying)
        {
            // Make sure it keeps going even if Looping was left off on the system.
            ParticleSystem.MainModule main = damagedParticles.main;
            main.loop = true;

            damagedParticles.Play();
        }
        else if (!shouldPlay && damagedParticles.isPlaying)
        {
            // Stop emitting but let the particles already in the air fade out.
            damagedParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void ShakeCameras()
    {
        if (deathShake <= 0f)
            return;

        // The chase camera detaches from the car at start, so it has to be found in the scene.
        CameraController chase = FindFirstObjectByType<CameraController>();
        if (chase != null)
            chase.AddShake(deathShake);

        // The interior camera is a child of the car.
        InteriorCameraController interior = GetComponentInChildren<InteriorCameraController>(true);
        if (interior != null)
            interior.AddShake(deathShake);
    }

    void Die()
    {
        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound(SoundID.Explosion, transform.position);

        IsDead = true;
        onDied?.Invoke();

        // The explosion takes over from the smoke.
        UpdateDamagedParticles();

        if (deathParticles != null)
            deathParticles.Play();

        ShakeCameras();

        // If your EndGame takes parameters, or the singleton accessor is named differently, change this one line.
        if (GameManager.Instance != null && !GameManager.Instance.IsGameOver)
            GameManager.Instance.EndGame();
    }
}