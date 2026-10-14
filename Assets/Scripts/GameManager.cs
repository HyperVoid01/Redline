using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameManager : MonoBehaviour
{
    [Tooltip("Freeze the game on win, like game over does.")]
    [SerializeField] private bool freezeOnWin = true;

    [Header("Game over")]
    [Tooltip("Seconds between losing and the game over panel appearing and the game freezing. The game keeps running during this time, so the crash can play out. 0 = straight away.")]
    [SerializeField] private float gameOverDelay = 2f;

    [Header("Pause")]
    [Tooltip("Pressing Escape pauses and resumes the game. Escape also backs out of the settings panel.")]
    [SerializeField] private bool pauseWithEscape = true;

    public bool IsGameOver { get; private set; }
    public bool IsWon { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsSettingsOpen { get; private set; }

    public static GameManager Instance;

    // Handle for the looping night ambience, kept so it can be stopped later.
    private AudioSource nightAmbience;

    // Cursor state from before the pause, so it can be put back on resume.
    private CursorLockMode cursorLockBeforePause;
    private bool cursorVisibleBeforePause;

    private Coroutine gameOverRoutine;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        InitializeGame();
    }

    private void Update()
    {
        if (pauseWithEscape && EscapePressed())
            HandleEscape();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            StopNightAmbience();

            // Never carry a paused game into the next scene.
            Time.timeScale = 1f;
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

        IsGameOver = false;
        IsWon = false;
        IsPaused = false;
        IsSettingsOpen = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowPause(false);
            UIManager.Instance.ShowGameOver(false);
            UIManager.Instance.ShowWin(false);
        }

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.StartTimer();
        }

        StartNightAmbience();
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

        Time.timeScale = 0f;

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

        Time.timeScale = 1f;
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

    public void MainMenu()
    {
        SceneManager.LoadScene(0);
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // ---------- End of run ----------

    // Game Over
    public void EndGame()
    {
        if (IsGameOver)
            return;

        // The run is over straight away: no more pausing, no win after this, and the timer stops.
        IsGameOver = true;

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.StopTimer();
        }

        if (gameOverDelay <= 0f)
        {
            ShowGameOverAndFreeze();
            return;
        }

        gameOverRoutine = StartCoroutine(GameOverAfterDelay());
    }

    private IEnumerator GameOverAfterDelay()
    {
        // Game time is still running here, so the crash and falling parts play out.
        yield return new WaitForSecondsRealtime(gameOverDelay);

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

        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound2D(SoundID.GameOver);

        // Freezes physics and Update-driven movement.
        Time.timeScale = 0f;
    }

    // Reached the finish platform
    public void WinGame()
    {
        // Marking the game as over also stops the timer running out after a win.
        if (IsGameOver)
            return;

        IsGameOver = true;
        IsWon = true;

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.StopTimer();
        }

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowWin(true);
        }

        // Loops keep playing at timeScale 0, so stop the ambience here too.
        StopNightAmbience();

        if (freezeOnWin)
        {
            Time.timeScale = 0f;
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