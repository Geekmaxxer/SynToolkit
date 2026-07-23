using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationServices
{
    public class DeliveryOptimisationConfigurationService : IConfigurationService

    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\DeliveryOptimisation";
        private const string STATE_VALUE_NAME = "state";

        private const string DELIVERY_OPTIMIZATION_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
        private const string DO_DOWNLOAD_MODE_VALUE_NAME = "DODownloadMode";

        private readonly ConfigurationStore _deliveryOptimisationConfigurationStore;
        public DeliveryOptimisationConfigurationService(
            [FromKeyedServices("DeliveryOptimisation")] ConfigurationStore deliveryOptimisationConfigurationStore)
        {
            _deliveryOptimisationConfigurationStore = deliveryOptimisationConfigurationStore;
        }
        public void Disable()
        {
            RegistryHelper.SetValue(DELIVERY_OPTIMIZATION_KEY_NAME, DO_DOWNLOAD_MODE_VALUE_NAME, 0, Microsoft.Win32.RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _deliveryOptimisationConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(DELIVERY_OPTIMIZATION_KEY_NAME, DO_DOWNLOAD_MODE_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _deliveryOptimisationConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object value = RegistryHelper.GetValue(
                    DELIVERY_OPTIMIZATION_KEY_NAME, DO_DOWNLOAD_MODE_VALUE_NAME);

                // The supported Windows default is LAN mode (1). Modes 1-3 use
                // peer delivery; 0 is HTTP-only, while 99/100 are non-peer modes.
                return value is null || value is int mode && mode is >= 1 and <= 3;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect Delivery Optimization mode: {exception.Message}");
                return false;
            }
        }
    }
}
