#nullable enable

using Microsoft.Win32;
using SynToolkit.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SynToolkit.Services
{
    internal sealed record AmePlaybookMarker(
        string? Name,
        string? Version,
        string? Source,
        DateTime? AppliedTimeUtc = null,
        bool? Overhaul = null,
        int? ErrorLevel = null);

    internal interface IAmePlaybookMetadataSource
    {
        IReadOnlyCollection<AmePlaybookMarker> ReadRegistryMarkers();

        AmePlaybookMarker? ReadLatestLegacyMarker();
    }

    /// <summary>
    /// Resolves the metadata that AME Wizard itself persists after applying a
    /// Playbook. Registry-backed Playbooks take priority over the legacy
    /// ProgramData history because the latter can contain older applications.
    /// </summary>
    internal static class AmePlaybookDetector
    {
        internal static PlaybookInformation Detect(IAmePlaybookMetadataSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            PlaybookInformation registry = DetectRegistry(source);
            return registry.Status != PlaybookDetectionStatus.NotDetected
                ? registry
                : DetectLegacy(source);
        }

        internal static PlaybookInformation DetectRegistry(IAmePlaybookMetadataSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            List<(AmePlaybookMarker Marker, PlaybookInformation Information)> registryMarkers = source
                .ReadRegistryMarkers()
                .Where(marker => marker.ErrorLevel is null or 0 or 1)
                .Select(marker => (Marker: marker, Information: TryNormalize(marker)))
                .Where(candidate => candidate.Information is not null)
                .Select(candidate => (candidate.Marker, candidate.Information!))
                .ToList();

            if (registryMarkers.Count > 0)
            {
                // AME can also apply non-overhaul utility Playbooks. When at
                // least one valid OS-overhaul record exists, it is the only
                // relevant set for the Windows identity summary.
                if (registryMarkers.Any(candidate => candidate.Marker.Overhaul == true))
                {
                    registryMarkers = registryMarkers
                        .Where(candidate => candidate.Marker.Overhaul == true)
                        .ToList();
                }

                // AME overwrites AppliedTimeUTC whenever a UniqueId is
                // reapplied or upgraded. Prefer that newest authoritative
                // record for a one-line UI summary when several Playbooks
                // coexist. If timestamps are absent, retain conflict-aware
                // resolution instead of guessing from registry order.
                bool everyMarkerIsTimestamped = registryMarkers.All(candidate =>
                    candidate.Marker.AppliedTimeUtc.HasValue);
                if (everyMarkerIsTimestamped)
                {
                    DateTime newestTime = registryMarkers.Max(candidate =>
                        candidate.Marker.AppliedTimeUtc!.Value);
                    List<PlaybookInformation> newestMarkers = registryMarkers
                        .Where(candidate => candidate.Marker.AppliedTimeUtc == newestTime)
                        .Select(candidate => candidate.Information)
                        .ToList();

                    return newestMarkers.Count == 1
                        ? newestMarkers[0]
                        : ResolveMarkers(newestMarkers);
                }

                // A mixture of timestamped and untimestamped entries has no
                // authoritative ordering. Resolve only when all metadata
                // agrees; otherwise surface the conflict instead of relying
                // on registry enumeration order.
                return ResolveMarkers(registryMarkers
                    .Select(candidate => candidate.Information)
                    .ToList());
            }

            return new PlaybookInformation(PlaybookDetectionStatus.NotDetected, null, null, null);
        }

        internal static PlaybookInformation DetectLegacy(IAmePlaybookMetadataSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            PlaybookInformation? legacyMarker = TryNormalize(source.ReadLatestLegacyMarker());
            return legacyMarker
                ?? new PlaybookInformation(PlaybookDetectionStatus.NotDetected, null, null, null);
        }

        internal static PlaybookInformation? TryNormalize(AmePlaybookMarker? marker)
        {
            if (marker is null || !TryCleanName(marker.Name, out string? name))
            {
                return null;
            }

            string? version = CleanVersion(marker.Version);
            string? source = CleanText(marker.Source, maximumLength: 1024);

            return new PlaybookInformation(
                PlaybookDetectionStatus.Detected,
                name,
                version,
                source);
        }

        internal static PlaybookInformation ResolveMarkers(IReadOnlyCollection<PlaybookInformation> markers)
        {
            List<IGrouping<string, PlaybookInformation>> distinctNames = markers
                .Where(marker => !string.IsNullOrWhiteSpace(marker.Name))
                .GroupBy(marker => marker.Name!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctNames.Count != 1)
            {
                return Conflicting(markers);
            }

            IGrouping<string, PlaybookInformation> selectedGroup = distinctNames[0];
            List<string> distinctVersions = selectedGroup
                .Select(marker => marker.Version)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Multiple records for the same Playbook are harmless when their
            // versions agree. Different versions are ambiguous and must not be
            // presented as a confident detection.
            if (distinctVersions.Count > 1)
            {
                return Conflicting(markers);
            }

            PlaybookInformation selected = selectedGroup
                .OrderByDescending(marker => !string.IsNullOrWhiteSpace(marker.Version))
                .First();

            return selected with
            {
                Source = JoinSources(selectedGroup)
            };
        }

        private static PlaybookInformation Conflicting(IEnumerable<PlaybookInformation> markers) =>
            new(
                PlaybookDetectionStatus.Conflicting,
                null,
                null,
                JoinSources(markers));

        private static string? JoinSources(IEnumerable<PlaybookInformation> markers)
        {
            string[] sources = markers
                .Select(marker => marker.Source)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return sources.Length == 0 ? null : string.Join("; ", sources);
        }

        private static bool TryCleanName(string? value, out string? cleaned)
        {
            cleaned = CleanText(value, maximumLength: 256);
            return !string.IsNullOrWhiteSpace(cleaned) && !LooksLikeUrl(cleaned);
        }

        private static string? CleanVersion(string? value)
        {
            string? cleaned = CleanText(value, maximumLength: 64);
            if (string.IsNullOrWhiteSpace(cleaned) || LooksLikeUrl(cleaned))
            {
                return null;
            }

            if (cleaned[0] == 'v' || cleaned[0] == 'V')
            {
                if (cleaned.Length == 1)
                {
                    return null;
                }

                cleaned = cleaned[1..].TrimStart();
            }

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            string[] components = cleaned.Split('.');
            return components.Length is >= 1 and <= 3
                && components.All(component => component.Length > 0 && component.All(char.IsAsciiDigit))
                    ? cleaned
                    : null;
        }

        private static string? CleanText(string? value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            {
                return null;
            }

            string cleaned = string.Join(
                " ",
                value.Trim().Trim('\0')
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (cleaned.Length == 0
                || cleaned.Length > maximumLength
                || cleaned.Any(character =>
                    (char.IsControl(character) && !char.IsWhiteSpace(character))
                    || char.GetUnicodeCategory(character) == UnicodeCategory.Format))
            {
                return null;
            }

            return cleaned;
        }

        private static bool LooksLikeUrl(string value) =>
            Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Read-only implementation of AME Wizard's applied-Playbook storage.
    /// Current AME releases use HKLM\SOFTWARE\AME\Playbooks\Applied\{GUID};
    /// older no-UniqueId Playbooks are retained as ProgramData XML metadata.
    /// </summary>
    internal sealed class WindowsAmePlaybookMetadataSource : IAmePlaybookMetadataSource
    {
        internal const string AppliedRegistryPath = @"SOFTWARE\AME\Playbooks\Applied";
        internal const long MaximumConfigurationBytes = 1024 * 1024;
        internal const int MaximumRegistryEntriesPerView = 128;
        private const int MaximumLegacyDirectories = 32;

        private readonly Action<Exception, string>? _logWarning;
        private readonly string _legacyRoot;

        internal WindowsAmePlaybookMetadataSource(Action<Exception, string>? logWarning = null)
            : this(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AME",
                    "AppliedPlaybooks"),
                logWarning)
        {
        }

        internal WindowsAmePlaybookMetadataSource(
            string legacyRoot,
            Action<Exception, string>? logWarning = null)
        {
            _legacyRoot = legacyRoot ?? throw new ArgumentNullException(nameof(legacyRoot));
            _logWarning = logWarning;
        }

        public IReadOnlyCollection<AmePlaybookMarker> ReadRegistryMarkers()
        {
            List<AmePlaybookMarker> markers = new();
            HashSet<Guid> seenIdentifiers = new();
            RegistryView[] views = Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Registry32 };

            foreach (RegistryView view in views)
            {
                int markersBeforeView = markers.Count;
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using RegistryKey? appliedKey = baseKey.OpenSubKey(AppliedRegistryPath, writable: false);
                    if (appliedKey is null)
                    {
                        continue;
                    }

                    foreach (string subKeyName in appliedKey
                        .GetSubKeyNames()
                        .Take(MaximumRegistryEntriesPerView))
                    {
                        string guidText = subKeyName.Trim('{', '}');
                        if (!Guid.TryParse(guidText, out Guid identifier)
                            || seenIdentifiers.Contains(identifier))
                        {
                            continue;
                        }

                        try
                        {
                            using RegistryKey? playbookKey = appliedKey.OpenSubKey(subKeyName, writable: false);
                            if (playbookKey is null)
                            {
                                continue;
                            }

                            object? rawAppliedTime = playbookKey.GetValue(
                                "AppliedTimeUTC",
                                null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames);
                            DateTime? appliedTimeUtc = TryDecodeAppliedTimeUtc(rawAppliedTime);
                            if (rawAppliedTime is not null && !appliedTimeUtc.HasValue)
                            {
                                LogWarning(
                                    new FormatException("AppliedTimeUTC is not a valid AME DateTime binary value."),
                                    $"Ignoring an invalid AME Playbook timestamp in '{subKeyName}' ({view}).");
                            }

                            object? rawOverhaul = playbookKey.GetValue(
                                "Overhaul",
                                null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames);
                            object? rawErrorLevel = playbookKey.GetValue(
                                "ErrorLevel",
                                null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames);
                            int? overhaulValue = TryDecodeRegistryInteger(rawOverhaul);
                            int? errorLevel = TryDecodeRegistryInteger(rawErrorLevel);
                            if ((rawOverhaul is not null && overhaulValue is not (0 or 1))
                                || (rawErrorLevel is not null && errorLevel is not (0 or 1 or 2)))
                            {
                                LogWarning(
                                    new FormatException("AME Overhaul or ErrorLevel metadata is invalid."),
                                    $"Ignoring invalid AME Playbook state metadata in '{subKeyName}' ({view}).");
                                continue;
                            }

                            if (errorLevel == 2)
                            {
                                // AME records fatal application attempts, but
                                // they do not represent a Playbook the system
                                // successfully reached and should not identify
                                // the current OS configuration.
                                continue;
                            }

                            AmePlaybookMarker marker = new(
                                ReadRegistryString(playbookKey, "Name"),
                                ReadRegistryString(playbookKey, "Version"),
                                $@"HKLM[{view}]\{AppliedRegistryPath}\{{{identifier.ToString().ToUpperInvariant()}}}",
                                appliedTimeUtc,
                                overhaulValue.HasValue ? overhaulValue == 1 : null,
                                errorLevel);
                            if (AmePlaybookDetector.TryNormalize(marker) is null)
                            {
                                continue;
                            }

                            markers.Add(marker);
                            seenIdentifiers.Add(identifier);
                        }
                        catch (Exception exception)
                        {
                            LogWarning(exception, $"Unable to read AME Playbook registry entry '{subKeyName}'.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    LogWarning(exception, $"Unable to enumerate AME Wizard's applied-Playbook registry metadata in {view}.");
                }

                // Current AME builds write the native 64-bit registry view.
                // Consult Registry32 only as a whole-view compatibility
                // fallback when Registry64 contained no usable records.
                if (markers.Count > markersBeforeView)
                {
                    break;
                }
            }

            return markers;
        }

        public AmePlaybookMarker? ReadLatestLegacyMarker()
        {
            try
            {
                if (!Directory.Exists(_legacyRoot))
                {
                    return null;
                }

                if (File.GetAttributes(_legacyRoot).HasFlag(FileAttributes.ReparsePoint))
                {
                    return null;
                }

                string rootPath = EnsureTrailingSeparator(Path.GetFullPath(_legacyRoot));
                IEnumerable<string> candidates = Directory
                    .EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => IsSafeDirectChild(rootPath, path))
                    .Where(path => GetLegacyDirectoryIndex(path) != int.MinValue)
                    .OrderByDescending(GetLegacyDirectoryIndex)
                    .ThenByDescending(GetLastWriteTimeUtcSafe)
                    .Take(MaximumLegacyDirectories);

                foreach (string candidate in candidates)
                {
                    string configurationPath = Path.Combine(candidate, "playbook.conf");
                    if (TryReadPlaybookConfiguration(configurationPath, out AmePlaybookMarker? marker))
                    {
                        return marker;
                    }
                }
            }
            catch (Exception exception)
            {
                LogWarning(exception, "Unable to enumerate AME Wizard's legacy applied-Playbook metadata.");
            }

            return null;
        }

        internal static bool TryReadPlaybookConfiguration(
            string configurationPath,
            out AmePlaybookMarker? marker)
        {
            marker = null;
            try
            {
                FileInfo configuration = new(configurationPath);
                if (!configuration.Exists
                    || configuration.Length <= 0
                    || configuration.Length > MaximumConfigurationBytes
                    || configuration.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }

                XmlReaderSettings settings = new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    MaxCharactersInDocument = MaximumConfigurationBytes
                };

                using FileStream stream = new(
                    configuration.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                if (stream.Length <= 0 || stream.Length > MaximumConfigurationBytes)
                {
                    return false;
                }

                configuration.Refresh();
                if (configuration.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }
                using XmlReader reader = XmlReader.Create(stream, settings);
                XDocument document = XDocument.Load(reader, LoadOptions.None);
                XElement? root = document.Root;
                if (root is null || !root.Name.LocalName.Equals("Playbook", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string? name = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                string? version = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("Version", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                marker = new AmePlaybookMarker(name, version, configuration.FullName);
                return AmePlaybookDetector.TryNormalize(marker) is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string? ReadRegistryString(RegistryKey key, string valueName) =>
            key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value
                ? value
                : null;

        internal static DateTime? TryDecodeAppliedTimeUtc(object? value, DateTime? utcNow = null)
        {
            try
            {
                long? binary = value switch
                {
                    long number => number,
                    ulong number when number <= long.MaxValue => (long)number,
                    _ => null
                };

                if (!binary.HasValue)
                {
                    return null;
                }

                DateTime timestamp = DateTime.FromBinary(binary.Value).ToUniversalTime();
                DateTime latestPlausibleTime = (utcNow ?? DateTime.UtcNow).ToUniversalTime().AddDays(1);
                return timestamp.Year >= 2000 && timestamp <= latestPlausibleTime
                    ? timestamp
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static int? TryDecodeRegistryInteger(object? value) => value switch
        {
            int number => number,
            uint number when number <= int.MaxValue => (int)number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            ulong number when number <= int.MaxValue => (int)number,
            _ => null
        };

        private static bool IsSafeDirectChild(string rootPath, string candidate)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                    && !File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return false;
            }
        }

        private static int GetLegacyDirectoryIndex(string path) =>
            int.TryParse(Path.GetFileName(path), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                ? index
                : int.MinValue;

        private static DateTime GetLastWriteTimeUtcSafe(string path)
        {
            try
            {
                return Directory.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static string EnsureTrailingSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;

        private void LogWarning(Exception exception, string message)
        {
            try
            {
                _logWarning?.Invoke(exception, message);
            }
            catch
            {
                // Detection must remain read-only and best-effort even when a
                // logging target is unavailable on a customized installation.
            }
        }
    }
}
