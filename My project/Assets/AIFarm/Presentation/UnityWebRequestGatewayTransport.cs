using System;
using System.Collections;
using System.Text;
using UnityEngine.Networking;

namespace AIFarm.Presentation
{
    public sealed class AiGatewayHttpResult
    {
        private AiGatewayHttpResult(
            bool succeeded,
            long statusCode,
            string body,
            string error)
        {
            Succeeded = succeeded;
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Succeeded { get; }

        public long StatusCode { get; }

        public string Body { get; }

        public string Error { get; }

        public static AiGatewayHttpResult Success(long statusCode, string body)
        {
            return new AiGatewayHttpResult(true, statusCode, body, string.Empty);
        }

        public static AiGatewayHttpResult Failure(long statusCode, string error)
        {
            return new AiGatewayHttpResult(false, statusCode, string.Empty, error);
        }
    }

    public interface IAiGatewayTransport
    {
        IEnumerator PostJson(
            string url,
            string json,
            int timeoutSeconds,
            Action<AiGatewayHttpResult> completed);
    }

    public sealed class UnityWebRequestGatewayTransport : IAiGatewayTransport
    {
        public IEnumerator PostJson(
            string url,
            string json,
            int timeoutSeconds,
            Action<AiGatewayHttpResult> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            byte[] payload = Encoding.UTF8.GetBytes(json ?? string.Empty);
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            try
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.SetRequestHeader("Accept", "application/json");

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    // Yield per frame so AiRequestCoordinator can observe cancellation or
                    // gateway shutdown instead of being suspended behind one opaque wait.
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string transportError = request.error ?? string.Empty;
                    bool timedOut = transportError.IndexOf(
                        "timed out",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                        transportError.IndexOf(
                            "timeout",
                            StringComparison.OrdinalIgnoreCase) >= 0;
                    string error = timedOut
                        ? $"AI gateway request timed out after {timeoutSeconds} seconds."
                        : request.responseCode > 0
                            ? $"HTTP {request.responseCode}: {transportError}"
                            : string.IsNullOrWhiteSpace(transportError)
                                ? request.result.ToString()
                                : transportError;
                    completed(AiGatewayHttpResult.Failure(request.responseCode, error));
                    yield break;
                }

                completed(AiGatewayHttpResult.Success(
                    request.responseCode,
                    request.downloadHandler?.text));
            }
            finally
            {
                if (!request.isDone)
                {
                    request.Abort();
                }

                request.Dispose();
            }
        }
    }
}
