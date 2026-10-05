using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private GameObject _cameraPosition;
    [SerializeField] private float cameraSpeed = 0.5f;

    private void Start()
    {
        transform.parent = null;
        //UIManager.Instance.cam = this.GetComponent<Camera>();
    }
    
    private void Update()
    {
        transform.position = Vector3.Slerp(transform.position, _cameraPosition.transform.position, Time.deltaTime * cameraSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, _cameraPosition.transform.rotation, Time.deltaTime * cameraSpeed);
    }
}
