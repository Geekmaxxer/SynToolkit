using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationServices
{
    internal class LocationConfigurationService : IConfigurationService
    {
        private const string SYNTOOLKIT_STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\Location";
        private const string STATE_VALUE_NAME = "state";

        private const string LOCATION_SERVICE_NAME = "lfsvc";
        private const string LOCATION_AND_SENSORS_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors";
        private const string DISABLE_LOCATION_VALUE_NAME = "DisableLocation";

        private readonly ConfigurationStore _locationConfigurationStore;

        public LocationConfigurationService(
            [FromKeyedServices("Location")] ConfigurationStore locationConfigurationStore)
        {
            _locationConfigurationStore = locationConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(LOCATION_AND_SENSORS_KEY_NAME, DISABLE_LOCATION_VALUE_NAME, 1, Microsoft.Win32.RegistryValueKind.DWord);
            if (ServiceHelper.ServiceExists(LOCATION_SERVICE_NAME))
            {
                ServiceHelper.SetStartupType(LOCATION_SERVICE_NAME, ServiceStartMode.Disabled);
                ServiceHelper.StopService(LOCATION_SERVICE_NAME);
            }
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 0);

            _locationConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            if (!ServiceHelper.ServiceExists(LOCATION_SERVICE_NAME))
            {
                throw new InvalidOperationException(
                    "Windows Location Service (lfsvc) is not installed on this system.");
            }

            ServiceHelper.SetStartupType(LOCATION_SERVICE_NAME, ServiceStartMode.Manual);
            RegistryHelper.DeleteValue(LOCATION_AND_SENSORS_KEY_NAME, DISABLE_LOCATION_VALUE_NAME);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY_NAME, STATE_VALUE_NAME, 1);

            _locationConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                object policy = RegistryHelper.GetValue(
                    LOCATION_AND_SENSORS_KEY_NAME, DISABLE_LOCATION_VALUE_NAME);
                return !(policy is int disabled && disabled != 0)
                    && ServiceHelper.TryGetStartupType(LOCATION_SERVICE_NAME, out ServiceStartMode startupType)
                    && startupType != ServiceStartMode.Disabled;
            }
            catch (Exception exception)
            {
                App.logger.Warn($"Unable to detect location-service state: {exception.Message}");
                return false;
            }
        }
    }
}
