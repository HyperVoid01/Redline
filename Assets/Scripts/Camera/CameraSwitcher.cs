using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraSwitcher : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private Camera[] cameras;
    [SerializeField] private int startIndex = 0;

    [Header("Controls")]
    [SerializeField] private bool enableNumberKeys = true; // 1-9 jump straight to a camera

#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key cycleKey = Key.C;
#else
    [SerializeField] private KeyCode cycleKey = KeyCode.C;
#endif

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

    private void Update()
    {
        if (CyclePressed())
        {
            Next();
        }

        if (enableNumberKeys)
        {
            int count = Mathf.Min(cameras.Length, 9);
            for (int i = 0; i < count; i++)
            {
                if (NumberPressed(i + 1))
                {
                    SwitchTo(i);
                    break;
                }
            }
        }
    }

    public void Next()
    {
        SwitchTo((currentIndex + 1) % cameras.Length);
    }

    public void Previous()
    {
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
        }
    }

    private bool CyclePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[cycleKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(cycleKey);
#endif
    }

    private bool NumberPressed(int number)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        Key key = (Key)((int)Key.Digit1 + (number - 1));
        return Keyboard.current[key].wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Alpha0 + number);
#endif
    }
}