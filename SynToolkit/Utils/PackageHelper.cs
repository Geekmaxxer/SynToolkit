using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace SynToolkit.Utils
{
    public static class PackageHelper
    {
        public static IReadOnlyList<Package> FindCurrentUserPackages(Func<Package, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            PackageManager packageManager = new();
            return packageManager
                .FindPackagesForUser(string.Empty)
                .Where(package => package.Status.VerifyIsOK())
                .Where(predicate)
                .ToArray();
        }

        public static bool IsCurrentUserPackageInstalled(string packageName)
        {
            return FindCurrentUserPackages(package =>
                package.Id.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase)).Count > 0;
        }

        public static bool IsCurrentUserPackageInstalledContaining(string packageNameFragment)
        {
            return FindCurrentUserPackages(package =>
                package.Id.Name.Contains(packageNameFragment, StringComparison.OrdinalIgnoreCase)).Count > 0;
        }
    }
}
