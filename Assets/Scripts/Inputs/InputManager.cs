using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)] // make sure this runs before other scripts
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    private PlayerControls controls;

    // Polled values (for continuous input)
    public float Steer => controls.Gameplay.Steer.ReadValue<float>();
    public float Throttle => controls.Gameplay.Throttle.ReadValue<float>();
    public bool Handbrake => controls.Gameplay.Handbrake.IsPressed();

    // Look input is scaled by the sensitivity settings and flipped if "invert Y" is on.
    public Vector2 LookMouse
    {
        get
        {
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
            SettingsData settings = SettingsManager.Current;
            return ScaleLook(
                controls.Gameplay.LookStick.ReadValue<Vector2>(),
                settings != null ? settings.stickSensitivity : 1f,
                settings != null && settings.invertLookY);
        }
    }

    public bool Recenter => controls.Gameplay.Recenter.IsPressed();

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

        controls.Gameplay.CycleCamera.performed    -= OnCycleCamera;
        controls.Dispose();
    }

    private void OnCycleCamera(InputAction.CallbackContext ctx)    => CycleCameraPressed?.Invoke();

    private static Vector2 ScaleLook(Vector2 look, float sensitivity, bool invertY)
    {
        look *= sensitivity;

        if (invertY)
            look.y = -look.y;

        return look;
    }
}