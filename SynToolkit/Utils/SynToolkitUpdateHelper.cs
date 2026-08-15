using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SynToolkit.Utils
{
    public static class SynToolkitUpdateHelper
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/synergy-tweaks/synergyos/releases/latest";
        private static string _downloadUrl;

        public static bool CheckUpdates()
        {
            try
            {
                using HttpClient client = CreateHttpClient();
                string json = client.GetStringAsync(LatestReleaseUrl).GetAwaiter().GetResult();
                using JsonDocument release = JsonDocument.Parse(json);

                string tag = release.RootElement.GetProperty("tag_name").GetString();
                if (!Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out Version availableVersion))
                {
                    return false;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                if (availableVersion <= currentVersion)
                {
                    return false;
                }

                JsonElement installerAsset = release.RootElement
                    .GetProperty("assets")
                    .EnumerateArray()
                    .FirstOrDefault(asset =>
                    {
                        string name = asset.GetProperty("name").GetString();
                        return name != null &&
                               name.StartsWith("SynToolkit-Setup-", StringComparison.OrdinalIgnoreCase) &&
                               name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                    });

                if (installerAsset.ValueKind == JsonValueKind.Undefined)
                {
                    return false;
                }

                _downloadUrl = installerAsset.GetProperty("browser_download_url").GetString();
                return !string.IsNullOrWhiteSpace(_downloadUrl);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Failed to check for SynToolkit updates.");
                _downloadUrl = null;
                return false;
            }
        }

        public static void InstallUpdate()
        {
            if (string.IsNullOrWhiteSpace(_downloadUrl) && !CheckUpdates())
            {
                return;
            }

            try
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "SynToolkit", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);
                string installerPath = Path.Combine(tempDirectory, "SynToolkit-Setup.exe");

                using (HttpClient client = CreateHttpClient())
                using (Stream source = client.GetStreamAsync(_downloadUrl).GetAwaiter().GetResult())
                using (FileStream destination = File.Create(installerPath))
                {
                    source.CopyTo(destination);
                }

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

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.6.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}
