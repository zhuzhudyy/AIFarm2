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

    public sealed class AiRequestCoordinator : IAiGatewayClient, IResidentTaskGateway
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

            public IEnumerator ActiveRoutine { get; set; }

            public bool ActiveRoutineUsesSharedClient { get; set; }

            public bool IsInvalidated { get; private set; }

            public bool IsCancelled => IsInvalidated ||
                (Cancellation != null && Cancellation.IsCancellationRequested);

            public void Invalidate()
            {
                IsInvalidated = true;
            }
        }

        private IAiGatewayClient sharedClient;
        private readonly IAiGatewayClient localFallback;
        private readonly List<RequestTicket> pending = new List<RequestTicket>();
        private readonly HashSet<RequestTicket> activeTickets =
            new HashSet<RequestTicket>();
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

        public void ReplaceClient(IAiGatewayClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            CancelAllForAuthoritativeReset();
            sharedClient = client;
            isShutdown = false;
            activeMode = client.ActiveMode;
        }

        public IEnumerator InterpretResidentTask(ResidentId owner, string command, string[] targets,
            string[] residents, Action<AiGatewayResult<ResidentTaskSpec>> completed)
        {
            return Coordinate<ResidentTaskSpec>(new[] { owner }, AiRequestPriority.High, null,
                (client, callback) => ((client as IResidentTaskGateway) ??
                    (IResidentTaskGateway)localFallback).InterpretResidentTask(owner, command, targets, residents, callback), completed);
        }

        public void Shutdown()
        {
            isShutdown = true;
            activeMode = AiGatewayMode.Local;
            foreach (RequestTicket ticket in activeTickets)
            {
                if (ticket.ActiveRoutineUsesSharedClient)
                {
                    DisposeActiveRoutine(ticket);
                }
            }
        }

        public int CancelAllForAuthoritativeReset()
        {
            var cancelledTickets = new HashSet<RequestTicket>();
            foreach (RequestTicket ticket in pending)
            {
                cancelledTickets.Add(ticket);
            }

            foreach (RequestTicket ticket in activeTickets)
            {
                cancelledTickets.Add(ticket);
            }

            foreach (RequestTicket ticket in cancelledTickets)
            {
                ticket.Invalidate();
            }

            pending.Clear();
            var activeSnapshot = new List<RequestTicket>(activeTickets);
            foreach (RequestTicket ticket in activeSnapshot)
            {
                DisposeActiveRoutine(ticket);
                Release(ticket);
            }

            // Release is deliberately idempotent, but clear these collections as a
            // final invariant guard so a reset can never strand a slot or owner.
            activeTickets.Clear();
            activeResidents.Clear();
            activeRequestCount = 0;
            activeMode = isShutdown ? AiGatewayMode.Local : sharedClient.ActiveMode;
            return cancelledTickets.Count;
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

        public IEnumerator DecideResident(
            ResidentDecisionRequest request,
            Action<AiGatewayResult<ResidentDecisionSpec>> completed)
        {
            return DecideResident(
                request,
                AiRequestPriority.Normal,
                cancellation: null,
                completed);
        }

        public IEnumerator DecideResident(
            ResidentDecisionRequest request,
            AiRequestPriority priority,
            AiRequestCancellation cancellation,
            Action<AiGatewayResult<ResidentDecisionSpec>> completed)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Coordinate(
                new[] { request.ResidentId },
                priority,
                cancellation,
                (client, callback) => client.DecideResident(request, callback),
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

            try
            {
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
                try
                {
                    ticket.ActiveRoutine = operation(
                        selectedClient,
                        value => result = value);
                    ticket.ActiveRoutineUsesSharedClient = selectedClient != localFallback;
                }
                catch (Exception exception)
                {
                    operationException = exception;
                }

                if (operationException == null && ticket.ActiveRoutine != null)
                {
                    while (!ticket.IsCancelled &&
                        !(isShutdown && ticket.ActiveRoutineUsesSharedClient) &&
                        ticket.ActiveRoutine != null)
                    {
                        bool moved = false;
                        object current = null;
                        try
                        {
                            moved = ticket.ActiveRoutine.MoveNext();
                            if (moved)
                            {
                                current = ticket.ActiveRoutine.Current;
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

                DisposeActiveRoutine(ticket);

                if (isShutdown && selectedClient != localFallback && !ticket.IsCancelled)
                {
                    result = null;
                    operationException = null;
                    try
                    {
                        ticket.ActiveRoutine = operation(
                            localFallback,
                            value => result = value);
                        ticket.ActiveRoutineUsesSharedClient = false;
                    }
                    catch (Exception exception)
                    {
                        operationException = exception;
                    }

                    if (operationException == null && ticket.ActiveRoutine != null)
                    {
                        while (!ticket.IsCancelled && ticket.ActiveRoutine != null)
                        {
                            bool moved = false;
                            object current = null;
                            try
                            {
                                moved = ticket.ActiveRoutine.MoveNext();
                                if (moved)
                                {
                                    current = ticket.ActiveRoutine.Current;
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

                    DisposeActiveRoutine(ticket);
                }

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
            finally
            {
                // Iterator disposal (including an owning scene coroutine being stopped)
                // must never leak a global slot or a resident ownership lock.
                DisposeActiveRoutine(ticket);
                Release(ticket);
            }
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
            activeTickets.Add(ticket);
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
            activeTickets.Remove(ticket);
            activeRequestCount = Math.Max(0, activeRequestCount - 1);
            foreach (ResidentId residentId in ticket.OwnerResidentIds)
            {
                activeResidents.Remove(residentId);
            }
        }

        private static void DisposeActiveRoutine(RequestTicket ticket)
        {
            if (ticket?.ActiveRoutine is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                    // Cleanup must not prevent fallback or leak coordinator ownership.
                }
            }

            if (ticket != null)
            {
                ticket.ActiveRoutine = null;
                ticket.ActiveRoutineUsesSharedClient = false;
            }
        }
    }
}
