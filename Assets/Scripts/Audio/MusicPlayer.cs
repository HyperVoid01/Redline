using System.Collections.Generic;
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

    [Header("Ducking")]
    [Tooltip("Music volume multiplier once the game is over. 0.25 = a quarter of normal volume.")]
    [SerializeField, Range(0f, 1f)] private float gameOverVolume = 0.25f;
    [Tooltip("Also lower the music when the player wins.")]
    [SerializeField] private bool duckOnWin = true;
    [Tooltip("Music volume multiplier while the game is paused. 1 = no change.")]
    [SerializeField, Range(0f, 1f)] private float pausedVolume = 1f;
    [Tooltip("Seconds the music takes to fade to the lowered volume and back.")]
    [SerializeField] private float duckFadeTime = 1f;

    private AudioSource audioSource;

    // Volume of the current track from the SoundLibrary.
    private float clipVolume = 1f;

    // Current ducking multiplier, eased toward its target every frame.
    private float duck = 1f;

    // Playlist state. A playlist is any MusicID with more than one entry in the library.
    private MusicID currentId;
    private bool hasCurrent;
    private bool playlistMode;
    private bool pausedByCode;
    private readonly List<SoundLibrary.MusicEntry> playlist = new List<SoundLibrary.MusicEntry>();
    private readonly List<SoundLibrary.MusicEntry> queue = new List<SoundLibrary.MusicEntry>();
    private SoundLibrary.MusicEntry lastPlayed;

    // Final music volume: local multiplier * main * music * this track's library volume * ducking
    private float EffectiveVolume
    {
        get
        {
            if (settingsData == null)
                return volume * clipVolume * duck;

            return volume * settingsData.mainVolume * settingsData.musicVolume * clipVolume * duck;
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

        // AudioListener.pause (used by the pause menu) must not hold the music.
        audioSource.ignoreListenerPause = true;

        audioSource.volume = EffectiveVolume;
    }

    private void Start()
    {
        if (playOnStart)
            PlayMusic(startingSong);
    }

    private void Update()
    {
        UpdateDuck();

        // Keeps the music in sync with the settings sliders and the ducking.
        float current = EffectiveVolume;

        if (!Mathf.Approximately(audioSource.volume, current))
            audioSource.volume = current;

        // Playlist: when the track ends, start the next one.
        // Music keeps playing while the game is paused, so only PauseMusic() holds it.
        if (playlistMode && !pausedByCode && !audioSource.isPlaying)
            PlayNextInPlaylist();
    }

    // Eases the duck toward what the game state asks for. Unscaled time, because the game
    // is frozen at timeScale 0 after game over and the fade still has to run.
    private void UpdateDuck()
    {
        float target = 1f;
        GameManager gm = GameManager.Instance;

        if (gm != null)
        {
            if (gm.IsGameOver && (duckOnWin || !gm.IsWon))
                target = gameOverVolume;
            else if (gm.IsPaused)
                target = pausedVolume;
        }

        if (duckFadeTime <= 0f)
        {
            duck = target;
            return;
        }

        float step = Time.unscaledDeltaTime / duckFadeTime;
        duck = Mathf.MoveTowards(duck, target, step);
    }

    /// <summary>
    /// Plays the music for this ID. One entry loops. Several entries with the same ID
    /// play in random order, moving on to the next track when one finishes.
    /// </summary>
    public void PlayMusic(MusicID musicID)
    {
        if (soundLibrary == null)
        {
            Debug.LogWarning("MusicPlayer has no SoundLibrary assigned.");
            return;
        }

        // Collect the usable entries for this ID.
        playlist.Clear();

        foreach (SoundLibrary.MusicEntry entry in soundLibrary.GetMusicEntries(musicID))
        {
            if (entry != null && entry.clip != null)
                playlist.Add(entry);
        }

        if (playlist.Count == 0)
            return;

        // Don't restart the same music unnecessarily.
        if (hasCurrent && currentId == musicID && audioSource.isPlaying)
            return;

        currentId = musicID;
        hasCurrent = true;
        pausedByCode = false;
        lastPlayed = null;
        queue.Clear();

        if (playlist.Count == 1)
        {
            playlistMode = false;
            PlayEntry(playlist[0], true);
        }
        else
        {
            playlistMode = true;
            PlayNextInPlaylist();
        }
    }

    public void StopMusic()
    {
        playlistMode = false;
        hasCurrent = false;
        pausedByCode = false;
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
        pausedByCode = true;
        audioSource.Pause();
    }

    public void ResumeMusic()
    {
        pausedByCode = false;
        audioSource.UnPause();
    }

    // ---------- Playlist ----------

    private void PlayNextInPlaylist()
    {
        if (playlist.Count == 0)
        {
            playlistMode = false;
            return;
        }

        if (queue.Count == 0)
            RefillQueue();

        // Take from the end of the shuffled queue.
        SoundLibrary.MusicEntry next = queue[queue.Count - 1];
        queue.RemoveAt(queue.Count - 1);

        PlayEntry(next, false);
    }

    // Shuffles every track into the queue. Each plays once before any repeats.
    private void RefillQueue()
    {
        queue.Clear();
        queue.AddRange(playlist);

        // Fisher-Yates shuffle.
        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            SoundLibrary.MusicEntry temp = queue[i];
            queue[i] = queue[j];
            queue[j] = temp;
        }

        // The next track is taken from the end, so make sure it isn't the one that just played.
        int last = queue.Count - 1;

        if (queue.Count > 1 && queue[last] == lastPlayed)
        {
            SoundLibrary.MusicEntry temp = queue[last];
            queue[last] = queue[0];
            queue[0] = temp;
        }
    }

    private void PlayEntry(SoundLibrary.MusicEntry entry, bool loop)
    {
        lastPlayed = entry;
        clipVolume = entry.volume;

        audioSource.clip = entry.clip;
        audioSource.volume = EffectiveVolume;
        audioSource.loop = loop;
        audioSource.Play();
    }
}