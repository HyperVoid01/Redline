using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameManager : MonoBehaviour
{
    [Header("Win")]
    [Tooltip("Freeze the game once the win panel appears, like game over does.")]
    [SerializeField] private bool freezeOnWin = true;
    [Tooltip("Real seconds between crossing the finish line and the win panel appearing. The car rolls on and brakes during this. 0 = straight away.")]
    [SerializeField] private float winPanelDelay = 3f;

    [Header("Win slow motion")]
    [Tooltip("Dips into slow motion for a moment as the car crosses the line.")]
    [SerializeField] private bool slowMoOnFinish = true;
    [Tooltip("Time scale during the dip. 0.3 = 30% speed.")]
    [SerializeField, Range(0.05f, 1f)] private float slowMoScale = 0.3f;
    [Tooltip("Real seconds spent at the slow speed.")]
    [SerializeField] private float slowMoDuration = 0.5f;
    [Tooltip("Real seconds to ease back to normal speed.")]
    [SerializeField] private float slowMoRecover = 0.5f;

    [Header("Game over")]
    [Tooltip("Default seconds between losing and the game over panel appearing and the game freezing. " +
             "Used for losses that don't pass their own delay (for example falling off the track). " +
             "The car's PlayerHealth passes its own delay for the explosion, timed to the death particles. 0 = straight away.")]
    [SerializeField] private float gameOverDelay = 2f;

    [Header("Pause")]
    [Tooltip("Pressing Escape (keyboard) or Start (controller) pauses and resumes the game. Escape and the controller's B button also back out of the settings panel.")]
    [SerializeField] private bool pauseWithEscape = true;

    public bool IsGameOver { get; private set; }
    public bool IsWon { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsSettingsOpen { get; private set; }

    /// <summary>True from crossing the finish line until the win panel appears (the car is rolling out).</summary>
    public bool IsFinishing { get; private set; }

    /// <summary>True once the game has actually frozen after game over or win. Car audio silences itself on this.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>True while player controls (look, camera switch, driving) should be ignored.</summary>
    public bool InputBlocked => IsPaused;

    /// <summary>True while the mouse cursor should be visible and free.</summary>
    public bool WantsCursorFree => IsPaused || (IsGameOver && !IsFinishing);

    public static GameManager Instance;

    /// <summary>Fires once, the moment the player crosses the finish line. Rumble, camera and sound effects listen to this.</summary>
    public static event Action RaceFinished;

    // Handle for the looping night ambience, kept so it can be stopped later.
    private AudioSource nightAmbience;

    // Cursor state from before the pause, so it can be put back on resume.
    private CursorLockMode cursorLockBeforePause;
    private bool cursorVisibleBeforePause;

    private Coroutine gameOverRoutine;
    private Coroutine winRoutine;

    // Physics step before any slow motion, so it can be put back.
    private float baseFixedDeltaTime;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        baseFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void Start()
    {
        InitializeGame();
    }

    private void Update()
    {
        if (!pauseWithEscape)
            return;

        if (PausePressed())
            HandleEscape();
        else if (BackPressed())
            HandleBack();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            StopNightAmbience();

            // Never carry a paused or slowed game into the next scene.
            SetTimeScale(1f);
            AudioListener.pause = false;
        }
    }

    // Initialize all scripts
    public void InitializeGame()
    {
        if (gameOverRoutine != null)
        {
            StopCoroutine(gameOverRoutine);
            gameOverRoutine = null;
        }

        if (winRoutine != null)
        {
            StopCoroutine(winRoutine);
            winRoutine = null;
        }

        IsGameOver = false;
        IsWon = false;
        IsFinishing = false;
        IsPaused = false;
        IsSettingsOpen = false;
        IsFrozen = false;
        SetTimeScale(1f);
        AudioListener.pause = false;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowPause(false);
            UIManager.Instance.ShowGameOver(false);
            UIManager.Instance.ShowWin(false);
        }

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.ResetRun();
        }

        StartNightAmbience();
    }

    // Sets the time scale and keeps the physics step in proportion, so slow motion stays smooth.
    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;

        if (baseFixedDeltaTime > 0f)
            Time.fixedDeltaTime = scale > 0f ? baseFixedDeltaTime * scale : baseFixedDeltaTime;
    }

    // ---------- Pause ----------

    /// <summary>Pauses or resumes. Hook this to a pause button if you add one to the HUD.</summary>
    public void TogglePause()
    {
        if (IsPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        // No pausing once the run is over: the game over and win panels have their own buttons.
        if (IsPaused || IsGameOver)
            return;

        IsPaused = true;
        IsSettingsOpen = false;

        SetTimeScale(0f);

        // Loops and the engine sound keep playing at timeScale 0, so pause all audio.
        AudioListener.pause = true;

        cursorLockBeforePause = Cursor.lockState;
        cursorVisibleBeforePause = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowPause(true);
    }

    public void ResumeGame()
    {
        if (!IsPaused)
            return;

        IsPaused = false;
        IsSettingsOpen = false;

        SetTimeScale(1f);
        AudioListener.pause = false;

        Cursor.lockState = cursorLockBeforePause;
        Cursor.visible = cursorVisibleBeforePause;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowPause(false);
    }

    /// <summary>Settings button on the pause menu.</summary>
    public void OpenSettings()
    {
        if (!IsPaused)
            return;

        IsSettingsOpen = true;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowSettings(true);
    }

    /// <summary>Back button on the settings panel. Returns to the pause menu.</summary>
    public void CloseSettings()
    {
        if (!IsPaused)
            return;

        IsSettingsOpen = false;

        if (UIManager.Instance != null)
            UIManager.Instance.ShowSettings(false);
    }

    // Escape or Start: pause, back out of settings, or resume.
    private void HandleEscape()
    {
        if (IsGameOver)
            return;

        if (!IsPaused)
            PauseGame();
        else if (IsSettingsOpen)
            CloseSettings();
        else
            ResumeGame();
    }

    // Controller B: only does something while paused. It never pauses the game by itself.
    private void HandleBack()
    {
        if (IsGameOver || !IsPaused)
            return;

        if (IsSettingsOpen)
            CloseSettings();
        else
            ResumeGame();
    }

    public void MainMenu()
    {
        SceneManager.LoadScene(0);
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Escape on the keyboard, or Start on a controller.
    private static bool PausePressed()
    {
#if ENABLE_INPUT_SYSTEM
        bool keyboard = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
        return keyboard || gamepad;
#else
        // JoystickButton7 is Start on an Xbox style controller.
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7);
#endif
    }

    // B on a controller (the east button, so it is Circle on a PlayStation controller).
    private static bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.JoystickButton1);
