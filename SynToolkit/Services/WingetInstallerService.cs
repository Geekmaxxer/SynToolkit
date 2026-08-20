#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using YamlDotNet.Serialization;

namespace SynToolkit.Services
{
    public sealed record WingetInstallResult(bool Succeeded, int ExitCode, string Output);

    public sealed record CuratedPackageProbe(
        string PackageIdentifier,
        IReadOnlyList<string> InstalledDisplayNamePrefixes);

    public sealed record CuratedPackageStatus(
        string PackageIdentifier,
        bool IsInstalled,
        bool IsUpdateAvailable,
        string? InstalledVersion,
        string? AvailableVersion,
        bool IsUpdateCheckComplete);

    /// <summary>
    /// Installs curated packages through the Windows Package Manager community source.
    /// Each invocation is non-interactive and can be canceled with its process tree.
    /// </summary>
    public sealed class WingetInstallerService
    {
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(15);
        private static readonly HttpClient ManifestClient = CreateManifestClient();
        private static readonly HttpClient ManifestCatalogClient = CreateManifestCatalogClient();
        private static readonly SemaphoreSlim ManifestLookupSemaphore = new(4, 4);
        private readonly AppFetchService _appFetchService;
        private readonly ConcurrentDictionary<string, string> _latestVersionCache =
            new(StringComparer.OrdinalIgnoreCase);
        private bool? _isWingetAvailable;

        public WingetInstallerService(AppFetchService appFetchService)
        {
            _appFetchService = appFetchService;
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            if (_isWingetAvailable.HasValue)
            {
                return _isWingetAvailable.Value;
            }

            try
            {
                WingetInstallResult result = await RunWingetAsync(["--version"], TimeSpan.FromSeconds(15), cancellationToken);
                _isWingetAvailable = result.Succeeded;
            }
            catch (Win32Exception exception)
            {
                App.logger.Warn(exception, "[Installers] Windows Package Manager was not found.");
                _isWingetAvailable = false;
            }

            return _isWingetAvailable.Value;
        }

        public async Task<WingetInstallResult> InstallAsync(
            string packageIdentifier,
            string? silentArgumentsOverride = null,
            bool isUpdate = false,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageIdentifier);

            if (await IsAvailableAsync(cancellationToken))
            {
                return await RunWingetAsync(
                    [
                        isUpdate ? "upgrade" : "install",
                        "--exact",
                        "--id",
                        packageIdentifier,
                        "--source",
                        "winget",
                        "--silent",
                        "--accept-source-agreements",
                        "--accept-package-agreements",
                        "--disable-interactivity"
                    ],
                    InstallTimeout,
                    cancellationToken);
            }

            return await InstallFromPackageManifestAsync(packageIdentifier, silentArgumentsOverride, progress, cancellationToken);
        }

        public async Task<WingetInstallResult> UninstallAsync(
            string packageIdentifier,
            IReadOnlyList<string> installedDisplayNamePrefixes,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageIdentifier);
            ArgumentNullException.ThrowIfNull(installedDisplayNamePrefixes);

            WingetInstallResult? wingetResult = null;
            if (await IsAvailableAsync(cancellationToken))
            {
                wingetResult = await RunWingetAsync(
                    [
                        "uninstall",
                        "--exact",
                        "--id",
                        packageIdentifier,
                        "--source",
                        "winget",
                        "--silent",
                        "--accept-source-agreements",
                        "--disable-interactivity"
                    ],
                    UninstallTimeout,
                    cancellationToken);

                if (wingetResult.Succeeded)
                {
                    return wingetResult;
                }

                // A canceled uninstall must stay canceled. Other failures may mean
                // WinGet cannot correlate a registry-installed copy with its package ID.
                if (unchecked((uint)wingetResult.ExitCode) == 0x8A15010C)
                {
                    return wingetResult;
                }
            }

            InstalledDesktopApplication? installedApplication = await Task.Run(
                () => FindInstalledApplication(installedDisplayNamePrefixes),
                cancellationToken);
            if (installedApplication == null)
            {
                return new WingetInstallResult(true, 0, "The app is no longer detected on this PC.");
            }

