#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Models;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public sealed record GpuSpecDisplay(string Name, string VramText, string DriverVersionText, string IconPath);

    public sealed record MemoryModuleDisplay(string ManufacturerText, string CapacityText, string SpeedText);

    public sealed record StorageDriveDisplay(string Model, string SizeText, string TypeText);

    /// <summary>
    /// Drives the Specs tab: a read-only snapshot of CPU, GPU, memory, storage, motherboard,
    /// and Windows identity via SystemSpecsService. Purely informational — makes no changes.
    /// </summary>
    public partial class SpecsPageViewModel : ObservableObject
    {
        private readonly ISystemInformationService _systemInformationService;

        [ObservableProperty]
        private bool _isLoading = true;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private string _cpuName = string.Empty;

        [ObservableProperty]
        private string _cpuDetailsText = string.Empty;

        [ObservableProperty]
        private string _motherboardText = string.Empty;

        [ObservableProperty]
        private string _totalMemoryText = string.Empty;

        [ObservableProperty]
        private string _windowsText = string.Empty;

        [ObservableProperty]
        private string _graphicsHeaderIcon = GpuDetectionService.DefaultGpuIconPath;

        public ObservableCollection<GpuSpecDisplay> Gpus { get; } = new();
        public ObservableCollection<MemoryModuleDisplay> MemoryModules { get; } = new();
        public ObservableCollection<StorageDriveDisplay> StorageDrives { get; } = new();

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
                        FormatBytes(module.CapacityBytes),
                        module.SpeedMHz.HasValue ? $"{module.SpeedMHz} MHz" : "Unknown speed"));
                }

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
