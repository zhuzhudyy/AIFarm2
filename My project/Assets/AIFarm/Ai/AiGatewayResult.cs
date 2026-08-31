using System;
using AIFarm.Core;

namespace AIFarm.Ai
{
    public sealed class AiGatewayResult<T>
        where T : class
    {
        private AiGatewayResult(ActionResult outcome, T value, AiGatewayMode source)
        {
            Outcome = outcome;
            Value = value;
            Source = source;
        }

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
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return new AiGatewayResult<T>(ActionResult.Success(message), value, source);
        }

        public static AiGatewayResult<T> Failure(
            ActionResult failure,
            AiGatewayMode source)
        {
            if (failure.Succeeded)
            {
                throw new ArgumentException(
                    "An AI gateway failure must contain a failed ActionResult.",
                    nameof(failure));
            }

            return new AiGatewayResult<T>(failure, null, source);
        }
    }
}