            string? uninstallCommand = !string.IsNullOrWhiteSpace(installedApplication.QuietUninstallString)
                ? installedApplication.QuietUninstallString
                : installedApplication.UninstallString;
            if (string.IsNullOrWhiteSpace(uninstallCommand))
            {
                return wingetResult ?? new WingetInstallResult(
                    false,
                    -1,
                    "Windows did not provide an uninstall command for this app.");
            }

            return await RunRegisteredUninstallerAsync(uninstallCommand, cancellationToken);
        }

        public async Task<IReadOnlyList<CuratedPackageStatus>> DetectPackageStatusesAsync(
            IReadOnlyList<CuratedPackageProbe> probes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(probes);

            IReadOnlyList<InstalledDesktopApplication> installedApplications = await Task.Run(
                ReadInstalledDesktopApplications,
                cancellationToken);

            Task<CuratedPackageStatus>[] statusTasks = probes
                .Select(probe => DetectPackageStatusAsync(probe, installedApplications, cancellationToken))
                .ToArray();
            return await Task.WhenAll(statusTasks);
        }

        private async Task<CuratedPackageStatus> DetectPackageStatusAsync(
            CuratedPackageProbe probe,
            IReadOnlyList<InstalledDesktopApplication> installedApplications,
            CancellationToken cancellationToken)
        {
            InstalledDesktopApplication? installedApplication = installedApplications
                .Where(application => probe.InstalledDisplayNamePrefixes.Any(prefix =>
                    MatchesInstalledDisplayName(application.DisplayName, prefix)))
                .OrderByDescending(application => ParseLooseVersion(application.DisplayVersion))
                .FirstOrDefault();

            if (installedApplication == null)
            {
                return new CuratedPackageStatus(probe.PackageIdentifier, false, false, null, null, true);
            }

            string? availableVersion = null;
            bool isUpdateAvailable = false;
            bool isUpdateCheckComplete = false;
            try
            {
                availableVersion = await GetLatestPackageVersionAsync(probe.PackageIdentifier, cancellationToken);
                if (TryParseLooseVersion(installedApplication.DisplayVersion, out Version installedVersion) &&
                    TryParseLooseVersion(availableVersion, out Version publishedVersion))
                {
                    isUpdateCheckComplete = true;
                    isUpdateAvailable = publishedVersion > installedVersion;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Debug(
                    exception,
                    "[Installers] Unable to check the published version for {PackageIdentifier}.",
                    probe.PackageIdentifier);
            }

            return new CuratedPackageStatus(
                probe.PackageIdentifier,
                true,
                isUpdateAvailable,
                installedApplication.DisplayVersion,
                availableVersion,
                isUpdateCheckComplete);
        }

        private async Task<WingetInstallResult> InstallFromPackageManifestAsync(
            string packageIdentifier,
            string? silentArgumentsOverride,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                AppFetchService.StorePackageDto? compatiblePackage =
                    await ResolveLatestPackageAsync(packageIdentifier, silentArgumentsOverride, cancellationToken);

                if (compatiblePackage == null)
                {
                    return new WingetInstallResult(false, -1, "No compatible installer was found for this system architecture.");
                }

                await _appFetchService.DownloadAndInstallPackagesAsync(
                    [compatiblePackage],
                    progress ?? new Progress<double>(_ => { }),
                    cancellationToken);

                return new WingetInstallResult(true, 0, "Installed directly from the Microsoft package manifest.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Error(
                    exception,
                    "[Installers] Direct package-manifest installation failed for {PackageIdentifier}.",
                    packageIdentifier);
                return new WingetInstallResult(false, -1, exception.Message);
            }
        }

        private async Task<AppFetchService.StorePackageDto?> ResolveLatestPackageAsync(
            string packageIdentifier,
            string? silentArgumentsOverride,
            CancellationToken cancellationToken)
        {
            string packagePath = GetPackageManifestPath(packageIdentifier);
            string latestVersion = await GetLatestPackageVersionAsync(packageIdentifier, cancellationToken);
            string escapedVersion = Uri.EscapeDataString(latestVersion);
            string manifestFileName = Uri.EscapeDataString(packageIdentifier + ".installer.yaml");
            string manifestUrl =
                $"https://raw.githubusercontent.com/microsoft/winget-pkgs/master/{packagePath}/{escapedVersion}/{manifestFileName}";
            string manifestYaml = await ManifestClient.GetStringAsync(manifestUrl, cancellationToken);

            IDeserializer deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            WingetInstallerManifest manifest = deserializer.Deserialize<WingetInstallerManifest>(manifestYaml);
            WingetInstallerEntry? compatibleInstaller = SelectCompatibleInstaller(manifest.Installers);
            if (compatibleInstaller == null || string.IsNullOrWhiteSpace(compatibleInstaller.InstallerUrl))
            {
                return null;
            }

            string installerType = compatibleInstaller.InstallerType ?? manifest.InstallerType ?? string.Empty;
            string extension = Path.GetExtension(new Uri(compatibleInstaller.InstallerUrl).AbsolutePath).TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = installerType.Equals("wix", StringComparison.OrdinalIgnoreCase) ? "msi" : "exe";
            }

            string? silentArguments =
                compatibleInstaller.InstallerSwitches?.Silent ??
                compatibleInstaller.InstallerSwitches?.SilentWithProgress ??
                manifest.InstallerSwitches?.Silent ??
                manifest.InstallerSwitches?.SilentWithProgress ??
                silentArgumentsOverride ??
                GetDefaultSilentArguments(installerType);

            return new AppFetchService.StorePackageDto
            {
                Name = packageIdentifier + "-" + compatibleInstaller.Architecture,
                FileExtension = extension,
                ResourceUri = compatibleInstaller.InstallerUrl,
                LastModified = DateTime.UtcNow,
                PackageId = "xp-curated-" + packageIdentifier,
                Checksum = compatibleInstaller.InstallerSha256,
                CommandLines = silentArguments
            };
        }

        private async Task<string> GetLatestPackageVersionAsync(
            string packageIdentifier,
            CancellationToken cancellationToken)
        {
            if (_latestVersionCache.TryGetValue(packageIdentifier, out string? cachedVersion))
            {
                return cachedVersion;
            }

            string packagePath = GetPackageManifestPath(packageIdentifier);
            string catalogUrl = $"https://github.com/microsoft/winget-pkgs/tree/master/{packagePath}";

            await ManifestLookupSemaphore.WaitAsync(cancellationToken);
            try
            {
                string catalogHtml = await ManifestCatalogClient.GetStringAsync(catalogUrl, cancellationToken);
                string versionLinkPrefix = $"/microsoft/winget-pkgs/tree/master/{packagePath}/";
                string? latestVersion = Regex.Matches(
                        catalogHtml,
                        Regex.Escape(versionLinkPrefix) + "([^\"?#/<>&]+)",
                        RegexOptions.CultureInvariant)
                    .Select(match => Uri.UnescapeDataString(match.Groups[1].Value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(version => TryParseVersion(version, out _))
                    .OrderByDescending(ParseVersion)
                    .FirstOrDefault();

                if (latestVersion == null)
                {
                    throw new InvalidOperationException(
                        $"No published WinGet manifest was found for {packageIdentifier}.");
                }

                _latestVersionCache.TryAdd(packageIdentifier, latestVersion);
                return latestVersion;
            }
            finally
            {
                ManifestLookupSemaphore.Release();
            }
        }

        private static string GetPackageManifestPath(string packageIdentifier)
        {
            string[] identifierSegments = packageIdentifier.Split('.');
            string escapedPackagePath = string.Join("/", identifierSegments.Select(Uri.EscapeDataString));
            string partition = char.ToLowerInvariant(packageIdentifier[0]).ToString();
            return $"manifests/{partition}/{escapedPackagePath}";
        }

        private static IReadOnlyList<InstalledDesktopApplication> ReadInstalledDesktopApplications()
        {
            List<InstalledDesktopApplication> applications = new();
            int readableRootCount = 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.LocalMachine, RegistryView.Registry64) ? 1 : 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.LocalMachine, RegistryView.Registry32) ? 1 : 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.CurrentUser, RegistryView.Registry64) ? 1 : 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.CurrentUser, RegistryView.Registry32) ? 1 : 0;

            if (readableRootCount == 0)
            {
                throw new InvalidOperationException("Windows did not allow access to the installed-program registry.");
            }

            return applications;
        }

        private static InstalledDesktopApplication? FindInstalledApplication(
            IReadOnlyList<string> installedDisplayNamePrefixes) =>
            ReadInstalledDesktopApplications()
                .Where(application => installedDisplayNamePrefixes.Any(prefix =>
                    MatchesInstalledDisplayName(application.DisplayName, prefix)))
                .OrderByDescending(application => ParseLooseVersion(application.DisplayVersion))
                .FirstOrDefault();

        private static bool ReadUninstallRegistryView(
            ICollection<InstalledDesktopApplication> applications,
            RegistryHive hive,
            RegistryView view)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstallKey = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey == null)
                {
                    return false;
                }

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? applicationKey = uninstallKey.OpenSubKey(subKeyName);
                        string? displayName = applicationKey?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            continue;
                        }

                        applications.Add(new InstalledDesktopApplication(
                            displayName.Trim(),
                            (applicationKey?.GetValue("DisplayVersion") as string)?.Trim(),
                            (applicationKey?.GetValue("UninstallString") as string)?.Trim(),
                            (applicationKey?.GetValue("QuietUninstallString") as string)?.Trim()));
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
                    {
                        App.logger.Debug(exception, "[Installers] Unable to read uninstall entry {EntryName}.", subKeyName);
                    }
                }

                return true;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
            {
                App.logger.Debug(
                    exception,
                    "[Installers] Unable to read the {Hive}/{View} uninstall registry.",
                    hive,
                    view);
                return false;
            }
        }

        private static bool MatchesInstalledDisplayName(string displayName, string prefix) =>
            displayName.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith(prefix + " (", StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

        private static async Task<WingetInstallResult> RunRegisteredUninstallerAsync(
            string commandLine,
            CancellationToken cancellationToken)
        {
            if (!TryCreateUninstallStartInfo(commandLine, out ProcessStartInfo startInfo))
            {
                return new WingetInstallResult(false, -1, "Windows provided an invalid uninstall command.");
            }

            using Process process = new() { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                {
                    return new WingetInstallResult(false, -1, "Unable to start the app's uninstaller.");
                }

                using CancellationTokenSource timeoutSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(UninstallTimeout);
                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKillProcessTree(process);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    return new WingetInstallResult(
                        false,
                        -1,
                        $"Uninstallation timed out after {UninstallTimeout.TotalMinutes:0} minutes.");
                }

                bool succeeded = process.ExitCode is 0 or 1641 or 3010;
                return new WingetInstallResult(
                    succeeded,
                    process.ExitCode,
                    succeeded
                        ? "The app's registered uninstaller completed."
                        : $"The app's registered uninstaller exited with code {process.ExitCode}.");
            }
            catch (Win32Exception exception)
            {
                App.logger.Warn(exception, "[Installers] Unable to start a registered app uninstaller.");
                return new WingetInstallResult(false, exception.NativeErrorCode, exception.Message);
            }
        }

        private static bool TryCreateUninstallStartInfo(
            string commandLine,
            out ProcessStartInfo startInfo)
        {
            startInfo = null!;
            string expandedCommand = Environment.ExpandEnvironmentVariables(commandLine).Trim();
            if (expandedCommand.Length == 0)
            {
                return false;
            }

            string executable;
            string arguments;
            Match quotedExecutable = Regex.Match(
                expandedCommand,
                @"^\s*""(?<executable>[^""]+\.exe)""\s*(?<arguments>.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (quotedExecutable.Success)
            {
                executable = quotedExecutable.Groups["executable"].Value;
                arguments = quotedExecutable.Groups["arguments"].Value;
            }
            else
            {
                Match executableMatch = Regex.Match(
                    expandedCommand,
                    @"^\s*(?<executable>.+?\.exe)\s*(?<arguments>.*)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!executableMatch.Success)
                {
                    return false;
                }

                executable = executableMatch.Groups["executable"].Value.Trim().Trim('"');
                arguments = executableMatch.Groups["arguments"].Value.Trim();
            }

            if (Path.GetFileName(executable).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                arguments = Regex.Replace(
                    arguments,
                    @"(^|\s)/I(?=\s*\{)",
                    "$1/X",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            startInfo = new ProcessStartInfo(executable)
            {
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            return true;
        }

        private static Version ParseLooseVersion(string? value) =>
            TryParseLooseVersion(value, out Version version) ? version : new Version(0, 0);

        private static bool TryParseLooseVersion(string? value, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = Regex.Match(value, @"\d+(?:\.\d+)+", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            string normalizedVersion = string.Join('.', match.Value.Split('.').Take(4));
            return Version.TryParse(normalizedVersion, out version!);
        }

        private static string? GetDefaultSilentArguments(string installerType) =>
            installerType.ToLowerInvariant() switch
            {
                "nullsoft" => "/S",
                "inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                "burn" => "/quiet /norestart",
                _ => null
            };

        private static WingetInstallerEntry? SelectCompatibleInstaller(
            IReadOnlyList<WingetInstallerEntry>? installers)
        {
            if (installers == null || installers.Count == 0)
            {
                return null;
            }

            List<WingetInstallerEntry> supportedInstallers = installers
                .Where(installer =>
                {
                    if (!Uri.TryCreate(installer.InstallerUrl, UriKind.Absolute, out Uri? installerUri))
                    {
                        return false;
                    }

                    string extension = Path.GetExtension(installerUri.AbsolutePath);
                    return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (supportedInstallers.Count == 0)
            {
                return null;
            }

            string[] preferredArchitectures = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => ["arm64", "x64", "x86", "neutral"],
                Architecture.X86 => ["x86", "neutral"],
                _ => ["x64", "x86", "neutral"]
            };

            foreach (string architecture in preferredArchitectures)
            {
                WingetInstallerEntry? match = supportedInstallers
                    .Where(installer => installer.Architecture?.Equals(architecture, StringComparison.OrdinalIgnoreCase) == true)
                    .OrderBy(GetLocalePreference)
                    .FirstOrDefault();
                if (match != null)
                {
                    return match;
                }
            }

            return supportedInstallers.OrderBy(GetLocalePreference).FirstOrDefault();
        }

        private static int GetLocalePreference(WingetInstallerEntry installer)
        {
            if (installer.InstallerLocale?.Equals(CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase) == true)
            {
                return 0;
            }

            if (installer.InstallerLocale?.Equals("en-US", StringComparison.OrdinalIgnoreCase) == true)
            {
                return 1;
            }

            return string.IsNullOrWhiteSpace(installer.InstallerLocale) ? 2 : 3;
        }

        private static bool TryParseVersion(string value, out Version version) =>
            TryParseLooseVersion(value.TrimStart('v', 'V'), out version);

        private static Version ParseVersion(string value) =>
            TryParseVersion(value, out Version version) ? version : new Version(0, 0);

        private static HttpClient CreateManifestClient()
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.5");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
            return client;
        }

        private static HttpClient CreateManifestCatalogClient()
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.5");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            return client;
        }

        private static async Task<WingetInstallResult> RunWingetAsync(
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new("winget.exe")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Unable to start Windows Package Manager.");
            }

            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return new WingetInstallResult(false, -1, $"The package operation timed out after {timeout.TotalMinutes:0} minutes.");
            }

            string standardOutput = await standardOutputTask;
            string standardError = await standardErrorTask;
            string output = string.Join(
                Environment.NewLine,
                new[] { standardOutput.Trim(), standardError.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

            App.logger.Info(
                "[Installers] winget finished with exit code {ExitCode}.\n{Output}",
                process.ExitCode,
                output);

            return new WingetInstallResult(process.ExitCode == 0, process.ExitCode, output);
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Installers] Unable to stop the canceled installer process.");
            }
        }

        public sealed class WingetInstallerManifest
        {
            public string? InstallerType { get; set; }

            public WingetInstallerSwitches? InstallerSwitches { get; set; }

            public List<WingetInstallerEntry>? Installers { get; set; }
        }

        public sealed class WingetInstallerEntry
        {
            public string? Architecture { get; set; }

            public string? InstallerLocale { get; set; }

            public string? InstallerType { get; set; }

            public string? InstallerUrl { get; set; }

            public string? InstallerSha256 { get; set; }

            public WingetInstallerSwitches? InstallerSwitches { get; set; }
        }

        public sealed class WingetInstallerSwitches
        {
            public string? Silent { get; set; }

            public string? SilentWithProgress { get; set; }
        }

        private sealed record InstalledDesktopApplication(
            string DisplayName,
            string? DisplayVersion,
            string? UninstallString,
            string? QuietUninstallString);
    }
}
