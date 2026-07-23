#nullable enable

using Microsoft.Win32;
using SynToolkit.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SynToolkit.Services
{
    public interface ISystemInformationService
    {
        SystemInformationSnapshot Detect();
    }

    /// <summary>
    /// Reads Windows and AME playbook identity without modifying the system.
    /// Playbook detection is deliberately conservative because AME does not
    /// require every playbook to persist a standard identity marker.
    /// </summary>
    public sealed partial class SystemInformationService : ISystemInformationService
    {
        private const string WindowsCurrentVersionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        private const string OemInformationPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation";
        private readonly IAmePlaybookMetadataSource _amePlaybookMetadataSource;

        private static readonly (string Path, string[] NameValues, string[] VersionValues)[] ExplicitPlaybookKeys =
        {
            (@"SOFTWARE\SynergyOS\Playbook", new[] { "Name", "DisplayName", "PlaybookName" }, new[] { "Version", "PlaybookVersion" }),
            (@"SOFTWARE\SynergyOS", new[] { "PlaybookName" }, new[] { "PlaybookVersion" }),
            (@"SOFTWARE\AME\Playbook", new[] { "Name", "DisplayName", "PlaybookName" }, new[] { "Version", "PlaybookVersion" }),
            (@"SOFTWARE\Ameliorated\Playbook", new[] { "Name", "DisplayName", "PlaybookName" }, new[] { "Version", "PlaybookVersion" })
        };

        public SystemInformationService()
        {
            _amePlaybookMetadataSource = new WindowsAmePlaybookMetadataSource(
                (exception, message) => App.logger.Warn(exception, message));
        }

        public SystemInformationSnapshot Detect()
        {
            string productName = ReadString(WindowsCurrentVersionPath, "ProductName")
                ?? RuntimeInformation.OSDescription
                ?? "Windows";
            string displayVersion = ReadString(WindowsCurrentVersionPath, "DisplayVersion")
                ?? ReadString(WindowsCurrentVersionPath, "ReleaseId")
                ?? "Unknown";
            string buildText = ReadString(WindowsCurrentVersionPath, "CurrentBuildNumber")
                ?? ReadString(WindowsCurrentVersionPath, "CurrentBuild")
                ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
            int buildNumber = int.TryParse(buildText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBuild)
                ? parsedBuild
                : Environment.OSVersion.Version.Build;
            int? updateBuildRevision = ReadInt32(WindowsCurrentVersionPath, "UBR");
            string installationType = ReadString(WindowsCurrentVersionPath, "InstallationType") ?? string.Empty;

            productName = NormalizeWindowsProductName(productName, installationType, buildNumber);
            string fullBuild = updateBuildRevision is >= 0
                ? $"{buildText}.{updateBuildRevision.Value.ToString(CultureInfo.InvariantCulture)}"
                : buildText;

            return new SystemInformationSnapshot(
                productName,
                displayVersion,
                fullBuild,
                FormatArchitecture(RuntimeInformation.OSArchitecture),
                DetectCustomWindowsBuild(),
                DetectPlaybook());
        }

        private static CustomWindowsInformation? DetectCustomWindowsBuild()
        {
            (string? Value, string Source)[] candidates =
            {
                (ReadString(OemInformationPath, "Model"), "Windows OEM model"),
                (ReadString(WindowsCurrentVersionPath, "RegisteredOwner"), "Windows registered owner")
            };

            foreach ((string? value, string source) in candidates)
            {
                CustomWindowsInformation? customWindows = TryParseCustomWindowsMarker(value, source);
                if (customWindows is not null)
                {
                    return customWindows;
                }
            }

            return null;
        }

        private PlaybookInformation DetectPlaybook()
        {
            PlaybookInformation amePlaybook = AmePlaybookDetector.DetectRegistry(_amePlaybookMetadataSource);
            if (amePlaybook.Status != PlaybookDetectionStatus.NotDetected)
            {
                return amePlaybook;
            }

            List<PlaybookInformation> explicitMarkers = new();
            foreach ((string path, string[] nameValues, string[] versionValues) in ExplicitPlaybookKeys)
            {
                string? name = ReadFirstStrictString(path, nameValues);
                AmePlaybookMarker marker = new(
                    name,
                    ReadFirstStrictString(path, versionValues),
                    $@"HKLM\{path}");
                PlaybookInformation? normalized = AmePlaybookDetector.TryNormalize(marker);
                if (normalized is null)
                {
                    continue;
                }

                explicitMarkers.Add(normalized);
            }

            PlaybookInformation currentPlaybook;
            if (explicitMarkers.Count > 0)
            {
                currentPlaybook = AmePlaybookDetector.ResolveMarkers(explicitMarkers);
            }
            else
            {
                List<PlaybookInformation> fallbackMarkers = new();
                AddFallbackMarker(fallbackMarkers, ReadString(OemInformationPath, "Model"), "Windows OEM model");
                AddFallbackMarker(fallbackMarkers, ReadString(WindowsCurrentVersionPath, "RegisteredOrganization"), "Windows registered organization");
                AddFallbackMarker(fallbackMarkers, ReadString(WindowsCurrentVersionPath, "RegisteredOwner"), "Windows registered owner");
                AddFallbackMarker(fallbackMarkers, ReadString(OemInformationPath, "Manufacturer"), "Windows OEM manufacturer");

                currentPlaybook = fallbackMarkers.Count > 0
                    ? AmePlaybookDetector.ResolveMarkers(fallbackMarkers)
                    : new PlaybookInformation(PlaybookDetectionStatus.NotDetected, null, null, null);
            }

            PlaybookInformation legacyPlaybook = currentPlaybook.Status == PlaybookDetectionStatus.NotDetected
                ? AmePlaybookDetector.DetectLegacy(_amePlaybookMetadataSource)
                : new PlaybookInformation(PlaybookDetectionStatus.NotDetected, null, null, null);

            // No-UniqueId AME releases stored historical playbook.conf files
            // under ProgramData. Keep that legacy history below current
            // Synergy and explicit OEM metadata so it cannot mask a newer
            // project-specific identity.
            return PreferCurrentPlaybook(currentPlaybook, legacyPlaybook);
        }

        private static void AddFallbackMarker(List<PlaybookInformation> markers, string? candidate, string source)
        {
            PlaybookInformation? marker = TryParseFallbackPlaybookMarker(candidate, source);
            if (marker is null)
            {
                return;
            }

            markers.Add(marker);
        }

        internal static CustomWindowsInformation? TryParseCustomWindowsMarker(string? candidate, string source)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 256 || LooksLikeUrl(candidate))
            {
                return null;
            }

            string marker = CleanMarker(candidate);
            return !PlaybookNameRegex().IsMatch(marker) && CustomOsMarkerRegex().IsMatch(marker)
                ? new CustomWindowsInformation(marker, source)
                : null;
        }

        internal static PlaybookInformation? TryParseFallbackPlaybookMarker(string? candidate, string source)
        {
            return TryParsePlaybookMarker(candidate, out string? name, out string? version)
                ? new PlaybookInformation(PlaybookDetectionStatus.Detected, name, version, source)
                : null;
        }

        internal static PlaybookInformation PreferCurrentPlaybook(
            PlaybookInformation currentPlaybook,
            PlaybookInformation legacyPlaybook)
        {
            ArgumentNullException.ThrowIfNull(currentPlaybook);
            ArgumentNullException.ThrowIfNull(legacyPlaybook);
            return currentPlaybook.Status == PlaybookDetectionStatus.NotDetected
                ? legacyPlaybook
                : currentPlaybook;
        }

        private static bool TryParsePlaybookMarker(string? candidate, out string? name, out string? version)
        {
            name = null;
            version = null;
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 256 || LooksLikeUrl(candidate))
            {
                return false;
            }

            string marker = CleanMarker(candidate);
            Match versionMatch = VersionRegex().Match(marker);
            version = versionMatch.Success ? CleanVersion(versionMatch.Groups["version"].Value) : null;

            Match playbookMatch = PlaybookNameRegex().Match(marker);
            if (playbookMatch.Success && version is not null)
            {
                name = CleanMarker(playbookMatch.Groups["name"].Value);
            }

            return !string.IsNullOrWhiteSpace(name);
        }

        private static string NormalizeWindowsProductName(string productName, string installationType, int buildNumber)
        {
            string normalized = CleanMarker(productName);
            bool isClient = !installationType.Contains("Server", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("Server", StringComparison.OrdinalIgnoreCase);

            if (isClient && buildNumber >= 22000)
            {
                int legacyNameIndex = normalized.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase);
                if (legacyNameIndex >= 0)
                {
                    normalized = string.Concat(
                        normalized.AsSpan(0, legacyNameIndex),
                        "Windows 11",
                        normalized.AsSpan(legacyNameIndex + "Windows 10".Length));
                }
            }

            return normalized;
        }

        private static string FormatArchitecture(Architecture architecture) => architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "ARM64",
            Architecture.Arm => "ARM",
            _ => architecture.ToString()
        };

        private static string? ReadFirstStrictString(string path, IEnumerable<string> valueNames)
        {
            foreach (string valueName in valueNames)
            {
                try
                {
                    if (ReadRegistryValue(path, valueName) is string value
                        && !string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                catch (Exception exception)
                {
                    App.logger.Warn(exception, $"Unable to read HKLM\\{path}\\{valueName} while detecting Playbook information.");
                }
            }

            return null;
        }

        private static string? ReadString(string path, string valueName)
        {
            try
            {
                object? value = ReadRegistryValue(path, valueName);
                string? text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, $"Unable to read HKLM\\{path}\\{valueName} while detecting system information.");
                return null;
            }
        }

        private static int? ReadInt32(string path, string valueName)
        {
            try
            {
                object? value = ReadRegistryValue(path, valueName);
                return value switch
                {
                    int number => number,
                    uint number => unchecked((int)number),
                    long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
                    string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
                    _ => null
                };
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, $"Unable to read HKLM\\{path}\\{valueName} while detecting system information.");
                return null;
            }
        }

        private static object? ReadRegistryValue(string path, string valueName)
        {
            RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey(path, writable: false);
            return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        private static bool LooksLikeUrl(string value) =>
            Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

        private static string CleanMarker(string value) =>
            WhitespaceRegex().Replace(value.Trim().Trim('\0'), " ");

        private static string? CleanVersion(string? value)
        {
            string? cleaned = string.IsNullOrWhiteSpace(value) ? null : CleanMarker(value);
            return cleaned?.TrimStart('v', 'V');
        }

        [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])(?:v(?:ersion)?\s*)?(?<version>\d+(?:\.\d+){0,2})(?![A-Za-z0-9.])", RegexOptions.CultureInvariant)]
        private static partial Regex VersionRegex();

        [GeneratedRegex(@"^(?<name>.+?\bPlaybook)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PlaybookNameRegex();

        [GeneratedRegex(@"^(?!BIOS\b)[A-Z][A-Za-z0-9._-]{1,30}OS(?:10|11)?\s+(?:v(?:ersion)?\s*)?\d+(?:\.\d+){1,2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex CustomOsMarkerRegex();
    }
}
