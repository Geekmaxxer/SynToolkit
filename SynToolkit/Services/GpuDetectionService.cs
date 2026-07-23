#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace SynToolkit.Services
{
    public sealed record DetectedGpu(string Name, bool IsAmd, bool IsNvidia, bool IsIntel);

    /// <summary>
    /// Detects installed GPUs via WMI so features can be disabled when the relevant vendor's
    /// hardware isn't present (e.g. the Radeon slimmer on an NVIDIA-only system). Matches on
    /// the PCI vendor ID in PNPDeviceID first (VEN_1002 = AMD/ATI, VEN_10DE = NVIDIA,
    /// VEN_8086 = Intel), since that's stable across locales and driver naming, falling back to
    /// the adapter Name string if the vendor ID can't be parsed.
    /// </summary>
    public static class GpuDetectionService
    {
        private const string AmdVendorId = "VEN_1002";
        private const string NvidiaVendorId = "VEN_10DE";
        private const string IntelVendorId = "VEN_8086";

        public const string DefaultGpuIconPath = "ms-appx:///assets/Icons/Gpu.png";
        public const string AmdGpuIconPath = "ms-appx:///assets/Icons/Amd.png";
        public const string NvidiaGpuIconPath = "ms-appx:///assets/Icons/Nvidia.png";
        public const string IntelGpuIconPath = "ms-appx:///assets/Icons/Intel.png";

        public static IReadOnlyList<DetectedGpu> GetDetectedGpus()
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

                        bool isAmd = pnpDeviceId.Contains(AmdVendorId, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
                        bool isNvidia = pnpDeviceId.Contains(NvidiaVendorId, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase);
                        bool isIntel = pnpDeviceId.Contains(IntelVendorId, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Intel", StringComparison.OrdinalIgnoreCase);

                        gpus.Add(new DetectedGpu(name, isAmd, isNvidia, isIntel));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[GpuDetection] Unable to enumerate video controllers via WMI.");
            }

            return gpus;
        }

        public static bool HasAmdGpu(IReadOnlyList<DetectedGpu>? gpus = null) => (gpus ?? GetDetectedGpus()).Any(gpu => gpu.IsAmd);

        public static bool HasNvidiaGpu(IReadOnlyList<DetectedGpu>? gpus = null) => (gpus ?? GetDetectedGpus()).Any(gpu => gpu.IsNvidia);

        public static string GetIconPath(string name, string pnpDeviceId)
        {
            if (IsBasicDisplayAdapter(name))
            {
                return DefaultGpuIconPath;
            }

            if (IsNvidiaGpu(name, pnpDeviceId))
            {
                return NvidiaGpuIconPath;
            }

            if (IsAmdGpu(name, pnpDeviceId))
            {
                return AmdGpuIconPath;
            }

            if (IsIntelGpu(name, pnpDeviceId))
            {
                return IntelGpuIconPath;
            }

            return DefaultGpuIconPath;
        }

        public static string GetPrimaryIconPath(IReadOnlyList<Models.GpuSpec> gpus)
        {
            string[] priority =
            [
                NvidiaGpuIconPath,
                AmdGpuIconPath,
                IntelGpuIconPath,
                DefaultGpuIconPath,
            ];

            HashSet<string> detectedIcons = gpus
                .Where(gpu => !IsBasicDisplayAdapter(gpu.Name))
                .Select(gpu => gpu.IconPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string iconPath in priority)
            {
                if (detectedIcons.Contains(iconPath))
                {
                    return iconPath;
                }
            }

            return DefaultGpuIconPath;
        }

        private static bool IsBasicDisplayAdapter(string name) =>
            name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Microsoft Remote Display", StringComparison.OrdinalIgnoreCase);

        private static bool IsAmdGpu(string name, string pnpDeviceId) =>
            pnpDeviceId.Contains(AmdVendorId, StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);

        private static bool IsNvidiaGpu(string name, string pnpDeviceId) =>
            pnpDeviceId.Contains(NvidiaVendorId, StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase);

        private static bool IsIntelGpu(string name, string pnpDeviceId) =>
            pnpDeviceId.Contains(IntelVendorId, StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Intel", StringComparison.OrdinalIgnoreCase);
    }
}
