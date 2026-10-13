using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Tooltip("Freeze the game on win, like game over does.")]
    [SerializeField] private bool freezeOnWin = true;

    public bool IsGameOver { get; private set; }
    public bool IsWon { get; private set; }

    public static GameManager Instance;

    // Handle for the looping night ambience, kept so it can be stopped later.
    private AudioSource nightAmbience;

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

    private void OnDestroy()
    {
        if (Instance == this)
            StopNightAmbience();
    }

    // Initialize all scripts
    public void InitializeGame()
    {
        IsGameOver = false;
        IsWon = false;
        Time.timeScale = 1f;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowGameOver(false);
            UIManager.Instance.ShowWin(false);
        }

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.StartTimer();
        }

        StartNightAmbience();
    }

    // Game Over
    public void EndGame()
    {
        if (IsGameOver)
            return;

        IsGameOver = true;

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.StopTimer();
        }

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