using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AIFarm.Editor
{
    public sealed class DesktopTownBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        [MenuItem("AIFarm/Build Windows Town")]
        public static void BuildWindows()
        {
            string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string outputDirectory = Path.Combine(repository, "Builds", "Windows");
            Directory.CreateDirectory(outputDirectory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.Combine(outputDirectory, "AIFarmTown.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException("AIFarm Windows build failed; inspect the Unity build report.");
            }
            Debug.Log("AIFarm Windows player and gateway sidecar ready. Launch through Tools/RunTown.ps1 or install Server dependencies in the build.");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 &&
                report.summary.platform != BuildTarget.StandaloneLinux64 &&
                report.summary.platform != BuildTarget.StandaloneOSX)
            {
                return;
            }
            string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string source = Path.Combine(repository, "Server");
            string destination = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "Server");
            CopyGateway(source, destination);
        }

        public static void CopyGateway(string source, string destination)
        {
            // An explicit source allowlist excludes virtual environments, caches, tests,
            // local configuration and credentials from desktop distribution.
            string appSource = Path.Combine(source, "app");
            if (!Directory.Exists(appSource))
            {
                throw new BuildFailedException("Server/app is missing; the desktop gateway cannot be packaged.");
            }
            Directory.CreateDirectory(Path.Combine(destination, "app"));
            foreach (string path in Directory.GetFiles(appSource, "*.py", SearchOption.TopDirectoryOnly))
            {
                File.Copy(path, Path.Combine(destination, "app", Path.GetFileName(path)), true);
            }
            foreach (string name in new[] { "requirements.txt", "pyproject.toml", "README.md", "__init__.py" })
            {
                File.Copy(Path.Combine(source, name), Path.Combine(destination, name), true);
            }
        }
    }
}
