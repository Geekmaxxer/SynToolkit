using System;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class CompactViewConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\CompactView";
        private const string STATE_VALUE_NAME = "state";


        private const string ADVANCED_KEY_NAME = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string USE_COMPACT_MODE_VALUE_NAME = "UseCompactMode";

        private readonly ConfigurationStore _compactViewConfigurationStore;

        public CompactViewConfigurationService(
            [FromKeyedServices("CompactView")] ConfigurationStore compactViewConfigurationStore)
        {
            _compactViewConfigurationStore = compactViewConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(ADVANCED_KEY_NAME, USE_COMPACT_MODE_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _compactViewConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(ADVANCED_KEY_NAME, USE_COMPACT_MODE_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _compactViewConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                return RegistryHelper.IsMatch(ADVANCED_KEY_NAME, USE_COMPACT_MODE_VALUE_NAME, 1);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect compact-view state: {exception.Message}");
                return false;
            }
        }
    }
}
