#nullable enable

namespace SynToolkit.Models
{
    public enum JunkCategory
    {
        TempFiles,
        BrowserCaches,
        WindowsUpdateCache,
        RecycleBin,
        LogFiles,
        ThumbnailCache,
    }

    public sealed record JunkCategoryScanResult(JunkCategory Category, string DisplayName, ulong SizeBytes, int FileCount);
}
