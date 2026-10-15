using UnityEngine;
using UnityEngine.InputSystem;

public class CameraSwitcher : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private Camera[] cameras;
    [SerializeField] private int startIndex = 0;
    [SerializeField] private int hideHUDIndex;

    [Header("Controls")]
    [SerializeField] private bool enableNumberKeys = true; // 1-9 jump straight to a camera (keyboard only)

    [Header("Cursor")]
    [Tooltip("Lock and hide the mouse cursor while playing, on every camera. It is freed when paused or when the game is over.")]
    [SerializeField] private bool lockCursor = true;

    private int currentIndex;

    public int CurrentIndex => currentIndex;
    public Camera CurrentCamera => (cameras != null && cameras.Length > 0) ? cameras[currentIndex] : null;

    private void Start()
    {
        if (cameras == null || cameras.Length == 0)
        {
            Debug.LogError("CameraSwitcher: No cameras assigned.", this);
            enabled = false;
            return;
        }

        SwitchTo(Mathf.Clamp(startIndex, 0, cameras.Length - 1));
    }

    private void OnEnable()
    {
        if (InputManager.Instance == null) return;

        InputManager.Instance.CycleCameraPressed += Next;
    }

    private void OnDisable()
    {
        if (InputManager.Instance == null) return;

        InputManager.Instance.CycleCameraPressed -= Next;
    }

    private void Update()
    {
        if (IsPaused()) return;
        if (!enableNumberKeys || Keyboard.current == null) return;

        int count = Mathf.Min(cameras.Length, 9);
        for (int i = 0; i < count; i++)
        {
            Key key = (Key)((int)Key.Digit1 + i);
            if (Keyboard.current[key].wasPressedThisFrame)
            {
                SwitchTo(i);
                break;
            }
        }
    }

    // One owner for the cursor, so it behaves the same on every camera.
    private void LateUpdate()
    {
        if (!lockCursor) return;

        GameManager gm = GameManager.Instance;
        bool free = gm != null && gm.WantsCursorFree;

        CursorLockMode wanted = free ? CursorLockMode.None : CursorLockMode.Locked;

        if (Cursor.lockState != wanted)
            Cursor.lockState = wanted;

        if (Cursor.visible != free)
            Cursor.visible = free;
    }

    private static bool IsPaused()
    {
        return GameManager.Instance != null && GameManager.Instance.IsPaused;
    }

    public void Next()
    {
        if (IsPaused()) return;
        SwitchTo((currentIndex + 1) % cameras.Length);
    }

    public void Previous()
    {
        if (IsPaused()) return;
        SwitchTo((currentIndex - 1 + cameras.Length) % cameras.Length);
    }

    public void SwitchTo(int index)
    {
        if (index < 0 || index >= cameras.Length) return;

        currentIndex = index;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null) continue;

            bool active = (i == currentIndex);
            cam.enabled = active;

            // Only one AudioListener may be active, or Unity spams warnings
            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;

            // Switch the interior look script on and off with its camera
            InteriorCameraController interior = cam.GetComponent<InteriorCameraController>();
            if (interior != null) interior.enabled = active;
        }

        if (UIManager.Instance != null)
            UIManager.Instance.ShowHUD(currentIndex != hideHUDIndex);
    }
}