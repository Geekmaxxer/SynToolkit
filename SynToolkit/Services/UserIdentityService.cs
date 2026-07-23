#nullable enable

using System;
using System.DirectoryServices.AccountManagement;
using System.Linq;

namespace SynToolkit.Services
{
    /// <summary>
    /// Changes the signed-in Windows user's password and display name, and the built-in
    /// Administrator account's password, via the standard .NET ADSI wrapper
    /// (System.DirectoryServices.AccountManagement). SetPassword intentionally does not
    /// require the old password — this is normal behavior for an elevated admin tool.
    /// </summary>
    public static class UserIdentityService
    {
        public static UserPrincipal GetCurrentUser() =>
            UserPrincipal.Current ?? throw new InvalidOperationException("Unable to resolve the signed-in Windows user.");

        public static void ChangeDisplayName(string newDisplayName)
        {
            UserPrincipal current = GetCurrentUser();
            current.DisplayName = newDisplayName;
            current.Save();
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
    }
}
