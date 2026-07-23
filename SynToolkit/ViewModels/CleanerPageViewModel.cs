#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Models;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    /// <summary>
    /// Drives the Cleaner tab: scans well-known junk locations (temp files, browser caches,
    /// Windows Update cache, Recycle Bin, old logs, thumbnail cache) via JunkFileCleanerService,
    /// shows how much space each would free, and deletes only the categories the user checks.
    /// </summary>
    public partial class CleanerPageViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private bool _isCleaning;

        [ObservableProperty]
        private bool _hasScanned;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _totalFoundText = string.Empty;

        [ObservableProperty]
        private string _lastCleanedText = string.Empty;

        public ObservableCollection<JunkCategoryItemViewModel> Categories { get; } = new();

        public async Task ScanAsync()
        {
            IsScanning = true;
            HasError = false;
            LastCleanedText = string.Empty;
            try
            {
                List<JunkCategoryScanResult> results = await Task.Run(JunkFileCleanerService.Scan);

                Categories.Clear();
                foreach (JunkCategoryScanResult result in results)
                {
                    Categories.Add(new JunkCategoryItemViewModel(result));
                }

                TotalFoundText = FormatBytes(results.Aggregate(0UL, (total, result) => total + result.SizeBytes));
                HasScanned = true;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Cleaner] Scanning for junk files failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsScanning = false;
            }
        }

        public async Task CleanSelectedAsync()
        {
            IsCleaning = true;
            HasError = false;
            try
            {
                List<JunkCategory> selected = Categories.Where(item => item.IsSelected).Select(item => item.Category).ToList();
                ulong freedBytes = await Task.Run(() => JunkFileCleanerService.Clean(selected));

                LastCleanedText = $"Freed {FormatBytes(freedBytes)}.";
                await ScanAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Cleaner] Cleaning junk files failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsCleaning = false;
            }
        }

        public void SetAllSelected(bool selected)
        {
            foreach (JunkCategoryItemViewModel item in Categories)
            {
                item.IsSelected = selected;
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            const double megabyte = 1024d * 1024;
            const double gigabyte = megabyte * 1024;

            if (bytes >= gigabyte)
            {
                return (bytes / gigabyte).ToString("0.##") + " GB";
            }

            return (bytes / megabyte).ToString("0.#") + " MB";
        }
    }
}
