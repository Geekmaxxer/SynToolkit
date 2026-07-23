#nullable enable

namespace SynToolkit.Models
{
    public enum PlaybookDetectionStatus
    {
        NotDetected,
        Detected,
        Conflicting
    }

    public sealed record PlaybookInformation(
        PlaybookDetectionStatus Status,
        string? Name,
        string? Version,
        string? Source);

    public sealed record CustomWindowsInformation(
        string DisplayName,
        string Source);

    public sealed record SystemInformationSnapshot(
        string WindowsProductName,
        string WindowsDisplayVersion,
        string WindowsBuild,
        string Architecture,
        CustomWindowsInformation? CustomWindowsBuild,
        PlaybookInformation Playbook);
}
