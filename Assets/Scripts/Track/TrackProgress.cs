using UnityEngine;

/// <summary>
/// Put this on the player car. Tracks how far along the track the car is.
/// The enemy uses it to know how far ahead the player is; you can also use it
/// for a progress bar or a finish line (Normalized reaches 1 at the end of the track).
/// </summary>
public class TrackProgress : MonoBehaviour
{
    public TrackSampler sampler;
    [Tooltip("How far either side of the last position to search. Must exceed the distance the car travels per frame.")]
    public float searchWindow = 60f;

    public float Distance { get; private set; }
    public bool IsReady { get; private set; }

    public float Normalized
    {
        get { return sampler != null && sampler.IsBuilt ? Distance / sampler.Length : 0f; }
    }

    int lastVersion = -1;

    void Update()
    {
        if (sampler == null || !sampler.IsBuilt)
        {
            IsReady = false;
            return;
        }

        // First frame after the track is built (or rebuilt): do one full search.
        if (!IsReady || lastVersion != sampler.Version)
        {
            Distance = sampler.FindDistance(transform.position);
            lastVersion = sampler.Version;
            IsReady = true;
            return;
        }

        Distance = sampler.FindDistance(transform.position, Distance, searchWindow);
    }

    /// <summary>Call after teleporting or respawning the car.</summary>
    public void ResetProgress()
    {
        IsReady = false;
    }
}
