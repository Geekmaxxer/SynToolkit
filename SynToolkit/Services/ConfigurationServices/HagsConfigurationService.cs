using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Hardware-accelerated GPU scheduling (HAGS) configuration service.
    /// Reduces latency and improves performance. Requires PC restart for changes to take effect.
    /// </summary>
    public class HagsConfigurationService : IConfigurationService
    {
        private const string GRAPHIC_DRIVERS_KEY_NAME = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string HW_SCH_MODE_VALUE_NAME = "HwSchMode";

        private readonly ConfigurationStore _hagsConfigurationStore;

        public HagsConfigurationService(
            [FromKeyedServices("Hags")] ConfigurationStore hagsConfigurationStore)
        {
            _hagsConfigurationStore = hagsConfigurationStore;
        }

        /// <summary>
        /// Checks if the system supports Hardware-accelerated GPU scheduling.
        /// HAGS is only available on Windows 10 2004+ with compatible GPU drivers.
        /// </summary>
        public static bool IsSupported()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                if (key == null) return false;

                object value = key.GetValue("HwSchMode");
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        public void Disable()
        {
            if (!IsSupported())
            {
                throw new NotSupportedException("Hardware-accelerated GPU scheduling is not supported on this device.");
            }

            RegistryHelper.SetValue(GRAPHIC_DRIVERS_KEY_NAME, HW_SCH_MODE_VALUE_NAME, 1);
            App.ContentDialogCaller("restart");
            _hagsConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            if (!IsSupported())
            {
                throw new NotSupportedException("Hardware-accelerated GPU scheduling is not supported on this device.");
            }

            RegistryHelper.SetValue(GRAPHIC_DRIVERS_KEY_NAME, HW_SCH_MODE_VALUE_NAME, 2);
            App.ContentDialogCaller("restart");
            _hagsConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            if (!IsSupported())
            {
                throw new NotSupportedException("Hardware-accelerated GPU scheduling is not supported on this device.");
            }

            return RegistryHelper.IsMatch(GRAPHIC_DRIVERS_KEY_NAME, HW_SCH_MODE_VALUE_NAME, 2);
        }
    }
}
