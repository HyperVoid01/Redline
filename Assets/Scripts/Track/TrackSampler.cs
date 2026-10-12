using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Samples the track spline into an evenly spaced lookup table.
/// Everything that needs "where is the road at distance D" or "how far along the road is this car"
/// (enemy car, player progress, gates, finish line) asks this instead of evaluating the spline every frame.
///
/// Call Build() once after TrackMaker has finished adding knots. Calling it again rebuilds the table.
/// </summary>
public class TrackSampler : MonoBehaviour
{
    public SplineContainer track;          // keep this container at scale 1
    public int splineIndex = 0;
    public float sampleSpacing = 1f;

    Vector3[] positions;
    Vector3[] tangents;
    float spacing;

    public bool IsBuilt { get; private set; }
    public float Length { get; private set; }
    public int Version { get; private set; }   // increases on every Build(), so followers can reset

    public bool Build()
    {
        IsBuilt = false;

        if (track == null)
        {
            Debug.LogError("TrackSampler: assign the track SplineContainer.", this);
            return false;
        }

        if (splineIndex < 0 || splineIndex >= track.Splines.Count || track.Splines[splineIndex].Count < 2)
        {
            Debug.LogWarning("TrackSampler: the spline doesn't exist yet or has fewer than 2 knots. Call Build() after TrackMaker finishes.", this);
            return false;
        }

        Length = track.CalculateLength(splineIndex);

        if (float.IsNaN(Length) || Length < sampleSpacing * 2f)
        {
            Debug.LogWarning("TrackSampler: the track is too short or not built yet (length " + Length + ").", this);
            return false;
        }

        int count = Mathf.CeilToInt(Length / Mathf.Max(0.1f, sampleSpacing)) + 1;
        spacing = Length / (count - 1);

        positions = new Vector3[count];
        tangents = new Vector3[count];

        Spline spline = track.Splines[splineIndex];

        for (int i = 0; i < count; i++)
        {
            float distance = Length * i / (count - 1);
            float t = SplineUtility.GetNormalizedInterpolation(spline, distance, PathIndexUnit.Distance);
            track.Evaluate(splineIndex, t, out float3 p, out float3 tan, out float3 up);

            positions[i] = p;

            Vector3 tangent = tan;
            if (tangent.sqrMagnitude < 1e-6f)
                tangent = i > 0 ? tangents[i - 1] : Vector3.forward;
            tangents[i] = tangent.normalized;
        }

        Version++;
        IsBuilt = true;
        return true;
    }

    /// <summary>Road centre position and travel direction (including slope) at a distance along the track.</summary>
    public void Sample(float distance, out Vector3 position, out Vector3 forward)
    {
        float f = Mathf.Clamp(distance, 0f, Length) / spacing;
        int i = Mathf.Min((int)f, positions.Length - 2);
        float t = f - i;

        position = Vector3.Lerp(positions[i], positions[i + 1], t);
        forward = Vector3.Lerp(tangents[i], tangents[i + 1], t).normalized;
    }

    /// <summary>Full search: where along the track is this point? Use once to initialise.</summary>
    public float FindDistance(Vector3 worldPos)
    {
        return SearchRange(worldPos, 0, positions.Length - 1);
    }

    /// <summary>
    /// Windowed search around a previous distance. Because it only looks nearby, it can't jump to a
    /// different stretch of road where the track bends back on itself.
    /// </summary>
    public float FindDistance(Vector3 worldPos, float hintDistance, float window)
    {
        int centre = Mathf.RoundToInt(hintDistance / spacing);
        int range = Mathf.CeilToInt(window / spacing);
        int lo = Mathf.Max(0, centre - range);
        int hi = Mathf.Min(positions.Length - 1, centre + range);
        return SearchRange(worldPos, lo, hi);
    }

    float SearchRange(Vector3 worldPos, int lo, int hi)
    {
        float bestSqr = float.MaxValue;
        int best = lo;

        for (int i = lo; i <= hi; i++)
        {
            float sqr = (positions[i] - worldPos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = i;
            }
        }

        return best * spacing;
    }
}
