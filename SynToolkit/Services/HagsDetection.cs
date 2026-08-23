#nullable enable

namespace SynToolkit.Services
{
    public enum HagsSupportState
    {
        NotSupportedByWindowsVersion,
        NotSupportedByGpuOrDriver,
        SupportedDisabled,
        SupportedEnabled,
        Unknown,
    }

    public readonly record struct HagsDetectionResult(
        HagsSupportState State,
        int? HwSchMode,
        int WindowsBuild);

    /// <summary>
    /// Classifies Hardware-accelerated GPU scheduling (HAGS) from the OS build and the
    /// HwSchMode DWORD. Windows 10 version 2004 is build 19041. Value meanings:
    /// 2 = enabled, 1 = present but disabled, missing = unsupported on this OS/GPU.
    /// </summary>
    public static class HagsDetection
    {
        public const int MinimumWindowsBuild = 19041;

        public const string GraphicsDriversKeyPath =
            @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public const string GraphicsDriversSubKey =
            @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public const string HwSchModeValueName = "HwSchMode";

        public static HagsSupportState Classify(
            int windowsBuild,
            int? hwSchMode,
            bool registryReadFailed = false)
        {
            if (registryReadFailed)
            {
                return HagsSupportState.Unknown;
            }

            if (windowsBuild < MinimumWindowsBuild)
            {
                return HagsSupportState.NotSupportedByWindowsVersion;
            }

            return hwSchMode switch
            {
                null => HagsSupportState.NotSupportedByGpuOrDriver,
                1 => HagsSupportState.SupportedDisabled,
                2 => HagsSupportState.SupportedEnabled,
                _ => HagsSupportState.Unknown,
            };
        }

        public static string GetStatusText(HagsSupportState state, int? hwSchMode = null) => state switch
        {
            HagsSupportState.NotSupportedByWindowsVersion => "Not supported by your Windows version.",
            HagsSupportState.NotSupportedByGpuOrDriver => "Not supported by your GPU/driver.",
            HagsSupportState.SupportedDisabled => "Supported — currently disabled.",
            HagsSupportState.SupportedEnabled => "Supported — currently enabled.",
            _ => hwSchMode.HasValue
                ? $"Unknown (HwSchMode={hwSchMode.Value})."
                : "Unknown.",
        };

        public static string GetStatusText(HagsDetectionResult result) =>
            GetStatusText(result.State, result.HwSchMode);

        public static bool CanToggle(HagsSupportState state) =>
            state is HagsSupportState.SupportedDisabled or HagsSupportState.SupportedEnabled;
    }
}
