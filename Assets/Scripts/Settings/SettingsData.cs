using UnityEngine;

public enum SpeedUnit
{
    Kmh,
    Mph
}

/// <summary>
/// Holds the current value of every setting. SettingsManager loads, changes, saves and applies them.
/// Other scripts read from here (through SettingsManager.Current).
/// </summary>
[CreateAssetMenu(fileName = "SettingsData", menuName = "Scriptable Objects/Settings Data")]
public class SettingsData : ScriptableObject
{
    [Header("Audio")]
    [Range(0f, 1f)] public float mainVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 0.7f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Controls")]
    [Tooltip("Multiplies mouse look. 1 = normal.")]
    [Range(0.1f, 3f)] public float mouseSensitivity = 1f;
    [Tooltip("Multiplies gamepad look. 1 = normal.")]
    [Range(0.1f, 3f)] public float stickSensitivity = 1f;
    public bool invertLookY = false;
    [Tooltip("How quickly the steering reacts to input. Multiplies CarData.steerSmoothing. Higher = snappier.")]
    [Range(0.5f, 2f)] public float steeringResponse = 1f;
    [Tooltip("Strength of the automatic counter-steer in drifts. Multiplies CarData.counterSteerAssist. 0 = off.")]
    [Range(0f, 2f)] public float steeringAssist = 1f;
    [Tooltip("Controller vibration strength. Multiplies all rumble. 0 = off, 1 = full.")]
    [Range(0f, 1f)] public float rumbleStrength = 1f;

    [Header("Interface")]
    public SpeedUnit speedUnit = SpeedUnit.Kmh;

    [Header("Graphics")]
    [Tooltip("Index into the project's quality levels. -1 = leave whatever the project is set to.")]
    public int qualityLevel = -1;
    public bool fullscreen = true;
    [Tooltip("0 = use the current screen size.")]
    public int resolutionWidth = 0;
    public int resolutionHeight = 0;
    [Tooltip("When on, VSync sets the frame rate and the cap below is ignored.")]
    public bool vsync = true;
    [Tooltip("-1 = unlimited.")]
    public int targetFrameRate = -1;

    /// <summary>Puts every setting back to its starting value.</summary>
    public void ResetToDefaults()
    {
        mainVolume = 1f;
        musicVolume = 0.7f;
        sfxVolume = 1f;

        mouseSensitivity = 1f;
        stickSensitivity = 1f;
        invertLookY = false;
        steeringResponse = 1f;
        steeringAssist = 1f;
        rumbleStrength = 1f;

        speedUnit = SpeedUnit.Kmh;

        qualityLevel = -1;
        fullscreen = true;
        resolutionWidth = 0;
        resolutionHeight = 0;
        vsync = true;
        targetFrameRate = -1;
    }
}