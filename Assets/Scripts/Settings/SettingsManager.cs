using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loads, changes, saves and applies the game's settings.
///
/// Put one in each scene that has the settings menu, assign the SettingsData asset, and drag the
/// menu's sliders, toggles and dropdowns into the slots below. Every slot is optional. The ranges
/// and dropdown options are filled in by this script, so the UI objects only need to exist.
///
/// Settings are saved to PlayerPrefs a moment after the last change, so they survive restarts.
///
/// Other scripts read values with SettingsManager.Current (null if there is no manager in the scene,
/// so treat null as "default") and listen to SettingsManager.Changed to react when something changes.
/// Always unsubscribe from Changed in OnDisable.
/// </summary>
[DefaultExecutionOrder(-200)] // load before other scripts read the values
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }
    public static SettingsData Current { get { return Instance != null ? Instance.data : null; } }

    /// <summary>Fires whenever any setting changes or is reset.</summary>
    public static event Action Changed;

    [SerializeField] private SettingsData data;
    [Tooltip("Seconds after the last change before the settings are written to disk.")]
    [SerializeField] private float saveDelay = 0.5f;

    [Header("Audio")]
    [SerializeField] private Slider mainVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;

    [Header("Controls")]
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private Slider stickSensitivitySlider;
    [SerializeField] private Toggle invertLookYToggle;
    [SerializeField] private Slider steeringResponseSlider;
    [SerializeField] private Slider steeringAssistSlider;

    [Header("Interface")]
    [SerializeField] private TMP_Dropdown speedUnitDropdown;

    [Header("Graphics")]
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Toggle vsyncToggle;
    [SerializeField] private TMP_Dropdown frameRateDropdown;

    [Header("Reset")]
    [SerializeField] private Button resetButton;

    const string KeyPrefix = "settings.";

    static readonly int[] FrameRates = { 30, 60, 90, 120, 144, 240, -1 };

    readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    bool dirty;
    float lastChangeTime;

    // ---------- Lifecycle ----------

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (data == null)
        {
            Debug.LogError("SettingsManager: assign the SettingsData asset.", this);
            enabled = false;
            return;
        }

        Load();
    }

    void Start()
    {
        if (Instance != this || data == null)
            return;

        BuildResolutionList();
        BindUI();
        RefreshUI();
        ApplyGraphics();
        ApplyDisplay();

        // Let anything that started before this a chance to pick up the loaded values.
        Changed?.Invoke();
    }

    void Update()
    {
        // Real time, because the settings menu is used while the game is paused.
        if (dirty && Time.unscaledTime - lastChangeTime >= saveDelay)
            Save();
    }

    void OnApplicationQuit()
    {
        if (dirty)
            Save();
    }

    void OnDestroy()
    {
        if (Instance != this)
            return;

        if (dirty)
            Save();

        Instance = null;
    }

    // ---------- Setters (use these from UI events or other scripts) ----------

    public void SetMainVolume(float value) { data.mainVolume = Mathf.Clamp01(value); MarkChanged(); }
    public void SetMusicVolume(float value) { data.musicVolume = Mathf.Clamp01(value); MarkChanged(); }
    public void SetSfxVolume(float value) { data.sfxVolume = Mathf.Clamp01(value); MarkChanged(); }

    public void SetMouseSensitivity(float value) { data.mouseSensitivity = Mathf.Clamp(value, 0.1f, 3f); MarkChanged(); }
    public void SetStickSensitivity(float value) { data.stickSensitivity = Mathf.Clamp(value, 0.1f, 3f); MarkChanged(); }
    public void SetInvertLookY(bool value) { data.invertLookY = value; MarkChanged(); }
    public void SetSteeringResponse(float value) { data.steeringResponse = Mathf.Clamp(value, 0.5f, 2f); MarkChanged(); }
    public void SetSteeringAssist(float value) { data.steeringAssist = Mathf.Clamp(value, 0f, 2f); MarkChanged(); }

    public void SetSpeedUnit(int index)
    {
        data.speedUnit = index == 1 ? SpeedUnit.Mph : SpeedUnit.Kmh;
        MarkChanged();
    }

    public void SetQuality(int index)
    {
        data.qualityLevel = Mathf.Clamp(index, 0, QualitySettings.names.Length - 1);
        ApplyGraphics();
        MarkChanged();
    }

    public void SetResolution(int index)
    {
        if (index < 0 || index >= resolutions.Count)
            return;

        data.resolutionWidth = resolutions[index].x;
        data.resolutionHeight = resolutions[index].y;
        ApplyDisplay();
        MarkChanged();
    }

    public void SetFullscreen(bool value)
    {
        data.fullscreen = value;
        ApplyDisplay();
        MarkChanged();
    }

    public void SetVsync(bool value)
    {
        data.vsync = value;
        ApplyGraphics();
        MarkChanged();
    }

    public void SetFrameRate(int index)
    {
        data.targetFrameRate = FrameRates[Mathf.Clamp(index, 0, FrameRates.Length - 1)];
        ApplyGraphics();
        MarkChanged();
    }

    /// <summary>Puts every setting back to its starting value. Hook this to the Reset button.</summary>
    public void ResetToDefaults()
    {
        SoundPlayer.Instance.PlaySound(SoundID.Click, Vector3.zero);
        
        data.ResetToDefaults();
        ApplyGraphics();
        ApplyDisplay();
        RefreshUI();
        MarkChanged();
    }

    void MarkChanged()
    {
        dirty = true;
        lastChangeTime = Time.unscaledTime;
        Changed?.Invoke();
    }

    // ---------- Applying ----------

    void ApplyGraphics()
    {
        if (data.qualityLevel >= 0)
            QualitySettings.SetQualityLevel(Mathf.Clamp(data.qualityLevel, 0, QualitySettings.names.Length - 1), true);

        // Changing the quality level resets these, so they are applied after it.
        QualitySettings.vSyncCount = data.vsync ? 1 : 0;
        Application.targetFrameRate = data.targetFrameRate;
    }

    void ApplyDisplay()
    {
        FullScreenMode mode = data.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

        if (data.resolutionWidth > 0 && data.resolutionHeight > 0)
            Screen.SetResolution(data.resolutionWidth, data.resolutionHeight, mode);
        else
            Screen.fullScreenMode = mode;
    }

    // ---------- Saving and loading ----------

    void Load()
    {
        data.mainVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key("mainVolume"), data.mainVolume));
        data.musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key("musicVolume"), data.musicVolume));
        data.sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key("sfxVolume"), data.sfxVolume));

        data.mouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Key("mouseSensitivity"), data.mouseSensitivity), 0.1f, 3f);
        data.stickSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Key("stickSensitivity"), data.stickSensitivity), 0.1f, 3f);
        data.invertLookY = PlayerPrefs.GetInt(Key("invertLookY"), data.invertLookY ? 1 : 0) == 1;
        data.steeringResponse = Mathf.Clamp(PlayerPrefs.GetFloat(Key("steeringResponse"), data.steeringResponse), 0.5f, 2f);
        data.steeringAssist = Mathf.Clamp(PlayerPrefs.GetFloat(Key("steeringAssist"), data.steeringAssist), 0f, 2f);

        data.speedUnit = (SpeedUnit)PlayerPrefs.GetInt(Key("speedUnit"), (int)data.speedUnit);

        data.qualityLevel = PlayerPrefs.GetInt(Key("qualityLevel"), data.qualityLevel);
        data.fullscreen = PlayerPrefs.GetInt(Key("fullscreen"), data.fullscreen ? 1 : 0) == 1;
        data.resolutionWidth = PlayerPrefs.GetInt(Key("resolutionWidth"), data.resolutionWidth);
        data.resolutionHeight = PlayerPrefs.GetInt(Key("resolutionHeight"), data.resolutionHeight);
        data.vsync = PlayerPrefs.GetInt(Key("vsync"), data.vsync ? 1 : 0) == 1;
        data.targetFrameRate = PlayerPrefs.GetInt(Key("targetFrameRate"), data.targetFrameRate);
    }

    /// <summary>Writes the settings to disk straight away. Normally this happens by itself.</summary>
    public void Save()
    {
        PlayerPrefs.SetFloat(Key("mainVolume"), data.mainVolume);
        PlayerPrefs.SetFloat(Key("musicVolume"), data.musicVolume);
        PlayerPrefs.SetFloat(Key("sfxVolume"), data.sfxVolume);

        PlayerPrefs.SetFloat(Key("mouseSensitivity"), data.mouseSensitivity);
        PlayerPrefs.SetFloat(Key("stickSensitivity"), data.stickSensitivity);
        PlayerPrefs.SetInt(Key("invertLookY"), data.invertLookY ? 1 : 0);
        PlayerPrefs.SetFloat(Key("steeringResponse"), data.steeringResponse);
        PlayerPrefs.SetFloat(Key("steeringAssist"), data.steeringAssist);

        PlayerPrefs.SetInt(Key("speedUnit"), (int)data.speedUnit);

        PlayerPrefs.SetInt(Key("qualityLevel"), data.qualityLevel);
        PlayerPrefs.SetInt(Key("fullscreen"), data.fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(Key("resolutionWidth"), data.resolutionWidth);
        PlayerPrefs.SetInt(Key("resolutionHeight"), data.resolutionHeight);
        PlayerPrefs.SetInt(Key("vsync"), data.vsync ? 1 : 0);
        PlayerPrefs.SetInt(Key("targetFrameRate"), data.targetFrameRate);

        PlayerPrefs.Save();
        dirty = false;
    }

    static string Key(string name)
    {
        return KeyPrefix + name;
    }

    // ---------- UI ----------

    void BuildResolutionList()
    {
        resolutions.Clear();

        Resolution[] all = Screen.resolutions;
        for (int i = 0; i < all.Length; i++)
        {
            // Screen.resolutions lists every refresh rate separately; one entry per size is enough.
            Vector2Int size = new Vector2Int(all[i].width, all[i].height);
            if (!resolutions.Contains(size))
                resolutions.Add(size);
        }

        if (resolutions.Count == 0)
            resolutions.Add(new Vector2Int(Screen.width, Screen.height));
    }

    void BindUI()
    {
        BindSlider(mainVolumeSlider, 0f, 1f, SetMainVolume);
        BindSlider(musicVolumeSlider, 0f, 1f, SetMusicVolume);
        BindSlider(sfxVolumeSlider, 0f, 1f, SetSfxVolume);

        BindSlider(mouseSensitivitySlider, 0.1f, 3f, SetMouseSensitivity);
        BindSlider(stickSensitivitySlider, 0.1f, 3f, SetStickSensitivity);
        BindSlider(steeringResponseSlider, 0.5f, 2f, SetSteeringResponse);
        BindSlider(steeringAssistSlider, 0f, 2f, SetSteeringAssist);

        if (invertLookYToggle != null) invertLookYToggle.onValueChanged.AddListener(SetInvertLookY);
        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        if (vsyncToggle != null) vsyncToggle.onValueChanged.AddListener(SetVsync);

        if (speedUnitDropdown != null)
        {
            speedUnitDropdown.ClearOptions();
            speedUnitDropdown.AddOptions(new List<string> { "km/h", "mph" });
            speedUnitDropdown.onValueChanged.AddListener(SetSpeedUnit);
        }

        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
            qualityDropdown.onValueChanged.AddListener(SetQuality);
        }

        if (resolutionDropdown != null)
        {
            List<string> labels = new List<string>();
            for (int i = 0; i < resolutions.Count; i++)
                labels.Add(resolutions[i].x + " x " + resolutions[i].y);

            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(labels);
            resolutionDropdown.onValueChanged.AddListener(SetResolution);
        }

        if (frameRateDropdown != null)
        {
            List<string> labels = new List<string>();
            for (int i = 0; i < FrameRates.Length; i++)
                labels.Add(FrameRates[i] < 0 ? "Unlimited" : FrameRates[i] + " FPS");

            frameRateDropdown.ClearOptions();
            frameRateDropdown.AddOptions(labels);
            frameRateDropdown.onValueChanged.AddListener(SetFrameRate);
        }

        if (resetButton != null)
            resetButton.onClick.AddListener(ResetToDefaults);
    }

    static void BindSlider(Slider slider, float min, float max, UnityEngine.Events.UnityAction<float> setter)
    {
        if (slider == null)
            return;

        slider.minValue = min;
        slider.maxValue = max;
        slider.onValueChanged.AddListener(setter);
    }

    /// <summary>Moves every control to match the current values, without triggering the setters.</summary>
    public void RefreshUI()
    {
        if (data == null)
            return;

        SetSlider(mainVolumeSlider, data.mainVolume);
        SetSlider(musicVolumeSlider, data.musicVolume);
        SetSlider(sfxVolumeSlider, data.sfxVolume);

        SetSlider(mouseSensitivitySlider, data.mouseSensitivity);
        SetSlider(stickSensitivitySlider, data.stickSensitivity);
        SetSlider(steeringResponseSlider, data.steeringResponse);
        SetSlider(steeringAssistSlider, data.steeringAssist);

        if (invertLookYToggle != null) invertLookYToggle.SetIsOnWithoutNotify(data.invertLookY);
        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(data.fullscreen);
        if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(data.vsync);

        if (speedUnitDropdown != null)
            speedUnitDropdown.SetValueWithoutNotify((int)data.speedUnit);

        if (qualityDropdown != null)
        {
            int quality = data.qualityLevel >= 0 ? data.qualityLevel : QualitySettings.GetQualityLevel();
            qualityDropdown.SetValueWithoutNotify(Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1));
        }

        if (resolutionDropdown != null)
            resolutionDropdown.SetValueWithoutNotify(FindResolutionIndex());

        if (frameRateDropdown != null)
            frameRateDropdown.SetValueWithoutNotify(FindFrameRateIndex());
    }

    static void SetSlider(Slider slider, float value)
    {
        if (slider != null)
            slider.SetValueWithoutNotify(value);
    }

    int FindResolutionIndex()
    {
        int width = data.resolutionWidth > 0 ? data.resolutionWidth : Screen.width;
        int height = data.resolutionHeight > 0 ? data.resolutionHeight : Screen.height;

        for (int i = 0; i < resolutions.Count; i++)
        {
            if (resolutions[i].x == width && resolutions[i].y == height)
                return i;
        }

        return resolutions.Count - 1;
    }

    int FindFrameRateIndex()
    {
        for (int i = 0; i < FrameRates.Length; i++)
        {
            if (FrameRates[i] == data.targetFrameRate)
                return i;
        }

        return FrameRates.Length - 1;
    }
}
