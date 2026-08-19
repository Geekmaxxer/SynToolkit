#nullable enable

using Microsoft.Win32;
using SynToolkit.Models;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services
{
    public interface IUserAccountInfoService
    {
        UserAccountInfo GetPlaceholder();

        Task<UserAccountInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads the signed-in Windows user's display name, account type, and profile picture once per session.
    /// Uses the same sources Windows Settings relies on (AccountPicture registry, Accounts metadata, ADSI/WMI).
    /// </summary>
    public sealed class UserAccountInfoService : IUserAccountInfoService
    {
        private const string AccountPictureRegistryRoot =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users";

        private static readonly int[] ProfilePictureResolutionPreference =
        [
            1080, 448, 424, 240, 208, 192, 96, 64, 48, 40, 32
        ];

        private readonly object _cacheLock = new();
        private Task<UserAccountInfo>? _cachedLookup;

        public UserAccountInfo GetPlaceholder()
        {
            return new UserAccountInfo(
                SanitizeDisplayName(Environment.UserName),
                null,
                null);
        }

        public Task<UserAccountInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            lock (_cacheLock)
            {
                _cachedLookup ??= Task.Run(() => LoadCurrentUserCore(cancellationToken), cancellationToken);
                return _cachedLookup;
            }
        }

        private static UserAccountInfo LoadCurrentUserCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string displayName = ResolveDisplayName();
            string? accountType = ResolveAccountTypeLabel();
            string? profilePicturePath = ResolveProfilePicturePath();

            return new UserAccountInfo(
                displayName,
                accountType,
                profilePicturePath);
        }

        internal static string SanitizeDisplayName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "there";
            }

            string sanitized = new string(rawName
                .Where(character => !char.IsControl(character))
                .Take(64)
                .ToArray())
                .Trim();

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return "there";
            }

            if (sanitized.All(static character => !char.IsWhiteSpace(character)) &&
                sanitized.Equals(sanitized, StringComparison.OrdinalIgnoreCase))
            {
                sanitized = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sanitized.ToLowerInvariant());
            }

            return sanitized;
        }

        private static string ResolveDisplayName()
        {
            try
            {
                UserPrincipal current = UserPrincipal.Current;
                if (!string.IsNullOrWhiteSpace(current.DisplayName))
                {
                    return SanitizeDisplayName(current.DisplayName);
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[UserAccount] UserPrincipal.Current.DisplayName lookup failed.");
            }

            if (TryGetWin32UserAccountFullName(out string fullName))
            {
                return SanitizeDisplayName(fullName);
            }

            return SanitizeDisplayName(Environment.UserName);
        }

        private static string? ResolveAccountTypeLabel()
        {
            string? accountsKeyLabel = ResolveAccountTypeFromAccountsRegistry();
            if (!string.IsNullOrWhiteSpace(accountsKeyLabel))
            {
                return accountsKeyLabel;
            }

            if (!TryGetWin32UserAccount(out bool localAccount, out string domain))
            {
                return null;
            }

            if (localAccount)
            {
                return "Local Account";
            }

            if (!string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return SanitizeDisplayName(domain);
            }

            return null;
        }

        private static string? ResolveAccountTypeFromAccountsRegistry()
        {
            try
            {
                using RegistryKey? accountsKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Accounts");
                if (accountsKey is null)
                {
                    return null;
                }

                foreach (string subKeyName in accountsKey.GetSubKeyNames())
                {
                    using RegistryKey? accountKey = accountsKey.OpenSubKey(subKeyName);
                    string? userName = accountKey?.GetValue("UserName") as string;
                    if (!string.Equals(userName, Environment.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    object accountTypeValue = accountKey?.GetValue("AccountType") ?? 0;
                    int accountType = accountTypeValue switch
                    {
                        int intValue => intValue,
                        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
                        _ => 0
                    };

                    return accountType switch
                    {
                        1 => "Microsoft Account",
                        2 => "Work or school account",
                        0 => "Local Account",
                        _ => null
                    };
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[UserAccount] Accounts registry lookup failed.");
            }

            return null;
        }

        private static bool TryGetWin32UserAccountFullName(out string fullName)
        {
            fullName = string.Empty;
            if (!TryGetWin32UserAccount(out _, out _))
            {
                return false;
            }

            try
            {
                string query =
                    $"SELECT FullName FROM Win32_UserAccount WHERE Name = '{EscapeWmiLiteral(Environment.UserName)}' AND Domain = '{EscapeWmiLiteral(Environment.UserDomainName)}'";
                using ManagementObjectSearcher searcher = new(query);
                using ManagementObjectCollection results = searcher.Get();
                foreach (ManagementObject result in results.Cast<ManagementObject>())
                {
                    string? candidate = result["FullName"] as string;
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        fullName = candidate;
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[UserAccount] Win32_UserAccount FullName lookup failed.");
            }

            return false;
        }

        private static bool TryGetWin32UserAccount(out bool localAccount, out string domain)
        {
            localAccount = true;
            domain = Environment.MachineName;

            try
            {
                string query =
                    $"SELECT LocalAccount, Domain FROM Win32_UserAccount WHERE Name = '{EscapeWmiLiteral(Environment.UserName)}' AND Domain = '{EscapeWmiLiteral(Environment.UserDomainName)}'";
                using ManagementObjectSearcher searcher = new(query);
                using ManagementObjectCollection results = searcher.Get();
                ManagementObject? match = results.Cast<ManagementObject>().FirstOrDefault();
                if (match is null)
                {
                    return false;
                }

                localAccount = match["LocalAccount"] is bool isLocal && isLocal;
                domain = match["Domain"] as string ?? Environment.MachineName;
                return true;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[UserAccount] Win32_UserAccount lookup failed.");
                return false;
            }
        }

        private static string? ResolveProfilePicturePath()
        {
            string? registryPath = ResolveProfilePicturePathFromRegistry();
            if (IsExistingImageFile(registryPath))
            {
                return registryPath;
            }

            string localCopy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows",
                "AccountPicture",
                "UserImage.jpg");
            if (IsExistingImageFile(localCopy))
            {
                return localCopy;
            }

            return null;
        }

        private static string? ResolveProfilePicturePathFromRegistry()
        {
            try
            {
                SecurityIdentifier? sid = WindowsIdentity.GetCurrent().User;
                if (sid is null)
                {
                    return null;
                }

                string registryPath = $@"HKLM\{AccountPictureRegistryRoot}\{sid.Value}";
                string? preferredPath = null;
                if (RegistryHelper.TryReadValue(registryPath, "UserPicturePath", out object userPicturePath) &&
                    userPicturePath is string userPicturePathString &&
                    IsExistingImageFile(userPicturePathString))
                {
                    preferredPath = userPicturePathString;
                }

                foreach (int resolution in ProfilePictureResolutionPreference)
                {
                    if (!RegistryHelper.TryReadValue(registryPath, $"Image{resolution}", out object value) ||
                        value is not string candidatePath ||
                        !IsExistingImageFile(candidatePath))
                    {
                        continue;
                    }

                    if (preferredPath is null || resolution > ExtractResolutionFromPath(preferredPath))
                    {
                        preferredPath = candidatePath;
                    }
                }

                return preferredPath;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[UserAccount] Account picture registry lookup failed.");
                return null;
            }
        }

        private static int ExtractResolutionFromPath(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            int imageIndex = fileName.LastIndexOf("Image", StringComparison.OrdinalIgnoreCase);
            if (imageIndex >= 0 &&
                int.TryParse(fileName[(imageIndex + "Image".Length)..], out int resolution))
            {
                return resolution;
            }

            return 0;
        }

        private static bool IsExistingImageFile(string? path) =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path);

        private static string EscapeWmiLiteral(string value) =>
            value.Replace("\\", "\\\\").Replace("'", "''");
    }
}
