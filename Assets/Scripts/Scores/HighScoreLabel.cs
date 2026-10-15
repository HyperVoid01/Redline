using TMPro;
using UnityEngine;

/// <summary>
/// Shows the saved best score. Put it on a TMP text in the main menu.
/// It reads the saved value directly, so no ScoreManager is needed in the menu scene.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class HighScoreLabel : MonoBehaviour
{
    [TextArea] [SerializeField] private string prefix = "Best: ";

    private TMP_Text label;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    // OnEnable, so it refreshes every time the menu is shown.
    private void OnEnable()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();

        label.text = prefix + ScoreManager.Format(ScoreManager.SavedHighScore);
    }
}