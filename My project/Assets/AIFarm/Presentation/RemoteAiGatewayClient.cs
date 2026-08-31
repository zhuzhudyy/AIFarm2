using System;
using System.Collections;
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
            EnsureCallback(completed);
            string boundedCommand = (command ?? string.Empty).Trim();
            if (boundedCommand.Length == 0 ||
                boundedCommand.Length > AiGatewayJsonCodec.MaximumCommandLength)
            {
                LastRemoteFailure = "Command is outside the remote gateway bounds.";
                yield return CompleteLocalInterpretation(command, completed);
                yield break;
            }

            AiGatewayHttpResult httpResult = null;
            yield return transport.PostJson(
                Endpoint("/v1/interpret-command"),
                AiGatewayJsonCodec.SerializeInterpretCommandRequest(boundedCommand),
                timeoutSeconds,
                result => httpResult = result);

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

            yield return CompleteLocalInterpretation(command, completed);
        }

        public IEnumerator GenerateUtterance(
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed)
        {
            EnsureCallback(completed);
            string boundedContext = AiGatewayJsonCodec.BoundText(
                context,
                AiGatewayJsonCodec.MaximumContextLength);
            AiGatewayHttpResult httpResult = null;
            yield return transport.PostJson(
                Endpoint("/v1/generate-utterance"),
                AiGatewayJsonCodec.SerializeGenerateUtteranceRequest(
                    trigger,
                    boundedContext),
                timeoutSeconds,
                result => httpResult = result);

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
            yield return fallback.GenerateUtterance(
                trigger,
                context,
                result => fallbackResult = result);
            CompleteLocal(fallbackResult, completed);
        }

        public IEnumerator Reflect(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            EnsureCallback(completed);
            if (goal == null)
            {
                LastRemoteFailure = "Reflection requires a farm goal.";
                yield return CompleteLocalReflection(
                    goal,
                    outcome,
                    eventSummary,
                    completed);
                yield break;
            }

            string boundedSummary = AiGatewayJsonCodec.BoundText(
                eventSummary,
                AiGatewayJsonCodec.MaximumEventSummaryLength);
            if (boundedSummary.Length == 0)
            {
                LastRemoteFailure = "Reflection event summary cannot be empty.";
                yield return CompleteLocalReflection(
                    goal,
                    outcome,
                    eventSummary,
                    completed);
                yield break;
            }

            AiGatewayHttpResult httpResult = null;
            yield return transport.PostJson(
                Endpoint("/v1/reflect"),
                AiGatewayJsonCodec.SerializeReflectRequest(
                    goal,
                    outcome,
                    boundedSummary),
                timeoutSeconds,
                result => httpResult = result);

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

            yield return CompleteLocalReflection(
                goal,
                outcome,
                eventSummary,
                completed);
        }

        private IEnumerator CompleteLocalInterpretation(
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            AiGatewayResult<FarmGoalSpec> fallbackResult = null;
            yield return fallback.InterpretCommand(
                command,
                result => fallbackResult = result);
            CompleteLocal(fallbackResult, completed);
        }

        private IEnumerator CompleteLocalReflection(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            AiGatewayResult<NpcReflection> fallbackResult = null;
            yield return fallback.Reflect(
                goal,
                outcome,
                eventSummary,
                result => fallbackResult = result);
            CompleteLocal(fallbackResult, completed);
        }

        private void CompleteRemote<T>(
            AiGatewayResult<T> result,
            Action<AiGatewayResult<T>> completed)
            where T : class
        {
            ActiveMode = AiGatewayMode.Remote;
            LastRemoteFailure = string.Empty;
            completed(result);
        }

        private void CompleteLocal<T>(
            AiGatewayResult<T> result,
            Action<AiGatewayResult<T>> completed)
            where T : class
        {
            ActiveMode = AiGatewayMode.Local;
            if (result == null)
            {
                completed(AiGatewayResult<T>.Failure(
                    ActionResult.Failure(
                        ActionFailureReason.ServiceUnavailable,
                        "The local AI fallback did not return a result."),
                    AiGatewayMode.Local));
                return;
            }

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
    }
}
