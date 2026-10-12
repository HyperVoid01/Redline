using UnityEngine;

[RequireComponent(typeof(Collider))]
public class GateTrigger : MonoBehaviour
{
    [SerializeField] private float timeBonus = 10f;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private GameObject mesh;

    private bool triggered;

    private void Reset()
    {
        // Makes sure a freshly added GateTrigger gets a trigger collider.
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
        mesh.SetActive(false);

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.AddTime(timeBonus);
        }
    }
}