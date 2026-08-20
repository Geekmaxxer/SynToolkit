#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Models;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public sealed record GpuSpecDisplay(string Name, string VramText, string DriverVersionText, string IconPath);

    public sealed record MemoryModuleDisplay(string ManufacturerText, string CapacityText);

    public sealed record StorageDriveDisplay(string Model, string SizeText, string TypeText);

    public sealed record NetworkAdapterDisplay(string Name, string ManufacturerText, string StatusText, string DetailsText);

    /// <summary>
    /// Drives the Specs tab: a read-only snapshot of CPU, GPU, memory, storage, motherboard,
    /// and Windows identity via SystemSpecsService. Purely informational — makes no changes.
    /// </summary>
    public partial class SpecsPageViewModel : ObservableObject
    {
        private readonly ISystemInformationService _systemInformationService;

        [ObservableProperty]
        public partial bool IsLoading { get; set; } = true;

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CpuName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CpuDetailsText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string MotherboardText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string TotalMemoryText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string WindowsText { get; set; } = string.Empty;

        private string _networkSummaryText = string.Empty;

        public string NetworkSummaryText
        {
            get => _networkSummaryText;
            private set => SetProperty(ref _networkSummaryText, value);
        }

        [ObservableProperty]
        public partial string GraphicsHeaderIcon { get; set; } = GpuDetectionService.DefaultGpuIconPath;

        public ObservableCollection<GpuSpecDisplay> Gpus { get; } = new();
        public ObservableCollection<MemoryModuleDisplay> MemoryModules { get; } = new();
        public ObservableCollection<StorageDriveDisplay> StorageDrives { get; } = new();
        public ObservableCollection<NetworkAdapterDisplay> NetworkAdapters { get; } = new();

        public SpecsPageViewModel(ISystemInformationService systemInformationService)
        {
            _systemInformationService = systemInformationService;
        }

        public async Task LoadAsync()
        {
            IsLoading = true;
            HasError = false;
            try
            {
                SystemSpecsSnapshot snapshot = await Task.Run(() => SystemSpecsService.GetSnapshot(_systemInformationService));

                CpuName = snapshot.Cpu?.Name ?? "Unknown CPU";
                CpuDetailsText = snapshot.Cpu is null
                    ? string.Empty
                    : $"{snapshot.Cpu.Cores} cores, {snapshot.Cpu.LogicalProcessors} logical processors, {snapshot.Cpu.MaxClockSpeedMHz / 1000.0:0.00} GHz";

                MotherboardText = snapshot.Motherboard is null
                    ? "Unknown"
                    : string.Join(" ", new[] { snapshot.Motherboard.Manufacturer, snapshot.Motherboard.Product }.Where(part => !string.IsNullOrWhiteSpace(part)));

                TotalMemoryText = FormatBytes(snapshot.TotalMemoryBytes);
                WindowsText = $"{snapshot.WindowsProductName} ({snapshot.WindowsDisplayVersion}, Build {snapshot.WindowsBuild}, {snapshot.Architecture})";

                Gpus.Clear();
                foreach (GpuSpec gpu in snapshot.Gpus)
                {
                    Gpus.Add(new GpuSpecDisplay(
                        gpu.Name,
                        gpu.AdapterRamBytes.HasValue ? FormatBytes(gpu.AdapterRamBytes.Value) : "Unknown",
                        string.IsNullOrWhiteSpace(gpu.DriverVersion) ? "Unknown driver version" : $"Driver {gpu.DriverVersion}",
                        gpu.IconPath));
                }

                GraphicsHeaderIcon = GpuDetectionService.GetPrimaryIconPath(snapshot.Gpus);

                MemoryModules.Clear();
                foreach (MemoryModuleSpec module in snapshot.MemoryModules)
                {
                    MemoryModules.Add(new MemoryModuleDisplay(
                        string.IsNullOrWhiteSpace(module.Manufacturer) ? "Unknown manufacturer" : module.Manufacturer!,
                        module.SpeedMHz.HasValue
                            ? $"{FormatBytes(module.CapacityBytes)} · {module.SpeedMHz.Value:N0} MHz"
                            : FormatBytes(module.CapacityBytes)));
                }


                NetworkAdapters.Clear();
                foreach (NetworkAdapterSpec adapter in snapshot.NetworkAdapters)
                {
                    List<string> details = new();
                    if (!string.IsNullOrWhiteSpace(adapter.ConnectionName))
                    {
                        details.Add(adapter.ConnectionName!);
                    }

                    if (adapter.SpeedBitsPerSecond.HasValue)
                    {
                        details.Add(FormatNetworkSpeed(adapter.SpeedBitsPerSecond.Value));
                    }

                    if (!string.IsNullOrWhiteSpace(adapter.MacAddress))
                    {
                        details.Add($"MAC {adapter.MacAddress}");
                    }

                    NetworkAdapters.Add(new NetworkAdapterDisplay(
                        adapter.Name,
                        string.IsNullOrWhiteSpace(adapter.Manufacturer) ? "Unknown manufacturer" : adapter.Manufacturer!,
                        adapter.ConnectionStatus,
                        details.Count == 0 ? "Physical network adapter" : string.Join(" · ", details)));
                }

                int activeAdapterCount = snapshot.NetworkAdapters.Count(adapter => adapter.IsConnected);
                NetworkSummaryText = snapshot.NetworkAdapters.Count == 0
                    ? "No physical network adapters detected"
                    : $"{snapshot.NetworkAdapters.Count} adapter(s), {activeAdapterCount} active";
                StorageDrives.Clear();
                foreach (StorageDriveSpec drive in snapshot.StorageDrives)
                {
                    string typeText = string.Join(" / ", new[] { drive.MediaType, drive.InterfaceType }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    StorageDrives.Add(new StorageDriveDisplay(drive.Model, FormatBytes(drive.SizeBytes), typeText));
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Specs] Unable to load system specs.");
                ErrorMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }


        private static string FormatNetworkSpeed(ulong bitsPerSecond)
        {
            const double gigabit = 1_000_000_000d;
            const double megabit = 1_000_000d;
            return bitsPerSecond >= (ulong)gigabit
                ? (bitsPerSecond / gigabit).ToString("0.##", CultureInfo.InvariantCulture) + " Gbps"
                : (bitsPerSecond / megabit).ToString("0.##", CultureInfo.InvariantCulture) + " Mbps";
        }
        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0)
            {
                return "Unknown";
            }

            const double gigabyte = 1024d * 1024 * 1024;
            return (bytes / gigabyte).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
