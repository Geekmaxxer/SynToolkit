#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using SynToolkit.Models;

namespace SynToolkit.Services
{
    /// <summary>
    /// Reads read-only hardware/OS identity via WMI for the Specs tab. Never modifies the
    /// system. GPU VRAM prefers the registry's HardwareInformation.qwMemorySize (a 64-bit
    /// value) over WMI's Win32_VideoController.AdapterRAM, which is a 32-bit field that wraps
    /// around for adapters with more than ~4 GB of VRAM — a well-documented WMI limitation, not
    /// a hypothetical edge case.
    /// </summary>
    public static class SystemSpecsService
    {
        private const string VideoClassKeyPath =
            @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        public static SystemSpecsSnapshot GetSnapshot(ISystemInformationService systemInformationService)
        {
            SystemInformationSnapshot windowsInfo = systemInformationService.Detect();

            return new SystemSpecsSnapshot(
                GetCpu(),
                GetGpus(),
                GetTotalMemoryBytes(),
                GetMemoryModules(),
                GetStorageDrives(),
                GetNetworkAdapters(),
                GetMotherboard(),
                windowsInfo.WindowsProductName,
                windowsInfo.WindowsDisplayVersion,
                windowsInfo.WindowsBuild,
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        }

        private static CpuSpec? GetCpu()
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string name = (item["Name"] as string ?? "Unknown CPU").Trim();
                        int cores = Convert.ToInt32(item["NumberOfCores"] ?? 0);
                        int logical = Convert.ToInt32(item["NumberOfLogicalProcessors"] ?? 0);
                        uint clock = Convert.ToUInt32(item["MaxClockSpeed"] ?? 0u);
                        return new CpuSpec(name, cores, logical, clock);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read CPU information via WMI.");
            }

            return null;
        }

        private static IReadOnlyList<GpuSpec> GetGpus()
        {
            List<GpuSpec> gpus = new();
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string name = item["Name"] as string ?? "Unknown GPU";
                        string pnpDeviceId = item["PNPDeviceID"] as string ?? string.Empty;
                        string? driverVersion = item["DriverVersion"] as string;

                        ulong? adapterRam = item["AdapterRAM"] is object rawAdapterRam
                            ? Convert.ToUInt64(rawAdapterRam)
                            : null;

                        ulong? accurateVram = TryGetAccurateVramBytes(pnpDeviceId);
                        GpuVendor vendor = GpuDetectionService.GetVendor(name, pnpDeviceId);
                        string iconPath = GpuDetectionService.GetIconPath(vendor);
                        gpus.Add(new GpuSpec(name, accurateVram ?? adapterRam, driverVersion, iconPath, vendor));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read GPU information via WMI.");
            }

