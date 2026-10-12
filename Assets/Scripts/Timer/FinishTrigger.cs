using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FinishTrigger : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    private bool triggered;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered)
        {
            return;
        }

        // Use the Rigidbody's object so any child collider on the car counts.
        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        if (!root.CompareTag(playerTag))
        {
            return;
        }

        triggered = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.WinGame();
        }
    }
}