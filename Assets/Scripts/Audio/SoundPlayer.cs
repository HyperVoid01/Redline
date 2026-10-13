using System.Collections.Generic;
using UnityEngine;

public class SoundPlayer : MonoBehaviour
{
    public static SoundPlayer Instance { get; private set; }

    [Header("Sound Library")]
    [SerializeField] private SoundLibrary soundLibrary;

    [Header("Settings")]
    [SerializeField] private SettingsData settingsData;

    [Header("Sound Settings")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;

    [Header("3D Sound")]
    [Tooltip("Full volume inside this distance.")]
    [SerializeField] private float minDistance = 5f;
    [Tooltip("Silent beyond this distance. Keep it modest or the enemy car is audible forever.")]
    [SerializeField] private float maxDistance = 80f;
    [SerializeField, Range(0f, 5f)] private float dopplerLevel = 1f;

    [Header("One-Shot Pool")]
    [Tooltip("Max simultaneous one-shot sounds. Sounds are skipped if all are busy.")]
    [SerializeField] private int maxOneShots = 32;

    // Volume pieces for a running loop. Final volume =
    // level (set by code, e.g. skid slip) * entryVolume (SoundLibrary) * settings volume.
    private class LoopState
    {
        public float level = 1f;
        public float entryVolume = 1f;
    }

    private readonly List<AudioSource> oneShotPool = new List<AudioSource>();
    private readonly Dictionary<AudioSource, LoopState> loops = new Dictionary<AudioSource, LoopState>();
    private readonly List<AudioSource> deadLoops = new List<AudioSource>();

    // Final SFX volume from the settings: local multiplier * main * sfx
    private float EffectiveVolume
    {
        get
        {
            if (settingsData == null)
                return volume;

            return volume * settingsData.mainVolume * settingsData.sfxVolume;
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
    }

    private void Update()
    {
        // Loops are long-lived, so keep them in sync with the settings sliders.
        CleanupLoops();

        float master = EffectiveVolume;

        foreach (KeyValuePair<AudioSource, LoopState> pair in loops)
            pair.Key.volume = pair.Value.level * pair.Value.entryVolume * master;
    }

    // ---------- One-shot sounds ----------

    /// <summary>Plays a sound at a world position (3D unless the library entry is marked 2D).</summary>
    public void PlaySound(SoundID soundID, Vector3 location)
    {
        PlayOneShot(soundID, location, false);
    }

    /// <summary>Plays a sound with no position: UI clicks, chimes, countdown beeps, game over.</summary>
    public void PlaySound2D(SoundID soundID)
    {
        PlayOneShot(soundID, Vector3.zero, true);
    }

    private void PlayOneShot(SoundID soundID, Vector3 location, bool force2D)
    {
        SoundLibrary.SoundEntry entry = GetEntry(soundID);
        AudioClip clip = PickClip(entry);

        if (clip == null)
            return;

        AudioSource source = GetPooledSource();

        if (source == null)
            return;

        source.transform.position = location;
        source.clip = clip;
        source.volume = EffectiveVolume * entry.volume;
        source.pitch = 1f + Random.Range(-entry.pitchVariation, entry.pitchVariation);
        source.spatialBlend = (force2D || entry.play2D) ? 0f : spatialBlend;
        source.loop = false;
        source.Play();
    }

    private AudioSource GetPooledSource()
    {
        foreach (AudioSource pooled in oneShotPool)
        {
            if (!pooled.isPlaying)
                return pooled;
        }

        if (oneShotPool.Count >= maxOneShots)
            return null;

        GameObject child = new GameObject("One-Shot Sound");
        child.transform.SetParent(transform, false);

        AudioSource source = child.AddComponent<AudioSource>();
        ConfigureSpatial(source);
        oneShotPool.Add(source);

        return source;
    }

    // ---------- Looped sounds ----------

    /// <summary>
    /// Starts a looping sound at a fixed position.
    /// Keep the returned AudioSource to pause, resume or stop it later.
    /// </summary>
    public AudioSource PlayLoop(SoundID soundID, Vector3 location)
    {
        return CreateLoop(soundID, location, null);
    }

    /// <summary>
    /// Starts a looping sound that follows the given transform
    /// (e.g. an engine hum on a car).
    /// </summary>
    public AudioSource PlayLoop(SoundID soundID, Transform followTarget)
    {
        if (followTarget == null)
        {
            Debug.LogWarning("PlayLoop called with a null follow target.");
            return null;
        }

        return CreateLoop(soundID, followTarget.position, followTarget);
    }

    /// <summary>
    /// Sets a loop's level (0-1), e.g. skid squeal from slip or wind from speed.
    /// The library volume for the clip and the settings volumes are applied on top.
    /// </summary>
    public void SetLoopVolume(AudioSource loop, float level)
    {
        if (loop == null || !loops.TryGetValue(loop, out LoopState state))
            return;

        state.level = Mathf.Clamp01(level);
        loop.volume = state.level * state.entryVolume * EffectiveVolume;
    }

    /// <summary>Sets a loop's pitch, e.g. the timer speeding up.</summary>
    public void SetLoopPitch(AudioSource loop, float pitch)
    {
        if (loop == null)
            return;

        loop.pitch = pitch;
    }

    /// <summary>Pauses a loop. Resume it with ResumeLoop.</summary>
    public void PauseLoop(AudioSource loop)
    {
        if (loop == null)
            return;

        loop.Pause();
    }

    /// <summary>Resumes a loop that was paused with PauseLoop.</summary>
    public void ResumeLoop(AudioSource loop)
    {
        if (loop == null)
            return;

        loop.UnPause();
    }

    /// <summary>Stops a loop and destroys it. The handle is invalid afterwards.</summary>
    public void StopLoop(AudioSource loop)
    {
        if (loop == null)
            return;

        loops.Remove(loop);
        loop.Stop();
        Destroy(loop.gameObject);
    }

    public void PauseAllLoops()
    {
        CleanupLoops();

        foreach (AudioSource loop in loops.Keys)
            loop.Pause();
    }

    public void ResumeAllLoops()
    {
        CleanupLoops();

        foreach (AudioSource loop in loops.Keys)
            loop.UnPause();
    }

    public void StopAllLoops()
    {
        foreach (AudioSource loop in loops.Keys)
        {
            if (loop == null)
                continue;

            loop.Stop();
            Destroy(loop.gameObject);
        }

        loops.Clear();
    }

    // ---------- Helpers ----------

    private AudioSource CreateLoop(SoundID soundID, Vector3 location, Transform parent)
    {
        SoundLibrary.SoundEntry entry = GetEntry(soundID);
        AudioClip clip = PickClip(entry);

        if (clip == null)
            return null;

        GameObject soundObject = new GameObject($"Looping Sound ({soundID})");
        soundObject.transform.position = location;

        if (parent != null)
            soundObject.transform.SetParent(parent, true);

        AudioSource source = soundObject.AddComponent<AudioSource>();
        ConfigureSpatial(source);

        source.clip = clip;
        source.loop = true;
        source.volume = EffectiveVolume * entry.volume;
        source.spatialBlend = entry.play2D ? 0f : spatialBlend;
        source.Play();

        loops.Add(source, new LoopState { entryVolume = entry.volume });
        return source;
    }

    private void ConfigureSpatial(AudioSource source)
    {
        source.playOnAwake = false;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = dopplerLevel;
    }

    private SoundLibrary.SoundEntry GetEntry(SoundID soundID)
    {
        if (soundLibrary == null)
        {
            Debug.LogWarning("SoundPlayer has no SoundLibrary assigned.");
            return null;
        }

        return soundLibrary.GetEntry(soundID);
    }

    private AudioClip PickClip(SoundLibrary.SoundEntry entry)
    {
        if (soundLibrary == null || entry == null)
            return null;

        return soundLibrary.PickClip(entry);
    }

    // Removes loops whose GameObjects were destroyed elsewhere
    // (e.g. a followed car was destroyed).
    private void CleanupLoops()
    {
        deadLoops.Clear();

        foreach (AudioSource loop in loops.Keys)
        {
            if (loop == null)
                deadLoops.Add(loop);
        }

        foreach (AudioSource dead in deadLoops)
            loops.Remove(dead);
    }
}