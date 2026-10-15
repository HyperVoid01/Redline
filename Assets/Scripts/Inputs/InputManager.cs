using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)] // make sure this runs before other scripts
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    private PlayerControls controls;

    // While the game is paused, every gameplay input reads as "nothing pressed".
    private static bool Blocked =>
        GameManager.Instance != null && GameManager.Instance.InputBlocked;

    // Polled values (for continuous input)
    public float Steer => Blocked ? 0f : controls.Gameplay.Steer.ReadValue<float>();
    public float Throttle => Blocked ? 0f : controls.Gameplay.Throttle.ReadValue<float>();
    public bool Handbrake => !Blocked && controls.Gameplay.Handbrake.IsPressed();

    // Look input is scaled by the sensitivity settings and flipped if "invert Y" is on.
    public Vector2 LookMouse
    {
        get
        {
            if (Blocked) return Vector2.zero;

            SettingsData settings = SettingsManager.Current;
            return ScaleLook(
                controls.Gameplay.LookMouse.ReadValue<Vector2>(),
                settings != null ? settings.mouseSensitivity : 1f,
                settings != null && settings.invertLookY);
        }
    }

    public Vector2 LookStick
    {
        get
        {
            if (Blocked) return Vector2.zero;

            SettingsData settings = SettingsManager.Current;
            return ScaleLook(
                controls.Gameplay.LookStick.ReadValue<Vector2>(),
                settings != null ? settings.stickSensitivity : 1f,
                settings != null && settings.invertLookY);
        }
    }

    public bool Recenter => !Blocked && controls.Gameplay.Recenter.IsPressed();

    // Events (for one-shot input)
    public event System.Action CycleCameraPressed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        controls = new PlayerControls();

        controls.Gameplay.CycleCamera.performed += OnCycleCamera;
    }

    private void OnEnable()  => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void OnDestroy()
    {
        if (Instance != this) return;

        controls.Gameplay.CycleCamera.performed -= OnCycleCamera;
        controls.Dispose();
    }

    private void OnCycleCamera(InputAction.CallbackContext ctx)
    {
        if (Blocked) return;
        CycleCameraPressed?.Invoke();
    }

    private static Vector2 ScaleLook(Vector2 look, float sensitivity, bool invertY)
    {
        look *= sensitivity;

        if (invertY)
            look.y = -look.y;

        return look;
    }
}