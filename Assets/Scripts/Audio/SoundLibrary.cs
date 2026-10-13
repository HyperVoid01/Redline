using System.Collections.Generic;
using UnityEngine;

// IMPORTANT: new IDs are only ever added at the END of each enum.
// Unity stores enums as numbers, so reordering or removing entries would
// scramble the clips already assigned in your SoundLibrary asset.
public enum SoundID
{
    Click,
    Crash,
    Checkpoint,
    MachineGun,
    EnemyCarEngine,
    TimerTicking,
    BulletImpact,
    GearShift,
    CarImpact,
    TireSkid,
    SpeedChime,

    // ----- added -----
    PlayerEngine,     // unused: the player engine is handled by SampleEngine
    BarrierScrape,    // looping, car scraping a steel barrier
    BulletWhiz,       // near miss
    GameOver,
    CountdownBeep,
    Wind,             // looping, volume driven by speed
    NightAmbience     // looping crickets/owls/wind in trees (2D)
}

public enum MusicID
{
    MainMenu,
    Gameplay,
    AmbientNature
}

[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Audio/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [System.Serializable]
    public class SoundEntry
    {
        public SoundID id;
        public AudioClip clip;

        [Tooltip("Optional extra clips. One is picked at random each time the sound plays. They all share this entry's volume.")]
        public AudioClip[] variations;

        [Tooltip("Volume of this sound. Applied on top of the Main and SFX volume settings, so use it to balance loud and quiet clips against each other.")]
        [Range(0f, 1f)] public float volume = 1f;

        [Tooltip("Random pitch change per play (0 = none). 0.1 is a good start for gunfire and impacts.")]
        [Range(0f, 0.5f)] public float pitchVariation;

        [Tooltip("Tick for sounds that are not in the world: UI, timer, chimes, wind.")]
        public bool play2D;
    }

    [System.Serializable]
    public class MusicEntry
    {
        public MusicID id;
        public AudioClip clip;

        [Tooltip("Volume of this track. Applied on top of the Main and Music volume settings.")]
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("Sound Effects")]
    public SoundEntry[] sounds;

    [Header("Music")]
    public MusicEntry[] music;

    // Used to give entries that existed before per-clip volume was added a volume of 1,
    // instead of leaving them silent.
    [SerializeField, HideInInspector] private int dataVersion;

    private Dictionary<SoundID, SoundEntry> soundLookup;
    private Dictionary<MusicID, MusicEntry> musicLookup;

    // Warn once per missing ID instead of every call (a missing machine gun
    // sound would otherwise spam the console dozens of times a second).
    private readonly HashSet<string> warned = new HashSet<string>();

    private void OnEnable()
    {
        MigrateData();
        ClearLookups();
    }

    private void OnValidate()
    {
        MigrateData();
        ClearLookups();
    }

    private void MigrateData()
    {
        if (dataVersion >= 1)
            return;

        if (sounds != null)
        {
            foreach (SoundEntry entry in sounds)
            {
                if (entry != null)
                    entry.volume = 1f;
            }
        }

        if (music != null)
        {
            foreach (MusicEntry entry in music)
            {
                if (entry != null)
                    entry.volume = 1f;
            }
        }

        dataVersion = 1;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    private void ClearLookups()
    {
        soundLookup = null;
        musicLookup = null;
        warned.Clear();
    }

    private void EnsureLookups()
    {
        if (soundLookup != null && musicLookup != null)
            return;

        soundLookup = new Dictionary<SoundID, SoundEntry>();
        musicLookup = new Dictionary<MusicID, MusicEntry>();

        if (sounds != null)
        {
            foreach (SoundEntry entry in sounds)
            {
                if (entry == null)
                    continue;

                if (soundLookup.ContainsKey(entry.id))
                {
                    Debug.LogWarning("Duplicate sound entry in SoundLibrary: " + entry.id);
                    continue;
                }

                soundLookup.Add(entry.id, entry);
            }
        }

        if (music != null)
        {
            foreach (MusicEntry entry in music)
            {
                if (entry == null)
                    continue;

                if (musicLookup.ContainsKey(entry.id))
                {
                    Debug.LogWarning("Duplicate music entry in SoundLibrary: " + entry.id);
                    continue;
                }

                musicLookup.Add(entry.id, entry);
            }
        }
    }

    // ---------- Sound effects ----------

    /// <summary>Returns the entry for a sound ID, or null if it is not in the library.</summary>
    public SoundEntry GetEntry(SoundID id)
    {
        EnsureLookups();

        if (soundLookup.TryGetValue(id, out SoundEntry entry))
            return entry;

        WarnOnce("Sound not found: " + id);
        return null;
    }

    /// <summary>Picks the main clip or one of the variations at random.</summary>
    public AudioClip PickClip(SoundEntry entry)
    {
        if (entry == null)
            return null;

        int extra = entry.variations != null ? entry.variations.Length : 0;
        int total = extra + (entry.clip != null ? 1 : 0);

        if (total == 0)
        {
            WarnOnce("Sound has no clip assigned: " + entry.id);
            return null;
        }

        int index = Random.Range(0, total);

        if (entry.clip != null)
        {
            if (index == 0)
                return entry.clip;

            index--;
        }

        return entry.variations[index];
    }

    // Kept so existing calls still work.
    public AudioClip GetSound(SoundID id)
    {
        return PickClip(GetEntry(id));
    }

    // ---------- Music ----------

    /// <summary>Returns the entry for a music ID, or null if it is not in the library.</summary>
    public MusicEntry GetMusicEntry(MusicID id)
    {
        EnsureLookups();

        if (musicLookup.TryGetValue(id, out MusicEntry entry))
            return entry;

        WarnOnce("Music not found: " + id);
        return null;
    }

    // Kept so existing calls still work.
    public AudioClip GetMusic(MusicID id)
    {
        MusicEntry entry = GetMusicEntry(id);
        return entry != null ? entry.clip : null;
    }

    private void WarnOnce(string message)
    {
        if (warned.Add(message))
            Debug.LogWarning(message);
    }
}