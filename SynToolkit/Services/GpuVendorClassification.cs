#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Services
{
    public enum GpuVendor
    {
        Unknown,
        Nvidia,
        Amd,
        Intel,
    }

    /// <summary>
    /// Shared GPU vendor classification used by Specs, the GPU tab, and the sidebar icon.
    /// Matches on the PCI vendor ID in PNPDeviceID first (VEN_1002 = AMD/ATI, VEN_10DE = NVIDIA,
    /// VEN_8086 = Intel), falling back to the adapter Name string if the vendor ID can't be parsed.
    /// Multi-GPU systems use a single priority: NVIDIA, then AMD, then Intel, then unknown —
    /// the same order the Specs graphics header already used.
    /// </summary>
    public static class GpuVendorClassification
    {
        private const string AmdVendorId = "VEN_1002";
        private const string NvidiaVendorId = "VEN_10DE";
        private const string IntelVendorId = "VEN_8086";

        public const string DefaultGpuIconPath = "ms-appx:///assets/Icons/Gpu.png";
        public const string AmdGpuIconPath = "ms-appx:///assets/Icons/Amd.png";
        public const string NvidiaGpuIconPath = "ms-appx:///assets/Icons/Nvidia.png";
        public const string IntelGpuIconPath = "ms-appx:///assets/Icons/Intel.png";

        public static GpuVendor GetVendor(string name, string pnpDeviceId)
        {
            if (IsNvidiaGpu(name, pnpDeviceId))
            {
                return GpuVendor.Nvidia;
            }

            if (IsAmdGpu(name, pnpDeviceId))
            {
                return GpuVendor.Amd;
            }

            if (IsIntelGpu(name, pnpDeviceId))
            {
                return GpuVendor.Intel;
            }

            if (IsBasicDisplayAdapter(name))
            {
                return GpuVendor.Unknown;
            }

            return GpuVendor.Unknown;
        }

        public static string GetIconPath(GpuVendor vendor) => vendor switch
        {
            GpuVendor.Nvidia => NvidiaGpuIconPath,
            GpuVendor.Amd => AmdGpuIconPath,
            GpuVendor.Intel => IntelGpuIconPath,
            _ => DefaultGpuIconPath,
        };

        public static string GetGpuTabIconPath(GpuVendor vendor) => vendor switch
        {
            GpuVendor.Nvidia => NvidiaGpuIconPath,
            GpuVendor.Amd => AmdGpuIconPath,
            _ => DefaultGpuIconPath,
        };

        public static GpuVendor GetPrimaryGpuVendor(IEnumerable<(string Name, GpuVendor Vendor)> gpus)
        {
            GpuVendor[] priority =
            [
                GpuVendor.Nvidia,
                GpuVendor.Amd,
                GpuVendor.Intel,
            ];

            HashSet<GpuVendor> detectedVendors = gpus
                .Where(gpu => !IsBasicDisplayAdapter(gpu.Name))
                .Select(gpu => gpu.Vendor)
                .Where(vendor => vendor != GpuVendor.Unknown)
                .ToHashSet();

            foreach (GpuVendor vendor in priority)
            {
                if (detectedVendors.Contains(vendor))
                {
                    return vendor;
                }
            }

            return GpuVendor.Unknown;
        }

        public static bool IsBasicDisplayAdapter(string name) =>
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
