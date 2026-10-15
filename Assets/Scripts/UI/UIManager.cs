using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Score")]
    [Tooltip("Live score on the HUD.")]
    [SerializeField] private TMP_Text scoreText;

    [Header("Game over results")]
    [SerializeField] private TMP_Text gameOverScoreText;
    [SerializeField] private TMP_Text gameOverBestText;
    [Tooltip("An object (for example a 'New High Score!' label) that is only shown when the run set a new best.")]
    [SerializeField] private GameObject gameOverNewBestLabel;

    [Header("Win results")]
    [SerializeField] private TMP_Text winScoreText;
    [SerializeField] private TMP_Text winBestText;
    [SerializeField] private GameObject winNewBestLabel;

    [Header("Pause")]
    [Tooltip("Shown while the game is paused. Put the Resume and Settings buttons on it.")]
    [SerializeField] private GameObject pausePanel;
    [Tooltip("Shown when Settings is opened from the pause menu. Put the volume sliders and a Back button on it.")]
    [SerializeField] private GameObject settingsPanel;

    [Header("Controller navigation")]
    [Tooltip("The button or slider a controller starts on when the pause menu opens (for example Resume).")]
    [SerializeField] private Selectable pauseFirstSelected;
    [Tooltip("The control a controller starts on when the settings panel opens (for example the first slider).")]
    [SerializeField] private Selectable settingsFirstSelected;
    [Tooltip("The control a controller starts on when the game over panel opens (for example Retry).")]
    [SerializeField] private Selectable gameOverFirstSelected;
    [Tooltip("The control a controller starts on when the win panel opens (for example Next or Menu).")]
    [SerializeField] private Selectable winFirstSelected;

    [Header("Player car")]
    [Tooltip("Set automatically by CarSpawner when the car is placed. Only assign this by hand if the car already exists in the scene and you aren't using CarSpawner.")]
    [SerializeField] private Rigidbody playerBody;
    [Tooltip("Read the speed straight from the car's Rigidbody every frame. " +
             "Turn this off if your car controller already calls UpdateSpeed itself. " +
             "CarController already does, so this can normally be off.")]
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

    // HUD visibility is worked out from these, so no single call can override the others.
    private bool hudWanted = true;     // what the current camera wants (CameraSwitcher)
    private bool pauseShown;
    private bool settingsShown;
    private bool gameOverShown;
    private bool winShown;

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
        KeepControllerSelection();

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

    // Kept so older scripts that still call it (TimeManager, CarHudBinder) keep compiling.
    // Delete it, RegisterTimerText and timerText once nothing uses the timer any more.
    public void UpdateTime(int totalSeconds)
    {
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        lastTimeString = $"{minutes:00}:{seconds:00}";
        SetText(timerText, extraTimerTexts, lastTimeString);
    }

    /// <summary>Shows the live score on the HUD. ScoreManager calls this when the number changes.</summary>
    public void UpdateScore(int score)
    {
        if (scoreText != null)
            scoreText.text = "Score: " + ScoreManager.Format(score);
    }

    public void UpdateHealth(float current, float max)
    {
        if (healthBar == null)
            return;

        healthBar.maxValue = max;
        healthBar.value = current;
    }

    // ---------- Panels ----------

    /// <summary>
    /// What the current camera wants (CameraSwitcher calls this). The HUD still stays hidden
    /// while the pause menu, game over panel or win panel is up.
    /// </summary>
    public void ShowHUD(bool show)
    {
        hudWanted = show;
        RefreshHud();
    }

    public void ShowGameOver(bool show)
    {
        gameOverShown = show;

        if (show)
            FillResults(gameOverScoreText, gameOverBestText, gameOverNewBestLabel);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(show);

        if (show)
            HidePauseMenus();

        RefreshHud();
        RefreshSelection();
    }

    public void ShowWin(bool show)
    {
        winShown = show;

        if (show)
            FillResults(winScoreText, winBestText, winNewBestLabel);

        if (winPanel != null)
            winPanel.SetActive(show);

        if (show)
            HidePauseMenus();

        RefreshHud();
        RefreshSelection();
    }

    /// <summary>Shows or hides the pause menu. The HUD is hidden while it is open, and the settings panel is closed.</summary>
    public void ShowPause(bool show)
    {
        pauseShown = show;
        settingsShown = false;

        if (pausePanel != null)
            pausePanel.SetActive(show);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        RefreshHud();
        RefreshSelection();
    }

    /// <summary>Opens the settings panel in place of the pause menu, or goes back to the pause menu.</summary>
    public void ShowSettings(bool show)
    {
        settingsShown = show;

        if (settingsPanel != null)
            settingsPanel.SetActive(show);

        if (pausePanel != null)
            pausePanel.SetActive(!show);

        RefreshSelection();
    }

    private void HidePauseMenus()
    {
        pauseShown = false;
        settingsShown = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    // The one place the HUD is shown or hidden.
    private void RefreshHud()
    {
        if (hudPanel == null)
            return;

        bool visible = hudWanted && !pauseShown && !gameOverShown && !winShown;
        hudPanel.SetActive(visible);
    }

    // ---------- Controller navigation ----------

    // The control a controller should be on for whichever menu is open, or null when no menu is open.
    private Selectable GetWantedSelection()
    {
        if (gameOverShown)
            return gameOverFirstSelected;

        if (winShown)
            return winFirstSelected;

        if (settingsShown)
            return settingsFirstSelected;

        if (pauseShown)
            return pauseFirstSelected;

        return null;
    }

    private bool IsMenuOpen()
    {
        return gameOverShown || winShown || settingsShown || pauseShown;
    }

    // Puts the controller's focus on the first control of the menu that just opened, or clears it when no menu is open.
    private void RefreshSelection()
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
            return;

        Selectable wanted = GetWantedSelection();

        // Clear first so the highlight is redrawn even if the same control was already selected.
        eventSystem.SetSelectedGameObject(null);

        if (wanted != null && wanted.gameObject.activeInHierarchy)
            eventSystem.SetSelectedGameObject(wanted.gameObject);
    }

    // A mouse click on empty space clears the selection, which leaves a controller with nothing to move from.
    // While a menu is open, put the focus back on its first control.
    private void KeepControllerSelection()
    {
        if (!IsMenuOpen())
            return;

        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
            return;

        GameObject current = eventSystem.currentSelectedGameObject;

        if (current != null && current.activeInHierarchy)
            return;

        Selectable wanted = GetWantedSelection();

        if (wanted != null && wanted.gameObject.activeInHierarchy)
            eventSystem.SetSelectedGameObject(wanted.gameObject);
    }

    // Writes the final score, the best score and the "new high score" label onto a results panel.
    private static void FillResults(TMP_Text scoreLabel, TMP_Text bestLabel, GameObject newBestLabel)
    {
        ScoreManager scores = ScoreManager.Instance;

        int score = scores != null ? scores.Score : 0;
        int best = scores != null ? scores.HighScore : ScoreManager.SavedHighScore;
        bool isNewBest = scores != null && scores.IsNewHighScore;

        if (scoreLabel != null)
            scoreLabel.text = "Score: " + ScoreManager.Format(score);

        if (bestLabel != null)
            bestLabel.text = "Best: " + ScoreManager.Format(best);

        if (newBestLabel != null)
            newBestLabel.SetActive(isNewBest);
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