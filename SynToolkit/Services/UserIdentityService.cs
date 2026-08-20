#nullable enable

using System;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Security.Principal;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    /// <summary>
    /// Changes the signed-in Windows user's password and display name, and the built-in
    /// Administrator account's password. Password changes use the standard .NET ADSI
    /// wrapper. Display-name changes use the LocalAccounts module, whose SAM-based API
    /// continues to work when the Windows Server (LanmanServer) service is disabled.
    /// SetPassword intentionally does not require the old password — this is normal
    /// behavior for an elevated admin tool.
    /// </summary>
    public static class UserIdentityService
    {
        public static UserPrincipal GetCurrentUser() =>
            UserPrincipal.Current ?? throw new InvalidOperationException("Unable to resolve the signed-in Windows user.");

        public static void ChangeDisplayName(string newDisplayName)
        {
            string normalizedDisplayName = LocalUserDisplayNameCommand.Normalize(newDisplayName);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value
                ?? throw new InvalidOperationException("Unable to resolve the signed-in user's SID.");
            string powerShellPath = GetWindowsPowerShellPath();
            string encodedCommand = LocalUserDisplayNameCommand.CreateEncodedPowerShellCommand(
                sid,
                normalizedDisplayName);

            CommandResult result = CommandPromptHelper.RunProcessResult(
                powerShellPath,
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-EncodedCommand",
                    encodedCommand
                ],
                timeoutMilliseconds: 30_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(BuildDisplayNameFailureMessage(result));
            }
        }

        public static void ChangePassword(string newPassword)
        {
            UserPrincipal current = GetCurrentUser();
            try
            {
                current.SetPassword(newPassword ?? string.Empty);
            }
            catch (PasswordException exception)
            {
                throw new InvalidOperationException($"Windows rejected the new password: {exception.Message}", exception);
            }
        }

        public static void ChangeAdministratorPassword(string newPassword)
        {
            using PrincipalContext context = new(ContextType.Machine);
            using PrincipalSearcher searcher = new(new UserPrincipal(context));
            UserPrincipal administrator = searcher.FindAll()
                .OfType<UserPrincipal>()
                .FirstOrDefault(user => user.SamAccountName == "Administrator")
                ?? throw new InvalidOperationException("The built-in Administrator account was not found.");

            try
            {
                administrator.SetPassword(newPassword ?? string.Empty);
            }
            catch (PasswordException exception)
            {
                throw new InvalidOperationException($"Windows rejected the new password: {exception.Message}", exception);
            }
        }

        private static string GetWindowsPowerShellPath()
        {
            string windowsDirectory = Environment.GetEnvironmentVariable("WINDIR")
                ?? throw new InvalidOperationException("Unable to locate the Windows directory.");
            string systemDirectory = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? "Sysnative"
                : "System32";
            string path = Path.Combine(
                windowsDirectory,
                systemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            return File.Exists(path)
                ? path
                : throw new InvalidOperationException("Windows PowerShell is not available on this system.");
        }

        private static string BuildDisplayNameFailureMessage(CommandResult result)
        {
            if (result.TimedOut)
            {
                return "Changing the display name timed out.";
            }

            string detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            return string.IsNullOrWhiteSpace(detail)
                ? $"Windows could not change the display name (exit code {result.ExitCode})."
                : $"Windows could not change the display name: {detail.Trim()}";
        }
    }
}
