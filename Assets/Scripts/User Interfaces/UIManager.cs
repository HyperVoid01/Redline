using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    private const float KmhToMph = 0.621371f;

    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Slider healthBar;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject hudPanel;

    [Header("Pause")]
    [Tooltip("Shown while the game is paused. Put the Resume and Settings buttons on it.")]
    [SerializeField] private GameObject pausePanel;
    [Tooltip("Shown when Settings is opened from the pause menu. Put the volume sliders and a Back button on it.")]
    [SerializeField] private GameObject settingsPanel;

    [Header("Player car")]
    [Tooltip("Set automatically by CarSpawner when the car is placed. Only assign this by hand if the car already exists in the scene and you aren't using CarSpawner.")]
    [SerializeField] private Rigidbody playerBody;
    [Tooltip("Read the speed straight from the car's Rigidbody every frame. " +
             "Turn this off if your car controller already calls UpdateSpeed itself.")]
    [SerializeField] private bool readSpeedFromCar = true;

    public static UIManager Instance;

    public Rigidbody PlayerBody { get { return playerBody; } }

    // Texts that live on the spawned car (see CarHudBinder). They are updated alongside the scene ones above.
    private readonly List<TMP_Text> extraTimerTexts = new List<TMP_Text>();
    private readonly List<TMP_Text> extraSpeedTexts = new List<TMP_Text>();

    // Last values, so a text that registers late (the car spawns after the game starts) shows the current value straight away.
    private string lastTimeString;
    private string lastSpeedString;
    private int lastSpeed = -1;
    private int lastSpeedKmh = -1;

    public void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        SettingsManager.Changed += OnSettingsChanged;
    }

    private void OnDisable()
    {
        SettingsManager.Changed -= OnSettingsChanged;
    }

    // The speed unit may have changed, so redraw the current speed in it.
    private void OnSettingsChanged()
    {
        if (lastSpeedKmh >= 0)
            UpdateSpeed(lastSpeedKmh);
    }

    /// <summary>
    /// Gives the UI the player's car. CarSpawner calls this after it places the car, so the reference
    /// doesn't need to be dragged in the Inspector (a car spawned at runtime can't be).
    /// </summary>
    public void SetPlayerCar(Transform car)
    {
        playerBody = car != null ? car.GetComponentInChildren<Rigidbody>() : null;
        lastSpeed = -1;   // force the text to refresh on the next frame
    }

    // ---------- Texts on spawned objects (used by CarHudBinder) ----------

    public void RegisterTimerText(TMP_Text text)
    {
        if (text == null || extraTimerTexts.Contains(text))
            return;

        extraTimerTexts.Add(text);

        if (lastTimeString != null)
            text.text = lastTimeString;
    }

    public void UnregisterTimerText(TMP_Text text)
    {
        extraTimerTexts.Remove(text);
    }

    public void RegisterSpeedText(TMP_Text text)
    {
        if (text == null || extraSpeedTexts.Contains(text))
            return;

        extraSpeedTexts.Add(text);

        if (lastSpeedString != null)
            text.text = lastSpeedString;
    }

    public void UnregisterSpeedText(TMP_Text text)
    {
        extraSpeedTexts.Remove(text);
    }

    // ---------- Updating the UI ----------

    private void Update()
    {
        if (!readSpeedFromCar || playerBody == null)
            return;

        // m/s to km/h
        int kmh = Mathf.RoundToInt(playerBody.linearVelocity.magnitude * 3.6f);

        // Only touch the text when the number changes, so TMP isn't rebuilt every frame.
        if (kmh != lastSpeed)
        {
            lastSpeed = kmh;
            UpdateSpeed(kmh);
        }
    }

    /// <summary>Takes the speed in km/h and shows it in the unit chosen in the settings.</summary>
    public void UpdateSpeed(int speedKmh)
    {
        lastSpeedKmh = speedKmh;

        SettingsData settings = SettingsManager.Current;
        bool mph = settings != null && settings.speedUnit == SpeedUnit.Mph;

        lastSpeedString = mph
            ? Mathf.RoundToInt(speedKmh * KmhToMph) + " mph"
            : speedKmh + " km/h";

        SetText(speedText, extraSpeedTexts, lastSpeedString);
    }

    public void UpdateTime(int totalSeconds)
    {
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        lastTimeString = $"{minutes:00}:{seconds:00}";
        SetText(timerText, extraTimerTexts, lastTimeString);
    }

    public void UpdateHealth(float current, float max)
    {
        healthBar.maxValue = max;
        healthBar.value = current;
    }

    public void ShowHUD(bool show)
    {
        if (hudPanel != null)
            hudPanel.SetActive(show);
    }

    public void ShowGameOver(bool show)
    {
        if (gameOverPanel != null)
        {
            hudPanel.SetActive(!show);
            gameOverPanel.SetActive(show);
        }

        if (show)
            HidePauseMenus();
    }
    
    public void ShowWin(bool show)
    {
        if (winPanel != null)
        {
            hudPanel.SetActive(!show);
            winPanel.SetActive(show);
        }

        if (show)
            HidePauseMenus();
    }

    /// <summary>Shows or hides the pause menu. The HUD is hidden while it is open, and the settings panel is closed.</summary>
    public void ShowPause(bool show)
    {
        if (pausePanel != null)
            pausePanel.SetActive(show);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (hudPanel != null)
            hudPanel.SetActive(!show);
    }

    /// <summary>Opens the settings panel in place of the pause menu, or goes back to the pause menu.</summary>
    public void ShowSettings(bool show)
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(show);

        if (pausePanel != null)
            pausePanel.SetActive(!show);
    }

    private void HidePauseMenus()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    // Updates the scene text (if there is one) and every registered text, dropping any that were destroyed.
    private static void SetText(TMP_Text sceneText, List<TMP_Text> extras, string value)
    {
        if (sceneText != null)
            sceneText.text = value;

        for (int i = extras.Count - 1; i >= 0; i--)
        {
            if (extras[i] == null)
                extras.RemoveAt(i);
            else
                extras[i].text = value;
        }
    }
}