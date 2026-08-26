#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Utils
{
    public sealed record SynToolkitUpdateStatus(
        Version CurrentVersion,
        Version? AvailableVersion,
        string? DownloadUrl)
    {
        public bool IsUpdateAvailable => AvailableVersion is not null &&
            AvailableVersion > CurrentVersion &&
            !string.IsNullOrWhiteSpace(DownloadUrl);
    }

    public static class SynToolkitUpdateHelper
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/Synergy-Tweaks/SynToolkit/releases/latest";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
        private static readonly HttpClient Client = CreateHttpClient();
        private static readonly SemaphoreSlim UpdateCheckGate = new(1, 1);
        private static SynToolkitUpdateStatus? _cachedStatus;
        private static DateTimeOffset _cacheExpiresUtc;

        public static async Task<SynToolkitUpdateStatus> CheckUpdatesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (!forceRefresh && _cachedStatus is not null && DateTimeOffset.UtcNow < _cacheExpiresUtc)
            {
                return _cachedStatus;
            }

            await UpdateCheckGate.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && _cachedStatus is not null && DateTimeOffset.UtcNow < _cacheExpiresUtc)
                {
                    return _cachedStatus;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                string json = await Client.GetStringAsync(LatestReleaseUrl, cancellationToken);
                using JsonDocument release = JsonDocument.Parse(json);

                (string? assetName, string? downloadUrl) = release.RootElement
                    .GetProperty("assets")
                    .EnumerateArray()
                    .Select(asset =>
                    {
                        string? name = asset.TryGetProperty("name", out JsonElement nameProperty)
                            ? nameProperty.GetString()
                            : null;
                        string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlProperty)
                            ? urlProperty.GetString()
                            : null;
                        return (name, url);
                    })
                    .Where(asset => IsSetupExecutable(asset.Item1) && !string.IsNullOrWhiteSpace(asset.Item2))
                    .FirstOrDefault();

                Version? version = TryGetSetupVersion(assetName, out Version? assetVersion)
                    ? assetVersion
                    : TryGetReleaseTagVersion(release.RootElement, out Version? tagVersion)
                        ? tagVersion
                        : null;

                _cachedStatus = new SynToolkitUpdateStatus(currentVersion, version, downloadUrl);
                _cacheExpiresUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
                return _cachedStatus;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Failed to check for SynToolkit updates.");
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                _cachedStatus = new SynToolkitUpdateStatus(currentVersion, null, null);
                _cacheExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(2);
                return _cachedStatus;
            }
            finally
            {
                UpdateCheckGate.Release();
            }
        }

        public static bool CheckUpdates() => CheckUpdatesAsync().GetAwaiter().GetResult().IsUpdateAvailable;

        public static void InstallUpdate()
        {
            SynToolkitUpdateStatus status = CheckUpdatesAsync().GetAwaiter().GetResult();
            if (!status.IsUpdateAvailable || string.IsNullOrWhiteSpace(status.DownloadUrl))
            {
                return;
            }

            try
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "SynToolkit", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);
                string installerPath = Path.Combine(tempDirectory, "SynToolkit-Setup.exe");

                using HttpResponseMessage response = Client.GetAsync(
                    status.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using Stream source = response.Content.ReadAsStream();
                using FileStream destination = File.Create(installerPath);
                source.CopyTo(destination);

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/SILENT /NORESTART",
                    UseShellExecute = true
                });

                App.ShutdownApplication();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Failed to install the SynToolkit update.");
            }
        }

        private static bool IsSetupExecutable(string? assetName) =>
            !string.IsNullOrWhiteSpace(assetName) &&
            assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            assetName.Contains("SynToolkit", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetSetupVersion(string? assetName, out Version? version)
        {
            version = null;
            if (!IsSetupExecutable(assetName) || assetName is null)
            {
                return false;
            }

            Match versionMatch = Regex.Match(
                assetName,
                @"(?<!\d)(?<version>\d+\.\d+(?:\.\d+){0,2})(?!\d)",
                RegexOptions.CultureInvariant);
            return versionMatch.Success && Version.TryParse(versionMatch.Groups["version"].Value, out version);
        }

        private static bool TryGetReleaseTagVersion(JsonElement release, out Version? version)
        {
            version = null;
            string? tag = release.TryGetProperty("tag_name", out JsonElement tagProperty)
                ? tagProperty.GetString()
                : null;
            return Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out version);
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.6");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}
