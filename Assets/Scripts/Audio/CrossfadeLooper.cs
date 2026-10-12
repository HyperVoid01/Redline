using UnityEngine;

public class CrossfadeLooper : MonoBehaviour
{
    private AudioSource a, b;
    private bool aActive = true;
    private AudioClip clip;
    private float fade;

    public float Volume { get; set; }
    public float Pitch { get; set; } = 1f;

    public void Init(AudioClip c, float fadeSeconds, float spatialBlend, float minDist, float maxDist)
    {
        clip = c;
        // Keep the fade well under half the clip length.
        fade = Mathf.Min(fadeSeconds, c.length * 0.4f);
        a = Make("A", spatialBlend, minDist, maxDist);
        b = Make("B", spatialBlend, minDist, maxDist);

        a.time = Random.value * clip.length * 0.9f;
        a.Play();
    }

    private AudioSource Make(string n, float blend, float minDist, float maxDist)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = false;
        s.playOnAwake = false;
        s.spatialBlend = blend;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = minDist;
        s.maxDistance = maxDist;
        s.volume = 0f;
        return s;
    }

    private void Update()
    {
        AudioSource active = aActive ? a : b;
        AudioSource other = aActive ? b : a;

        if (!active.isPlaying)
        {
            // Active voice finished (or a frame was skipped): hand over.
            aActive = !aActive;
            active.volume = 0f;
            if (!other.isPlaying) { other.time = 0f; other.Play(); }
            return;
        }

        float remaining = clip.length - active.time;
        float gainActive = 1f, gainOther = 0f;

        if (remaining <= fade)
        {
            if (!other.isPlaying) { other.time = 0f; other.Play(); }

            float k = 1f - Mathf.Clamp01(remaining / fade);
            gainActive = Mathf.Cos(k * Mathf.PI * 0.5f);
            gainOther = Mathf.Sin(k * Mathf.PI * 0.5f);
        }

        active.pitch = other.pitch = Pitch;
        active.volume = Volume * gainActive;
        other.volume = Volume * gainOther;
    }
}