            return gpus;
        }

        private static ulong? TryGetAccurateVramBytes(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId))
            {
                return null;
            }

            RegistryKey? classKey;
            try
            {
                classKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Specs] Unable to open the video adapter class registry key.");
                return null;
            }

            if (classKey is null)
            {
                return null;
            }

            using (classKey)
            {
                string[] subKeyNames;
                try
                {
                    subKeyNames = classKey.GetSubKeyNames();
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[Specs] Unable to enumerate video adapter registry subkeys.");
                    return null;
                }

                foreach (string subKeyName in subKeyNames)
                {
                    // Some subkeys under this class GUID (e.g. a reserved "Properties" key,
                    // confirmed present on real hardware, not hypothetical) can throw a
                    // permission exception on OpenSubKey. That must not abort the whole scan —
                    // otherwise a permission failure on one subkey silently discards an
                    // already-found correct match on another, and every GPU falls back to
                    // WMI's AdapterRAM, which is known to be wrong for cards with more than ~4 GB.
                    try
                    {
                        using RegistryKey? subKey = classKey.OpenSubKey(subKeyName);
                        object? matchingDeviceId = subKey?.GetValue("MatchingDeviceId");
                        if (matchingDeviceId is not string matchText || matchText.Length == 0)
                        {
                            continue;
                        }

                        if (!pnpDeviceId.Contains(matchText, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        object? memorySize = subKey?.GetValue("HardwareInformation.qwMemorySize");
                        if (memorySize is not null)
                        {
                            return Convert.ToUInt64(memorySize);
                        }
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[Specs] Unable to read video adapter registry subkey '{0}'.", subKeyName);
                    }
                }
            }

            return null;
        }

        private static ulong GetTotalMemoryBytes()
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        return Convert.ToUInt64(item["TotalPhysicalMemory"] ?? 0UL);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read total memory via WMI.");
            }

            return 0;
        }

        private static IReadOnlyList<MemoryModuleSpec> GetMemoryModules()
        {
            List<MemoryModuleSpec> modules = new();
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Manufacturer, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string? manufacturer = (item["Manufacturer"] as string)?.Trim();
                        ulong capacity = Convert.ToUInt64(item["Capacity"] ?? 0UL);
                        uint? configuredClockSpeed = ReadPositiveUInt32(item["ConfiguredClockSpeed"]);
                        uint? reportedSpeed = ReadPositiveUInt32(item["Speed"]);
                        modules.Add(new MemoryModuleSpec(manufacturer, capacity, configuredClockSpeed ?? reportedSpeed));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read memory module information via WMI.");
            }

            return modules;
        }

        private static IReadOnlyList<StorageDriveSpec> GetStorageDrives()
        {
            List<StorageDriveSpec> drives = new();
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string model = (item["Model"] as string ?? "Unknown drive").Trim();
                        ulong size = item["Size"] is object rawSize ? Convert.ToUInt64(rawSize) : 0UL;
                        string? mediaType = item["MediaType"] as string;
                        string? interfaceType = item["InterfaceType"] as string;
                        drives.Add(new StorageDriveSpec(model, size, mediaType, interfaceType));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read storage drive information via WMI.");
            }

            return drives.OrderByDescending(drive => drive.SizeBytes).ToList();
        }

        private static IReadOnlyList<NetworkAdapterSpec> GetNetworkAdapters()
        {
            List<NetworkAdapterSpec> adapters = new();
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT Name, Manufacturer, MACAddress, NetConnectionID, NetConnectionStatus, PhysicalAdapter, PNPDeviceID, ConfigManagerErrorCode, Speed FROM Win32_NetworkAdapter");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        if (!IsUserFacingPhysicalNetworkAdapter(item))
                        {
                            continue;
                        }

                        ushort? connectionStatus = ReadUInt16(item["NetConnectionStatus"]);
                        adapters.Add(new NetworkAdapterSpec(
                            (item["Name"] as string ?? "Unknown adapter").Trim(),
                            (item["Manufacturer"] as string)?.Trim(),
                            (item["NetConnectionID"] as string)?.Trim(),
                            (item["MACAddress"] as string)?.Trim(),
                            ReadNetworkAdapterSpeed(item["Speed"]),
                            GetNetworkConnectionStatus(connectionStatus),
                            connectionStatus == 2));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read network adapter information via WMI.");
            }

            return adapters
                .OrderByDescending(adapter => adapter.IsConnected)
                .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsUserFacingPhysicalNetworkAdapter(ManagementBaseObject adapter)
        {
            if (adapter["PhysicalAdapter"] is not bool isPhysical || !isPhysical ||
                ReadUInt32(adapter["ConfigManagerErrorCode"]) == 22)
            {
                return false;
            }

            string name = (adapter["Name"] as string ?? string.Empty).Trim();
            string manufacturer = (adapter["Manufacturer"] as string ?? string.Empty).Trim();
            string pnpDeviceId = (adapter["PNPDeviceID"] as string ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string identifyingText = string.Join(" ", name, manufacturer, pnpDeviceId);
            string[] excludedTerms =
            {
                "Bluetooth",
                "Microsoft",
                "WAN Miniport",
                "Kernel Debug",
                "Virtual",
                "Hyper-V",
                "Teredo",
                "6to4",
                "ISATAP",
                "Loopback",
                "NdisWan",
                "TAP-Windows",
                "WireGuard",
                "Wintun"
            };

            return !excludedTerms.Any(term =>
                identifyingText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetNetworkConnectionStatus(ushort? status) => status switch
        {
            0 => "Disconnected",
            1 => "Connecting",
            2 => "Connected",
            3 => "Disconnecting",
            4 => "Hardware not present",
            5 => "Hardware disabled",
            6 => "Hardware malfunction",
            7 => "Media disconnected",
            8 => "Authenticating",
            9 => "Authentication succeeded",
            10 => "Authentication failed",
            11 => "Invalid address",
            12 => "Credentials required",
            _ => "Inactive"
        };

        private static uint? ReadPositiveUInt32(object? value)
        {
            if (value is null)
            {
                return null;
            }

            uint parsed = Convert.ToUInt32(value);
            return parsed == 0 ? null : parsed;
        }

        private static ushort? ReadUInt16(object? value) =>
            value is null ? null : Convert.ToUInt16(value);

        private static uint? ReadUInt32(object? value) =>
            value is null ? null : Convert.ToUInt32(value);

        private static ulong? ReadPositiveUInt64(object? value)
        {
            if (value is null)
            {
                return null;
            }

            ulong parsed = Convert.ToUInt64(value);
            return parsed == 0 ? null : parsed;
        }
        private static ulong? ReadNetworkAdapterSpeed(object? value)
        {
            ulong? speed = ReadPositiveUInt64(value);
            return speed is null or >= (ulong)long.MaxValue ? null : speed;
        }
        private static MotherboardSpec? GetMotherboard()
        {
            try
            {
                using ManagementObjectSearcher searcher = new("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string? manufacturer = (item["Manufacturer"] as string)?.Trim();
                        string? product = (item["Product"] as string)?.Trim();
                        return new MotherboardSpec(manufacturer, product);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Specs] Unable to read motherboard information via WMI.");
            }

            return null;
        }
    }
}
