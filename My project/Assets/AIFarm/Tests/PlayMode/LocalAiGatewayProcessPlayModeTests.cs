using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AIFarm.Core;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AIFarm.Tests.PlayMode
{
    public sealed class LocalAiGatewayProcessPlayModeTests
    {
        private const string BaseUrl = "http://127.0.0.1:8123";
        private const string ModelId = "deepseek-v4-flash";
        private const string SecretMarker = "PLAYMODE_KEY_MUST_NOT_LEAK_27c1";

        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject createdObject in createdObjects)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.Destroy(createdObject);
                }
            }

            createdObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnavailablePort_StartsOneOwnedProcess_ThenBecomesReady()
        {
            var launcher = new FakeProcessLauncher(() => new FakeProcessHandle());
            var probe = new ScriptedProbe(callNumber =>
                callNumber == 1
                    ? LocalAiGatewayRuntimeStatus.Unavailable("not running")
                    : LocalAiGatewayRuntimeStatus.Available(
                        "openai",
                        ModelId,
                        true,
                        launcher.LastLaunchSpec.InstanceId));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);

            Assert.That(started.Succeeded, Is.True, started.Message);
            Assert.That(started.Message, Does.Not.Contain(SecretMarker));
            yield return WaitForTerminalState(process);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Ready));
            Assert.That(process.ActiveModelId, Is.EqualTo(ModelId));
            Assert.That(process.OwnsProcess, Is.True);
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
            Assert.That(launcher.StartCount, Is.EqualTo(1));
            Assert.That(launcher.HadApiKeyAtStart, Is.True);
            Assert.That(launcher.LastLaunchSpec.HasApiKey, Is.False,
                "The launch coroutine must clear its in-memory secret after Start returns.");
            Assert.That(probe.CallCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SecondStartWhileChecking_IsRejectedWithoutSpawningAnotherProcess()
        {
            var launcher = new FakeProcessLauncher(() => new FakeProcessHandle());
            var probe = new BlockingProbe(
                LocalAiGatewayRuntimeStatus.Available(
                    "openai",
                    ModelId,
                    true,
                    "external-compatible-instance"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult first = process.StartGateway(SecretMarker, ModelId);
            ActionResult duplicate = process.StartGateway("ANOTHER_PRIVATE_KEY", ModelId);

            Assert.That(first.Succeeded, Is.True, first.Message);
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.CheckingExistingGateway));
            Assert.That(process.IsBusy, Is.True);
            Assert.That(duplicate.Failed, Is.True);
            Assert.That(duplicate.FailureReason, Is.EqualTo(ActionFailureReason.InvalidState));
            Assert.That(duplicate.Message, Does.Not.Contain("ANOTHER_PRIVATE_KEY"));
            Assert.That(launcher.StartCount, Is.Zero);

            probe.Release();
            yield return WaitForTerminalState(process);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Ready));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(launcher.StartCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MatchingExternalGateway_IsReusedWithoutSpawningOrOwningIt()
        {
            var launcher = new FakeProcessLauncher(() => new FakeProcessHandle());
            var probe = new ScriptedProbe(_ =>
                LocalAiGatewayRuntimeStatus.Available(
                    "openai",
                    ModelId,
                    true,
                    "external-compatible-instance"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);
            Assert.That(started.Succeeded, Is.True, started.Message);
            yield return WaitForTerminalState(process);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Ready));
            Assert.That(process.ActiveModelId, Is.EqualTo(ModelId));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(launcher.StartCount, Is.Zero);
            Assert.That(probe.CallCount, Is.EqualTo(1));
            Assert.That(process.StatusMessage, Does.Contain("复用"));
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
        }

        [UnityTest]
        public IEnumerator IncompatibleExternalGateway_FailsWithoutStartingOrStoppingAProcess()
        {
            var unusedHandle = new FakeProcessHandle();
            var launcher = new FakeProcessLauncher(() => unusedHandle);
            var probe = new ScriptedProbe(_ =>
                LocalAiGatewayRuntimeStatus.Available(
                    "openai",
                    "different-model",
                    true,
                    "external-incompatible-instance"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);
            Assert.That(started.Succeeded, Is.True, started.Message);
            yield return WaitForTerminalState(process);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Failed));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(process.ActiveModelId, Is.Empty);
            Assert.That(launcher.StartCount, Is.Zero);
            Assert.That(unusedHandle.DisposeCount, Is.Zero);
            Assert.That(unusedHandle.StopCount, Is.Zero);
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
        }

        [UnityTest]
        public IEnumerator StopWhileChecking_PreventsLateProcessStartAndRemainsStopped()
        {
            var unusedHandle = new FakeProcessHandle();
            var launcher = new FakeProcessLauncher(() => unusedHandle);
            var probe = new BlockingProbe(
                LocalAiGatewayRuntimeStatus.Unavailable("released after cancellation"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);
            Assert.That(started.Succeeded, Is.True, started.Message);
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.CheckingExistingGateway));

            process.StopOwnedGateway();

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Stopped));
            Assert.That(process.IsBusy, Is.False);
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(launcher.StartCount, Is.Zero);
            Assert.That(unusedHandle.DisposeCount, Is.Zero);
            Assert.That(unusedHandle.StopCount, Is.Zero);

            probe.Release();
            yield return null;
            yield return null;

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Stopped));
            Assert.That(launcher.StartCount, Is.Zero);
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
        }

        [UnityTest]
        public IEnumerator ReadyOwnedGateway_StopDisposesAndStopsExactHandleOnce()
        {
            var ownedHandle = new FakeProcessHandle();
            var launcher = new FakeProcessLauncher(() => ownedHandle);
            var probe = new ScriptedProbe(callNumber =>
                callNumber == 1
                    ? LocalAiGatewayRuntimeStatus.Unavailable("not running")
                    : LocalAiGatewayRuntimeStatus.Available(
                        "openai",
                        ModelId,
                        true,
                        launcher.LastLaunchSpec.InstanceId));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);
            Assert.That(started.Succeeded, Is.True, started.Message);
            yield return WaitForTerminalState(process);
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Ready));
            Assert.That(process.OwnsProcess, Is.True);

            process.StopOwnedGateway();

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Stopped));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(process.ActiveModelId, Is.Empty);
            Assert.That(ownedHandle.DisposeCount, Is.EqualTo(1));
            Assert.That(ownedHandle.StopCount, Is.EqualTo(1));
            yield return null;
            Assert.That(ownedHandle.DisposeCount, Is.EqualTo(1));
            Assert.That(ownedHandle.StopCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ReadyOwnedGateway_UnexpectedExitUpdatesStatusAndReleasesHandle()
        {
            var ownedHandle = new FakeProcessHandle();
            var launcher = new FakeProcessLauncher(() => ownedHandle);
            var probe = new ScriptedProbe(callNumber =>
                callNumber == 1
                    ? LocalAiGatewayRuntimeStatus.Unavailable("not running")
                    : LocalAiGatewayRuntimeStatus.Available(
                        "openai",
                        ModelId,
                        true,
                        launcher.LastLaunchSpec.InstanceId));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            Assert.That(
                process.StartGateway(SecretMarker, ModelId).Succeeded,
                Is.True);
            yield return WaitForTerminalState(process);
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Ready));

            ownedHandle.ExitCodeValue = 31;
            ownedHandle.HasExitedValue = true;
            yield return new WaitForSecondsRealtime(0.6f);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Failed));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(process.ActiveModelId, Is.Empty);
            Assert.That(ownedHandle.DisposeCount, Is.EqualTo(1));
            Assert.That(process.StatusMessage, Does.Contain("31"));
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
        }

        [UnityTest]
        public IEnumerator OwnedProcessExitsBeforeReady_IsDisposedAndFailsLocally()
        {
            var exitedHandle = new FakeProcessHandle
            {
                HasExitedValue = true,
                ExitCodeValue = 23
            };
            var launcher = new FakeProcessLauncher(() => exitedHandle);
            var probe = new ScriptedProbe(_ =>
                LocalAiGatewayRuntimeStatus.Unavailable("not ready"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);
            Assert.That(started.Succeeded, Is.True, started.Message);
            yield return WaitForTerminalState(process);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Failed));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(process.ActiveModelId, Is.Empty);
            Assert.That(launcher.StartCount, Is.EqualTo(1));
            Assert.That(exitedHandle.DisposeCount, Is.EqualTo(1));
            Assert.That(process.StatusMessage, Does.Contain("23"));
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
        }

        [UnityTest]
        public IEnumerator OwnedProcessInstanceMismatch_DisposesOnlyOwnedHandleAndFails()
        {
            var ownedHandle = new FakeProcessHandle();
            var launcher = new FakeProcessLauncher(() => ownedHandle);
            var probe = new ScriptedProbe(callNumber =>
                callNumber == 1
                    ? LocalAiGatewayRuntimeStatus.Unavailable("not running")
                    : LocalAiGatewayRuntimeStatus.Available(
                        "openai",
                        ModelId,
                        true,
                        "different-process-instance"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);

            ActionResult started = process.StartGateway(SecretMarker, ModelId);
            Assert.That(started.Succeeded, Is.True, started.Message);
            yield return WaitForTerminalState(process);

            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Failed));
            Assert.That(process.OwnsProcess, Is.False);
            Assert.That(launcher.StartCount, Is.EqualTo(1));
            Assert.That(ownedHandle.DisposeCount, Is.EqualTo(1));
            Assert.That(ownedHandle.StopCount, Is.EqualTo(1));
            Assert.That(process.StatusMessage, Does.Contain("不匹配"));
            Assert.That(process.StatusMessage, Does.Not.Contain(SecretMarker));
        }

        [UnityTest]
        public IEnumerator SetupPanel_UsesPasswordDefaultModelAndClearsSubmittedKey()
        {
            var launcher = new FakeProcessLauncher(() => new FakeProcessHandle());
            var probe = new BlockingProbe(
                LocalAiGatewayRuntimeStatus.Unavailable("held by test"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);
            GameObject host = process.gameObject;
            ApiGatewaySetupPanel panel = host.AddComponent<ApiGatewaySetupPanel>();
            Button openButton = CreateButton(host.transform, "OpenButton");
            var panelRoot = new GameObject("PanelRoot", typeof(RectTransform));
            panelRoot.transform.SetParent(host.transform, false);
            InputField keyInput = CreateInput(host.transform, "ApiKeyInput");
            InputField modelInput = CreateInput(host.transform, "ModelInput");
            Button startButton = CreateButton(host.transform, "StartButton");
            Button stopButton = CreateButton(host.transform, "StopButton");
            Button closeButton = CreateButton(host.transform, "CloseButton");
            Text statusText = CreateText(host.transform, "StatusText");
            modelInput.text = "   ";

            ActionResult configured = panel.Configure(
                process,
                openButton,
                panelRoot,
                keyInput,
                modelInput,
                startButton,
                stopButton,
                closeButton,
                statusText);

            Assert.That(configured.Succeeded, Is.True, configured.Message);
            Assert.That(keyInput.contentType, Is.EqualTo(InputField.ContentType.Password));
            Assert.That(
                keyInput.characterLimit,
                Is.EqualTo(LocalAiGatewayLaunchSpec.MaximumApiKeyLength));
            Assert.That(
                modelInput.characterLimit,
                Is.EqualTo(LocalAiGatewayLaunchSpec.MaximumModelIdLength));
            Assert.That(modelInput.text, Is.EqualTo(LocalAiGatewayProcess.DefaultModelId));

            keyInput.text = SecretMarker;
            ActionResult submitted = panel.StartConfiguredGateway();

            Assert.That(submitted.Succeeded, Is.True, submitted.Message);
            Assert.That(submitted.Message, Does.Not.Contain(SecretMarker));
            Assert.That(keyInput.text, Is.Empty);
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.CheckingExistingGateway));
            Assert.That(launcher.StartCount, Is.Zero,
                "The blocking fake probe must prevent any real or fake process launch.");

            process.StopOwnedGateway();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetupPanel_RejectedSubmissionAlsoClearsPassword()
        {
            var launcher = new FakeProcessLauncher(() => new FakeProcessHandle());
            var probe = new ScriptedProbe(_ =>
                LocalAiGatewayRuntimeStatus.Unavailable("unused"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);
            GameObject host = process.gameObject;
            ApiGatewaySetupPanel panel = host.AddComponent<ApiGatewaySetupPanel>();
            Button openButton = CreateButton(host.transform, "OpenButton");
            var panelRoot = new GameObject("PanelRoot", typeof(RectTransform));
            panelRoot.transform.SetParent(host.transform, false);
            InputField keyInput = CreateInput(host.transform, "ApiKeyInput");
            InputField modelInput = CreateInput(host.transform, "ModelInput");
            Button startButton = CreateButton(host.transform, "StartButton");
            Button stopButton = CreateButton(host.transform, "StopButton");
            Button closeButton = CreateButton(host.transform, "CloseButton");
            Text statusText = CreateText(host.transform, "StatusText");
            Assert.That(panel.Configure(
                process,
                openButton,
                panelRoot,
                keyInput,
                modelInput,
                startButton,
                stopButton,
                closeButton,
                statusText).Succeeded, Is.True);
            keyInput.text = SecretMarker;
            modelInput.text = "invalid model";

            ActionResult submitted = panel.StartConfiguredGateway();

            Assert.That(submitted.Failed, Is.True);
            Assert.That(keyInput.text, Is.Empty);
            Assert.That(launcher.StartCount, Is.Zero);
            Assert.That(probe.CallCount, Is.Zero);
            Assert.That(statusText.text, Does.Not.Contain(SecretMarker));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetupPanel_ButtonsInvokeOpenStartAndCloseListeners()
        {
            var launcher = new FakeProcessLauncher(() => new FakeProcessHandle());
            var probe = new BlockingProbe(
                LocalAiGatewayRuntimeStatus.Unavailable("held by button test"));
            LocalAiGatewayProcess process = CreateProcess(launcher, probe);
            GameObject host = process.gameObject;
            ApiGatewaySetupPanel panel = host.AddComponent<ApiGatewaySetupPanel>();
            Button openButton = CreateButton(host.transform, "OpenButton");
            var panelRoot = new GameObject("PanelRoot", typeof(RectTransform));
            panelRoot.transform.SetParent(host.transform, false);
            InputField keyInput = CreateInput(panelRoot.transform, "ApiKeyInput");
            InputField modelInput = CreateInput(panelRoot.transform, "ModelInput");
            Button startButton = CreateButton(panelRoot.transform, "StartButton");
            Button stopButton = CreateButton(panelRoot.transform, "StopButton");
            Button closeButton = CreateButton(panelRoot.transform, "CloseButton");
            Text statusText = CreateText(panelRoot.transform, "StatusText");

            Assert.That(panel.Configure(
                process,
                openButton,
                panelRoot,
                keyInput,
                modelInput,
                startButton,
                stopButton,
                closeButton,
                statusText).Succeeded, Is.True);
            panelRoot.SetActive(false);

            yield return null;

            openButton.onClick.Invoke();
            Assert.That(panel.IsVisible, Is.True);

            keyInput.text = SecretMarker;
            modelInput.text = ModelId;
            startButton.onClick.Invoke();

            Assert.That(keyInput.text, Is.Empty);
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.CheckingExistingGateway));
            Assert.That(process.IsBusy, Is.True);
            Assert.That(launcher.StartCount, Is.Zero);
            Assert.That(statusText.text, Does.Not.Contain(SecretMarker));

            closeButton.onClick.Invoke();
            Assert.That(panel.IsVisible, Is.False);

            process.StopOwnedGateway();
            probe.Release();
            yield return null;
            Assert.That(process.State, Is.EqualTo(LocalAiGatewayState.Stopped));
            Assert.That(launcher.StartCount, Is.Zero);
        }

        private LocalAiGatewayProcess CreateProcess(
            ILocalAiGatewayProcessLauncher launcher,
            ILocalAiGatewayReadinessProbe probe)
        {
            var processObject = new GameObject("LocalAiGatewayProcessUnderTest");
            createdObjects.Add(processObject);
            LocalAiGatewayProcess process =
                processObject.AddComponent<LocalAiGatewayProcess>();
            ActionResult configured = process.Configure(
                BaseUrl,
                launcher,
                probe,
                ResolveServerDirectory(),
                "fake-python");
            Assert.That(configured.Succeeded, Is.True, configured.Message);
            return process;
        }

        private static string ResolveServerDirectory()
        {
            string serverDirectory = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "Server"));
            Assert.That(
                File.Exists(Path.Combine(serverDirectory, "app", "main.py")),
                Is.True,
                "The fake-launch tests require only the checked-in Server/app/main.py path.");
            return serverDirectory;
        }

        private static IEnumerator WaitForTerminalState(
            LocalAiGatewayProcess process,
            int maximumFrames = 30)
        {
            for (int frame = 0; frame < maximumFrames && process.IsBusy; frame++)
            {
                yield return null;
            }

            Assert.That(
                process.IsBusy,
                Is.False,
                $"Gateway process did not reach a terminal state. Current state: {process.State}.");
        }

        private static Button CreateButton(Transform parent, string name)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.AddComponent<Button>();
        }

        private static InputField CreateInput(Transform parent, string name)
        {
            var inputObject = new GameObject(name, typeof(RectTransform));
            inputObject.transform.SetParent(parent, false);
            return inputObject.AddComponent<InputField>();
        }

        private static Text CreateText(Transform parent, string name)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            return textObject.AddComponent<Text>();
        }

        private sealed class FakeProcessLauncher : ILocalAiGatewayProcessLauncher
        {
            private readonly Func<FakeProcessHandle> handleFactory;

            public FakeProcessLauncher(Func<FakeProcessHandle> processHandleFactory)
            {
                handleFactory = processHandleFactory;
            }

            public int StartCount { get; private set; }

            public bool HadApiKeyAtStart { get; private set; }

            public LocalAiGatewayLaunchSpec LastLaunchSpec { get; private set; }

            public ActionResult Start(
                LocalAiGatewayLaunchSpec launchSpec,
                out ILocalAiGatewayProcessHandle processHandle)
            {
                StartCount++;
                LastLaunchSpec = launchSpec;
                HadApiKeyAtStart = launchSpec != null && launchSpec.HasApiKey;
                processHandle = handleFactory();
                return ActionResult.Success("Fake process started without invoking Python.");
            }
        }

        private sealed class FakeProcessHandle : ILocalAiGatewayProcessHandle
        {
            private bool disposed;

            public bool HasExitedValue { get; set; }

            public int? ExitCodeValue { get; set; }

            public int StopCount { get; private set; }

            public int DisposeCount { get; private set; }

            public bool HasExited => HasExitedValue;

            public int? ExitCode => HasExited ? ExitCodeValue : null;

            public void Stop()
            {
                StopCount++;
                HasExitedValue = true;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                DisposeCount++;
                Stop();
            }
        }

        private sealed class ScriptedProbe : ILocalAiGatewayReadinessProbe
        {
            private readonly Func<int, LocalAiGatewayRuntimeStatus> statusFactory;

            public ScriptedProbe(
                Func<int, LocalAiGatewayRuntimeStatus> probeStatusFactory)
            {
                statusFactory = probeStatusFactory;
            }

            public int CallCount { get; private set; }

            public IEnumerator Probe(
                string baseUrl,
                int timeoutSeconds,
                Action<LocalAiGatewayRuntimeStatus> completed)
            {
                CallCount++;
                yield return null;
                completed(statusFactory(CallCount));
            }
        }

        private sealed class BlockingProbe : ILocalAiGatewayReadinessProbe
        {
            private readonly LocalAiGatewayRuntimeStatus status;
            private bool released;

            public BlockingProbe(LocalAiGatewayRuntimeStatus completionStatus)
            {
                status = completionStatus;
            }

            public int CallCount { get; private set; }

            public void Release()
            {
                released = true;
            }

            public IEnumerator Probe(
                string baseUrl,
                int timeoutSeconds,
                Action<LocalAiGatewayRuntimeStatus> completed)
            {
                CallCount++;
                while (!released)
                {
                    yield return null;
                }

                completed(status);
            }
        }
    }
}
