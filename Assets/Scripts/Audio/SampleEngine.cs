using UnityEngine;

public class SampleEngine : MonoBehaviour
{
    [System.Serializable]
    public class Band
    {
        [Tooltip("Looping clip played while the throttle is pressed.")]
        public AudioClip onThrottle;
        [Tooltip("Optional looping clip for lifting off the throttle.")]
        public AudioClip offThrottle;
        [Tooltip("Approximate RPM this clip was recorded at.")]
        public float rpm = 1000f;
    }

    [SerializeField] private Band[] bands;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField] private float minPitch = 0.6f;
    [SerializeField] private float maxPitch = 1.6f;
    [Tooltip("How quickly the on/off-throttle blend reacts.")]
    [SerializeField] private float loadResponse = 6f;
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
    [SerializeField] private float minDistance = 3f;
    [SerializeField] private float maxDistance = 40f;

    [SerializeField] private float loopFade = 0.1f;
    private CrossfadeLooper[] onSources;
    private CrossfadeLooper[] offSources;
    private float rpm;
    private float load;
    private float targetLoad;

    private const float HalfPi = Mathf.PI * 0.5f;

    private void Awake()
    {
        if (bands == null || bands.Length == 0)
        {
            Debug.LogError($"{name}: SampleEngine has no bands assigned.", this);
            enabled = false;
            return;
        }

        // Bands must be ordered low to high RPM.
        System.Array.Sort(bands, (a, b) => a.rpm.CompareTo(b.rpm));

        onSources = new CrossfadeLooper[bands.Length];
        offSources = new CrossfadeLooper[bands.Length];

        for (int i = 0; i < bands.Length; i++)
        {
            onSources[i] = CreateLooper(bands[i].onThrottle, $"EngineOn_{i}");
            offSources[i] = bands[i].offThrottle != null
                ? CreateLooper(bands[i].offThrottle, $"EngineOff_{i}")
                : null;
        }

        rpm = bands[0].rpm;
    }

    /// <summary>Called every frame by the driver.</summary>
    public void SetState(float newRpm, float throttle)
    {
        rpm = newRpm;
        targetLoad = Mathf.Clamp01(throttle);
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;

        if (gm != null)
        {
            // Game over / win froze the game: mute the engine completely.
            if (gm.IsFrozen)
            {
                Silence();
                return;
            }

            // Paused: AudioListener.pause holds the sound, leave everything as it is.
            if (gm.IsPaused)
                return;
        }

        load = Mathf.Lerp(load, targetLoad, 1f - Mathf.Exp(-loadResponse * Time.deltaTime));

        // Find the two bands surrounding the current RPM.
        int lo = 0;
        for (int i = 0; i < bands.Length; i++)
        {
            if (bands[i].rpm <= rpm) lo = i;
        }

        int hi = Mathf.Min(lo + 1, bands.Length - 1);
        float t = hi == lo
            ? 0f
            : Mathf.Clamp01(Mathf.InverseLerp(bands[lo].rpm, bands[hi].rpm, rpm));

        // Constant-power crossfade keeps loudness steady between bands.
        float wLo = Mathf.Cos(t * HalfPi);
        float wHi = Mathf.Sin(t * HalfPi);

        for (int i = 0; i < bands.Length; i++)
        {
            float w = 0f;
            if (i == lo) w = wLo;
            else if (i == hi) w = wHi;

            ApplyBand(i, w);
        }
    }

    private void Silence()
    {
        for (int i = 0; i < bands.Length; i++)
        {
            onSources[i].Volume = 0f;

            if (offSources[i] != null)
                offSources[i].Volume = 0f;
        }
    }

    private void ApplyBand(int i, float weight)
    {
        float pitch = Mathf.Clamp(rpm / Mathf.Max(1f, bands[i].rpm), minPitch, maxPitch);
        bool hasOff = offSources[i] != null;

        float onGain = hasOff ? Mathf.Sin(load * HalfPi) : 1f;
        float offGain = Mathf.Cos(load * HalfPi);

        onSources[i].Pitch = pitch;
        onSources[i].Volume = volume * weight * onGain;

        if (hasOff)
        {
            offSources[i].Pitch = pitch;
            offSources[i].Volume = volume * weight * offGain;
        }
    }

    private CrossfadeLooper CreateLooper(AudioClip clip, string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var looper = go.AddComponent<CrossfadeLooper>();
        looper.Init(clip, loopFade, spatialBlend, minDistance, maxDistance);
        return looper;
    }
}