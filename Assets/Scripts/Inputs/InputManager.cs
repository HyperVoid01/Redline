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
    
    public Vector2 LookMouse => controls.Gameplay.LookMouse.ReadValue<Vector2>();
    public Vector2 LookStick => controls.Gameplay.LookStick.ReadValue<Vector2>();
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
}