using UnityEngine;

public class MusicPlayer : MonoBehaviour
{
    public static MusicPlayer Instance { get; private set; }

    [Header("Sound Library")]
    [SerializeField] private SoundLibrary soundLibrary;

    [Header("Settings")]
    [SerializeField] private SettingsData settingsData;

    [Header("Music Settings")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private bool playOnStart = false;
    [SerializeField] private MusicID startingSong = MusicID.MainMenu;

    private AudioSource audioSource;

    // Volume of the current track from the SoundLibrary.
    private float clipVolume = 1f;

    // Final music volume: local multiplier * main * music * this track's library volume
    private float EffectiveVolume
    {
        get
        {
            if (settingsData == null)
                return volume * clipVolume;

            return volume * settingsData.mainVolume * settingsData.musicVolume * clipVolume;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = EffectiveVolume;
    }

    private void Start()
    {
        if (playOnStart)
            PlayMusic(startingSong);
    }

    private void Update()
    {
        // Keeps the music in sync when the player moves a slider in the settings menu.
        float current = EffectiveVolume;

        if (!Mathf.Approximately(audioSource.volume, current))
            audioSource.volume = current;
    }

    public void PlayMusic(MusicID musicID)
    {
        if (soundLibrary == null)
        {
            Debug.LogWarning("MusicPlayer has no SoundLibrary assigned.");
            return;
        }

        SoundLibrary.MusicEntry entry = soundLibrary.GetMusicEntry(musicID);

        if (entry == null || entry.clip == null)
            return;

        // Don't restart the same song unnecessarily.
        if (audioSource.clip == entry.clip && audioSource.isPlaying)
            return;

        clipVolume = entry.volume;

        audioSource.clip = entry.clip;
        audioSource.volume = EffectiveVolume;
        audioSource.loop = true;
        audioSource.Play();
    }

    public void StopMusic()
    {
        audioSource.Stop();
    }

    /// <summary>
    /// Sets this player's local multiplier. The settings volumes and the track's
    /// library volume still apply on top.
    /// </summary>
    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        audioSource.volume = EffectiveVolume;
    }

    public void PauseMusic()
    {
        audioSource.Pause();
    }

    public void ResumeMusic()
    {
        audioSource.UnPause();
    }
}