#endif
    }

    // ---------- End of run ----------

    /// <summary>Game over using the default delay. Kept without parameters so it can still be hooked to a UnityEvent.</summary>
    public void EndGame()
    {
        EndGame(-1f);
    }

    /// <summary>
    /// Game over. delayOverride is the real-time seconds to wait before the panel shows and the game freezes
    /// (PlayerHealth passes one timed to the death particles). A negative value uses the default Game Over Delay.
    /// </summary>
    public void EndGame(float delayOverride)
    {
        if (IsGameOver)
            return;

        // The run is over straight away: no more pausing and no win after this.
        IsGameOver = true;

        // The score is locked in (and the best saved) at the moment of the crash,
        // not after the game over delay.
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.FinishRun(false);
        }

        float delay = delayOverride >= 0f ? delayOverride : gameOverDelay;

        if (delay <= 0f)
        {
            ShowGameOverAndFreeze();
            return;
        }

        gameOverRoutine = StartCoroutine(GameOverAfterDelay(delay));
    }

    private IEnumerator GameOverAfterDelay(float delay)
    {
        // Game time is still running here, so the crash and falling parts play out.
        yield return new WaitForSecondsRealtime(delay);

        gameOverRoutine = null;
        ShowGameOverAndFreeze();
    }

    private void ShowGameOverAndFreeze()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowGameOver(true);
        }

        StopNightAmbience();

        // Set before the game over sound so car audio shuts up but the sting still plays.
        IsFrozen = true;

        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound2D(SoundID.GameOver);

        // Freezes physics and Update-driven movement.
        SetTimeScale(0f);
    }

    // Reached the finish line
    public void WinGame()
    {
        // Marking the game as over also stops a loss being triggered after a win.
        if (IsGameOver)
            return;

        IsGameOver = true;
        IsWon = true;
        IsFinishing = true;

        // Adds the finish bonus, then saves the best score. The score is locked in at the line,
        // before the win panel reads it.
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.FinishRun(true);
        }

        // Rumble, camera kick, sting and confetti all hang off this.
        RaceFinished?.Invoke();

        if (winPanelDelay <= 0f && !slowMoOnFinish)
        {
            ShowWinAndFreeze();
            return;
        }

        winRoutine = StartCoroutine(WinSequence());
    }

    // Slow motion dip, then the car rolls on until the win panel appears.
    private IEnumerator WinSequence()
    {
        float start = Time.unscaledTime;

        if (slowMoOnFinish && slowMoDuration > 0f)
        {
            SetTimeScale(slowMoScale);

            float held = 0f;
            while (held < slowMoDuration)
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }

            float recovered = 0f;
            float recover = Mathf.Max(0.01f, slowMoRecover);
            while (recovered < recover)
            {
                recovered += Time.unscaledDeltaTime;
                SetTimeScale(Mathf.Lerp(slowMoScale, 1f, Mathf.SmoothStep(0f, 1f, recovered / recover)));
                yield return null;
            }

            SetTimeScale(1f);
        }

        // Real time, so slow motion doesn't stretch the wait.
        while (Time.unscaledTime - start < winPanelDelay)
            yield return null;

        winRoutine = null;
        ShowWinAndFreeze();
    }

    private void ShowWinAndFreeze()
    {
        IsFinishing = false;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowWin(true);
        }

        // Loops keep playing at timeScale 0, so stop the ambience here too.
        StopNightAmbience();

        if (freezeOnWin)
        {
            IsFrozen = true;
            SetTimeScale(0f);
        }
        else
        {
            SetTimeScale(1f);
        }
    }

    // ---------- Night ambience ----------

    private void StartNightAmbience()
    {
        if (SoundPlayer.Instance == null)
            return;

        // Avoid stacking loops if InitializeGame is called again (restart).
        StopNightAmbience();

        // NightAmbience is a 2D entry in the SoundLibrary, so the position doesn't matter.
        nightAmbience = SoundPlayer.Instance.PlayLoop(SoundID.NightAmbience, Vector3.zero);
    }

    private void StopNightAmbience()
    {
        if (nightAmbience == null)
            return;

        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.StopLoop(nightAmbience);

        nightAmbience = null;
    }
}