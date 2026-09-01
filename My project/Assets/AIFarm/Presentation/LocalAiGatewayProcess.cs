using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using AIFarm.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AIFarm.Presentation
{
    public enum LocalAiGatewayState
    {
        Stopped = 0,
        CheckingExistingGateway,
        Starting,
        Ready,
        Failed
    }

    public sealed class LocalAiGatewayLaunchSpec
    {
        public const int MaximumApiKeyLength = 512;
        public const int MaximumModelIdLength = 128;

        private static readonly Regex ModelIdPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._:/-]*$",
            RegexOptions.CultureInvariant);

        private string apiKey;

        private LocalAiGatewayLaunchSpec(
            string baseUrl,
            string modelId,
            string apiKeyValue,
            string pythonExecutable,
            string serverDirectory,
            string instanceId,
            int port)
        {
            BaseUrl = baseUrl;
            ModelId = modelId;
            apiKey = apiKeyValue;
            PythonExecutable = pythonExecutable;
            ServerDirectory = serverDirectory;
            InstanceId = instanceId;
            Port = port;
        }

        public string BaseUrl { get; }

        public string ModelId { get; }

        public bool HasApiKey => !string.IsNullOrEmpty(apiKey);

        public string PythonExecutable { get; }

        public string ServerDirectory { get; }

        public string InstanceId { get; }

        public int Port { get; }

        public string SafeArguments =>
            $"-m uvicorn app.main:app --host 127.0.0.1 --port {Port} " +
            "--workers 1 --no-access-log";

        internal string ApiKey => apiKey;

        public static ActionResult TryCreate(
            string apiKeyValue,
            string modelId,
            string baseUrl,
            string pythonExecutable,
            string serverDirectory,
            out LocalAiGatewayLaunchSpec spec)
        {
            spec = null;
            string normalizedApiKey = (apiKeyValue ?? string.Empty).Trim();
            if (normalizedApiKey.Length < 1 ||
                normalizedApiKey.Length > MaximumApiKeyLength ||
                ContainsControlCharacter(normalizedApiKey))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"API Key must contain 1 to {MaximumApiKeyLength} visible characters.");
            }

            string normalizedModelId = (modelId ?? string.Empty).Trim();
            if (normalizedModelId.Length < 1 ||
                normalizedModelId.Length > MaximumModelIdLength ||
                !ModelIdPattern.IsMatch(normalizedModelId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Model ID must start with a letter or number and contain only " +
                    "letters, numbers, '.', '_', ':', '/', or '-'.");
            }

            string normalizedBaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out Uri gatewayUri) ||
                gatewayUri.Scheme != Uri.UriSchemeHttp ||
                !gatewayUri.IsLoopback ||
                !string.Equals(
                    gatewayUri.Host,
                    "127.0.0.1",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(gatewayUri.UserInfo) ||
                !string.IsNullOrEmpty(gatewayUri.Query) ||
                !string.IsNullOrEmpty(gatewayUri.Fragment) ||
                (gatewayUri.AbsolutePath != string.Empty && gatewayUri.AbsolutePath != "/") ||
                gatewayUri.Port < 1 || gatewayUri.Port > 65535)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Automatic gateway startup requires a root " +
                    "http://127.0.0.1:<port> URL.");
            }

            string normalizedPython = (pythonExecutable ?? string.Empty).Trim();
            string normalizedServerDirectory = (serverDirectory ?? string.Empty).Trim();
            if (normalizedPython.Length == 0 || normalizedServerDirectory.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "Python or the local gateway Server directory could not be resolved.");
            }

            spec = new LocalAiGatewayLaunchSpec(
                normalizedBaseUrl,
                normalizedModelId,
                normalizedApiKey,
                normalizedPython,
                normalizedServerDirectory,
                Guid.NewGuid().ToString("N"),
                gatewayUri.Port);
            return ActionResult.Success("Local AI gateway launch settings are valid.");
        }

        public override string ToString()
        {
            return $"Local AI gateway {BaseUrl}, model {ModelId}, instance {InstanceId}.";
        }

        internal void ClearSecret()
        {
            apiKey = string.Empty;
        }

        private static bool ContainsControlCharacter(string value)
        {
            foreach (char character in value)
            {
                if (char.IsControl(character))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public interface ILocalAiGatewayProcessHandle : IDisposable
    {
        bool HasExited { get; }

        int? ExitCode { get; }

        void Stop();
    }

    public interface ILocalAiGatewayProcessLauncher
    {
        ActionResult Start(
            LocalAiGatewayLaunchSpec launchSpec,
            out ILocalAiGatewayProcessHandle processHandle);
    }

    public sealed class LocalAiGatewayRuntimeStatus
    {
        private LocalAiGatewayRuntimeStatus(
            bool reachable,
            string provider,
            string modelId,
            bool apiKeyConfigured,
            string instanceId,
            string message)
        {
            Reachable = reachable;
            Provider = provider ?? string.Empty;
            ModelId = modelId ?? string.Empty;
            ApiKeyConfigured = apiKeyConfigured;
            InstanceId = instanceId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Reachable { get; }

        public string Provider { get; }

        public string ModelId { get; }

        public bool ApiKeyConfigured { get; }

        public string InstanceId { get; }

        public string Message { get; }

        public static LocalAiGatewayRuntimeStatus Available(
            string provider,
            string modelId,
            bool apiKeyConfigured,
            string instanceId)
        {
            return new LocalAiGatewayRuntimeStatus(
                true,
                provider,
                modelId,
                apiKeyConfigured,
                instanceId,
                "The local AI gateway is reachable.");
        }

        public static LocalAiGatewayRuntimeStatus Unavailable(string message)
        {
            return new LocalAiGatewayRuntimeStatus(
                false,
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                message);
        }
    }

    public interface ILocalAiGatewayReadinessProbe
    {
        IEnumerator Probe(
            string baseUrl,
            int timeoutSeconds,
            Action<LocalAiGatewayRuntimeStatus> completed);
    }

    public sealed class UnityLocalAiGatewayReadinessProbe : ILocalAiGatewayReadinessProbe
    {
        [Serializable]
        private sealed class GatewayConfigDto
        {
            [SerializeField]
            private string provider;

            [SerializeField]
            private string model;

            [SerializeField]
            private bool api_key_configured;

            [SerializeField]
            private string instance_id;

            public string Provider => provider;

            public string Model => model;

            public bool ApiKeyConfigured => api_key_configured;

            public string InstanceId => instance_id;
        }

        public IEnumerator Probe(
            string baseUrl,
            int timeoutSeconds,
            Action<LocalAiGatewayRuntimeStatus> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            string statusUrl = $"{(baseUrl ?? string.Empty).TrimEnd('/')}/v1/gateway-config";
            var request = UnityWebRequest.Get(statusUrl);
            try
            {
                request.timeout = Math.Max(1, timeoutSeconds);
                request.SetRequestHeader("Accept", "application/json");
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode != 200 ||
                    string.IsNullOrWhiteSpace(request.downloadHandler?.text) ||
                    request.downloadHandler.text.Length > AiGatewayJsonCodec.MaximumResponseLength)
                {
                    completed(LocalAiGatewayRuntimeStatus.Unavailable(
                        "No compatible local AI gateway answered yet."));
                    yield break;
                }

                GatewayConfigDto response;
                try
                {
                    response = JsonUtility.FromJson<GatewayConfigDto>(
                        request.downloadHandler.text);
                }
                catch (ArgumentException)
                {
                    response = null;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.Provider))
                {
                    completed(LocalAiGatewayRuntimeStatus.Unavailable(
                        "The service on the gateway port returned an incompatible status."));
                    yield break;
                }

                completed(LocalAiGatewayRuntimeStatus.Available(
                    response.Provider,
                    response.Model,
                    response.ApiKeyConfigured,
                    response.InstanceId));
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

    public sealed class SystemLocalAiGatewayProcessLauncher : ILocalAiGatewayProcessLauncher
    {
        public ActionResult Start(
            LocalAiGatewayLaunchSpec launchSpec,
            out ILocalAiGatewayProcessHandle processHandle)
        {
            processHandle = null;
            if (launchSpec == null || !launchSpec.HasApiKey)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A complete launch specification is required.");
            }

            if (!Directory.Exists(launchSpec.ServerDirectory) ||
                !File.Exists(Path.Combine(launchSpec.ServerDirectory, "app", "main.py")))
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "The local gateway Server directory is unavailable.");
            }

            if (Path.IsPathRooted(launchSpec.PythonExecutable) &&
                !File.Exists(launchSpec.PythonExecutable))
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "The configured Python executable is unavailable.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = launchSpec.PythonExecutable,
                Arguments = launchSpec.SafeArguments,
                WorkingDirectory = launchSpec.ServerDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables["AIFARM_PROVIDER"] = "openai";
            startInfo.EnvironmentVariables["OPENAI_MODEL"] = launchSpec.ModelId;
            startInfo.EnvironmentVariables["OPENAI_API_KEY"] = launchSpec.ApiKey;
            startInfo.EnvironmentVariables["AIFARM_GATEWAY_INSTANCE_ID"] =
                launchSpec.InstanceId;
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
            startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            Process process = null;
            try
            {
                process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                if (!process.Start())
                {
                    process.Dispose();
                    return ActionResult.Failure(
                        ActionFailureReason.ServiceUnavailable,
                        "The operating system refused to start the local AI gateway.");
                }

                var startedHandle = new SystemLocalAiGatewayProcessHandle(process);
                processHandle = startedHandle;
                try
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch
                {
                    // A failure after Process.Start must not leave an untracked gateway.
                    startedHandle.Dispose();
                    processHandle = null;
                    process = null;
                    throw;
                }

                process = null;
                return ActionResult.Success("The owned local AI gateway process was started.");
            }
            catch (Exception error) when (
                error is InvalidOperationException ||
                error is System.ComponentModel.Win32Exception ||
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is ArgumentException ||
                error is NotSupportedException)
            {
                if (processHandle != null)
                {
                    processHandle.Dispose();
                }
                else if (process != null)
                {
                    new SystemLocalAiGatewayProcessHandle(process).Dispose();
                }

                processHandle = null;
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "Unable to start Python. Install the Server dependencies or configure " +
                    "AIFARM_GATEWAY_PYTHON.");
            }
            finally
            {
                // Do not retain credentials in the reusable ProcessStartInfo instance.
                startInfo.EnvironmentVariables["OPENAI_API_KEY"] = string.Empty;
            }
        }

        private sealed class SystemLocalAiGatewayProcessHandle : ILocalAiGatewayProcessHandle
        {
            private Process process;

            public SystemLocalAiGatewayProcessHandle(Process ownedProcess)
            {
                process = ownedProcess ?? throw new ArgumentNullException(nameof(ownedProcess));
            }

            public bool HasExited
            {
                get
                {
                    if (process == null)
                    {
                        return true;
                    }

                    try
                    {
                        return process.HasExited;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }
            }

            public int? ExitCode
            {
                get
                {
                    if (!HasExited || process == null)
                    {
                        return null;
                    }

                    try
                    {
                        return process.ExitCode;
                    }
                    catch (InvalidOperationException)
                    {
                        return null;
                    }
                }
            }

            public void Stop()
            {
                if (process == null || HasExited)
                {
                    return;
                }

                try
                {
                    process.Kill();
                }
                catch (Exception error) when (
                    error is InvalidOperationException ||
                    error is System.ComponentModel.Win32Exception ||
                    error is NotSupportedException)
                {
                    // The handle owns only this exact process. Failure to stop is non-fatal
                    // during application teardown and must never broaden into name/port kills.
                }
            }

            public void Dispose()
            {
                if (process == null)
                {
                    return;
                }

                Stop();
                process.Dispose();
                process = null;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class LocalAiGatewayProcess : MonoBehaviour
    {
        public const string DefaultModelId = "deepseek-v4-flash";
        private const int ProbeTimeoutSeconds = 1;

        [SerializeField]
        private string gatewayBaseUrl = DemoSceneConfig.DefaultAiGatewayBaseUrl;

        [Range(3f, 30f)]
        [SerializeField]
        private float startupTimeoutSeconds = 12f;

        [Range(0.1f, 2f)]
        [SerializeField]
        private float probeIntervalSeconds = 0.25f;

        [SerializeField]
        private string serverDirectoryOverride = string.Empty;

        [SerializeField]
        private string pythonExecutableOverride = string.Empty;

        private ILocalAiGatewayProcessLauncher processLauncher;
        private ILocalAiGatewayReadinessProbe readinessProbe;
        private ILocalAiGatewayProcessHandle ownedProcess;
        private LocalAiGatewayLaunchSpec pendingLaunchSpec;
        private Coroutine startupCoroutine;
        private Coroutine ownedProcessSupervisorCoroutine;
        private bool applicationQuitting;

        public LocalAiGatewayState State { get; private set; } =
            LocalAiGatewayState.Stopped;

        public string StatusMessage { get; private set; } =
            "未启动；居民继续使用本地 AI。";

        public string ActiveModelId { get; private set; } = string.Empty;

        public bool IsBusy =>
            State == LocalAiGatewayState.CheckingExistingGateway ||
            State == LocalAiGatewayState.Starting;

        public bool OwnsProcess => ownedProcess != null;

        public event Action<LocalAiGatewayState, string> StatusChanged;

        public ActionResult Configure(
            string baseUrl,
            ILocalAiGatewayProcessLauncher launcher = null,
            ILocalAiGatewayReadinessProbe probe = null,
            string serverDirectory = null,
            string pythonExecutable = null)
        {
            if (IsBusy)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Gateway process configuration cannot change while startup is active.");
            }

            string candidateBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? DemoSceneConfig.DefaultAiGatewayBaseUrl
                : baseUrl.Trim().TrimEnd('/');
            ActionResult urlValidation = LocalAiGatewayLaunchSpec.TryCreate(
                "validation-key",
                DefaultModelId,
                candidateBaseUrl,
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec validationSpec);
            validationSpec?.ClearSecret();
            if (urlValidation.Failed)
            {
                return urlValidation;
            }

            gatewayBaseUrl = candidateBaseUrl;
            processLauncher = launcher ?? new SystemLocalAiGatewayProcessLauncher();
            readinessProbe = probe ?? new UnityLocalAiGatewayReadinessProbe();
            serverDirectoryOverride = serverDirectory ?? string.Empty;
            pythonExecutableOverride = pythonExecutable ?? string.Empty;
            return ActionResult.Success("Local AI gateway process coordination configured.");
        }

        public ActionResult StartGateway(string apiKey, string modelId)
        {
            if (applicationQuitting)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The application is shutting down.");
            }

            if (IsBusy)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The local AI gateway is already starting.");
            }

            if (!IsDesktopProcessSupported())
            {
                SetState(
                    LocalAiGatewayState.Failed,
                    "当前平台不能启动本地网关；居民继续使用本地 AI。",
                    string.Empty);
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "Automatic local gateway startup is supported only in the Editor and desktop players.");
            }

            ActionResult pathsResolved = TryResolveLaunchPaths(
                out string serverDirectory,
                out string pythonExecutable);
            if (pathsResolved.Failed)
            {
                SetState(
                    LocalAiGatewayState.Failed,
                    "找不到 Python 网关运行环境；居民继续使用本地 AI。",
                    string.Empty);
                return pathsResolved;
            }

            ActionResult launchValidated = LocalAiGatewayLaunchSpec.TryCreate(
                apiKey,
                modelId,
                gatewayBaseUrl,
                pythonExecutable,
                serverDirectory,
                out LocalAiGatewayLaunchSpec launchSpec);
            if (launchValidated.Failed)
            {
                SetState(
                    LocalAiGatewayState.Failed,
                    launchValidated.Message,
                    string.Empty);
                return launchValidated;
            }

            if (ownedProcess != null)
            {
                StopOwnedProcessSupervisor();
                ReleaseOwnedProcess();
            }

            processLauncher = processLauncher ?? new SystemLocalAiGatewayProcessLauncher();
            readinessProbe = readinessProbe ?? new UnityLocalAiGatewayReadinessProbe();
            pendingLaunchSpec = launchSpec;
            startupCoroutine = StartCoroutine(StartGatewayRoutine(launchSpec));
            return ActionResult.Success(
                "Gateway startup accepted. The API Key was not written to Unity storage.");
        }

        public void StopOwnedGateway()
        {
            if (startupCoroutine != null)
            {
                StopCoroutine(startupCoroutine);
                startupCoroutine = null;
            }

            ClearPendingLaunchSecret();
            StopOwnedProcessSupervisor();
            ReleaseOwnedProcess();
            if (!applicationQuitting)
            {
                SetState(
                    LocalAiGatewayState.Stopped,
                    "网关已停止；居民继续使用本地 AI。",
                    string.Empty);
            }
        }

        private IEnumerator StartGatewayRoutine(LocalAiGatewayLaunchSpec launchSpec)
        {
            SetState(
                LocalAiGatewayState.CheckingExistingGateway,
                "正在检查本机网关端口……",
                string.Empty);

            LocalAiGatewayRuntimeStatus existingStatus = null;
            yield return readinessProbe.Probe(
                launchSpec.BaseUrl,
                ProbeTimeoutSeconds,
                status => existingStatus = status);
            if (existingStatus != null && existingStatus.Reachable)
            {
                ClearPendingLaunchSecret();
                startupCoroutine = null;
                if (IsCompatible(existingStatus, launchSpec.ModelId))
                {
                    SetState(
                        LocalAiGatewayState.Ready,
                        $"已复用共享网关：{launchSpec.ModelId}。本次输入的 Key 未应用；" +
                        "正在使用该网关已有的凭据。",
                        launchSpec.ModelId);
                }
                else
                {
                    SetState(
                        LocalAiGatewayState.Failed,
                        "本机端口已有另一套网关配置；未覆盖它，也未使用新输入的密钥。",
                        string.Empty);
                }

                yield break;
            }

            SetState(
                LocalAiGatewayState.Starting,
                $"正在启动共享网关：{launchSpec.ModelId}",
                string.Empty);
            ActionResult started = processLauncher.Start(launchSpec, out ownedProcess);
            ClearPendingLaunchSecret();
            if (started.Failed || ownedProcess == null)
            {
                ReleaseOwnedProcess();
                startupCoroutine = null;
                SetState(
                    LocalAiGatewayState.Failed,
                    "网关启动失败。请安装 Server 依赖或配置 Python 路径；居民继续使用本地 AI。",
                    string.Empty);
                yield break;
            }

            StatusChanged?.Invoke(State, StatusMessage);

            float deadline = UnityEngine.Time.realtimeSinceStartup +
                Math.Max(3f, startupTimeoutSeconds);
            while (UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                if (ownedProcess.HasExited)
                {
                    int? exitCode = ownedProcess.ExitCode;
                    ReleaseOwnedProcess();
                    startupCoroutine = null;
                    SetState(
                        LocalAiGatewayState.Failed,
                        exitCode.HasValue
                            ? $"网关进程提前退出（代码 {exitCode.Value}）；居民继续使用本地 AI。"
                            : "网关进程提前退出；居民继续使用本地 AI。",
                        string.Empty);
                    yield break;
                }

                LocalAiGatewayRuntimeStatus readyStatus = null;
                yield return readinessProbe.Probe(
                    launchSpec.BaseUrl,
                    ProbeTimeoutSeconds,
                    status => readyStatus = status);
                if (readyStatus != null && readyStatus.Reachable)
                {
                    if (readyStatus.InstanceId == launchSpec.InstanceId &&
                        IsCompatible(readyStatus, launchSpec.ModelId))
                    {
                        startupCoroutine = null;
                        SetState(
                            LocalAiGatewayState.Ready,
                            $"共享网关已就绪：{launchSpec.ModelId}（密钥将在请求时验证）",
                            launchSpec.ModelId);
                        StartOwnedProcessSupervisor();
                        yield break;
                    }

                    ReleaseOwnedProcess();
                    startupCoroutine = null;
                    SetState(
                        LocalAiGatewayState.Failed,
                        "网关端口被其他进程占用或启动配置不匹配；未终止外部进程。",
                        string.Empty);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(
                    Math.Max(0.1f, probeIntervalSeconds));
            }

            ReleaseOwnedProcess();
            startupCoroutine = null;
            SetState(
                LocalAiGatewayState.Failed,
                "等待网关就绪超时；已清理本次进程，居民继续使用本地 AI。",
                string.Empty);
        }

        private void SetState(
            LocalAiGatewayState state,
            string message,
            string modelId)
        {
            State = state;
            StatusMessage = message ?? string.Empty;
            ActiveModelId = modelId ?? string.Empty;
            StatusChanged?.Invoke(State, StatusMessage);
        }

        private static bool IsCompatible(
            LocalAiGatewayRuntimeStatus status,
            string requestedModelId)
        {
            return status != null &&
                status.Reachable &&
                string.Equals(status.Provider, "openai", StringComparison.Ordinal) &&
                status.ApiKeyConfigured &&
                string.Equals(status.ModelId, requestedModelId, StringComparison.Ordinal);
        }

        private ActionResult TryResolveLaunchPaths(
            out string serverDirectory,
            out string pythonExecutable)
        {
            serverDirectory = string.Empty;
            pythonExecutable = string.Empty;
            string environmentServer = Environment.GetEnvironmentVariable(
                "AIFARM_GATEWAY_SERVER_DIR");
            string explicitServer = !string.IsNullOrWhiteSpace(environmentServer)
                ? environmentServer
                : serverDirectoryOverride;

            var serverCandidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitServer))
            {
                serverCandidates.Add(explicitServer);
            }
            else
            {
                serverCandidates.Add(Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    "Server"));
                serverCandidates.Add(Path.Combine(AppContext.BaseDirectory, "Server"));
                serverCandidates.Add(Path.Combine(
                    Application.streamingAssetsPath,
                    "AIFarmGateway"));
            }

            foreach (string candidate in serverCandidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(candidate);
                    if (Directory.Exists(fullPath) &&
                        File.Exists(Path.Combine(fullPath, "app", "main.py")))
                    {
                        serverDirectory = fullPath;
                        break;
                    }
                }
                catch (Exception error) when (
                    error is ArgumentException ||
                    error is NotSupportedException ||
                    error is PathTooLongException)
                {
                    // Continue through trusted resolver candidates.
                }
            }

            if (serverDirectory.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "AIFarm Server/app/main.py was not found. Desktop builds require the gateway sidecar.");
            }

            string environmentPython = Environment.GetEnvironmentVariable(
                "AIFARM_GATEWAY_PYTHON");
            string explicitPython = !string.IsNullOrWhiteSpace(environmentPython)
                ? environmentPython.Trim()
                : (pythonExecutableOverride ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(explicitPython))
            {
                if (Path.IsPathRooted(explicitPython) && !File.Exists(explicitPython))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.ServiceUnavailable,
                        "The configured AIFARM_GATEWAY_PYTHON executable does not exist.");
                }

                pythonExecutable = explicitPython;
                return ActionResult.Success("Explicit local gateway runtime resolved.");
            }

            bool windows = Path.DirectorySeparatorChar == '\\';
            string venvPython = windows
                ? Path.Combine(serverDirectory, ".venv", "Scripts", "python.exe")
                : Path.Combine(serverDirectory, ".venv", "bin", "python");
            pythonExecutable = File.Exists(venvPython)
                ? venvPython
                : windows ? "python" : "python3";
            return ActionResult.Success("Local gateway runtime resolved.");
        }

        private void ReleaseOwnedProcess()
        {
            if (ownedProcess == null)
            {
                return;
            }

            ownedProcess.Dispose();
            ownedProcess = null;
        }

        private void ClearPendingLaunchSecret()
        {
            pendingLaunchSpec?.ClearSecret();
            pendingLaunchSpec = null;
        }

        private void StartOwnedProcessSupervisor()
        {
            StopOwnedProcessSupervisor();
            if (ownedProcess != null && !ownedProcess.HasExited)
            {
                ownedProcessSupervisorCoroutine =
                    StartCoroutine(SuperviseOwnedProcessRoutine());
            }
        }

        private IEnumerator SuperviseOwnedProcessRoutine()
        {
            while (!applicationQuitting && ownedProcess != null)
            {
                if (ownedProcess.HasExited)
                {
                    int? exitCode = ownedProcess.ExitCode;
                    ownedProcessSupervisorCoroutine = null;
                    ReleaseOwnedProcess();
                    SetState(
                        LocalAiGatewayState.Failed,
                        exitCode.HasValue
                            ? $"共享网关进程已退出（代码 {exitCode.Value}）；居民继续使用本地 AI。"
                            : "共享网关进程已退出；居民继续使用本地 AI。",
                        string.Empty);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            ownedProcessSupervisorCoroutine = null;
        }

        private void StopOwnedProcessSupervisor()
        {
            if (ownedProcessSupervisorCoroutine == null)
            {
                return;
            }

            StopCoroutine(ownedProcessSupervisorCoroutine);
            ownedProcessSupervisorCoroutine = null;
        }

        private static bool IsDesktopProcessSupported()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            return true;
#else
            return false;
#endif
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
            if (startupCoroutine != null)
            {
                StopCoroutine(startupCoroutine);
                startupCoroutine = null;
            }

            ClearPendingLaunchSecret();
            StopOwnedProcessSupervisor();
            ReleaseOwnedProcess();
        }

        private void OnDestroy()
        {
            if (startupCoroutine != null)
            {
                StopCoroutine(startupCoroutine);
                startupCoroutine = null;
            }

            ClearPendingLaunchSecret();
            StopOwnedProcessSupervisor();
            ReleaseOwnedProcess();
        }
    }
}
