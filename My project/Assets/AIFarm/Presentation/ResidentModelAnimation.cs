using UnityEngine;
using UnityEngine.AI;

namespace AIFarm.Presentation
{
    /// <summary>
    /// Adds breathing and a velocity-driven gait to a resident model. Attach to the
    /// imported model below the existing action visual; navigation remains on its owner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResidentModelAnimation : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent navigationAgent;

        private Transform leftLeg;
        private Transform rightLeg;
        private Transform leftArm;
        private Transform rightArm;
        private Quaternion leftLegRest;
        private Quaternion rightLegRest;
        private Quaternion leftArmRest;
        private Quaternion rightArmRest;
        private Vector3 restPosition;
        private Vector3 restScale;
        private Vector3 localUpAxis;
        private float gaitPhase;
        private float motionBlend;
        private bool capturedRestPose;

        private void Awake()
        {
            CaptureRestPose();
        }

        private void LateUpdate()
        {
            CaptureRestPose();
            float speed = navigationAgent != null && navigationAgent.enabled && navigationAgent.isOnNavMesh
                ? navigationAgent.velocity.magnitude
                : 0f;
            float targetBlend = Mathf.Clamp01(speed / 2.4f);
            motionBlend = Mathf.MoveTowards(motionBlend, targetBlend, UnityEngine.Time.deltaTime * 6f);
            gaitPhase += speed * UnityEngine.Time.deltaTime * 3.8f;
            float stride = Mathf.Sin(gaitPhase) * motionBlend;

            ApplyLimb(leftLeg, leftLegRest, stride * 24f);
            ApplyLimb(rightLeg, rightLegRest, -stride * 24f);
            ApplyLimb(leftArm, leftArmRest, -stride * 17f);
            ApplyLimb(rightArm, rightArmRest, stride * 17f);

            float breath = Mathf.Sin(UnityEngine.Time.time * 1.8f) * 0.006f * (1f - motionBlend);
            transform.localScale = Vector3.Scale(restScale, Vector3.one + localUpAxis * breath);
            transform.localPosition = restPosition +
                Vector3.up * (Mathf.Abs(Mathf.Sin(gaitPhase)) * motionBlend * 0.025f);
        }

        private void OnDisable()
        {
            if (!capturedRestPose)
            {
                return;
            }

            ApplyLimb(leftLeg, leftLegRest, 0f);
            ApplyLimb(rightLeg, rightLegRest, 0f);
            ApplyLimb(leftArm, leftArmRest, 0f);
            ApplyLimb(rightArm, rightArmRest, 0f);
            transform.localPosition = restPosition;
            transform.localScale = restScale;
            motionBlend = 0f;
        }

        private void CaptureRestPose()
        {
            if (capturedRestPose)
            {
                return;
            }

            if (navigationAgent == null)
            {
                navigationAgent = GetComponentInParent<NavMeshAgent>();
            }

            foreach (Transform part in GetComponentsInChildren<Transform>(true))
            {
                switch (part.name)
                {
                    case "LeftLeg":
                        leftLeg = part;
                        leftLegRest = part.localRotation;
                        break;
                    case "RightLeg":
                        rightLeg = part;
                        rightLegRest = part.localRotation;
                        break;
                    case "LeftArm":
                        leftArm = part;
                        leftArmRest = part.localRotation;
                        break;
                    case "RightArm":
                        rightArm = part;
                        rightArmRest = part.localRotation;
                        break;
                }
            }

            restPosition = transform.localPosition;
            restScale = transform.localScale;
            Vector3 up = transform.InverseTransformDirection(Vector3.up);
            localUpAxis = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
            capturedRestPose = true;
        }

        private static void ApplyLimb(Transform limb, Quaternion rest, float angle)
        {
            if (limb != null)
            {
                limb.localRotation = rest * Quaternion.Euler(angle, 0f, 0f);
            }
        }
    }
}
