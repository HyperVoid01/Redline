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
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        Current = Mathf.Max(0f, Current - amount);
        onHealthChanged?.Invoke(Current, maxHealth);
        UIManager.Instance.UpdateHealth(Current, maxHealth);

        // Knock off any parts whose health threshold has been reached.
        if (partDetacher != null)
            partDetacher.UpdateFromHealth(Current, maxHealth);

        if (Current <= 0f)
            Die();
    }

    void Die()
    {
        SoundPlayer.Instance.PlaySound(SoundID.Explosion, transform.position);
        IsDead = true;
        onDied?.Invoke();
        deathParticles.Play();

        // If your EndGame takes parameters, or the singleton accessor is named differently, change this one line.
        if (GameManager.Instance != null && !GameManager.Instance.IsGameOver)
            GameManager.Instance.EndGame();
    }
}