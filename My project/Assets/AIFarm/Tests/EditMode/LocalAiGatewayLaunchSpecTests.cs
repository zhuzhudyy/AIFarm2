using AIFarm.Core;
using AIFarm.Presentation;
using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class LocalAiGatewayLaunchSpecTests
    {
        private const string SecretMarker = "KEY_SHOULD_NEVER_APPEAR_9f4d";

        [Test]
        public void ValidSettings_CreateBoundedRedactedLaunchSpec()
        {
            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                $"  {SecretMarker}  ",
                "  deepseek-v4-flash  ",
                "http://127.0.0.1:8123/",
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(spec, Is.Not.Null);
            Assert.That(spec.BaseUrl, Is.EqualTo("http://127.0.0.1:8123"));
            Assert.That(spec.ModelId, Is.EqualTo("deepseek-v4-flash"));
            Assert.That(spec.Port, Is.EqualTo(8123));
            Assert.That(spec.HasApiKey, Is.True);
            Assert.That(spec.InstanceId, Is.Not.Empty);
            Assert.That(
                spec.SafeArguments,
                Is.EqualTo(
                    "-m uvicorn app.main:app --host 127.0.0.1 --port 8123 " +
                    "--workers 1 --no-access-log"));
            Assert.That(spec.SafeArguments, Does.Not.Contain(SecretMarker));
            Assert.That(spec.ToString(), Does.Not.Contain(SecretMarker));
            Assert.That(result.Message, Does.Not.Contain(SecretMarker));
            Assert.That(result.ToString(), Does.Not.Contain(SecretMarker));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void EmptyApiKey_AllowsUnauthenticatedLocalModel(string apiKey)
        {
            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                apiKey, "local/model alias", "http://127.0.0.1:8000", "python", "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(spec.HasApiKey, Is.False);
            Assert.That(spec.ModelId, Is.EqualTo("local/model alias"));
        }

        [TestCase("line1\nline2")]
        [TestCase("tab\tkey")]
        public void InvalidApiKey_IsRejectedWithoutDisclosingIt(string apiKey)
        {
            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                apiKey,
                "deepseek-v4-flash",
                "http://127.0.0.1:8000",
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InvalidArgument));
            Assert.That(spec, Is.Null);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                Assert.That(result.Message, Does.Not.Contain(apiKey));
            }
        }

        [Test]
        public void OversizedApiKey_IsRejectedWithoutDisclosingIt()
        {
            string apiKey = new string('k', LocalAiGatewayLaunchSpec.MaximumApiKeyLength + 1);

            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                apiKey,
                "deepseek-v4-flash",
                "http://127.0.0.1:8000",
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Failed, Is.True);
            Assert.That(spec, Is.Null);
            Assert.That(result.Message, Does.Not.Contain(apiKey));
        }

        [TestCase("")]
        [TestCase("model\nname")]
        [TestCase("model\tname")]
        public void InvalidModelId_IsRejectedBeforeLaunch(string modelId)
        {
            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                SecretMarker, modelId, "http://127.0.0.1:8000", "python", "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InvalidArgument));
            Assert.That(result.Message, Does.Not.Contain(SecretMarker));
            Assert.That(spec, Is.Null);
        }

        [TestCase("bad model")]
        [TestCase("deepseek-v4-flash;calc.exe")]
        [TestCase("-starts-with-a-separator")]
        public void FreeModelAliases_AreAcceptedWithoutEnteringShellArguments(string modelId)
        {
            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                SecretMarker,
                modelId,
                "http://127.0.0.1:8000",
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(spec.ModelId, Is.EqualTo(modelId));
            Assert.That(spec.SafeArguments, Does.Not.Contain(modelId));
            Assert.That(result.Message, Does.Not.Contain(SecretMarker));
            Assert.That(spec, Is.Not.Null);
        }

        [Test]
        public void OversizedModelId_IsRejectedBeforeLaunch()
        {
            string modelId = new string(
                'm',
                LocalAiGatewayLaunchSpec.MaximumModelIdLength + 1);

            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                SecretMarker,
                modelId,
                "http://127.0.0.1:8000",
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Failed, Is.True);
            Assert.That(spec, Is.Null);
        }

        [TestCase("https://127.0.0.1:8000")]
        [TestCase("http://gateway.example:8000")]
        [TestCase("http://user:secret@127.0.0.1:8000")]
        [TestCase("http://127.0.0.1:8000/setup")]
        [TestCase("http://127.0.0.1:8000?token=secret")]
        [TestCase("http://127.0.0.1:8000#fragment")]
        [TestCase("http://localhost:8000")]
        [TestCase("http://127.0.0.2:8000")]
        [TestCase("not-a-url")]
        public void UnsafeOrNonRootGatewayUrl_IsRejectedBeforeLaunch(string baseUrl)
        {
            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                SecretMarker,
                "deepseek-v4-flash",
                baseUrl,
                "python",
                "Server",
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InvalidArgument));
            Assert.That(result.Message, Does.Not.Contain(SecretMarker));
            Assert.That(spec, Is.Null);
        }

        [Test]
        public void UnicodeAndSpacePaths_ArePreservedWithoutShellComposition()
        {
            const string pythonPath =
                @"C:\学习\研究生\Elys AI\Server\.venv\Scripts\python.exe";
            const string serverPath = @"C:\学习\研究生\Elys AI\Server";

            ActionResult result = LocalAiGatewayLaunchSpec.TryCreate(
                SecretMarker,
                "deepseek-v4-flash",
                "http://127.0.0.1:8000",
                pythonPath,
                serverPath,
                out LocalAiGatewayLaunchSpec spec);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(spec.PythonExecutable, Is.EqualTo(pythonPath));
            Assert.That(spec.ServerDirectory, Is.EqualTo(serverPath));
            Assert.That(spec.SafeArguments, Does.Not.Contain(pythonPath));
            Assert.That(spec.SafeArguments, Does.Not.Contain(serverPath));
            Assert.That(spec.SafeArguments, Does.Not.Contain(SecretMarker));
        }

        [Test]
        public void OwnedTreeTermination_UsesOnlyExactPidAndHiddenBoundedHelper()
        {
            Type handleType = typeof(SystemLocalAiGatewayProcessLauncher).GetNestedType(
                "SystemLocalAiGatewayProcessHandle", BindingFlags.NonPublic);
            MethodInfo factory = handleType.GetMethod(
                "CreateWindowsTreeTerminationStartInfo", BindingFlags.Static | BindingFlags.NonPublic);
            var startInfo = (ProcessStartInfo)factory.Invoke(null, new object[] { 12345 });

            Assert.That(startInfo.Arguments, Is.EqualTo("/PID 12345 /T /F"));
            Assert.That(startInfo.FileName, Does.EndWith("taskkill.exe"));
            Assert.That(startInfo.Arguments, Does.Not.Contain("/IM"));
            Assert.That(startInfo.Arguments, Does.Not.Contain("python"));
            Assert.That(startInfo.Arguments, Does.Not.Contain("8000"));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.CreateNoWindow, Is.True);
            Assert.That(startInfo.WindowStyle, Is.EqualTo(ProcessWindowStyle.Hidden));
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
            Assert.That((int)handleType.GetField("TreeTerminationTimeoutMilliseconds",
                BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue(), Is.EqualTo(2000));
        }
    }
}
