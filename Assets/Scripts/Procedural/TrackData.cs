using UnityEngine;

[CreateAssetMenu(fileName = "NewTrackData", menuName = "Scriptable Objects/Track Data")]
public class TrackData : ScriptableObject
{
    [Header("Length")]
    [Tooltip("Horizontal length to generate. The real spline is slightly longer once hills are added.")]
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

    [Header("Elevation")]
    [Tooltip("Maximum height above or below the start (metres). 0 = flat track. The start straight stays flat.")]
    public float hillAmplitude = 10f;
    [Tooltip("Rough distance (metres) of one hill-and-valley cycle.")]
    public float hillWavelength = 350f;
    [Tooltip("Steepest allowed slope as a percentage (8 = 8% grade). Higher values risk launching the car off crests.")]
    public float maxGradePercent = 8f;

    [Header("Barriers")]
    public bool generateBarriers = true;
    public GameObject barrierPrefab;
    [Tooltip("Length of one barrier piece along the road, in metres. Check the model's mesh bounds.")]
    public float barrierSegmentLength = 4f;
    [Tooltip("Distance from the road edge to the barrier's pivot line. Negative values move the barrier onto the road.")]
    public float barrierSideOffset = 0.5f;
    [Tooltip("Raise or lower the barrier, for example if the model's pivot is not at its base.")]
    public float barrierHeightOffset = 0f;
    [Tooltip("Rotates the model so its length runs along the road. Use 90 if it is modelled along X, or 180 to flip which way it faces.")]
    public float barrierYawOffset = 0f;

    [Header("Gates (optional)")]
    public GameObject gatePrefab;
    public float firstGateDistance = 150f;
    public float gateSpacing = 250f;
    
    [Header("Finish Platform")]
    public bool generateFinishPlatform = true;
    [Tooltip("How far the platform extends past the end of the road, in metres.")]
    public float finishPlatformLength = 30f;
    [Tooltip("Extra width on each side of the road.")]
    public float finishPlatformSideMargin = 6f;
    [Tooltip("Slab thickness. Make it thicker if the land beneath the end of the road looks too low.")]
    public float finishPlatformThickness = 3f;
    [Tooltip("Optional. Falls back to the road material.")]
    public Material finishPlatformMaterial;
    [Tooltip("Optional decoration placed in the middle of the platform, e.g. a finish arch.")]
    public GameObject finishDecorPrefab;

    private void OnValidate()
    {
        totalLength = Mathf.Max(100f, totalLength);
        startStraightLength = Mathf.Max(0f, startStraightLength);
        finishStraightLength = Mathf.Max(0f, finishStraightLength);
        roadWidth = Mathf.Max(2f, roadWidth);
        uvTileLength = Mathf.Max(1f, uvTileLength);
        sampleSpacing = Mathf.Clamp(sampleSpacing, 0.5f, 10f);
        maxHeadingDrift = Mathf.Clamp(maxHeadingDrift, 30f, 360f);

        hillAmplitude = Mathf.Max(0f, hillAmplitude);
        hillWavelength = Mathf.Max(50f, hillWavelength);
        maxGradePercent = Mathf.Clamp(maxGradePercent, 0f, 20f);

        barrierSegmentLength = Mathf.Max(0.5f, barrierSegmentLength);

        // Negative offsets pull the barrier onto the road. Stop it before it reaches the centre line.
        barrierSideOffset = Mathf.Max(barrierSideOffset, -roadWidth * 0.5f + 0.5f);

        straightWeight = Mathf.Max(0f, straightWeight);
        bendWeight = Mathf.Max(0f, bendWeight);
        sharpTurnWeight = Mathf.Max(0f, sharpTurnWeight);
        if (straightWeight + bendWeight + sharpTurnWeight <= 0f)
        {
            straightWeight = 1f;
        }

        // A turn tighter than the road plus barriers would fold the inside edge of the mesh over itself.
        // Only an outward offset widens the footprint, so a negative offset doesn't loosen the limit.
        float minRadius = roadWidth * 0.5f + Mathf.Max(barrierSideOffset, 0f) + 2f;
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
        
        finishPlatformLength = Mathf.Max(5f, finishPlatformLength);
        finishPlatformSideMargin = Mathf.Max(0f, finishPlatformSideMargin);
        finishPlatformThickness = Mathf.Max(0.5f, finishPlatformThickness);
    }

    private static Vector2 ClampRange(Vector2 range, float minimum)
    {
        range.x = Mathf.Max(range.x, minimum);
        range.y = Mathf.Max(range.y, range.x);
        return range;
    }
}