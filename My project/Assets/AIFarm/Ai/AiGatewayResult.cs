using System;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public sealed class AiGatewayResult<T>
        where T : class
    {
        private AiGatewayResult(
            ResidentId residentId,
            ActionResult outcome,
            T value,
            AiGatewayMode source)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "An AI gateway result requires a valid ResidentId.",
                    nameof(residentId));
            }

            ResidentId = residentId;
            Outcome = outcome;
            Value = value;
            Source = source;
        }

        public ResidentId ResidentId { get; }

        public ActionResult Outcome { get; }

        public T Value { get; }

        public AiGatewayMode Source { get; }

        public bool Succeeded => Outcome.Succeeded && Value != null;

        public bool Failed => !Succeeded;

        public static AiGatewayResult<T> Success(
            T value,
            AiGatewayMode source,
            string message = "")
        {
            return Success(ResidentIds.Yaya, value, source, message);
        }

        public static AiGatewayResult<T> Success(
            ResidentId residentId,
            T value,
            AiGatewayMode source,
            string message = "")
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return new AiGatewayResult<T>(
                residentId,
                ActionResult.Success(message),
                value,
                source);
        }

        public static AiGatewayResult<T> Failure(
            ActionResult failure,
            AiGatewayMode source)
        {
            return Failure(ResidentIds.Yaya, failure, source);
        }

        public static AiGatewayResult<T> Failure(
            ResidentId residentId,
            ActionResult failure,
            AiGatewayMode source)
        {
            if (failure.Succeeded)
            {
                throw new ArgumentException(
                    "An AI gateway failure must contain a failed ActionResult.",
                    nameof(failure));
            }

            return new AiGatewayResult<T>(residentId, failure, null, source);
        }
    }
}
