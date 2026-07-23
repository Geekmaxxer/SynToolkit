using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Windows.ApplicationModel.Activation;

namespace SynToolkit.Services.ConfigurationServices
{
    public class GiveAccessToMenuConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\GiveAccessToMenu";
        private const string STATE_VALUE_NAME = "state";

        private const string SHARING_1_KEY_NAME = @"HKLM\SOFTWARE\Classes\*\shellex\ContextMenuHandlers\Sharing";
        private const string SHARING_2_KEY_NAME = @"HKLM\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\Sharing";
        private const string SHARING_3_KEY_NAME = @"HKLM\SOFTWARE\Classes\Directory\shellex\ContextMenuHandlers\Sharing";
        private const string SHARING_4_KEY_NAME = @"HKLM\SOFTWARE\Classes\Drive\shellex\ContextMenuHandlers\Sharing";
        private const string SHARING_5_KEY_NAME = @"HKLM\SOFTWARE\Classes\LibraryFolder\background\shellex\ContextMenuHandlers\Sharing";
        private const string SHARING_6_KEY_NAME = @"HKLM\SOFTWARE\Classes\UserLibraryFolder\shellex\ContextMenuHandlers\Sharing";

        private const string VALUE_VALUE_NAME = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}";

        private readonly ConfigurationStore _configurationStore;

        public GiveAccessToMenuConfigurationService(
            [FromKeyedServices("GiveAccessToMenu")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Disable()
        {
            RegistryHelper.DeleteValue(SHARING_1_KEY_NAME, string.Empty);
            RegistryHelper.DeleteValue(SHARING_2_KEY_NAME, string.Empty);
            RegistryHelper.DeleteValue(SHARING_3_KEY_NAME, string.Empty);
            RegistryHelper.DeleteValue(SHARING_4_KEY_NAME, string.Empty);
            RegistryHelper.DeleteValue(SHARING_5_KEY_NAME, string.Empty);
            RegistryHelper.DeleteValue(SHARING_6_KEY_NAME, string.Empty);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(SHARING_1_KEY_NAME, string.Empty, VALUE_VALUE_NAME, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SHARING_2_KEY_NAME, string.Empty, VALUE_VALUE_NAME, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SHARING_3_KEY_NAME, string.Empty, VALUE_VALUE_NAME, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SHARING_4_KEY_NAME, string.Empty, VALUE_VALUE_NAME, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SHARING_5_KEY_NAME, string.Empty, VALUE_VALUE_NAME, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SHARING_6_KEY_NAME, string.Empty, VALUE_VALUE_NAME, Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                return RegistryHelper.IsMatch(SHARING_1_KEY_NAME, string.Empty, VALUE_VALUE_NAME)
                    && RegistryHelper.IsMatch(SHARING_2_KEY_NAME, string.Empty, VALUE_VALUE_NAME)
                    && RegistryHelper.IsMatch(SHARING_3_KEY_NAME, string.Empty, VALUE_VALUE_NAME)
                    && RegistryHelper.IsMatch(SHARING_4_KEY_NAME, string.Empty, VALUE_VALUE_NAME)
                    && RegistryHelper.IsMatch(SHARING_5_KEY_NAME, string.Empty, VALUE_VALUE_NAME)
                    && RegistryHelper.IsMatch(SHARING_6_KEY_NAME, string.Empty, VALUE_VALUE_NAME);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect Give access to context-menu state: {exception.Message}");
                return false;
            }
        }
    }
}
