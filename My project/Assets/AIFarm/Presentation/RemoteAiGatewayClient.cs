using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Presentation
{
    public sealed class RemoteAiGatewayClient : IAiGatewayClient
    {
        private readonly string baseUrl;
        private readonly int timeoutSeconds;
        private readonly IAiGatewayTransport transport;
        private readonly IAiGatewayClient fallback;

        public RemoteAiGatewayClient(
            string gatewayBaseUrl,
            int requestTimeoutSeconds,
            IAiGatewayTransport gatewayTransport = null,
            IAiGatewayClient localFallback = null)
        {
            baseUrl = NormalizeBaseUrl(gatewayBaseUrl);
            if (requestTimeoutSeconds < 1 || requestTimeoutSeconds > 60)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestTimeoutSeconds),
                    "AI gateway timeout must be between 1 and 60 seconds.");
            }

            timeoutSeconds = requestTimeoutSeconds;
            transport = gatewayTransport ?? new UnityWebRequestGatewayTransport();
            fallback = localFallback ?? new LocalAiGatewayClient();
            ActiveMode = AiGatewayMode.Remote;
        }

        public AiGatewayMode ConfiguredMode => AiGatewayMode.Remote;

        public AiGatewayMode ActiveMode { get; private set; }

        public string LastRemoteFailure { get; private set; } = string.Empty;

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
            EnsureResidentId(residentId);
            EnsureCallback(completed);
            string boundedCommand = (command ?? string.Empty).Trim();
            if (boundedCommand.Length == 0 ||
                boundedCommand.Length > AiGatewayJsonCodec.MaximumCommandLength)
            {
                LastRemoteFailure = "Command is outside the remote gateway bounds.";
                foreach (object step in RunChild(
                    CompleteLocalInterpretation(residentId, command, completed)))
                {
                    yield return step;
                }

                yield break;
            }

            AiGatewayHttpResult httpResult = null;
            LogRequest(residentId, "interpret-command");
            foreach (object step in RunChild(transport.PostJson(
                Endpoint("/v1/interpret-command"),
                AiGatewayJsonCodec.SerializeInterpretCommandRequest(residentId, boundedCommand),
                timeoutSeconds,
                result => httpResult = result)))
            {
                yield return step;
            }

            ActionResult transportOutcome = ValidateHttpResult(httpResult);
            if (transportOutcome.Succeeded)
            {
                ActionResult parsed = AiGatewayJsonCodec.TryParseFarmGoal(
                    httpResult.Body,
                    out FarmGoalSpec goal);
                if (parsed.Succeeded)
                {
                    CompleteRemote(
                        AiGatewayResult<FarmGoalSpec>.Success(
                            residentId,
                            goal,
                            AiGatewayMode.Remote,
                            parsed.Message),
                        completed);
                    yield break;
                }

                LastRemoteFailure = parsed.Message;
            }
            else
            {
                LastRemoteFailure = transportOutcome.Message;
            }

            foreach (object step in RunChild(
                CompleteLocalInterpretation(residentId, command, completed)))
            {
                yield return step;
            }
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
            EnsureResidentId(residentId);
            EnsureCallback(completed);
            string boundedContext = AiGatewayJsonCodec.BoundText(
                context,
                AiGatewayJsonCodec.MaximumContextLength);
            AiGatewayHttpResult httpResult = null;
            LogRequest(residentId, "generate-utterance");
            foreach (object step in RunChild(transport.PostJson(
                Endpoint("/v1/generate-utterance"),
                AiGatewayJsonCodec.SerializeGenerateUtteranceRequest(
                    residentId,
                    trigger,
                    boundedContext),
                timeoutSeconds,
                result => httpResult = result)))
            {
                yield return step;
            }

            ActionResult transportOutcome = ValidateHttpResult(httpResult);
            if (transportOutcome.Succeeded)
            {
                ActionResult parsed = AiGatewayJsonCodec.TryParseUtterance(
                    httpResult.Body,
                    trigger,
                    out NpcExpression expression);
                if (parsed.Succeeded)
                {
                    CompleteRemote(
                        AiGatewayResult<NpcExpression>.Success(
                            residentId,
                            expression,
                            AiGatewayMode.Remote,
                            parsed.Message),
                        completed);
                    yield break;
                }

                LastRemoteFailure = parsed.Message;
            }
            else
            {
                LastRemoteFailure = transportOutcome.Message;
            }

            AiGatewayResult<NpcExpression> fallbackResult = null;
            foreach (object step in RunChild(fallback.GenerateUtterance(
                residentId,
                trigger,
                context,
                result => fallbackResult = result)))
            {
                yield return step;
            }

            CompleteLocal(residentId, fallbackResult, completed);
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
            EnsureResidentId(residentId);
            EnsureCallback(completed);
            if (goal == null)
            {
                LastRemoteFailure = "Reflection requires a farm goal.";
                foreach (object step in RunChild(CompleteLocalReflection(
                    residentId,
                    goal,
                    outcome,
                    eventSummary,
                    completed)))
                {
                    yield return step;
                }

                yield break;
            }

            string boundedSummary = AiGatewayJsonCodec.BoundText(
                eventSummary,
                AiGatewayJsonCodec.MaximumEventSummaryLength);
            if (boundedSummary.Length == 0)
            {
                LastRemoteFailure = "Reflection event summary cannot be empty.";
                foreach (object step in RunChild(CompleteLocalReflection(
                    residentId,
                    goal,
                    outcome,
                    eventSummary,
                    completed)))
                {
                    yield return step;
                }

                yield break;
            }

            AiGatewayHttpResult httpResult = null;
            LogRequest(residentId, "reflect");
            foreach (object step in RunChild(transport.PostJson(
                Endpoint("/v1/reflect"),
                AiGatewayJsonCodec.SerializeReflectRequest(
                    residentId,
                    goal,
                    outcome,
                    boundedSummary),
                timeoutSeconds,
                result => httpResult = result)))
            {
                yield return step;
            }

            ActionResult transportOutcome = ValidateHttpResult(httpResult);
            if (transportOutcome.Succeeded)
            {
                ActionResult parsed = AiGatewayJsonCodec.TryParseReflection(
                    httpResult.Body,
                    goal,
                    outcome,
                    out NpcReflection reflection);
                if (parsed.Succeeded)
                {
                    CompleteRemote(
                        AiGatewayResult<NpcReflection>.Success(
                            residentId,
                            reflection,
                            AiGatewayMode.Remote,
                            parsed.Message),
                        completed);
                    yield break;
                }

                LastRemoteFailure = parsed.Message;
            }
            else
            {
                LastRemoteFailure = transportOutcome.Message;
            }

            foreach (object step in RunChild(CompleteLocalReflection(
                residentId,
                goal,
                outcome,
                eventSummary,
                completed)))
            {
                yield return step;
            }
        }

        public IEnumerator GenerateConversationScript(
            ConversationScriptRequest request,
            Action<AiGatewayResult<ConversationScriptSpec>> completed)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            EnsureCallback(completed);
            AiGatewayHttpResult httpResult = null;
            LogRequest(request.ResidentId, "conversation-script");
            foreach (object step in RunChild(transport.PostJson(
                Endpoint("/v1/conversation-script"),
                AiGatewayJsonCodec.SerializeConversationScriptRequest(request),
                timeoutSeconds,
                result => httpResult = result)))
            {
                yield return step;
            }

            ActionResult transportOutcome = ValidateHttpResult(httpResult);
            if (transportOutcome.Succeeded)
            {
                ActionResult parsed = AiGatewayJsonCodec.TryParseConversationScript(
                    httpResult.Body,
                    request,
                    out ConversationScriptSpec script);
                if (parsed.Succeeded)
                {
                    CompleteRemote(
                        AiGatewayResult<ConversationScriptSpec>.Success(
                            request.ResidentId,
                            script,
                            AiGatewayMode.Remote,
                            parsed.Message),
                        completed);
                    yield break;
                }

                LastRemoteFailure = parsed.Message;
            }
            else
            {
                LastRemoteFailure = transportOutcome.Message;
            }

            ActiveMode = AiGatewayMode.Local;
            LogResponse(request.ResidentId, AiGatewayMode.Local, succeeded: false);
            completed(AiGatewayResult<ConversationScriptSpec>.Failure(
                request.ResidentId,
                ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    $"Remote conversation script unavailable: {LastRemoteFailure}"),
                AiGatewayMode.Local));
        }

        public IEnumerator DecideResident(
            ResidentDecisionRequest request,
            Action<AiGatewayResult<ResidentDecisionSpec>> completed)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            EnsureCallback(completed);
            AiGatewayHttpResult httpResult = null;
            LogRequest(request.ResidentId, "resident-decision");
            foreach (object step in RunChild(transport.PostJson(
                Endpoint("/v1/resident-decision"),
                AiGatewayJsonCodec.SerializeResidentDecisionRequest(request),
                timeoutSeconds,
                result => httpResult = result)))
            {
                yield return step;
            }

            ActionResult transportOutcome = ValidateHttpResult(httpResult);
            if (transportOutcome.Succeeded)
            {
                ActionResult parsed = AiGatewayJsonCodec.TryParseResidentDecision(
                    httpResult.Body,
                    request,
                    out ResidentDecisionSpec decision);
                if (parsed.Succeeded)
                {
                    CompleteRemote(
                        AiGatewayResult<ResidentDecisionSpec>.Success(
                            request.ResidentId,
                            decision,
                            AiGatewayMode.Remote,
                            parsed.Message),
                        completed);
                    yield break;
                }

                LastRemoteFailure = parsed.Message;
            }
            else
            {
                LastRemoteFailure = transportOutcome.Message;
            }

            AiGatewayResult<ResidentDecisionSpec> fallbackResult = null;
            foreach (object step in RunChild(fallback.DecideResident(
                request,
                result => fallbackResult = result)))
            {
                yield return step;
            }

            CompleteLocal(request.ResidentId, fallbackResult, completed);
        }

        private IEnumerator CompleteLocalInterpretation(
            ResidentId residentId,
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            AiGatewayResult<FarmGoalSpec> fallbackResult = null;
            foreach (object step in RunChild(fallback.InterpretCommand(
                residentId,
                command,
                result => fallbackResult = result)))
            {
                yield return step;
            }

            CompleteLocal(residentId, fallbackResult, completed);
        }

        private IEnumerator CompleteLocalReflection(
            ResidentId residentId,
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            AiGatewayResult<NpcReflection> fallbackResult = null;
            foreach (object step in RunChild(fallback.Reflect(
                residentId,
                goal,
                outcome,
                eventSummary,
                result => fallbackResult = result)))
            {
                yield return step;
            }

            CompleteLocal(residentId, fallbackResult, completed);
        }

        private static IEnumerable<object> RunChild(IEnumerator child)
        {
            if (child == null)
            {
                yield break;
            }

            try
            {
                while (child.MoveNext())
                {
                    yield return child.Current;
                }
            }
            finally
            {
                if (child is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private void CompleteRemote<T>(
            AiGatewayResult<T> result,
            Action<AiGatewayResult<T>> completed)
            where T : class
        {
            ActiveMode = AiGatewayMode.Remote;
            LastRemoteFailure = string.Empty;
            LogResponse(result.ResidentId, result.Source, result.Succeeded);
            completed(result);
        }

        private void CompleteLocal<T>(
            ResidentId residentId,
            AiGatewayResult<T> result,
            Action<AiGatewayResult<T>> completed)
            where T : class
        {
            ActiveMode = AiGatewayMode.Local;
            if (result == null)
            {
                completed(AiGatewayResult<T>.Failure(
                    residentId,
                    ActionResult.Failure(
                        ActionFailureReason.ServiceUnavailable,
                        "The local AI fallback did not return a result."),
                    AiGatewayMode.Local));
                return;
            }

            if (result.ResidentId != residentId)
            {
                completed(AiGatewayResult<T>.Failure(
                    residentId,
                    ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "The local AI fallback returned a result for another resident."),
                    AiGatewayMode.Local));
                return;
            }

            LogResponse(result.ResidentId, result.Source, result.Succeeded);
            completed(result);
        }

        private ActionResult ValidateHttpResult(AiGatewayHttpResult result)
        {
            if (result == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "The remote AI gateway did not return a transport result.");
            }

            if (!result.Succeeded)
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    string.IsNullOrWhiteSpace(result.Error)
                        ? "The remote AI gateway is unavailable."
                        : result.Error);
            }

            if (string.IsNullOrWhiteSpace(result.Body) ||
                result.Body.Length > AiGatewayJsonCodec.MaximumResponseLength)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "The remote AI gateway returned an empty or oversized response.");
            }

            return ActionResult.Success();
        }

        private string Endpoint(string path)
        {
            return baseUrl + path;
        }

        private static string NormalizeBaseUrl(string value)
        {
            string candidate = (value ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException(
                    "AI gateway URL must be an HTTP(S) base URL without credentials, query, or fragment.",
                    nameof(value));
            }

            return candidate;
        }

        private static void EnsureCallback<T>(Action<AiGatewayResult<T>> completed)
            where T : class
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }
        }

        private static void EnsureResidentId(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "AI gateway requests require a valid ResidentId.",
                    nameof(residentId));
            }
        }

        private static void LogRequest(ResidentId residentId, string operation)
        {
            UnityEngine.Debug.Log(
                $"AI request residentId={residentId} operation={operation}");
        }

        private static void LogResponse(
            ResidentId residentId,
            AiGatewayMode source,
            bool succeeded)
        {
            UnityEngine.Debug.Log(
                $"AI response residentId={residentId} source={source} succeeded={succeeded}");
        }
    }
}
