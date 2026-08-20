#nullable enable

using System;
using System.Linq;
using System.Text;

namespace SynToolkit.Services
{
    /// <summary>
    /// Builds the encoded Windows PowerShell command used to update a local account's
    /// full name. The display name is carried as Base64 data so user input never becomes
    /// executable PowerShell syntax.
    /// </summary>
    internal static class LocalUserDisplayNameCommand
    {
        internal const int MaximumDisplayNameLength = 256;

        internal static string Normalize(string displayName)
        {
            ArgumentNullException.ThrowIfNull(displayName);

            string normalized = displayName.Trim();
            if (normalized.Length == 0)
            {
                throw new ArgumentException("Enter a display name.", nameof(displayName));
            }

            if (normalized.Length > MaximumDisplayNameLength)
            {
                throw new ArgumentException(
                    $"The display name cannot be longer than {MaximumDisplayNameLength} characters.",
                    nameof(displayName));
            }

            if (normalized.Any(char.IsControl))
            {
                throw new ArgumentException("The display name cannot contain control characters.", nameof(displayName));
            }

            return normalized;
        }

        internal static string CreateEncodedPowerShellCommand(string sid, string displayName)
        {
            ValidateSid(sid);
            string normalizedDisplayName = Normalize(displayName);
            string encodedDisplayName = Convert.ToBase64String(Encoding.Unicode.GetBytes(normalizedDisplayName));

            string script =
                "$ErrorActionPreference='Stop';" +
                "try{" +
                "Import-Module Microsoft.PowerShell.LocalAccounts -ErrorAction Stop;" +
                $"$DisplayName=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{encodedDisplayName}'));" +
                $"Set-LocalUser -SID '{sid}' -FullName $DisplayName -ErrorAction Stop" +
                "}catch{" +
                "[Console]::Error.WriteLine($_.Exception.Message);" +
                "exit 1" +
                "}";

            return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        }

        private static void ValidateSid(string sid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);
            if (!sid.StartsWith("S-", StringComparison.OrdinalIgnoreCase) ||
                sid.Skip(2).Any(character => character != '-' && !char.IsDigit(character)))
            {
                throw new ArgumentException("The current Windows account has an invalid SID.", nameof(sid));
            }
        }
    }
}
