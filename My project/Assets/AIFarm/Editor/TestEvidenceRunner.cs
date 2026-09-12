using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AIFarm.Editor
{
    /// <summary>
    /// Persist the actual Unity Test Framework result tree, including failures and
    /// skipped tests. The callback is registered again after Play Mode domain reload.
    /// It also observes runs launched by the editor UI or automation tools.
    /// </summary>
    [InitializeOnLoad]
    public static class TestEvidenceRunner
    {
        private const string ActiveModeKey = "AIFarm.Verification.ActiveMode";
        private const string ActiveStartKey = "AIFarm.Verification.ActiveStart";
        private static readonly EvidenceCallbacks Callbacks = new EvidenceCallbacks();

        public static string OutputDirectory => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../Docs/Verification"));

        static TestEvidenceRunner()
        {
            // CallbacksHolder is cleared on domain reload by Test Framework;
            // initialization registers the fresh callback before results arrive.
            TestRunnerApi.RegisterTestCallback(Callbacks);
        }

        [MenuItem("AIFarm/Verification/Run EditMode and Export XML")]
        public static void RunEditMode() => Run(TestMode.EditMode);

        [MenuItem("AIFarm/Verification/Run PlayMode and Export XML")]
        public static void RunPlayMode() => Run(TestMode.PlayMode);

        public static string Run(TestMode mode, string[] testNames = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before starting a new test run.");
            Directory.CreateDirectory(OutputDirectory);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                // Do not run synchronously: UnityTest and coroutine setup must run too.
                return api.Execute(new ExecutionSettings(new Filter { testMode = mode, testNames = testNames }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(api);
            }
        }

        private sealed class EvidenceCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Directory.CreateDirectory(OutputDirectory);
                string mode = testsToRun.TestMode.ToString().Replace(", ", "-");
                string started = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                SessionState.SetString(ActiveModeKey, mode);
                SessionState.SetString(ActiveStartKey, started);
                File.WriteAllText(Path.Combine(OutputDirectory, "Unity-active-run.txt"),
                    $"RUNNING\nMode: {mode}\nStarted UTC: {started}\nDiscovered cases: {testsToRun.TestCaseCount}\n",
                    new UTF8Encoding(false));
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory(OutputDirectory);
                string mode = SessionState.GetString(ActiveModeKey, result.Test.TestMode.ToString());
                string started = SessionState.GetString(ActiveStartKey, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                string filename = "Unity-" + mode + "-" + started + ".xml";
                string path = Path.Combine(OutputDirectory, filename);
                TestRunnerApi.SaveResultToFile(result, path);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("Unity Test Framework could not persist its result XML: " + path);
                TestRunnerApi.SaveResultToFile(result, Path.Combine(OutputDirectory, "Unity-" + mode + "-latest.xml"));
                File.WriteAllText(Path.Combine(OutputDirectory, "Unity-active-run.txt"),
                    $"FINISHED\nMode: {mode}\nResult: {result.ResultState}\n" +
                    $"Passed: {result.PassCount}\nFailed: {result.FailCount}\n" +
                    $"Skipped: {result.SkipCount}\nInconclusive: {result.InconclusiveCount}\n" +
                    $"Duration: {result.Duration:0.000}s\nXML: {filename}\n",
                    new UTF8Encoding(false));
                Debug.Log($"Unity test evidence saved: {path}; passed={result.PassCount}, failed={result.FailCount}, " +
                    $"skipped={result.SkipCount}, inconclusive={result.InconclusiveCount}.");
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
