using AIFarm.Core;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ConversationAnchor : MonoBehaviour
    {
        [SerializeField]
        private string anchorId;

        [SerializeField]
        private LocationArrivalPoint firstStandPoint;

        [SerializeField]
        private LocationArrivalPoint secondStandPoint;

        public string AnchorId => anchorId ?? string.Empty;

        public LocationArrivalPoint FirstStandPoint => firstStandPoint;

        public LocationArrivalPoint SecondStandPoint => secondStandPoint;

        public TownLocationId LocationId => firstStandPoint == null
            ? default
            : firstStandPoint.LocationId;

        public ActionResult Configure(
            string stableAnchorId,
            LocationArrivalPoint firstPoint,
            LocationArrivalPoint secondPoint)
        {
            if (string.IsNullOrWhiteSpace(stableAnchorId) || firstPoint == null ||
                secondPoint == null || !firstPoint.LocationId.IsValid ||
                firstPoint.LocationId != secondPoint.LocationId ||
                string.Equals(
                    firstPoint.InteractionPointId,
                    secondPoint.InteractionPointId,
                    System.StringComparison.Ordinal))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A conversation anchor requires two distinct points at one location.");
            }

            anchorId = stableAnchorId.Trim();
            firstStandPoint = firstPoint;
            secondStandPoint = secondPoint;
            return ActionResult.Success($"Conversation anchor '{anchorId}' configured.");
        }
    }
}
