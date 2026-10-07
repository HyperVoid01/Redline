using UnityEngine;

public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    [SerializeField] private float startingTime = 30f;

    private int lastDisplayedSeconds = -1;

    public float RemainingTime { get; private set; }
    public bool IsRunning { get; private set; }

    public void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        RemainingTime -= Time.deltaTime;
        CheckExpired();
        RefreshHud();
    }

    /// <summary>Resets the clock to the starting time and begins counting down.</summary>
    public void StartTimer()
    {
        RemainingTime = startingTime;
        lastDisplayedSeconds = -1;
        IsRunning = true;
        RefreshHud();
    }

    public void StopTimer()
    {
        IsRunning = false;
    }

    public void AddTime(float seconds)
    {
        if (!IsRunning)
        {
            return;
        }

        RemainingTime += seconds;
        CheckExpired();
        RefreshHud();
    }

    public void SubtractTime(float seconds)
    {
        AddTime(-seconds);
    }

    private void CheckExpired()
    {
        if (RemainingTime > 0f)
        {
            return;
        }

        RemainingTime = 0f;
        IsRunning = false;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.EndGame();
        }
    }

    private void RefreshHud()
    {
        if (UIManager.Instance == null)
        {
            return;
        }

        // Round up so the display hits 0 exactly when the game ends.
        int seconds = Mathf.CeilToInt(RemainingTime);

        // Only touch the text when the displayed number changes.
        if (seconds != lastDisplayedSeconds)
        {
            lastDisplayedSeconds = seconds;
            UIManager.Instance.UpdateTime(seconds);
        }
    }
}