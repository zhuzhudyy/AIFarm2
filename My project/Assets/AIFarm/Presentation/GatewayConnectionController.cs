using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class GatewayConnectionController : MonoBehaviour
    {
        [Serializable]
        public sealed class Configuration
        {
            public string provider = "openai";
            public string protocol = "chat_completions";
            public string base_url = "https://api.openai.com";
            public string model = "";
            public string api_key = "";
            public bool persist = true;
        }
        [Serializable]
        private sealed class Status
        {
            public string provider, protocol, base_url, model, endpoint, upstream_status, last_error_code, last_error;
            public long config_version;
            public bool api_key_configured, persisted;
        }
        [Serializable]
        private sealed class Probe
        {
            public bool ok;
            public string code, message, endpoint;
            public long config_version;
        }

        public string GatewayUrl { get; private set; } = DemoSceneConfig.DefaultAiGatewayBaseUrl;
        public string StateLabel { get; private set; } = "未配置";
        public string LastError { get; private set; } = "";
        public string Endpoint { get; private set; } = "";
        public string Model { get; private set; } = "";
        public long ConfigVersion
        {
            get; private set;
        }
        public bool IsOnline
        {
            get; private set;
        }
        public bool GatewayReachable
        {
            get; private set;
        }
        public bool Busy
        {
            get; private set;
        }
        public bool KeyConfigured
        {
            get; private set;
        }
        public Configuration Editable { get; private set; } = new Configuration();
        public event Action<long, bool, string, string> ConfigurationChanged;
        public event Action StatusChanged;
        private LocalAiGatewayProcess process;
        private GameBootstrap bootstrap;
        private float nextProbe;
        private bool disposed;

        private IEnumerator Start()
        {
            process = GetComponent<LocalAiGatewayProcess>() ?? gameObject.AddComponent<LocalAiGatewayProcess>();
            bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (bootstrap?.SceneConfig != null)
                GatewayUrl = bootstrap.SceneConfig.AiGatewayBaseUrl;
            process.Configure(GatewayUrl);
            yield return Connect();
            while (!disposed)
            {
                yield return new WaitForSecondsRealtime(10);
                if (!Busy)
                {
                    yield return ReadStatus(false);
                    if (GatewayReachable && Editable.provider != "mock" && !IsOnline && UnityEngine.Time.realtimeSinceStartup >= nextProbe)
                        yield return ProbeUpstream();
                }
            }
        }

        public void Apply(Configuration configuration)
        {
            if (Busy || configuration == null)
                return;
            StartCoroutine(ApplyRoutine(configuration));
        }

        public static string SerializeConfiguration(Configuration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            string json = JsonUtility.ToJson(configuration);
            // JsonUtility serializes null strings as empty strings. The gateway
            // deliberately distinguishes retaining a saved key from clearing it.
            return configuration.api_key == null ? json.Replace("\"api_key\":\"\"", "\"api_key\":null") : json;
        }

        public void Reconnect()
        {
            if (!Busy)
                StartCoroutine(Connect());
        }

        public void Clear()
        {
            if (!Busy)
                StartCoroutine(ClearRoutine());
        }

        private IEnumerator Connect()
        {
            Busy = true;
            StateLabel = "连接中";
            StatusChanged?.Invoke();
            yield return ReadStatus(true);
            if (!GatewayReachable)
            {
                var result = process.StartGateway("", LocalAiGatewayProcess.DefaultModelId);
                if (result.Succeeded)
                {
                    float until = UnityEngine.Time.realtimeSinceStartup + 16;
                    while (process.IsBusy && UnityEngine.Time.realtimeSinceStartup < until)
                        yield return null;
                    yield return ReadStatus(true);
                }
                if (!GatewayReachable)
                    LastError = result.Failed ? result.Message : process.StatusMessage;
            }
            if (GatewayReachable && Editable.provider != "mock")
                yield return ProbeUpstream();
            Busy = false;
            if (!GatewayReachable)
                StateLabel = "错误";
            Notify();
        }

        private IEnumerator ApplyRoutine(Configuration configuration)
        {
            Busy = true;
            StateLabel = "连接中";
            StatusChanged?.Invoke();
            if (!GatewayReachable)
            {
                Busy = false;
                yield return Connect();
                Busy = true;
            }
            bool ok = false;
            string payload = null;
            yield return Request("/v1/gateway-config", "POST", SerializeConfiguration(configuration), 15, (success, body) => { ok = success; payload = body; });
            if (ok)
            {
                ApplyStatus(JsonUtility.FromJson<Status>(payload), false);
                IsOnline = false;
                Notify();
                yield return ProbeUpstream();
            }
            else
                StateLabel = "错误";
            Busy = false;
            Notify();
        }

        private IEnumerator ClearRoutine()
        {
            Busy = true;
            yield return Request("/v1/gateway-config/clear", "POST", "{}", 10, (ok, body) =>
            {
                if (ok)
                {
                    Editable = new Configuration();
                    ApplyStatus(JsonUtility.FromJson<Status>(body), true);
                }
            });
            Busy = false;
            Notify();
        }

        private IEnumerator ReadStatus(bool populate)
        {
            yield return Request("/v1/gateway-config", "GET", null, 3, (ok, body) =>
            {
                GatewayReachable = ok;
                if (ok)
                    ApplyStatus(JsonUtility.FromJson<Status>(body), populate);
                else
                {
                    IsOnline = false;
                    StateLabel = "降级";
                }
            });
            Notify();
        }

        private void ApplyStatus(Status status, bool populate)
        {
            if (status == null)
                return;
            bool changed = status.config_version != ConfigVersion;
            ConfigVersion = status.config_version;
            Endpoint = status.endpoint ?? "";
            Model = status.model ?? "";
            KeyConfigured = status.api_key_configured;
            Editable.provider = status.provider ?? "mock";
            if (populate || changed)
            {
                Editable.protocol = status.protocol ?? "chat_completions";
                Editable.base_url = status.base_url ?? "";
                Editable.model = Model;
                Editable.persist = status.persisted;
                Editable.api_key = null;
            }
            IsOnline = status.provider != "mock" && status.upstream_status == "online";
            LastError = status.last_error ?? "";
            StateLabel = status.provider == "mock" ? "未配置" : IsOnline ? "在线" : string.IsNullOrEmpty(LastError) ? "连接中" : "降级";
        }

        private IEnumerator ProbeUpstream()
        {
            bool priorBusy = Busy;
            Busy = true;
            StateLabel = "连接中";
            StatusChanged?.Invoke();
            yield return Request("/v1/gateway-config/probe", "POST", "{}", 100, (ok, body) =>
            {
                var probe = ok ? JsonUtility.FromJson<Probe>(body) : null;
                IsOnline = probe != null && probe.ok && probe.code != "offline";
                StateLabel = IsOnline ? "在线" : "降级";
                if (probe != null)
                {
                    LastError = IsOnline ? "" : probe.code + ": " + probe.message;
                    if (!string.IsNullOrEmpty(probe.endpoint))
                        Endpoint = probe.endpoint;
                }
            });
            nextProbe = UnityEngine.Time.realtimeSinceStartup + 45;
            Busy = priorBusy;
            Notify();
        }

        private IEnumerator Request(string path, string method, string body, int timeout, Action<bool, string> completed)
        {
            using (var request = new UnityWebRequest(GatewayUrl.TrimEnd('/') + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-AIFarm-Client", "unity");
                request.timeout = timeout;
                yield return request.SendWebRequest();
                bool ok = request.result == UnityWebRequest.Result.Success;
                if (!ok)
                    LastError = request.responseCode == 0 ? "本机网关不可达：" + request.error : "网关 HTTP " + request.responseCode + "：" + request.downloadHandler.text;
                completed(ok, request.downloadHandler.text);
            }
        }

        private long notifiedVersion = -1;
        private bool notifiedOnline;
        private void Notify()
        {
            if (notifiedVersion != ConfigVersion || notifiedOnline != IsOnline)
            {
                notifiedVersion = ConfigVersion;
                notifiedOnline = IsOnline;
                bootstrap?.ApplyGatewayConfiguration(ConfigVersion, IsOnline, Model, LastError);
                ConfigurationChanged?.Invoke(ConfigVersion, IsOnline, Model, LastError);
            }
            StatusChanged?.Invoke();
        }
        private void OnDestroy()
        {
            disposed = true;
        }
    }
}
