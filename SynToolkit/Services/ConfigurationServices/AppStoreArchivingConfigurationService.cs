using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class AppStoreArchivingConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\AppStoreArchiving";
        private const string STATE_VALUE_NAME = "state";

        private const string APPX_KEY_NAME = @"HKLM\Software\Policies\Microsoft\Windows\Appx";

        private const string ALLOW_AUTOMATIC_APP_ARCHIVING_VALUE_NAME = "AllowAutomaticAppArchiving";

        private readonly ConfigurationStore _appStoreArchivingConfigurationService;

        public AppStoreArchivingConfigurationService(
            [FromKeyedServices("AppStoreArchiving")] ConfigurationStore appStoreArchivingConfigurationStore)
        {
            _appStoreArchivingConfigurationService = appStoreArchivingConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(APPX_KEY_NAME, ALLOW_AUTOMATIC_APP_ARCHIVING_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _appStoreArchivingConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(APPX_KEY_NAME, ALLOW_AUTOMATIC_APP_ARCHIVING_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _appStoreArchivingConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(APPX_KEY_NAME, ALLOW_AUTOMATIC_APP_ARCHIVING_VALUE_NAME);
                // Microsoft documents 0=deny, 1=enable and 65535/not present as
                // the default user-controlled behavior.
                return value is null
                    || value is int policy && policy is 1 or 65535;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect app-archiving policy: {exception.Message}");
                return false;
            }
        }
    }
}
