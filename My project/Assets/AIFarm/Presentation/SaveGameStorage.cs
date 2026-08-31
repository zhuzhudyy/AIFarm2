using System;
using System.IO;
using System.Text;
using AIFarm.Core;
using UnityEngine;

namespace AIFarm.Presentation
{
    public interface ISaveGameStorage
    {
        string SavePath { get; }

        ActionResult Write(string json);

        ActionResult Read(out string json);

        ActionResult Delete();
    }

    public sealed class PersistentSaveGameStorage : ISaveGameStorage
    {
        public const string DefaultFileName = "aifarm-save-v1.json";
        public const int MaximumSaveCharacters = 1024 * 1024;

        public PersistentSaveGameStorage(string fileName = DefaultFileName)
        {
            string normalized = (fileName ?? string.Empty).Trim();
            if (normalized.Length == 0 ||
                normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                normalized != Path.GetFileName(normalized))
            {
                throw new ArgumentException("Save file name must be a plain valid file name.", nameof(fileName));
            }

            SavePath = Path.Combine(Application.persistentDataPath, normalized);
        }

        public string SavePath { get; }

        public ActionResult Write(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumSaveCharacters)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Save JSON is empty or exceeds the 1 MB limit.");
            }

            string temporaryPath = SavePath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(SavePath))
                {
                    try
                    {
                        File.Replace(temporaryPath, SavePath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, SavePath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, SavePath);
                }

                return ActionResult.Success($"Game saved to {SavePath}.");
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                TryDeleteTemporaryFile(temporaryPath);
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    $"Could not write the save file: {exception.Message}");
            }
        }

        public ActionResult Read(out string json)
        {
            json = string.Empty;
            try
            {
                if (!File.Exists(SavePath))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        "No save file exists yet.");
                }

                var fileInfo = new FileInfo(SavePath);
                if (fileInfo.Length > MaximumSaveCharacters * 4L)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Save file exceeds the supported size limit.");
                }

                json = File.ReadAllText(SavePath, Encoding.UTF8);
                if (json.Length == 0 || json.Length > MaximumSaveCharacters)
                {
                    json = string.Empty;
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Save file is empty or exceeds the supported size limit.");
                }

                return ActionResult.Success("Save file read.");
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                json = string.Empty;
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    $"Could not read the save file: {exception.Message}");
            }
        }

        public ActionResult Delete()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    File.Delete(SavePath);
                }

                TryDeleteTemporaryFile(SavePath + ".tmp");
                return ActionResult.Success("Save file removed.");
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                return ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    $"Could not remove the save file: {exception.Message}");
            }
        }

        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                Debug.LogWarning($"Could not remove temporary save file: {exception.Message}");
            }
        }
    }
}
