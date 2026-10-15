using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds the saved best score and a reset button to the ScoreManager inspector,
/// and the same two actions under the Tools menu.
/// </summary>
[CustomEditor(typeof(ScoreManager))]
public class ScoreManagerEditor : Editor
{
    // Must match HighScoreKey in ScoreManager.
    private const string HighScoreKey = "Redline_HighScore";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("High Score (saved)", EditorStyles.boldLabel);

        int saved = PlayerPrefs.GetInt(HighScoreKey, 0);
        EditorGUILayout.LabelField("Saved best", ScoreManager.Format(saved));

        if (Application.isPlaying)
        {
            ScoreManager scores = (ScoreManager)target;
            EditorGUILayout.LabelField("Current run score", ScoreManager.Format(scores.Score));
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Reset Saved High Score"))
            ResetHighScore();

        // Keep the saved value updating while the game runs.
        if (Application.isPlaying)
            Repaint();
    }

    [MenuItem("Tools/Redline/Show High Score")]
    private static void ShowHighScore()
    {
        int saved = PlayerPrefs.GetInt(HighScoreKey, 0);
        EditorUtility.DisplayDialog("High Score", "Saved best: " + ScoreManager.Format(saved), "OK");
    }

    [MenuItem("Tools/Redline/Reset High Score")]
    private static void ResetHighScore()
    {
        if (!EditorUtility.DisplayDialog(
                "Reset High Score",
                "Delete the saved high score?",
                "Reset",
                "Cancel"))
        {
            return;
        }

        PlayerPrefs.DeleteKey(HighScoreKey);
        PlayerPrefs.Save();
    }
}