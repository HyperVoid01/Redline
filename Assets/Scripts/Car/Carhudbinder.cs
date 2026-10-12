using TMPro;
using UnityEngine;

/// <summary>
/// Put this on the player car prefab (the root is the safest place) and drag in the texts from the car's
/// canvases. When the car appears, the texts register themselves with the scene's UIManager, so the
/// timer (and optionally the speed) on the car's own canvas is updated by UIManager.
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

    private UIManager manager;
    private bool registered;

    private void OnEnable()
    {
        Register();
    }

    // If this object is enabled before UIManager.Awake has run (a car that is already in the scene),
    // OnEnable finds no UIManager. Every Awake has run by Start, so try again here.
    private void Start()
    {
        Register();
    }

    private void OnDisable()
    {
        Unregister();
    }

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