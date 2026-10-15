using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Plays the click sound when the mouse hovers a button, and when a controller or keyboard moves onto it.
/// Put it on every button or slider that should make the sound.
/// </summary>
public class UIHoverSound : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    /// <summary>
    /// Set to Time.frameCount just before selecting a control from code, so that selection doesn't play a sound.
    /// Only a player moving onto a control should make a noise.
    /// </summary>
    public static int SuppressFrame = -1;

    [SerializeField] private SoundID sound = SoundID.Click;

    private Selectable selectable;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Play();
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (Time.frameCount == SuppressFrame)
            return;

        Play();
    }

    private void Play()
    {
        // No sound over a disabled button.
        if (selectable != null && !selectable.IsInteractable())
            return;

        if (SoundPlayer.Instance != null)
            SoundPlayer.Instance.PlaySound(sound, Vector3.zero);
    }
}