#nullable enable

using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Globalization;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Hardware-accelerated GPU scheduling (HAGS) toggle for General Configuration.
    /// Single read/write path: HwSchMode DWORD under
    /// HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers, accessed through
    /// RegistryHelper (RegistryView.Registry64 via OpenBaseKey). 2 = on, 1 = off.
    /// Windows Settings uses this SYSTEM key; the SOFTWARE\...\GraphicsDrivers path
    /// is not where Windows stores HwSchMode. A restart is required for the GPU
    /// scheduler to pick up the new DWORD.
    /// </summary>
    public class HagsConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _hagsConfigurationStore;

        public HagsConfigurationService(
            [FromKeyedServices("Hags")] ConfigurationStore hagsConfigurationStore)
        {
            _hagsConfigurationStore = hagsConfigurationStore;
        }

        public static bool IsSupported() => HagsDetection.CanToggle(Detect().State);

        public static HagsDetectionResult Detect()
        {
            int windowsBuild = ReadWindowsBuildNumber();
            HwSchModeRead read = ReadHwSchMode();
            HagsSupportState state = HagsDetection.Classify(
                windowsBuild,
                read.Value,
                read.Failed);

            if (read.Failed)
            {
                App.logger.Warn("[HAGS] Registry read failed while inspecting HwSchMode on Windows build {0}.", windowsBuild);
            }
            else if (state == HagsSupportState.Unknown)
            {
                App.logger.Warn(
                    "[HAGS] Unexpected HwSchMode value {0} ({1}) on Windows build {2}.",
                    read.RawDisplay,
                    read.RawType,
                    windowsBuild);
            }

            return new HagsDetectionResult(state, read.Value, windowsBuild);
        }

        public void Disable() => WriteHwSchMode(enabled: false);

        public void Enable() => WriteHwSchMode(enabled: true);

        public bool IsEnabled()
        {
            HagsDetectionResult result = Detect();
            if (!HagsDetection.CanToggle(result.State))
            {
                throw new NotSupportedException(HagsDetection.GetStatusText(result));
            }

            return RegistryHelper.IsMatch(
                HagsDetection.GraphicsDriversKeyPath,
                HagsDetection.HwSchModeValueName,
                2);
        }

        private void WriteHwSchMode(bool enabled)
        {
            EnsureCanToggle();
            int value = enabled ? 2 : 1;
            RegistryHelper.SetValue(
                HagsDetection.GraphicsDriversKeyPath,
                HagsDetection.HwSchModeValueName,
                value,
                RegistryValueKind.DWord);

            // Optimistic: the DWORD is what Windows will apply after reboot.
            // Do not requery a live kernel flag — HwSchMode is a persisted setting.
            _hagsConfigurationStore.CurrentSetting = enabled;
            App.ContentDialogCaller("restart");
        }

        private static void EnsureCanToggle()
        {
            HagsDetectionResult result = Detect();
            if (!HagsDetection.CanToggle(result.State))
            {
                throw new NotSupportedException(HagsDetection.GetStatusText(result));
            }
        }

        private static int ReadWindowsBuildNumber()
        {
            if (RegistryHelper.TryReadValue(
                    @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber",
                    out object? buildValue)
                && TryParseInt32(buildValue, out int build))
            {
                return build;
            }

            if (RegistryHelper.TryReadValue(
                    @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuild",
                    out object? fallbackValue)
                && TryParseInt32(fallbackValue, out int fallbackBuild))
            {
                return fallbackBuild;
            }

            return Environment.OSVersion.Version.Build;
        }

        private readonly record struct HwSchModeRead(int? Value, bool Failed, string RawDisplay, string RawType);

        private static HwSchModeRead ReadHwSchMode()
        {
            if (!RegistryHelper.TryReadValue(
                    HagsDetection.GraphicsDriversKeyPath,
                    HagsDetection.HwSchModeValueName,
                    out object? raw))
            {
                return new HwSchModeRead(null, true, "read failed", "none");
            }

            if (raw is null)
            {
                return new HwSchModeRead(null, false, "missing value", "none");
            }

            if (TryParseInt32(raw, out int parsed))
            {
                return new HwSchModeRead(parsed, false, parsed.ToString(CultureInfo.InvariantCulture), raw.GetType().Name);
            }

            return new HwSchModeRead(
                null,
                true,
                Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "(unprintable)",
                raw.GetType().FullName ?? raw.GetType().Name);
        }

        private static bool TryParseInt32(object? value, out int mode)
        {
            switch (value)
            {
                case int intValue:
                    mode = intValue;
                    return true;
                case uint unsignedValue when unsignedValue <= int.MaxValue:
                    mode = (int)unsignedValue;
                    return true;
                case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                    mode = (int)longValue;
                    return true;
                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                    mode = parsed;
                    return true;
                default:
                    mode = 0;
                    return false;
            }
        }
    }
}
