using System;

namespace AIFarm.Core
{
    public readonly struct ActionResult
    {
        private ActionResult(bool succeeded, ActionFailureReason failureReason, string message)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public bool Failed => !Succeeded;

        public ActionFailureReason FailureReason { get; }

        public string Message { get; }

        public static ActionResult Success(string message = "")
        {
            return new ActionResult(true, ActionFailureReason.None, message);
        }

        public static ActionResult Failure(ActionFailureReason failureReason, string message)
        {
            if (failureReason == ActionFailureReason.None)
            {
                throw new ArgumentException("A failed action must include a failure reason.", nameof(failureReason));
            }

            return new ActionResult(false, failureReason, message);
        }
    }
}
