using UnityEngine;

/// <summary>
/// Camera and sound effects for crossing the finish line: a FOV kick, a camera shake and a sting.
/// Put one on the finish prefab or anywhere in the scene. It listens to GameManager.RaceFinished.
/// Slow motion lives on the GameManager, the controller rumble on CarRumble, and the confetti on FinishTrigger.
/// </summary>
public class FinishCelebration : MonoBehaviour
{
    [Header("FOV kick")]
    [SerializeField] private bool fovKick = true;
    [Tooltip("Extra degrees of field of view at the peak of the kick.")]
    [SerializeField] private float fovKickDegrees = 10f;
    [Tooltip("Real seconds to reach the peak.")]
    [SerializeField] private float fovKickInTime = 0.15f;
    [Tooltip("Real seconds to ease back.")]
    [SerializeField] private float fovReturnTime = 1.2f;

    [Header("Camera shake")]
    [SerializeField] private bool cameraShake = true;
    [Tooltip("Shake strength, 0 to 1. Same scale as the death shake.")]
    [SerializeField, Range(0f, 1f)] private float shakeStrength = 0.35f;

    [Header("Sound")]
    [SerializeField] private bool playSting = true;
    [Tooltip("A short crowd, horn or fanfare sound for crossing the line.")]
    [SerializeField] private AudioClip stingClip;
    [SerializeField, Range(0f, 1f)] private float stingVolume = 1f;

    private AudioSource source;

    // FOV state
    private bool kicking;
    private float kickStart;
    private Camera kickCamera;
    private float baseFov;
    private float lastSetFov = -1f;

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D
    }

    private void OnEnable()
    {
        GameManager.RaceFinished += OnRaceFinished;
    }

    private void OnDisable()
    {
        GameManager.RaceFinished -= OnRaceFinished;
    }

    private void OnRaceFinished()
    {
        if (fovKick)
        {
            kicking = true;
            kickStart = Time.unscaledTime;
            kickCamera = null;
        }

        if (cameraShake)
            ShakeCameras();

        if (playSting && stingClip != null)
            source.PlayOneShot(stingClip, stingVolume * GetSfxVolume());
    }

    // Same cameras PlayerHealth shakes on death.
    private void ShakeCameras()
    {
        if (shakeStrength <= 0f)
            return;

        CameraController chase = FindFirstObjectByType<CameraController>();
        if (chase != null)
            chase.AddShake(shakeStrength);

        InteriorCameraController interior = FindFirstObjectByType<InteriorCameraController>(FindObjectsInactive.Include);
        if (interior != null)
            interior.AddShake(shakeStrength);
    }

    // Master and SFX volume from the settings menu, 1 if there is no SettingsManager.
    private static float GetSfxVolume()
    {
        SettingsData settings = SettingsManager.Current;
        return settings != null ? settings.mainVolume * settings.sfxVolume : 1f;
    }

    // LateUpdate so it lands after the camera scripts. If a camera script writes its own FOV,
    // the kick is added on top of whatever it wrote.
    private void LateUpdate()
    {
        if (!kicking)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        float elapsed = Time.unscaledTime - kickStart;
        float inTime = Mathf.Max(0.01f, fovKickInTime);
        float returnTime = Mathf.Max(0.01f, fovReturnTime);

        float offset = elapsed < inTime
            ? Mathf.SmoothStep(0f, fovKickDegrees, elapsed / inTime)
            : Mathf.SmoothStep(fovKickDegrees, 0f, (elapsed - inTime) / returnTime);

        // Work out the FOV the camera would have without the kick.
        if (cam != kickCamera)
        {
            kickCamera = cam;
            baseFov = cam.fieldOfView;
        }
        else if (!Mathf.Approximately(cam.fieldOfView, lastSetFov))
        {
            // Something else changed the FOV this frame, so that is the new base.
            baseFov = cam.fieldOfView;
        }

        if (elapsed >= inTime + returnTime)
        {
            cam.fieldOfView = baseFov;
            kicking = false;
            return;
        }

        cam.fieldOfView = baseFov + offset;
        lastSetFov = cam.fieldOfView;
    }
}