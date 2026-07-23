using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class ToggleWindowsUpdateConfigurationService : IConfigurationService
    {
        private const string WINDOWS_UPDATE_SERVICE_NAME = "wuauserv";

        private readonly ConfigurationStore _configurationStore;

        public ToggleWindowsUpdateConfigurationService(
            [FromKeyedServices("ToggleWindowsUpdates")] ConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        public void Disable()
        {
            ServiceHelper.SetStartupType(WINDOWS_UPDATE_SERVICE_NAME, ServiceStartMode.Disabled);
            StopServiceIfRunning();
            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            // Windows 11 configures wuauserv as Manual and starts it on demand.
            ServiceHelper.SetStartupType(WINDOWS_UPDATE_SERVICE_NAME, ServiceStartMode.Manual);
            UpdateDetectedState(expectedState: true);
        }

        public bool IsEnabled()
        {
            return ServiceHelper.GetStartupType(WINDOWS_UPDATE_SERVICE_NAME) != ServiceStartMode.Disabled;
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _configurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested Windows Update service state.");
            }
        }

        private static void StopServiceIfRunning()
        {
            using ServiceController controller = new(WINDOWS_UPDATE_SERVICE_NAME);
            controller.Refresh();

            if (controller.Status is ServiceControllerStatus.Stopped)
            {
                return;
            }

            if (controller.Status is not ServiceControllerStatus.StopPending)
            {
                controller.Stop();
            }

            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
    }
}
