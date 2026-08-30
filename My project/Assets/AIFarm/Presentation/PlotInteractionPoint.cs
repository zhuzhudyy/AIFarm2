using AIFarm.Core;
using AIFarm.Farming;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlotInteractionPoint : MonoBehaviour
    {
        [Range(1, FarmField.PlotCount)]
        [SerializeField]
        private int plotNumber = 1;

        [SerializeField]
        private Transform facingTarget;

        public int PlotNumber => plotNumber;

        public Vector3 Position => transform.position;

        public Vector3 FacingPosition => facingTarget == null ? transform.position : facingTarget.position;

        public ActionResult Configure(int targetPlotNumber, Transform targetToFace)
        {
            if (targetPlotNumber < 1 || targetPlotNumber > FarmField.PlotCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Interaction point plot number must be between 1 and {FarmField.PlotCount}.");
            }

            if (targetToFace == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "An interaction point requires a facing target.");
            }

            plotNumber = targetPlotNumber;
            facingTarget = targetToFace;
            return ActionResult.Success($"Configured interaction point for Plot {plotNumber:00}.");
        }
    }
}
