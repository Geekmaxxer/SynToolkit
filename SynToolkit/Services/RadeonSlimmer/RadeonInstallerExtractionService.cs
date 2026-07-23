#nullable enable

using System;
using System.IO;
using System.Linq;
using SynToolkit.Utils;

namespace SynToolkit.Services.RadeonSlimmer
{
    /// <summary>
    /// Validates and extracts an AMD Radeon Software installer via the bundled 7-Zip console
    /// tool, and launches the resulting (possibly slimmed) Setup.exe. Ported from
    /// GSDragoon/RadeonSoftwareSlimmer's InstallerFilesModel
    /// (https://github.com/GSDragoon/RadeonSoftwareSlimmer, GPL-3.0 License) — same license as
    /// SynToolkit itself.
    /// </summary>
    public static class RadeonInstallerExtractionService
    {
        public static string DefaultExtractionFolderFor(string installerFilePath)
        {
            string? directory = Path.GetDirectoryName(installerFilePath);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(installerFilePath);
            return string.IsNullOrEmpty(directory)
                ? nameWithoutExtension
                : Path.Combine(directory, nameWithoutExtension);
        }

        public static void ValidateInstallerFile(string installerFilePath)
        {
            if (string.IsNullOrWhiteSpace(installerFilePath))
            {
                throw new ArgumentException("Choose a Radeon Software installer file.", nameof(installerFilePath));
            }

            if (!File.Exists(installerFilePath))
            {
                throw new FileNotFoundException("The selected installer file does not exist or cannot be accessed.", installerFilePath);
            }
        }

        public static void ValidatePreExtractLocation(string extractionFolderPath)
        {
            if (string.IsNullOrWhiteSpace(extractionFolderPath))
            {
                throw new ArgumentException("Enter an extraction folder.", nameof(extractionFolderPath));
            }

            if (Path.GetInvalidPathChars().Any(extractionFolderPath.Contains))
            {
                throw new ArgumentException("The extraction folder path contains invalid characters.", nameof(extractionFolderPath));
            }

            if (Directory.Exists(extractionFolderPath) &&
                (Directory.EnumerateDirectories(extractionFolderPath).Any() || Directory.EnumerateFiles(extractionFolderPath).Any()))
            {
                throw new InvalidOperationException($"The extraction folder {extractionFolderPath} is not empty.");
            }
        }

        public static void ValidateExtractedLocation(string extractionFolderPath)
        {
            bool looksValid = Directory.Exists(extractionFolderPath) &&
                Directory.Exists(Path.Combine(extractionFolderPath, "Bin64")) &&
                Directory.Exists(Path.Combine(extractionFolderPath, "Config")) &&
                File.Exists(Path.Combine(extractionFolderPath, "Setup.exe")) &&
                File.Exists(Path.Combine(extractionFolderPath, "Bin64", "AMDCleanupUtility.exe"));

            if (!looksValid)
            {
                throw new InvalidOperationException($"Expected Radeon Software installer files were not found in {extractionFolderPath}.");
            }
        }

        public static void ExtractInstallerFiles(string installerFilePath, string extractionFolderPath)
        {
            string sevenZipExe = Path.Combine(AppContext.BaseDirectory, "assets", "7-Zip", "7z.exe");
            if (!File.Exists(sevenZipExe))
            {
                throw new FileNotFoundException("The bundled 7-Zip tool is missing from this SynToolkit install.", sevenZipExe);
            }

            CommandResult result = CommandPromptHelper.RunProcessResult(
                sevenZipExe,
                ["x", installerFilePath, $"-o{extractionFolderPath}"],
                timeoutMilliseconds: 30 * 60_000);

            // https://sevenzip.osdn.jp/chm/cmdline/exit_codes.htm
            if (!result.Succeeded)
            {
                throw new IOException($"Extraction failed (7-Zip exit code {result.ExitCode}). {result.CombinedOutput}".Trim());
            }
        }

        public static void RunSetup(string extractionFolderPath) =>
            ProcessHelper.StartShellExecute(Path.Combine(extractionFolderPath, "Setup.exe"));
    }
}
