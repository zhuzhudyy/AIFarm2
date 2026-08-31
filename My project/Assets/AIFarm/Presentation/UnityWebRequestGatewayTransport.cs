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
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.responseCode > 0
                        ? $"HTTP {request.responseCode}"
                        : request.result.ToString();
                    completed(AiGatewayHttpResult.Failure(request.responseCode, error));
                    yield break;
                }

                completed(AiGatewayHttpResult.Success(
                    request.responseCode,
                    request.downloadHandler?.text));
            }
        }
    }
}
