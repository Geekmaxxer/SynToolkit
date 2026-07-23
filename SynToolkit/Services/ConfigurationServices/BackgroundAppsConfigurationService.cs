using SynToolkit.Services;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class BackgroundAppsConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\BackgroundApps";
        private const string STATE_VALUE_NAME = "state";

        private const string BACKGROUND_ACCESS_APPLICATION_KEY_NAME = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        private const string SEARCH_KEY_NAME = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search";
        private const string APP_PRIVACY_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

        private const string GLOBAL_USER_DISABLED_VALUE_NAME = "GlobalUserDisabled";
        private const string BACKGROUND_APP_GLOBAL_TOGGLE_VALUE_NAME = "BackgroundAppGlobalToggle";
        private const string LET_APPS_RUN_IN_BACKGROUND_VALUE_NAME = "LetAppsRunInBackground";

        private readonly ConfigurationStore _backgroundAppsConfigurationService;
        public BackgroundAppsConfigurationService(
            [FromKeyedServices("BackgroundApps")] ConfigurationStore backgroundAppsConfigurationService)
        {
            _backgroundAppsConfigurationService = backgroundAppsConfigurationService;
        }
        public void Disable()
        {
            // Policy value 2 is Microsoft's supported machine-wide Force Deny.
            RegistryHelper.SetValue(APP_PRIVACY_KEY_NAME, LET_APPS_RUN_IN_BACKGROUND_VALUE_NAME, 2, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(BACKGROUND_ACCESS_APPLICATION_KEY_NAME, GLOBAL_USER_DISABLED_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SEARCH_KEY_NAME, BACKGROUND_APP_GLOBAL_TOGGLE_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _backgroundAppsConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(APP_PRIVACY_KEY_NAME, LET_APPS_RUN_IN_BACKGROUND_VALUE_NAME);
            RegistryHelper.DeleteValue(BACKGROUND_ACCESS_APPLICATION_KEY_NAME, GLOBAL_USER_DISABLED_VALUE_NAME);
            RegistryHelper.DeleteValue(SEARCH_KEY_NAME, BACKGROUND_APP_GLOBAL_TOGGLE_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _backgroundAppsConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object policy = RegistryHelper.GetValue(
                    APP_PRIVACY_KEY_NAME, LET_APPS_RUN_IN_BACKGROUND_VALUE_NAME);
                object globalDisabled = RegistryHelper.GetValue(
                    BACKGROUND_ACCESS_APPLICATION_KEY_NAME, GLOBAL_USER_DISABLED_VALUE_NAME);
                object searchToggle = RegistryHelper.GetValue(
                    SEARCH_KEY_NAME, BACKGROUND_APP_GLOBAL_TOGGLE_VALUE_NAME);

                // Per-app allow/deny lists intentionally remain untouched. This
                // reports the global/default background-app state. An explicit
                // machine policy takes precedence over the legacy user toggle.
                if (policy is int policyValue)
                {
                    if (policyValue == 2)
                    {
                        return false;
                    }

                    if (policyValue == 1)
                    {
                        return true;
                    }
                }

                return !(globalDisabled is int disabled && disabled != 0)
                    && !(searchToggle is int toggle && toggle == 0);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect background-app state: {exception.Message}");
                return false;
            }
        }
    }
}
