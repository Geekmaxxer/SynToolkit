using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    public class NetworkNavigationPaneConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\NetworkNavigationPane";
        private const string STATE_VALUE_NAME = "state";

        private const string KEY_KEY_NAME = @"HKCU\SOFTWARE\Classes\CLSID\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        private const string MACHINE_NETWORK_CLSID_KEY_NAME = @"HKLM\SOFTWARE\Classes\CLSID\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        private const string SYSTEM_PINNED_VALUE_NAME = "System.IsPinnedToNameSpaceTree";
        private readonly ConfigurationStore _configurationStore;

        public NetworkNavigationPaneConfigurationService(
            [FromKeyedServices("NetworkNavigationPane")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY_KEY_NAME, SYSTEM_PINNED_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(KEY_KEY_NAME, SYSTEM_PINNED_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _configurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(KEY_KEY_NAME, SYSTEM_PINNED_VALUE_NAME);
                // Absence falls back to Explorer's built-in pinned Network item.
                return RegistryHelper.KeyExists(MACHINE_NETWORK_CLSID_KEY_NAME)
                    && (value is null || value is int pinned && pinned != 0);
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect Network navigation-pane state: {exception.Message}");
                return false;
            }
        }
    }
}
