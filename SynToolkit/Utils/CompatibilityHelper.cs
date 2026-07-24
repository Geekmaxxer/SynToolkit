#nullable enable

using System;

namespace SynToolkit.Utils
{
    public static class CompatibilityHelper
    {
        public const int MinimumWindowsBuild = 17763;
        public const string SynergyOsReleasesUrl = "https://github.com/kwanteks/synergyos/releases";

        private const string OemInformationPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation";
        private const string SynergyOsOemManufacturer = "Kwanteks";

        // SynergyOS installs may use either the short SOS marker or the full name.
        private static readonly string[] SynergyOsOemModels = ["SOS 11", "SYNERGYOS"];

        /// <summary>
        /// SynToolkit requires 64-bit Windows 10 version 1809 or newer.
        /// </summary>
        public static bool IsWindowsCompatible() =>
            Environment.Is64BitOperatingSystem &&
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild);

        /// <summary>
        /// SynToolkit requires a SynergyOS installation identified by the OEM
        /// registry markers written by the SynergyOS playbook:
        /// Model = SOS 11 (or SYNERGYOS), Manufacturer = Kwanteks.
        /// </summary>
        public static bool IsSynergyOsCompatible()
        {
            string? model = ReadRegistryString(OemInformationPath, "Model");
            string? manufacturer = ReadRegistryString(OemInformationPath, "Manufacturer");

            return IsSynergyOsModel(model)
                && IsRegistryMatch(manufacturer, SynergyOsOemManufacturer);
        }

        private static bool IsSynergyOsModel(string? actual) =>
            !string.IsNullOrWhiteSpace(actual)
            && Array.Exists(
                SynergyOsOemModels,
                expected => actual.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase));

        private static bool IsRegistryMatch(string? actual, string expected) =>
            !string.IsNullOrWhiteSpace(actual)
            && actual.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

        private static string? ReadRegistryString(string keyPath, string valueName)
        {
            object? value = RegistryHelper.GetValue(keyPath, valueName);
            string? text = value?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
