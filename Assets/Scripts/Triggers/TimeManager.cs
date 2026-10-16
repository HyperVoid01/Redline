using UnityEngine;

public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance;

    [SerializeField] private float startingTime = 30f;

    [Header("Audio")]
    [Tooltip("The ticking loop starts when this many seconds (or fewer) remain.")]
    [SerializeField] private float tickingStartsAt = 10f;
    [Tooltip("Ticking pitch when it first starts.")]
    [SerializeField] private float tickingStartPitch = 1f;
    [Tooltip("Ticking pitch at 0 seconds. Set equal to the start pitch for a constant tick rate.")]
    [SerializeField] private float tickingEndPitch = 1.4f;
    [Tooltip("Play the Checkpoint sound whenever time is added. Untick if the gate plays its own sound.")]
    [SerializeField] private bool playTimeAddedSound = true;

    private int lastDisplayedSeconds = -1;
    private AudioSource tickingLoop;

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

    private void OnDestroy()
    {
        StopTicking();
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
        UpdateTickingAudio();
    }

    /// <summary>Resets the clock to the starting time and begins counting down.</summary>
    public void StartTimer()
    {
        RemainingTime = startingTime;
        lastDisplayedSeconds = -1;
        IsRunning = true;
        RefreshHud();
        UpdateTickingAudio();
    }

    public void StopTimer()
    {
        IsRunning = false;
        StopTicking();
    }

    public void AddTime(float seconds)
    {
        if (!IsRunning)
        {
            return;
        }

        RemainingTime += seconds;

        if (seconds > 0f && playTimeAddedSound && SoundPlayer.Instance != null)
        {
            SoundPlayer.Instance.PlaySound2D(SoundID.Checkpoint);
        }

        CheckExpired();
        RefreshHud();
        UpdateTickingAudio();
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
        StopTicking();

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

    // ---------------------------------------------------------------- Audio

    // Low-time warning: a looping tick that starts at tickingStartsAt seconds,
    // speeds up as the clock runs down, and stops when time is added back,
    // the timer stops, or it runs out.
    private void UpdateTickingAudio()
    {
        bool shouldTick = IsRunning && RemainingTime > 0f && RemainingTime <= tickingStartsAt;

        if (!shouldTick)
        {
            StopTicking();
            return;
        }

        SoundPlayer player = SoundPlayer.Instance;

        if (player == null)
        {
            return;
        }

        if (tickingLoop == null)
        {
            tickingLoop = player.PlayLoop(SoundID.TimerTicking, transform);

            if (tickingLoop == null)
            {
                return;
            }
        }

        // 0 when ticking starts, 1 when the clock hits zero.
        float urgency = 1f - Mathf.Clamp01(RemainingTime / Mathf.Max(0.01f, tickingStartsAt));
        player.SetLoopPitch(tickingLoop, Mathf.Lerp(tickingStartPitch, tickingEndPitch, urgency));
    }

    private void StopTicking()
    {
        if (tickingLoop == null)
        {
            return;
        }

        if (SoundPlayer.Instance != null)
        {
            SoundPlayer.Instance.StopLoop(tickingLoop);
        }

        tickingLoop = null;
    }
}