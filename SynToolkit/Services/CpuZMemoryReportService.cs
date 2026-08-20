#nullable enable

using System;
using System.Diagnostics;
using System.IO;

namespace SynToolkit.Services
{
    /// <summary>
    /// Reads current memory timings from the bundled CPU-Z report once per app session. CPU-Z's
    /// documented -txt argument runs in ghost mode, so Specs collection does not open its UI.
    /// </summary>
    internal static class CpuZMemoryReportService
    {
        private const int ReportTimeoutMilliseconds = 25_000;
        private static readonly Lazy<CpuZMemoryTimings?> CurrentTimings = new(ReadCurrentTimings);

        internal static CpuZMemoryTimings? GetCurrentTimings() => CurrentTimings.Value;

        private static CpuZMemoryTimings? ReadCurrentTimings()
        {
            string executablePath = Path.Combine(AppContext.BaseDirectory, "assets", "Tools", "cpuz_x64.exe");
            if (!File.Exists(executablePath))
            {
                return null;
            }

            string reportBasePath = Path.Combine(
                Path.GetTempPath(),
                "SynToolkit-CpuZ-" + Guid.NewGuid().ToString("N"));
            string reportPath = reportBasePath + ".txt";
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.StartInfo.ArgumentList.Add("-txt=" + reportBasePath);

                if (!process.Start() || !process.WaitForExit(ReportTimeoutMilliseconds) || !File.Exists(reportPath))
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    return null;
                }

                return CpuZMemoryTimingParser.TryParse(File.ReadAllText(reportPath));
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] CPU-Z memory timing report was unavailable.");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(reportPath))
                    {
                        File.Delete(reportPath);
                    }
                }
                catch (IOException)
                {
                    // CPU-Z's report is temporary and harmless if a third-party scanner still holds it.
                }
            }
        }
    }
}