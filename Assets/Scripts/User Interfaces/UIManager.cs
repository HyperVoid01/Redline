using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private GameObject gameOverPanel;

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

    public void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }

        Instance = this;
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

    public void UpdateSpeed(int speed)
    {
        lastSpeedString = speed + " km/h";
        SetText(speedText, extraSpeedTexts, lastSpeedString);
    }

    public void UpdateTime(int totalSeconds)
    {
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        lastTimeString = $"{minutes:00}:{seconds:00}";
        SetText(timerText, extraTimerTexts, lastTimeString);
    }

    public void ShowGameOver(bool show)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(show);
        }
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