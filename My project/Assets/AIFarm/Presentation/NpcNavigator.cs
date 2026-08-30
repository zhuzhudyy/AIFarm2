using System.Collections.Generic;
using AIFarm.Core;
using UnityEngine;
using UnityEngine.AI;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class NpcNavigator : MonoBehaviour, INpcNavigationDriver
    {
        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private PlotInteractionPoint[] interactionPoints = new PlotInteractionPoint[0];

        [SerializeField]
        private bool useNavMesh = true;

        [Min(0.01f)]
        [SerializeField]
        private float directMoveSpeed = 4f;

        [Min(0f)]
        [SerializeField]
        private float arrivalTolerance = 0.08f;

        [Min(0.1f)]
        [SerializeField]
        private float pathResolveTimeout = 2f;

        private PlotInteractionPoint currentTarget;
        private float pathResolveElapsed;
        private bool hasReceivedPath;

        public bool IsMoving { get; private set; }

        public bool UsesNavMesh => useNavMesh;

        public PlotInteractionPoint CurrentTarget => currentTarget;

        public ActionResult Configure(
            NavMeshAgent agent,
            PlotInteractionPoint[] points,
            bool requireNavMesh,
            float movementSpeed = 4f)
        {
            if (points == null || points.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC navigation requires at least one plot interaction point.");
            }

            if (requireNavMesh && agent == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NavMesh navigation requires a NavMeshAgent.");
            }

            if (float.IsNaN(movementSpeed) || float.IsInfinity(movementSpeed) || movementSpeed <= 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC movement speed must be finite and positive.");
            }

            var plotNumbers = new HashSet<int>();
            foreach (PlotInteractionPoint point in points)
            {
                if (point == null || !plotNumbers.Add(point.PlotNumber))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "NPC navigation points must be non-null and have unique plot numbers.");
                }
            }

            navMeshAgent = agent;
            interactionPoints = (PlotInteractionPoint[])points.Clone();
            useNavMesh = requireNavMesh;
            directMoveSpeed = movementSpeed;
            if (navMeshAgent != null)
            {
                navMeshAgent.speed = movementSpeed;
            }

            return ActionResult.Success("NPC navigation configured.");
        }

        public ActionResult BeginMove(int plotNumber)
        {
            if (IsMoving)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC navigation is already moving to an interaction point.");
            }

            PlotInteractionPoint target = FindInteractionPoint(plotNumber);
            if (target == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.NavigationFailed,
                    $"No interaction point is configured for Plot {plotNumber:00}.");
            }

            if (useNavMesh)
            {
                if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        "NPC cannot move because its NavMeshAgent is not on a NavMesh.");
                }

                if (!navMeshAgent.SetDestination(target.Position))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"NavMesh rejected the destination for Plot {plotNumber:00}.");
                }
            }

            currentTarget = target;
            pathResolveElapsed = 0f;
            hasReceivedPath = false;
            IsMoving = true;
            return ActionResult.Success($"Moving to Plot {plotNumber:00}.");
        }

        public ActionResult Tick(float deltaTime, out bool arrived)
        {
            arrived = false;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC navigation delta time must be finite and non-negative.");
            }

            if (!IsMoving || currentTarget == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC navigation has no active destination.");
            }

            ActionResult movement = useNavMesh
                ? TickNavMesh(deltaTime, out arrived)
                : TickDirect(deltaTime, out arrived);
            if (movement.Failed || !arrived)
            {
                return movement;
            }

            FinishMovement();
            return ActionResult.Success($"Reached Plot {currentTarget.PlotNumber:00} interaction point.");
        }

        public ActionResult CancelMove()
        {
            if (useNavMesh && navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
            }

            IsMoving = false;
            currentTarget = null;
            pathResolveElapsed = 0f;
            hasReceivedPath = false;
            return ActionResult.Success("NPC movement cancelled.");
        }

        private ActionResult TickDirect(float deltaTime, out bool arrived)
        {
            Vector3 destination = currentTarget.Position;
            Vector3 toDestination = destination - transform.position;
            Vector3 flatDirection = new Vector3(toDestination.x, 0f, toDestination.z);
            if (flatDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 720f * deltaTime);
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
                    "NPC left the NavMesh before reaching its destination.");
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
                if (pathResolveElapsed > pathResolveTimeout)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"Timed out while resolving the path to Plot {currentTarget.PlotNumber:00}.");
                }

                return ActionResult.Success();
            }

            if (navMeshAgent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                return ActionResult.Failure(
                    ActionFailureReason.NavigationFailed,
                    $"No complete NavMesh path exists to Plot {currentTarget.PlotNumber:00}.");
            }

            hasReceivedPath |= navMeshAgent.hasPath;
            if (!hasReceivedPath && directDistance > navMeshAgent.stoppingDistance + arrivalTolerance)
            {
                if (pathResolveElapsed > pathResolveTimeout)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"NavMesh did not produce a path to Plot {currentTarget.PlotNumber:00}.");
                }

                return ActionResult.Success();
            }

            arrived = navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance + arrivalTolerance &&
                navMeshAgent.velocity.sqrMagnitude <= 0.01f;
            return ActionResult.Success();
        }

        private void FinishMovement()
        {
            if (useNavMesh && navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
            }

            Vector3 facingDirection = currentTarget.FacingPosition - transform.position;
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
            }

            IsMoving = false;
        }

        private PlotInteractionPoint FindInteractionPoint(int plotNumber)
        {
            foreach (PlotInteractionPoint point in interactionPoints)
            {
                if (point != null && point.PlotNumber == plotNumber)
                {
                    return point;
                }
            }

            return null;
        }
    }
}
