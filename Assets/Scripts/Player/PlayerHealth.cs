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

    [Tooltip("Makes the controller vibrate on damage and on the explosion. Found automatically on this car if left empty.")]
    public CarRumble rumble;

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

    [Header("Game Over Timing")]
    [Tooltip("ON: the game over screen waits until the death particles have finished (found from the particle systems themselves). " +
             "OFF: it waits the Manual Delay below.")]
    public bool matchDeathParticles = true;
    [Tooltip("Extra seconds added after the particles finish, so the player gets a moment with the wreck.")]
    public float extraHold = 0.5f;
    [Tooltip("Used when Match Death Particles is off, or when there are no death particles assigned. Seconds, in real time.")]
    public float manualDelay = 2f;
    [Tooltip("The longest the game over screen is ever held back, in case a particle system is very long or looping.")]
    public float maxDelay = 6f;

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public float Normalized { get { return maxHealth > 0f ? Current / maxHealth : 0f; } }

    void Awake()
    {
        if (partDetacher == null)
            partDetacher = GetComponentInChildren<CarPartDetacher>();

        if (rumble == null)
            rumble = GetComponentInParent<CarRumble>();
        if (rumble == null)
            rumble = GetComponentInChildren<CarRumble>();

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

        if (rumble != null)
            rumble.ResetState();

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

        // A killing blow goes straight to the explosion rumble in Die().
        if (Current > 0f && rumble != null)
            rumble.OnDamaged(amount);

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

    // How long to wait, in real seconds, before the game over screen appears.
    float GetGameOverDelay()
    {
        if (!matchDeathParticles || deathParticles == null)
            return Mathf.Min(manualDelay, maxDelay);

        // Longest any of the death particle systems (including children) takes to fully finish.
        float longest = 0f;
        ParticleSystem[] systems = deathParticles.GetComponentsInChildren<ParticleSystem>(true);

        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;

            // A looping system never finishes, so it can't be matched. Fall back to the manual delay for it.
            if (main.loop)
            {
                longest = Mathf.Max(longest, manualDelay);
                continue;
            }

            float speed = Mathf.Max(0.01f, main.simulationSpeed);
            float total = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
            longest = Mathf.Max(longest, total / speed);
        }

        return Mathf.Min(longest + extraHold, maxDelay);
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

        if (rumble != null)
            rumble.OnExploded();

        ShakeCameras();

        // If your EndGame takes parameters, or the singleton accessor is named differently, change this one line.
        if (GameManager.Instance != null && !GameManager.Instance.IsGameOver)
            GameManager.Instance.EndGame(GetGameOverDelay());
    }
}