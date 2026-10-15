using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Controller navigation")]
    [Tooltip("The control a controller starts on in the main menu (for example Play).")]
    [SerializeField] private Selectable mainFirstSelected;
    [Tooltip("The control a controller starts on when settings opens (for example the first slider or the Back button).")]
    [SerializeField] private Selectable settingsFirstSelected;
    [Tooltip("Escape and the controller's B button go back out of the settings panel.")]
    [SerializeField] private bool backOutWithCancel = true;

    [SerializeField] private Renderer titleRenderer;
    [SerializeField] private Renderer settingsRenderer;
    [SerializeField] private Color titleEmissionColor = Color.red;
    [SerializeField] private Color settingsEmissionColor = Color.red;
    [SerializeField] private float titleIntensity = 2f;
    [SerializeField] private float settingsIntensity = 2f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private Material titleMaterial;
    private Material settingsMaterial;

    private void Awake()
    {
        titleMaterial = titleRenderer.material;
        titleMaterial.EnableKeyword("_EMISSION");

        settingsMaterial = settingsRenderer.material;
        settingsMaterial.EnableKeyword("_EMISSION");

        SetTitleIntensity(titleIntensity); // apply the starting intensity
        SetSettingsIntensity(settingsIntensity);
    }

    private void Start()
    {
        // The menu opens with a control already highlighted, so a controller can move straight away.
        SelectWanted();
    }

    private void Update()
    {
        if (backOutWithCancel && settingsPanel != null && settingsPanel.activeSelf && CancelPressed())
        {
            HideSettings();
            return;
        }

        KeepControllerSelection();
    }

    private void OnDestroy()
    {
        // .material makes a copy, so clean it up
        if (titleMaterial != null)
        {
            Destroy(titleMaterial);
        }

        if (settingsMaterial != null)
        {
            Destroy(settingsMaterial);
        }
    }

    public void ShowSettings()
    {
        PlayClick();
        SetSettingsIntensity(settingsIntensity);
        mainMenuPanel.SetActive(false);
        settingsPanel.SetActive(true);
        SelectWanted();
    }

    public void HideSettings()
    {
        PlayClick();
        mainMenuPanel.SetActive(true);
        settingsPanel.SetActive(false);
        SelectWanted();
    }

    public void SetTitleIntensity(float value)
    {
        titleMaterial.SetColor(EmissionColorId, titleEmissionColor * value);
    }

    public void SetSettingsIntensity(float value)
    {
        settingsMaterial.SetColor(EmissionColorId, settingsEmissionColor * value);
    }

    public void OnPlayClick()
    {
        PlayClick();
        SetTitleIntensity(titleIntensity);
        SceneManager.LoadScene(1);
    }

    // ---------- Helpers ----------

    private static void PlayClick()
    {
        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound(SoundID.Click, Vector3.zero);
    }

    // The control a controller should be on for whichever panel is showing.
    private Selectable GetWantedSelection()
    {
        if (settingsPanel != null && settingsPanel.activeSelf)
            return settingsFirstSelected;

        return mainFirstSelected;
    }

    // Puts the controller's focus on the first control of the panel that is showing.
    private void SelectWanted()
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
            return;

        Selectable wanted = GetWantedSelection();

        // Selecting from code shouldn't play the hover sound.
        UIHoverSound.SuppressFrame = Time.frameCount;

        // Clear first so the highlight is redrawn even if the same control was already selected.
        eventSystem.SetSelectedGameObject(null);

        if (wanted != null && wanted.gameObject.activeInHierarchy)
            eventSystem.SetSelectedGameObject(wanted.gameObject);
    }

    // A mouse click on empty space clears the selection, which leaves a controller with nothing to move from.
    // Put the focus back on the first control of the panel that is showing.
    private void KeepControllerSelection()
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
            return;

        GameObject current = eventSystem.currentSelectedGameObject;

        if (current != null && current.activeInHierarchy)
            return;

        Selectable wanted = GetWantedSelection();

        if (wanted == null || !wanted.gameObject.activeInHierarchy)
            return;

        UIHoverSound.SuppressFrame = Time.frameCount;
        eventSystem.SetSelectedGameObject(wanted.gameObject);
    }

    // Escape on the keyboard, or B on a controller (the east button, so Circle on a PlayStation controller).
    private static bool CancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        bool keyboard = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        return keyboard || gamepad;
#else
        // JoystickButton1 is B on an Xbox style controller.
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
#endif
    }
}