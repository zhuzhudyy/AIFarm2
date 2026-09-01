using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public enum AiRequestPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public sealed class AiRequestCancellation
    {
        public bool IsCancellationRequested { get; private set; }

        public void Cancel()
        {
            IsCancellationRequested = true;
        }
    }

    public sealed class AiRequestCoordinator : IAiGatewayClient
    {
        public const int DefaultMaximumConcurrentRequests = 2;

        private sealed class RequestTicket
        {
            public RequestTicket(
                long sequence,
                AiRequestPriority priority,
                IEnumerable<ResidentId> ownerResidentIds,
                AiRequestCancellation cancellation)
            {
                Sequence = sequence;
                Priority = priority;
                OwnerResidentIds = new List<ResidentId>(ownerResidentIds).ToArray();
                Cancellation = cancellation;
            }

            public long Sequence { get; }

            public AiRequestPriority Priority { get; }

            public ResidentId[] OwnerResidentIds { get; }

            public AiRequestCancellation Cancellation { get; }

            public bool IsActive { get; set; }

            public bool IsCancelled => Cancellation != null &&
                Cancellation.IsCancellationRequested;
        }

        private readonly IAiGatewayClient sharedClient;
        private readonly IAiGatewayClient localFallback;
        private readonly List<RequestTicket> pending = new List<RequestTicket>();
        private readonly HashSet<ResidentId> activeResidents = new HashSet<ResidentId>();
        private long nextSequence;
        private int activeRequestCount;
        private bool isShutdown;
        private AiGatewayMode activeMode;

        public AiRequestCoordinator(
            IAiGatewayClient gatewayClient,
            int maximumConcurrentRequests = DefaultMaximumConcurrentRequests,
            IAiGatewayClient deterministicLocalFallback = null)
        {
            if (maximumConcurrentRequests < 1 || maximumConcurrentRequests > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentRequests),
                    "AI concurrency must be between 1 and 8.");
            }

            sharedClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
            localFallback = deterministicLocalFallback ?? new LocalAiGatewayClient();
            MaximumConcurrentRequests = maximumConcurrentRequests;
            activeMode = sharedClient.ActiveMode;
        }

        public AiGatewayMode ConfiguredMode => sharedClient.ConfiguredMode;

        public AiGatewayMode ActiveMode
        {
            get
            {
                if (isShutdown || sharedClient.ActiveMode == AiGatewayMode.Local)
                {
                    return AiGatewayMode.Local;
                }

                return activeMode;
            }
        }

        public int MaximumConcurrentRequests { get; }

        public int ActiveRequestCount => activeRequestCount;

        public int PendingRequestCount
        {
            get
            {
                int count = 0;
                foreach (RequestTicket ticket in pending)
                {
                    if (!ticket.IsCancelled)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool IsShutdown => isShutdown;

        public void Shutdown()
        {
            isShutdown = true;
            activeMode = AiGatewayMode.Local;
        }

        public IEnumerator InterpretCommand(
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            return InterpretCommand(ResidentIds.Yaya, command, completed);
        }

        public IEnumerator InterpretCommand(
            ResidentId residentId,
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            return Coordinate(
                new[] { residentId },
                AiRequestPriority.High,
                cancellation: null,
                (client, callback) => client.InterpretCommand(
                    residentId,
                    command,
                    callback),
                completed);
        }

        public IEnumerator GenerateUtterance(
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed)
        {
            return GenerateUtterance(ResidentIds.Yaya, trigger, context, completed);
        }

        public IEnumerator GenerateUtterance(
            ResidentId residentId,
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed)
        {
            return Coordinate(
                new[] { residentId },
                AiRequestPriority.Normal,
                cancellation: null,
                (client, callback) => client.GenerateUtterance(
                    residentId,
                    trigger,
                    context,
                    callback),
                completed);
        }

        public IEnumerator Reflect(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            return Reflect(
                ResidentIds.Yaya,
                goal,
                outcome,
                eventSummary,
                completed);
        }

        public IEnumerator Reflect(
            ResidentId residentId,
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            return Coordinate(
                new[] { residentId },
                AiRequestPriority.Low,
                cancellation: null,
                (client, callback) => client.Reflect(
                    residentId,
                    goal,
                    outcome,
                    eventSummary,
                    callback),
                completed);
        }

        public IEnumerator GenerateConversationScript(
            ConversationScriptRequest request,
            Action<AiGatewayResult<ConversationScriptSpec>> completed)
        {
            return GenerateConversationScript(
                request,
                AiRequestPriority.Normal,
                cancellation: null,
                completed);
        }

        public IEnumerator GenerateConversationScript(
            ConversationScriptRequest request,
            AiRequestPriority priority,
            AiRequestCancellation cancellation,
            Action<AiGatewayResult<ConversationScriptSpec>> completed)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Coordinate(
                request.ParticipantIds,
                priority,
                cancellation,
                (client, callback) => client.GenerateConversationScript(request, callback),
                completed);
        }

        private IEnumerator Coordinate<T>(
            IEnumerable<ResidentId> ownerResidentIds,
            AiRequestPriority priority,
            AiRequestCancellation cancellation,
            Func<IAiGatewayClient, Action<AiGatewayResult<T>>, IEnumerator> operation,
            Action<AiGatewayResult<T>> completed)
            where T : class
        {
            if (ownerResidentIds == null)
            {
                throw new ArgumentNullException(nameof(ownerResidentIds));
            }

            if (!Enum.IsDefined(typeof(AiRequestPriority), priority))
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }

            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            var uniqueOwners = new HashSet<ResidentId>();
            foreach (ResidentId residentId in ownerResidentIds)
            {
                if (!residentId.IsValid || !uniqueOwners.Add(residentId))
                {
                    throw new ArgumentException(
                        "AI requests require unique valid owner ResidentIds.",
                        nameof(ownerResidentIds));
                }
            }

            if (uniqueOwners.Count == 0)
            {
                throw new ArgumentException(
                    "AI requests require at least one owner.",
                    nameof(ownerResidentIds));
            }

            var ticket = new RequestTicket(
                ++nextSequence,
                priority,
                uniqueOwners,
                cancellation);
            pending.Add(ticket);

            // The first yield ensures a transport is never launched from an Update() call
            // that happened to enqueue this request. It also lets same-frame priorities settle.
            yield return null;

            while (!ticket.IsCancelled && !CanStart(ticket))
            {
                yield return null;
            }

            if (ticket.IsCancelled)
            {
                pending.Remove(ticket);
                yield break;
            }

            Activate(ticket);
            IAiGatewayClient selectedClient = isShutdown ? localFallback : sharedClient;
            AiGatewayResult<T> result = null;
            Exception operationException = null;
            IEnumerator routine = null;
            try
            {
                routine = operation(selectedClient, value => result = value);
            }
            catch (Exception exception)
            {
                operationException = exception;
            }

            if (operationException == null && routine != null)
            {
                while (true)
                {
                    bool moved = false;
                    object current = null;
                    try
                    {
                        moved = routine.MoveNext();
                        if (moved)
                        {
                            current = routine.Current;
                        }
                    }
                    catch (Exception exception)
                    {
                        operationException = exception;
                    }

                    if (operationException != null || !moved)
                    {
                        break;
                    }

                    yield return current;
                }
            }

            if (isShutdown && selectedClient != localFallback && !ticket.IsCancelled)
            {
                result = null;
                operationException = null;
                IEnumerator fallbackRoutine = null;
                try
                {
                    fallbackRoutine = operation(localFallback, value => result = value);
                }
                catch (Exception exception)
                {
                    operationException = exception;
                }

                if (operationException == null && fallbackRoutine != null)
                {
                    while (true)
                    {
                        bool moved = false;
                        object current = null;
                        try
                        {
                            moved = fallbackRoutine.MoveNext();
                            if (moved)
                            {
                                current = fallbackRoutine.Current;
                            }
                        }
                        catch (Exception exception)
                        {
                            operationException = exception;
                        }

                        if (operationException != null || !moved)
                        {
                            break;
                        }

                        yield return current;
                    }
                }
            }

            Release(ticket);
            if (ticket.IsCancelled)
            {
                yield break;
            }

            ResidentId resultOwner = ticket.OwnerResidentIds[0];
            if (operationException != null)
            {
                result = AiGatewayResult<T>.Failure(
                    resultOwner,
                    ActionResult.Failure(
                        ActionFailureReason.ServiceUnavailable,
                        $"AI request failed safely: {operationException.Message}"),
                    AiGatewayMode.Local);
            }
            else if (result == null)
            {
                result = AiGatewayResult<T>.Failure(
                    resultOwner,
                    ActionResult.Failure(
                        ActionFailureReason.ServiceUnavailable,
                        "AI request completed without a result."),
                    AiGatewayMode.Local);
            }

            activeMode = result.Source;
            completed(result);
        }

        private bool CanStart(RequestTicket candidate)
        {
            if (activeRequestCount >= MaximumConcurrentRequests ||
                candidate.IsActive || candidate.IsCancelled ||
                HasActiveOwner(candidate))
            {
                return false;
            }

            RequestTicket best = null;
            foreach (RequestTicket ticket in pending)
            {
                if (ticket.IsActive || ticket.IsCancelled || HasActiveOwner(ticket))
                {
                    continue;
                }

                if (best == null || ticket.Priority > best.Priority ||
                    (ticket.Priority == best.Priority && ticket.Sequence < best.Sequence))
                {
                    best = ticket;
                }
            }

            return ReferenceEquals(best, candidate);
        }

        private bool HasActiveOwner(RequestTicket ticket)
        {
            foreach (ResidentId residentId in ticket.OwnerResidentIds)
            {
                if (activeResidents.Contains(residentId))
                {
                    return true;
                }
            }

            return false;
        }

        private void Activate(RequestTicket ticket)
        {
            pending.Remove(ticket);
            ticket.IsActive = true;
            activeRequestCount++;
            foreach (ResidentId residentId in ticket.OwnerResidentIds)
            {
                activeResidents.Add(residentId);
            }
        }

        private void Release(RequestTicket ticket)
        {
            if (!ticket.IsActive)
            {
                pending.Remove(ticket);
                return;
            }

            ticket.IsActive = false;
            activeRequestCount = Math.Max(0, activeRequestCount - 1);
            foreach (ResidentId residentId in ticket.OwnerResidentIds)
            {
                activeResidents.Remove(residentId);
            }
        }
    }
}
