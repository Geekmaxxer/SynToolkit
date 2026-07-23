#nullable enable

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace SynToolkit.Services
{
    public enum LegacyProfileMigrationStatus
    {
        NotEligible,
        AlreadyCompleted,
        Completed,
        Failed
    }

    public readonly record struct LegacyProfileMigrationResult(
        LegacyProfileMigrationStatus Status,
        int Copied,
        int SkippedExisting,
        int Rejected,
        string? Error = null);

    /// <summary>
    /// Performs a one-time, copy-only migration of legacy profile JSON files.
    /// It never resolves configuration services and never applies profile state.
    /// </summary>
    public static class LegacyProfileMigrationService
    {
        private const string LegacyUninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{98A41650-0BAA-476E-A372-01B5FB0A76FA}_is1";
        private const string CompletionMarkerName =
            "LegacyProfiles-Kwanteks-Syntoolkit-1.5.0.completed";
        private const int MaximumDirectoryEntries = 4096;

        public static LegacyProfileMigrationResult TryMigrateAtStartup()
        {
            if (Array.Exists(
                    Environment.GetCommandLineArgs(),
                    argument => string.Equals(
                        argument,
                        "--shutdown-for-update",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return new LegacyProfileMigrationResult(LegacyProfileMigrationStatus.NotEligible, 0, 0, 0);
            }

            if (!TryReadExactLegacyRegistration())
            {
                return new LegacyProfileMigrationResult(LegacyProfileMigrationStatus.NotEligible, 0, 0, 0);
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string commonApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(windowsDirectory)
                || string.IsNullOrWhiteSpace(commonApplicationData))
            {
                return Failed("Windows profile paths are unavailable.");
            }

            string sourceDirectory = Path.Combine(
                windowsDirectory,
                "AtlasModules",
                "Toolbox",
                "Profiles");
            string destinationDirectory = Path.Combine(
                commonApplicationData,
                "Synergy",
                "Profiles");
            string migrationStateDirectory = Path.Combine(
                commonApplicationData,
                "Synergy",
                "MigrationState");

            LegacyProfileMigrationResult result = MigrateProfiles(
                windowsDirectory,
                sourceDirectory,
                commonApplicationData,
                destinationDirectory,
                migrationStateDirectory);

            switch (result.Status)
            {
                case LegacyProfileMigrationStatus.Completed:
                    App.logger.Info(
                        $"Legacy profile migration completed: copied={result.Copied}, " +
                        $"existing={result.SkippedExisting}, rejected={result.Rejected}. " +
                        "No profile was applied.");
                    break;
                case LegacyProfileMigrationStatus.AlreadyCompleted:
                    App.logger.Debug("Legacy profile migration was already completed.");
                    break;
                case LegacyProfileMigrationStatus.Failed:
                    App.logger.Warn($"Legacy profile migration was not completed: {result.Error}");
                    break;
            }

            return result;
        }

        internal static LegacyProfileMigrationResult MigrateProfiles(
            string sourceRoot,
            string sourceDirectory,
            string destinationRoot,
            string destinationDirectory,
            string migrationStateDirectory)
        {
            try
            {
                if (!TryPrepareSafeDirectory(
                        destinationRoot,
                        destinationDirectory,
                        createIfMissing: true,
                        out string destinationError))
                {
                    return Failed(destinationError);
                }

                if (!TryPrepareSafeDirectory(
                        destinationRoot,
                        migrationStateDirectory,
                        createIfMissing: true,
                        out string stateError))
                {
                    return Failed(stateError);
                }

                string markerPath = Path.Combine(migrationStateDirectory, CompletionMarkerName);
                if (TryGetAttributes(markerPath, out FileAttributes markerAttributes))
                {
                    if ((markerAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return Failed("The migration completion marker is a reparse point.");
                    }

                    return new LegacyProfileMigrationResult(
                        LegacyProfileMigrationStatus.AlreadyCompleted,
                        0,
                        0,
                        0);
                }

                if (!TryValidateExistingPath(
                        sourceRoot,
                        sourceDirectory,
                        requireTargetDirectory: false,
                        out string sourcePathError))
                {
                    return Failed(sourcePathError);
                }

                if (!Directory.Exists(sourceDirectory))
                {
                    if (!TryWriteCompletionMarker(markerPath, 0, 0, 0, out string markerError))
                    {
                        return Failed(markerError);
                    }

                    return new LegacyProfileMigrationResult(
                        LegacyProfileMigrationStatus.Completed,
                        0,
                        0,
                        0);
                }

                if (!TryValidateExistingPath(
                        sourceRoot,
                        sourceDirectory,
                        requireTargetDirectory: true,
                        out string sourceError))
                {
                    return Failed(sourceError);
                }

                int copied = 0;
                int skippedExisting = 0;
                int rejected = 0;
                int entryCount = 0;
                bool copyFailed = false;
                string? copyError = null;

                foreach (string entryPath in Directory.EnumerateFileSystemEntries(
                    sourceDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaximumDirectoryEntries)
                    {
                        copyFailed = true;
                        copyError = "The legacy profile directory exceeds the safe entry limit.";
                        break;
                    }

                    string entryName = Path.GetFileName(entryPath);
                    if (!TryGetAttributes(entryPath, out FileAttributes attributes))
                    {
                        copyFailed = true;
                        copyError = "A legacy profile disappeared while it was being inspected.";
                        continue;
                    }

                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                        || !string.Equals(
                            Path.GetExtension(entryName),
                            ".json",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        rejected++;
                        continue;
                    }

                    string destinationPath = Path.Combine(destinationDirectory, entryName);
                    if (TryGetAttributes(destinationPath, out _))
                    {
                        skippedExisting++;
                        continue;
                    }

                    if (!TryReadBoundedFile(
                            entryPath,
                            out byte[] content,
                            out bool invalidFile,
                            out string readError))
                    {
                        if (invalidFile)
                        {
                            rejected++;
                            App.logger.Warn(
                                $"Skipping legacy profile '{SafeLogName(entryName)}': {readError}");
                        }
                        else
                        {
                            copyFailed = true;
                            copyError = readError;
                            App.logger.Warn(
                                $"Unable to read legacy profile '{SafeLogName(entryName)}': {readError}");
                        }

                        continue;
                    }

                    if (!LegacyProfileMigrationPolicy.TryValidateProfile(
                            content,
                            entryName,
                            out string rejectionReason))
                    {
                        rejected++;
                        App.logger.Warn(
                            $"Skipping legacy profile '{SafeLogName(entryName)}': {rejectionReason}.");
                        continue;
                    }

                    if (TryCopyAtomicallyWithoutOverwrite(
                            content,
                            destinationDirectory,
                            destinationPath,
                            out bool destinationAlreadyExists,
                            out string writeError))
                    {
                        if (destinationAlreadyExists)
                        {
                            skippedExisting++;
                        }
                        else
                        {
                            copied++;
                        }

                        continue;
                    }

                    copyFailed = true;
                    copyError = writeError;
                    App.logger.Warn(
                        $"Unable to copy legacy profile '{SafeLogName(entryName)}': {writeError}");
                }

                if (copyFailed)
                {
                    return new LegacyProfileMigrationResult(
                        LegacyProfileMigrationStatus.Failed,
                        copied,
                        skippedExisting,
                        rejected,
                        copyError ?? "One or more valid profiles could not be copied.");
                }

                if (!TryWriteCompletionMarker(
                        markerPath,
                        copied,
                        skippedExisting,
                        rejected,
                        out string completionError))
                {
                    return new LegacyProfileMigrationResult(
                        LegacyProfileMigrationStatus.Failed,
                        copied,
                        skippedExisting,
                        rejected,
                        completionError);
                }

                return new LegacyProfileMigrationResult(
                    LegacyProfileMigrationStatus.Completed,
                    copied,
                    skippedExisting,
                    rejected);
            }
            catch (Exception exception) when (IsExpectedMigrationException(exception))
            {
                return Failed(exception.Message);
            }
        }

        private static bool TryReadExactLegacyRegistration()
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry64);
                using RegistryKey? uninstallKey = baseKey.OpenSubKey(LegacyUninstallKey, writable: false);
                if (uninstallKey is null)
                {
                    return false;
                }

                string? displayName = uninstallKey.GetValue(
                    "DisplayName",
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                string? publisher = uninstallKey.GetValue(
                    "Publisher",
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                string? displayVersion = uninstallKey.GetValue(
                    "DisplayVersion",
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

                return LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                    displayName,
                    publisher,
                    displayVersion);
            }
            catch (Exception exception) when (exception is SecurityException
                or UnauthorizedAccessException
                or IOException)
            {
                App.logger.Warn(exception, "Unable to inspect the legacy Syntoolkit registration.");
                return false;
            }
        }

        private static bool TryReadBoundedFile(
            string path,
            out byte[] content,
            out bool invalidFile,
            out string error)
        {
            content = Array.Empty<byte>();
            invalidFile = false;
            error = string.Empty;

            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);
                if (stream.Length <= 0
                    || stream.Length > LegacyProfileMigrationPolicy.MaximumProfileBytes)
                {
                    invalidFile = true;
                    error = "the file is empty or exceeds the profile size limit";
                    return false;
                }

                content = new byte[checked((int)stream.Length)];
                stream.ReadExactly(content);
                if (stream.ReadByte() != -1)
                {
                    content = Array.Empty<byte>();
                    invalidFile = false;
                    error = "the file changed while it was being read";
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (IsExpectedMigrationException(exception))
            {
                content = Array.Empty<byte>();
                invalidFile = false;
                error = exception.Message;
                return false;
            }
        }

        private static bool TryCopyAtomicallyWithoutOverwrite(
            byte[] content,
            string destinationDirectory,
            string destinationPath,
            out bool destinationAlreadyExists,
            out string error)
        {
            destinationAlreadyExists = false;
            error = string.Empty;
            string temporaryPath = Path.Combine(
                destinationDirectory,
                $".legacy-profile-{Guid.NewGuid():N}.tmp");

            try
            {
                using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(content);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(temporaryPath, destinationPath);
                    return true;
                }
                catch (IOException) when (TryGetAttributes(destinationPath, out _))
                {
                    destinationAlreadyExists = true;
                    return true;
                }
            }
            catch (Exception exception) when (IsExpectedMigrationException(exception))
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (IsExpectedMigrationException(exception))
                {
                    App.logger.Warn(
                        exception,
                        "Unable to remove a temporary legacy-profile migration file.");
                }
            }
        }

        private static bool TryWriteCompletionMarker(
            string markerPath,
            int copied,
            int skippedExisting,
            int rejected,
            out string error)
        {
            error = string.Empty;
            string markerDirectory = Path.GetDirectoryName(markerPath)
                ?? throw new InvalidOperationException("The migration marker directory is unavailable.");
            string temporaryPath = Path.Combine(
                markerDirectory,
                $".{CompletionMarkerName}.{Guid.NewGuid():N}.tmp");
            string markerText =
                "Migration=LegacyProfiles-Kwanteks-Syntoolkit-1.5.0" + Environment.NewLine +
                $"CompletedUtc={DateTimeOffset.UtcNow:O}" + Environment.NewLine +
                $"Copied={copied}" + Environment.NewLine +
                $"SkippedExisting={skippedExisting}" + Environment.NewLine +
                $"Rejected={rejected}" + Environment.NewLine +
                "ProfilesApplied=0" + Environment.NewLine;

            try
            {
                using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(markerText);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(temporaryPath, markerPath);
                    return true;
                }
                catch (IOException) when (TryGetAttributes(markerPath, out FileAttributes attributes)
                    && (attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // Another concurrently starting instance completed the same
                    // migration. Its marker is authoritative.
                    return true;
                }
            }
            catch (Exception exception) when (IsExpectedMigrationException(exception))
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (IsExpectedMigrationException(exception))
                {
                    App.logger.Warn(
                        exception,
                        "Unable to remove a temporary legacy-profile migration marker.");
                }
            }
        }

        private static bool TryPrepareSafeDirectory(
            string trustedRoot,
            string targetDirectory,
            bool createIfMissing,
            out string error)
        {
            if (!TryValidateExistingPath(
                    trustedRoot,
                    targetDirectory,
                    requireTargetDirectory: false,
                    out error))
            {
                return false;
            }

            if (createIfMissing)
            {
                Directory.CreateDirectory(targetDirectory);
            }

            return TryValidateExistingPath(
                trustedRoot,
                targetDirectory,
                requireTargetDirectory: true,
                out error);
        }

        private static bool TryValidateExistingPath(
            string trustedRoot,
            string targetDirectory,
            bool requireTargetDirectory,
            out string error)
        {
            error = string.Empty;
            string root = Path.GetFullPath(trustedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string target = Path.GetFullPath(targetDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string relative = Path.GetRelativePath(root, target);

            if (relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                error = "A migration path is outside its trusted root.";
                return false;
            }

            string current = root;
            if (!TryValidateDirectoryComponent(current, required: true, out error))
            {
                return false;
            }

            if (!string.Equals(relative, ".", StringComparison.Ordinal))
            {
                foreach (string component in relative.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, component);
                    if (!TryValidateDirectoryComponent(current, required: false, out error))
                    {
                        return false;
                    }
                }
            }

            if (requireTargetDirectory && !Directory.Exists(target))
            {
                error = "A required migration directory is missing.";
                return false;
            }

            return true;
        }

        private static bool TryValidateDirectoryComponent(
            string path,
            bool required,
            out string error)
        {
            error = string.Empty;
            if (!TryGetAttributes(path, out FileAttributes attributes))
            {
                if (required)
                {
                    error = "A required migration path component is missing.";
                    return false;
                }

                return true;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                error = "A migration path contains a reparse point.";
                return false;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                error = "A migration path component is not a directory.";
                return false;
            }

            return true;
        }

        private static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                attributes = default;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                attributes = default;
                return false;
            }
        }

        private static bool IsExpectedMigrationException(Exception exception)
        {
            return exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or ArgumentException
                or NotSupportedException;
        }

        private static LegacyProfileMigrationResult Failed(string error)
        {
            return new LegacyProfileMigrationResult(
                LegacyProfileMigrationStatus.Failed,
                0,
                0,
                0,
                error);
        }

        private static string SafeLogName(string name)
        {
            StringBuilder builder = new(capacity: Math.Min(name.Length, 128));
            foreach (char character in name)
            {
                if (builder.Length >= 128)
                {
                    break;
                }

                builder.Append(char.IsControl(character) ? '?' : character);
            }

            return builder.ToString();
        }
    }
}
