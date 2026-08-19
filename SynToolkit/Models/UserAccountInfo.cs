#nullable enable

namespace SynToolkit.Models
{
    public sealed record UserAccountInfo(
        string DisplayName,
        string? AccountTypeLabel,
        string? ProfilePicturePath);
}
