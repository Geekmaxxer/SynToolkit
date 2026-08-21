#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SynToolkit.Services
{
    /// <summary>
    /// Reads one hidden GPU-Z XML dump on demand. Its result is shared by all GPU expanders, so
    /// Specs does not launch GPU-Z until a user explicitly opens a card's details.
    /// </summary>
    internal static class GpuZReportService
    {
        private const int ReportTimeoutMilliseconds = 45_000;
        private static readonly Lazy<IReadOnlyList<GpuZCardDetails>> CurrentReport = new(ReadCurrentReport);

        internal static GpuZCardDetails? GetDetailsFor(string gpuName)
        {
            IReadOnlyList<GpuZCardDetails> cards = CurrentReport.Value;
            return cards.FirstOrDefault(card => card.CardName.Equals(gpuName, StringComparison.OrdinalIgnoreCase))
                ?? cards.FirstOrDefault(card => card.CardName.Contains(gpuName, StringComparison.OrdinalIgnoreCase)
                    || gpuName.Contains(card.CardName, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<GpuZCardDetails> ReadCurrentReport()
        {
            string executablePath = Path.Combine(AppContext.BaseDirectory, "assets", "Tools", "GPU-Z.2.70.0.exe");
            if (!File.Exists(executablePath))
            {
                return [];
            }

            string reportPath = Path.Combine(
                Path.GetTempPath(),
                "SynToolkit-GpuZ-" + Guid.NewGuid().ToString("N") + ".xml");
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
                process.StartInfo.ArgumentList.Add("-dump");
                process.StartInfo.ArgumentList.Add(reportPath);

                if (!process.Start() || !process.WaitForExit(ReportTimeoutMilliseconds) || !File.Exists(reportPath))
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    return [];
                }

                using FileStream reportStream = File.OpenRead(reportPath);
                return GpuZReportParser.Parse(reportStream);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] GPU-Z report was unavailable.");
                return [];
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
                    // The temporary report is harmless if a third-party scanner still holds it.
                }
            }
        }
    }
}