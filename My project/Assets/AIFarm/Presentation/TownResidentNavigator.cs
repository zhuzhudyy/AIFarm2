using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Town;
using UnityEngine;
using UnityEngine.AI;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class TownResidentNavigator : MonoBehaviour, IResidentLocationNavigator
    {
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private LocationArrivalPoint[] arrivalPoints = Array.Empty<LocationArrivalPoint>();

        [SerializeField]
        private bool useNavMesh = true;

        [Min(0.01f)]
        [SerializeField]
        private float directMoveSpeed = 3.2f;

        [Min(0f)]
        [SerializeField]
        private float arrivalTolerance = 0.08f;

        [Min(0.1f)]
        [SerializeField]
        private float pathResolveTimeout = 2f;

        private LocationArrivalPoint currentTarget;
        private float pathResolveElapsed;
        private bool hasReceivedPath;
        private bool triedSynchronousPathRecovery;

        public bool IsMoving { get; private set; }

        public bool UsesNavMesh => useNavMesh;

        public LocationArrivalPoint CurrentTarget => currentTarget;

        public int PathRecoveryCount { get; private set; }

        public ActionResult Configure(
            NavMeshAgent agent,
            LocationArrivalPoint[] points,
            bool requireNavMesh,
            float movementSpeed = 3.2f)
        {
            if (points == null || points.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Resident navigation requires at least one town arrival point.");
            }

            if (requireNavMesh && agent == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NavMesh navigation requires a NavMeshAgent.");
            }

            if (float.IsNaN(movementSpeed) || float.IsInfinity(movementSpeed) ||
                movementSpeed <= 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Resident movement speed must be finite and positive.");
            }

            var pointIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (LocationArrivalPoint point in points)
            {
                if (point == null || !point.LocationId.IsValid ||
                    string.IsNullOrWhiteSpace(point.InteractionPointId) ||
                    !pointIds.Add(point.InteractionPointId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "Town arrival points must be complete and have unique IDs.");
                }
            }

            navMeshAgent = agent;
            arrivalPoints = (LocationArrivalPoint[])points.Clone();
            useNavMesh = requireNavMesh;
            directMoveSpeed = movementSpeed;
            if (navMeshAgent != null)
            {
                navMeshAgent.speed = movementSpeed;
            }

            return ActionResult.Success("Resident town navigation configured.");
        }

        public ActionResult BeginMove(string interactionPointId)
        {
            if (IsMoving)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Resident navigation is already moving.");
            }

            LocationArrivalPoint target = FindPoint(interactionPointId);
            if (target == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.NavigationFailed,
                    $"No arrival point named '{interactionPointId}' is configured.");
            }

            if (useNavMesh)
            {
                if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        "Resident is not placed on a NavMesh.");
                }

                if (!navMeshAgent.SetDestination(target.Position))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"NavMesh rejected arrival point '{interactionPointId}'.");
                }
            }

            currentTarget = target;
            pathResolveElapsed = 0f;
            hasReceivedPath = false;
            triedSynchronousPathRecovery = false;
            IsMoving = true;
            return ActionResult.Success($"Moving to '{interactionPointId}'.");
        }

        public ActionResult Tick(float deltaTime, out bool arrived)
        {
            arrived = false;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Resident navigation delta time must be finite and non-negative.");
            }

            if (!IsMoving || currentTarget == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Resident navigation has no active destination.");
            }

            ActionResult movement = useNavMesh
                ? TickNavMesh(deltaTime, out arrived)
                : TickDirect(deltaTime, out arrived);
            if (movement.Failed || !arrived)
            {
                return movement;
            }

            FinishMovement();
            return ActionResult.Success(
                $"Reached arrival point '{currentTarget.InteractionPointId}'.");
        }

        public ActionResult CancelMove()
        {
            if (useNavMesh && navMeshAgent != null && navMeshAgent.enabled &&
                navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
            }

            IsMoving = false;
            currentTarget = null;
            pathResolveElapsed = 0f;
            hasReceivedPath = false;
            triedSynchronousPathRecovery = false;
            return ActionResult.Success("Resident movement cancelled.");
        }

        private ActionResult TickDirect(float deltaTime, out bool arrived)
        {
            Vector3 destination = currentTarget.Position;
            Vector3 toDestination = destination - transform.position;
            Vector3 flatDirection = new Vector3(toDestination.x, 0f, toDestination.z);
            if (flatDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    rotation,
                    720f * deltaTime);
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                destination,
                directMoveSpeed * deltaTime);
            arrived = Vector3.Distance(transform.position, destination) <= arrivalTolerance;
            if (arrived)
            {
                transform.position = destination;
            }

            return ActionResult.Success();
        }

        private ActionResult TickNavMesh(float deltaTime, out bool arrived)
        {
            arrived = false;
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                return ActionResult.Failure(
                    ActionFailureReason.NavigationFailed,
                    "Resident left the NavMesh before arrival.");
            }

            float directDistance = Vector3.Distance(transform.position, currentTarget.Position);
            if (directDistance <= navMeshAgent.stoppingDistance + arrivalTolerance)
            {
                arrived = true;
                return ActionResult.Success();
            }

            pathResolveElapsed += deltaTime;
            if (navMeshAgent.pathPending)
            {
                return pathResolveElapsed > pathResolveTimeout
                    ? ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"Path resolution timed out for '{currentTarget.InteractionPointId}'.")
                    : ActionResult.Success();
            }

            if (navMeshAgent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                return ActionResult.Failure(
                    ActionFailureReason.NavigationFailed,
                    $"No complete path exists to '{currentTarget.InteractionPointId}'.");
            }

            hasReceivedPath |= navMeshAgent.hasPath;
            if (!hasReceivedPath &&
                directDistance > navMeshAgent.stoppingDistance + arrivalTolerance)
            {
                // Unity 6000.5 can accept SetDestination immediately after ResetPath while
                // producing neither a pending request nor a path. Resolve exactly once on
                // a subsequent tick; never teleport or accept an incomplete route.
                if (!triedSynchronousPathRecovery)
                {
                    triedSynchronousPathRecovery = true;
                    var recoveredPath = new NavMeshPath();
                    if (!NavMesh.SamplePosition(currentTarget.Position, out NavMeshHit targetHit,
                            0.5f, navMeshAgent.areaMask) ||
                        !navMeshAgent.CalculatePath(targetHit.position, recoveredPath) ||
                        recoveredPath.status != NavMeshPathStatus.PathComplete ||
                        !navMeshAgent.SetPath(recoveredPath))
                    {
                        return ActionResult.Failure(ActionFailureReason.NavigationFailed,
                            $"No complete recovery path exists to '{currentTarget.InteractionPointId}'.");
                    }
                    PathRecoveryCount++;
                    hasReceivedPath = navMeshAgent.hasPath;
                    return ActionResult.Success();
                }
                return pathResolveElapsed > pathResolveTimeout
                    ? ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"NavMesh produced no path to '{currentTarget.InteractionPointId}'. " +
                        $"position={transform.position:F3}, target={currentTarget.Position:F3}, " +
                        $"destination={navMeshAgent.destination:F3}, distance={directDistance:F3}, stopped={navMeshAgent.isStopped}.")
                    : ActionResult.Success();
            }

            arrived = navMeshAgent.remainingDistance <=
                navMeshAgent.stoppingDistance + arrivalTolerance &&
                navMeshAgent.velocity.sqrMagnitude <= 0.01f;
            return ActionResult.Success();
        }

        private void FinishMovement()
        {
            if (useNavMesh && navMeshAgent != null && navMeshAgent.enabled &&
                navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
            }

            Vector3 facing = currentTarget.FacingPosition - transform.position;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            }

            IsMoving = false;
        }

        private LocationArrivalPoint FindPoint(string interactionPointId)
        {
            if (string.IsNullOrWhiteSpace(interactionPointId))
            {
                return null;
            }

            foreach (LocationArrivalPoint point in arrivalPoints)
            {
                if (point != null && string.Equals(
                        point.InteractionPointId,
                        interactionPointId,
                        StringComparison.Ordinal))
                {
                    return point;
                }
            }

            return null;
        }
    }
}
