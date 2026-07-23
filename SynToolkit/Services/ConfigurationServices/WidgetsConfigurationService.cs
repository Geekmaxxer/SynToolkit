using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationServices
{
    class WidgetsConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\Widgets";
        private const string STATE_VALUE_NAME = "state";

        private const string WINDOWS_FEED_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds";
        private const string DSH_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh";

        private const string ENABLE_FIELDS_VALUE_NAME = "EnableFeeds";
        private const string ALLOW_NEWS_AND_INTERESTS_VALUE_NAME = "AllowNewsAndInterests";

        private readonly ConfigurationStore _widgetsConfigurationService;

        public WidgetsConfigurationService(
            [FromKeyedServices("Widgets")] ConfigurationStore WidgetsConfigurationService)
        {
            _widgetsConfigurationService = WidgetsConfigurationService;
        }


        public void Disable()
        {
            RegistryHelper.SetValue(WINDOWS_FEED_KEY_NAME, ENABLE_FIELDS_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(DSH_KEY_NAME, ALLOW_NEWS_AND_INTERESTS_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            CommandPromptHelper.RestartExplorer();

            _widgetsConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(WINDOWS_FEED_KEY_NAME, ENABLE_FIELDS_VALUE_NAME);
            RegistryHelper.DeleteValue(DSH_KEY_NAME, ALLOW_NEWS_AND_INTERESTS_VALUE_NAME);

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            CommandPromptHelper.RestartExplorer();
            _widgetsConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return !RegistryHelper.IsMatch(WINDOWS_FEED_KEY_NAME, ENABLE_FIELDS_VALUE_NAME, 0)
                && !RegistryHelper.IsMatch(DSH_KEY_NAME, ALLOW_NEWS_AND_INTERESTS_VALUE_NAME, 0);
        }
    }
}
