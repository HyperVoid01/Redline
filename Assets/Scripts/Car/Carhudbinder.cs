using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on the player car prefab (the root is the safest place) and drag in the texts and health bar
/// from the car's canvases. When the car appears, the texts register themselves with the scene's UIManager,
/// and the health bar follows the car's PlayerHealth live.
///
/// This is needed because a scene object like UIManager can't hold a reference to something that is
/// spawned at runtime, but a spawned object can look up the scene's UIManager.
/// </summary>
public class CarHudBinder : MonoBehaviour
{
    [Tooltip("The timer text on the car's canvas. UIManager.UpdateTime writes to it.")]
    [SerializeField] private TMP_Text timerText;
    [Tooltip("Optional: a speed text on the car's canvas. UIManager.UpdateSpeed writes to it.")]
    [SerializeField] private TMP_Text speedText;

    [Header("Health")]
    [Tooltip("The health slider on the car's canvas. Updated live from PlayerHealth.")]
    [SerializeField] private Slider healthBar;
    [Tooltip("Leave empty to auto-find PlayerHealth on this car (this object or its parents/children).")]
    [SerializeField] private PlayerHealth playerHealth;

    private UIManager manager;
    private bool registered;
    private bool healthSubscribed;

    private void Awake()
    {
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            playerHealth = GetComponentInChildren<PlayerHealth>();
    }

    private void OnEnable()
    {
        Register();
        SubscribeHealth();
    }

    // If this object is enabled before UIManager.Awake has run (a car that is already in the scene),
    // OnEnable finds no UIManager. Every Awake has run by Start, so try again here.
    private void Start()
    {
        Register();
        SubscribeHealth();

        // PlayerHealth may have fired its first event before we subscribed, so pull the current value once.
        if (playerHealth != null)
            OnHealthChanged(playerHealth.Current, playerHealth.maxHealth);
    }

    private void OnDisable()
    {
        Unregister();
        UnsubscribeHealth();
    }

    // ---------- Health ----------

    private void SubscribeHealth()
    {
        if (healthSubscribed || playerHealth == null)
            return;

        playerHealth.onHealthChanged.AddListener(OnHealthChanged);
        healthSubscribed = true;
    }

    private void UnsubscribeHealth()
    {
        if (!healthSubscribed)
            return;

        if (playerHealth != null)
            playerHealth.onHealthChanged.RemoveListener(OnHealthChanged);

        healthSubscribed = false;
    }

    private void OnHealthChanged(float current, float max)
    {
        if (healthBar == null)
            return;

        healthBar.minValue = 0f;
        healthBar.maxValue = max;
        healthBar.value = current;
    }

    // ---------- UIManager texts ----------

    private void Register()
    {
        if (registered)
            return;

        manager = UIManager.Instance;
        if (manager == null)
            return;

        if (timerText != null)
            manager.RegisterTimerText(timerText);
        if (speedText != null)
            manager.RegisterSpeedText(speedText);

        registered = true;
    }

    private void Unregister()
    {
        if (!registered)
            return;

        // The manager may already be gone when the scene is torn down.
        if (manager != null)
        {
            if (timerText != null)
                manager.UnregisterTimerText(timerText);
            if (speedText != null)
                manager.UnregisterSpeedText(speedText);
        }

        manager = null;
        registered = false;
    }
}