#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace SynToolkit.Services
{
    public sealed record DetectedGpu(string Name, GpuVendor Vendor)
    {
        public bool IsAmd => Vendor == GpuVendor.Amd;

        public bool IsNvidia => Vendor == GpuVendor.Nvidia;

        public bool IsIntel => Vendor == GpuVendor.Intel;
    }

    /// <summary>
    /// Detects installed GPUs via WMI so features can be disabled when the relevant vendor's
    /// hardware isn't present (e.g. the Radeon slimmer on an NVIDIA-only system). Vendor
    /// classification is delegated to <see cref="GpuVendorClassification"/> so Specs, the GPU
    /// tab, and the sidebar icon all share one set of PCI-ID/name rules. The WMI enumeration is
    /// cached for the process lifetime — GPU hardware does not change during a running session.
    /// </summary>
    public static class GpuDetectionService
    {
        public const string DefaultGpuIconPath = GpuVendorClassification.DefaultGpuIconPath;
        public const string AmdGpuIconPath = GpuVendorClassification.AmdGpuIconPath;
        public const string NvidiaGpuIconPath = GpuVendorClassification.NvidiaGpuIconPath;
        public const string IntelGpuIconPath = GpuVendorClassification.IntelGpuIconPath;

        private static readonly object DetectedGpusLock = new();
        private static IReadOnlyList<DetectedGpu>? CachedDetectedGpus;

        public static IReadOnlyList<DetectedGpu> GetDetectedGpus()
        {
            IReadOnlyList<DetectedGpu>? cached = CachedDetectedGpus;
            if (cached is not null)
            {
                return cached;
            }

            lock (DetectedGpusLock)
            {
                if (CachedDetectedGpus is not null)
                {
                    return CachedDetectedGpus;
                }

                CachedDetectedGpus = EnumerateDetectedGpus();
                return CachedDetectedGpus;
            }
        }

        public static bool HasAmdGpu(IReadOnlyList<DetectedGpu>? gpus = null) => (gpus ?? GetDetectedGpus()).Any(gpu => gpu.IsAmd);

        public static bool HasNvidiaGpu(IReadOnlyList<DetectedGpu>? gpus = null) => (gpus ?? GetDetectedGpus()).Any(gpu => gpu.IsNvidia);

        public static GpuVendor GetVendor(string name, string pnpDeviceId) =>
            GpuVendorClassification.GetVendor(name, pnpDeviceId);

        public static string GetIconPath(string name, string pnpDeviceId) =>
            GpuVendorClassification.GetIconPath(GetVendor(name, pnpDeviceId));

        public static string GetIconPath(GpuVendor vendor) => GpuVendorClassification.GetIconPath(vendor);

        public static string GetGpuTabIconPath(GpuVendor vendor) =>
            GpuVendorClassification.GetGpuTabIconPath(vendor);

        public static string GetGpuTabIconPath(IReadOnlyList<DetectedGpu>? gpus = null) =>
            GetGpuTabIconPath(GetPrimaryGpuVendor(gpus));

        public static GpuVendor GetPrimaryGpuVendor(IReadOnlyList<DetectedGpu>? gpus = null) =>
            GpuVendorClassification.GetPrimaryGpuVendor((gpus ?? GetDetectedGpus()).Select(gpu => (gpu.Name, gpu.Vendor)));

        public static GpuVendor GetPrimaryGpuVendor(IReadOnlyList<Models.GpuSpec> gpus) =>
            GpuVendorClassification.GetPrimaryGpuVendor(gpus.Select(gpu => (gpu.Name, gpu.Vendor)));

        public static string GetPrimaryIconPath(IReadOnlyList<Models.GpuSpec> gpus) =>
            GetIconPath(GetPrimaryGpuVendor(gpus));

        private static IReadOnlyList<DetectedGpu> EnumerateDetectedGpus()
        {
            List<DetectedGpu> gpus = new();

            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Name, PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string name = item["Name"] as string ?? "Unknown GPU";
                        string pnpDeviceId = item["PNPDeviceID"] as string ?? string.Empty;
                        gpus.Add(new DetectedGpu(name, GetVendor(name, pnpDeviceId)));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[GpuDetection] Unable to enumerate video controllers via WMI.");
            }

            return gpus;
        }
    }
}
