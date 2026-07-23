using System;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class LockScreenConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\LockScreen";
        private const string STATE_VALUE_NAME = "state";

        private const string PERSONALIZATION_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization";

        private const string NO_LOCK_SCREEN_VALUE_NAME = "NoLockScreen";

        private readonly ConfigurationStore _lockScreenConfigurationStore;

        public LockScreenConfigurationService(
            [FromKeyedServices("LockScreen")] ConfigurationStore lockScreenConfigurationStore)
        {
            _lockScreenConfigurationStore = lockScreenConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(PERSONALIZATION_KEY_NAME, NO_LOCK_SCREEN_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _lockScreenConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(PERSONALIZATION_KEY_NAME, NO_LOCK_SCREEN_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _lockScreenConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object policy = RegistryHelper.GetValue(PERSONALIZATION_KEY_NAME, NO_LOCK_SCREEN_VALUE_NAME);
                return !(policy is int noLockScreen && noLockScreen != 0);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect lock-screen policy: {exception.Message}");
                return false;
            }
        }
    }
}
