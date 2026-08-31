using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class WorldSpaceBillboard : MonoBehaviour
    {
        [SerializeField]
        private Camera targetCamera;

        private void LateUpdate()
        {
            Camera cameraToFace = targetCamera == null ? Camera.main : targetCamera;
            if (cameraToFace == null)
            {
                return;
            }

            Vector3 direction = transform.position - cameraToFace.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
    }
}
