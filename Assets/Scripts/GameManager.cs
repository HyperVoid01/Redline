using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Tooltip("Freeze the game on win, like game over does.")]
    [SerializeField] private bool freezeOnWin = true;

    public bool IsGameOver { get; private set; }
    public bool IsWon { get; private set; }

    public static GameManager Instance;

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

        if (freezeOnWin)
        {
            Time.timeScale = 0f;
        }
    }
}