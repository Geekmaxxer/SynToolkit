using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class AutomaticFolderDiscoveryConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\AutomaticFolderDiscovery";
        private const string STATE_VALUE_NAME = "state";

        private const string SHELL_KEY_NAME = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell";

        private const string FOLDER_TYPE_VALUE_NAME = "FolderType";

        private readonly ConfigurationStore _automaticFolderDiscoveryConfigurationService;

        public AutomaticFolderDiscoveryConfigurationService(
            [FromKeyedServices("AutomaticFolderDiscovery")] ConfigurationStore automaticFolderDiscoveryConfigurationService)
        {
            _automaticFolderDiscoveryConfigurationService = automaticFolderDiscoveryConfigurationService;
        }

        public void Disable()
        {
            // This Explorer override stops content sniffing without changing any
            // of the user's other Bag settings.
            RegistryHelper.SetValue(SHELL_KEY_NAME, FOLDER_TYPE_VALUE_NAME, "NotSpecified", Microsoft.Win32.RegistryValueKind.String);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _automaticFolderDiscoveryConfigurationService.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(SHELL_KEY_NAME, FOLDER_TYPE_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _automaticFolderDiscoveryConfigurationService.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                return RegistryHelper.GetValue(SHELL_KEY_NAME, FOLDER_TYPE_VALUE_NAME) is null;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect folder-discovery override: {exception.Message}");
                return false;
            }
        }
    }
}
