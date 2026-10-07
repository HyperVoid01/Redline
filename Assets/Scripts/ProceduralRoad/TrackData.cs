using UnityEngine;

[CreateAssetMenu(fileName = "NewTrackData", menuName = "Scriptable Objects/Track Data")]
public class TrackData : ScriptableObject
{
    [Header("Length")]
    public float totalLength = 3000f;
    public float startStraightLength = 60f;
    public float finishStraightLength = 60f;

    [Header("Road")]
    public float roadWidth = 12f;
    public Material roadMaterial;
    [Tooltip("Metres of road per texture repeat along the track (UV v).")]
    public float uvTileLength = 12f;
    [Tooltip("Distance between mesh cross-sections. Smaller = smoother corners, more vertices.")]
    public float sampleSpacing = 2f;

    [Header("Spline")]
    [Tooltip("Distance between spline knots. Clamped automatically so tight corners stay smooth.")]
    public float knotSpacing = 8f;

    [Header("Section Weights")]
    public float straightWeight = 3f;
    public float bendWeight = 4f;
    public float sharpTurnWeight = 2f;

    [Header("Straights (x = min, y = max)")]
    public Vector2 straightLength = new Vector2(60f, 200f);

    [Header("Bends (x = min, y = max)")]
    public Vector2 bendAngle = new Vector2(20f, 60f);
    public Vector2 bendRadius = new Vector2(80f, 150f);

    [Header("Sharp Turns (x = min, y = max)")]
    public Vector2 sharpAngle = new Vector2(70f, 120f);
    public Vector2 sharpRadius = new Vector2(30f, 50f);

    [Header("Layout")]
    [Tooltip("How far (degrees) the track may swing away from its starting direction. Keeps it snaking forward instead of looping back on itself.")]
    public float maxHeadingDrift = 140f;

    [Header("Gates (optional)")]
    public GameObject gatePrefab;
    public float firstGateDistance = 150f;
    public float gateSpacing = 250f;

    private void OnValidate()
    {
        totalLength = Mathf.Max(100f, totalLength);
        startStraightLength = Mathf.Max(0f, startStraightLength);
        finishStraightLength = Mathf.Max(0f, finishStraightLength);
        roadWidth = Mathf.Max(2f, roadWidth);
        uvTileLength = Mathf.Max(1f, uvTileLength);
        sampleSpacing = Mathf.Clamp(sampleSpacing, 0.5f, 10f);
        maxHeadingDrift = Mathf.Clamp(maxHeadingDrift, 30f, 360f);

        straightWeight = Mathf.Max(0f, straightWeight);
        bendWeight = Mathf.Max(0f, bendWeight);
        sharpTurnWeight = Mathf.Max(0f, sharpTurnWeight);
        if (straightWeight + bendWeight + sharpTurnWeight <= 0f)
        {
            straightWeight = 1f;
        }

        // A turn tighter than half the road width would fold the inside edge of the mesh over itself.
        float minRadius = roadWidth * 0.5f + 2f;
        straightLength = ClampRange(straightLength, 10f);
        bendAngle = ClampRange(bendAngle, 5f);
        sharpAngle = ClampRange(sharpAngle, 5f);
        bendRadius = ClampRange(bendRadius, minRadius);
        sharpRadius = ClampRange(sharpRadius, minRadius);

        // Knots must be close together relative to the tightest corner or the spline cuts corners.
        float tightest = Mathf.Min(bendRadius.x, sharpRadius.x);
        knotSpacing = Mathf.Clamp(knotSpacing, 2f, Mathf.Max(2f, tightest * 0.35f));

        gateSpacing = Mathf.Max(0f, gateSpacing);
        firstGateDistance = Mathf.Max(0f, firstGateDistance);
    }

    private static Vector2 ClampRange(Vector2 range, float minimum)
    {
        range.x = Mathf.Max(range.x, minimum);
        range.y = Mathf.Max(range.y, range.x);
        return range;
    }
}