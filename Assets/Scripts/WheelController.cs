using UnityEngine;

public class WheelController : MonoBehaviour
{
    [SerializeField] private WheelCollider wheelCollider;
    [Tooltip("The empty parent that holds the mesh. Leave empty to use this object.")]
    [SerializeField] private Transform wheelMesh;
    [Tooltip("Keep the offset between the mesh and collider at start. Leave off for a pivot parent.")]
    [SerializeField] private bool preserveStartOffset = false;

    private Quaternion rotationOffset = Quaternion.identity;
    private Vector3 positionOffset = Vector3.zero;

    private void Awake()
    {
        if (wheelMesh == null)
        {
            wheelMesh = transform;
        }
    }

    private void Start()
    {
        if (!preserveStartOffset)
        {
            return;
        }

        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        rotationOffset = Quaternion.Inverse(rot) * wheelMesh.rotation;
        positionOffset = Quaternion.Inverse(rot) * (wheelMesh.position - pos);
    }

    private void LateUpdate()
    {
        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        wheelMesh.SetPositionAndRotation(pos + rot * positionOffset, rot * rotationOffset);
    }
}