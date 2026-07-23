using System;

namespace SynToolkit.Utils
{
    public static class CompatibilityHelper
    {
        public const int MinimumWindowsBuild = 22000;

        /// <summary>
        /// SynToolkit supports genuine and custom 64-bit Windows 11 installations and does not
        /// require any third-party Windows modification to be installed.
        /// </summary>
        public static bool IsCompatible() =>
            Environment.Is64BitOperatingSystem &&
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild);
    }
}
