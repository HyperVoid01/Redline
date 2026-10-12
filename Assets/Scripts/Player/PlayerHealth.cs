using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on the ROOT of the player car (the same object as its Rigidbody),
/// so bullets that hit any collider in the car find it via GetComponentInParent.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;

    [Tooltip("Current health, max health. Hook a health bar up to this.")]
    public UnityEvent<float, float> onHealthChanged;
    public UnityEvent onDied;

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public float Normalized { get { return maxHealth > 0f ? Current / maxHealth : 0f; } }

    void Awake()
    {
        ResetHealth();
    }

    public void ResetHealth()
    {
        Current = maxHealth;
        IsDead = false;
        onHealthChanged?.Invoke(Current, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        Current = Mathf.Max(0f, Current - amount);
        onHealthChanged?.Invoke(Current, maxHealth);

        if (Current <= 0f)
            Die();
    }

    void Die()
    {
        IsDead = true;
        onDied?.Invoke();

        // If your EndGame takes parameters, or the singleton accessor is named differently, change this one line.
        if (GameManager.Instance != null && !GameManager.Instance.IsGameOver)
            GameManager.Instance.EndGame();
    }
}
