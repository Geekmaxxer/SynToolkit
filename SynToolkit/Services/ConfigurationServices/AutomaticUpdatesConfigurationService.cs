using SynToolkit.Services;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class AutomaticUpdatesConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\AutomaticUpdates";
        private const string STATE_VALUE_NAME = "state";

        private const string AU_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

        private const string AU_OPTIONS_VALUE_NAME = "AUOptions";
        private const string NO_AUTO_UPDATE_VALUE_NAME = "NoAutoUpdate";

        private readonly ConfigurationStore _automaticRepairConfigurationStore;
        public AutomaticUpdatesConfigurationService(
            [FromKeyedServices("AutomaticUpdates")] ConfigurationStore automaticUpdatesConfigurationStore) 
        {
            _automaticRepairConfigurationStore = automaticUpdatesConfigurationStore;
        }
        public void Disable()
        {
            RegistryHelper.SetValue(AU_KEY_NAME, NO_AUTO_UPDATE_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(AU_KEY_NAME, AU_OPTIONS_VALUE_NAME, 2, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _automaticRepairConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(AU_KEY_NAME, NO_AUTO_UPDATE_VALUE_NAME);
            RegistryHelper.DeleteValue(AU_KEY_NAME, AU_OPTIONS_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _automaticRepairConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object noAutoUpdate = RegistryHelper.GetValue(AU_KEY_NAME, NO_AUTO_UPDATE_VALUE_NAME);
                object auOptions = RegistryHelper.GetValue(AU_KEY_NAME, AU_OPTIONS_VALUE_NAME);
                // Not configured uses Windows' automatic-update defaults. Values
                // 3-5 retain automatic download/install behavior; 2 is notify-only.
                return !(noAutoUpdate is int disabled && disabled != 0)
                    && (auOptions is null || auOptions is int option && option is >= 3 and <= 5);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect Automatic Updates policy: {exception.Message}");
                return false;
            }
        }   
    }
}
