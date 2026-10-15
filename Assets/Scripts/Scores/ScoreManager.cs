using System.Globalization;
using UnityEngine;

/// <summary>
/// Adds up the score during a run, saves the best score when the run ends, and tells the UI.
/// Put one in the game scene. Score comes from distance driven, drifting and the finish bonus.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    private const string HighScoreKey = "Redline_HighScore";

    [Header("Distance")]
    [Tooltip("Points for every metre driven forward. Reversing scores nothing.")]
    [SerializeField] private float pointsPerMetre = 1f;

    [Header("Drifting")]
    [Tooltip("Points per second while the car is drifting.")]
    [SerializeField] private float driftPointsPerSecond = 50f;

    [Header("Finish")]
    [Tooltip("Flat bonus for reaching the finish platform.")]
    [SerializeField] private int finishBonus = 1000;

    public static ScoreManager Instance { get; private set; }

    /// <summary>Score of the current (or just finished) run.</summary>
    public int Score
    {
        get { return Mathf.FloorToInt(distanceScore + driftScore + bonusScore); }
    }

    /// <summary>Best score so far, including this run once it has finished.</summary>
    public int HighScore { get { return highScore; } }

    /// <summary>True when the run that just finished beat the old best.</summary>
    public bool IsNewHighScore { get; private set; }

    public bool RunFinished { get { return runFinished; } }

    /// <summary>The saved best score. Works without a ScoreManager in the scene, so the main menu can use it.</summary>
    public static int SavedHighScore
    {
        get { return PlayerPrefs.GetInt(HighScoreKey, 0); }
    }

    /// <summary>Formats a score with comma separators, the same way everywhere.</summary>
    public static string Format(int score)
    {
        return score.ToString("N0", CultureInfo.InvariantCulture);
    }

    private float distanceScore;
    private float driftScore;
    private float bonusScore;
    private int highScore;
    private bool runFinished;
    private int shownScore = -1;

    private Rigidbody cachedBody;
    private CarController car;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        highScore = SavedHighScore;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (runFinished)
            return;

        // Nothing scores while paused or once the run is over (including the game over delay).
        GameManager gm = GameManager.Instance;

        if (gm != null && (gm.IsPaused || gm.IsGameOver))
            return;

        if (!FindCar())
            return;

        float dt = Time.deltaTime;

        if (dt <= 0f)
            return;

        // Forward speed only, so reversing or being pushed backwards doesn't score.
        float forwardMetresPerSecond = car.ForwardSpeedKmh / 3.6f;

        if (forwardMetresPerSecond > 0f)
            distanceScore += forwardMetresPerSecond * dt * pointsPerMetre;

        if (car.IsDrifting)
            driftScore += driftPointsPerSecond * dt;

        PushScoreToUI();
    }

    // The player car is spawned at runtime, so it is found through UIManager (CarSpawner sets it there).
    private bool FindCar()
    {
        UIManager ui = UIManager.Instance;
        Rigidbody body = ui != null ? ui.PlayerBody : null;

        if (body == null)
        {
            cachedBody = null;
            car = null;
            return false;
        }

        if (body != cachedBody)
        {
            cachedBody = body;
            car = body.GetComponentInParent<CarController>();
        }

        return car != null;
    }

    /// <summary>Starts a fresh run. GameManager calls this from InitializeGame.</summary>
    public void ResetRun()
    {
        distanceScore = 0f;
        driftScore = 0f;
        bonusScore = 0f;
        runFinished = false;
        IsNewHighScore = false;
        highScore = SavedHighScore;
        shownScore = -1;

        PushScoreToUI();
    }

    /// <summary>
    /// Ends the run and saves the best score. GameManager calls this from EndGame and WinGame.
    /// A win adds the finish bonus first. Safe to call more than once.
    /// </summary>
    public void FinishRun(bool won)
    {
        if (runFinished)
            return;

        if (won)
            bonusScore += finishBonus;

        runFinished = true;

        int finalScore = Score;
        IsNewHighScore = finalScore > highScore;

        if (IsNewHighScore)
        {
            highScore = finalScore;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
            PlayerPrefs.Save();
        }

        PushScoreToUI();
    }

    /// <summary>For other scripts that want to give points later (pickups, near misses). Ignored when paused or over.</summary>
    public void AddPoints(int amount)
    {
        if (amount <= 0 || runFinished)
            return;

        GameManager gm = GameManager.Instance;

        if (gm != null && (gm.IsPaused || gm.IsGameOver))
            return;

        bonusScore += amount;
        PushScoreToUI();
    }

    // Only touches the UI text when the shown number changes.
    private void PushScoreToUI()
    {
        int score = Score;

        if (score == shownScore)
            return;

        shownScore = score;

        if (UIManager.Instance != null)
            UIManager.Instance.UpdateScore(score);
    }

    /// <summary>Right-click the component in the Inspector to wipe the saved best score while testing.</summary>
    [ContextMenu("Reset Saved High Score")]
    private void ResetSavedHighScore()
    {
        PlayerPrefs.DeleteKey(HighScoreKey);
        PlayerPrefs.Save();
        highScore = 0;
    }
}