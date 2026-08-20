#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Models;

namespace SynToolkit.ViewModels
{
    public partial class JunkCategoryItemViewModel : ObservableObject
    {
        public JunkCategory Category { get; }
        public string DisplayName { get; }
        public ulong SizeBytes { get; }
        public int FileCount { get; }
        public string SizeText { get; }
        public string FileCountText { get; }

        [ObservableProperty]
        public partial bool IsSelected { get; set; } = true;

        public JunkCategoryItemViewModel(JunkCategoryScanResult scanResult)
        {
            Category = scanResult.Category;
            DisplayName = scanResult.DisplayName;
            SizeBytes = scanResult.SizeBytes;
            FileCount = scanResult.FileCount;
            SizeText = FormatBytes(scanResult.SizeBytes);
            FileCountText = scanResult.FileCount == 1 ? "1 file" : $"{scanResult.FileCount} files";
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
