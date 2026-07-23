using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class LanmanWorkstationConfigurationService : IConfigurationService
    {
        private const string LANMAN_WORKSTATION_SERVICE_NAME = "LanmanWorkstation";

        private readonly ConfigurationStore _lanmanWorkstationConfigurationStore;

        public LanmanWorkstationConfigurationService(
            [FromKeyedServices("LanmanWorkstation")] ConfigurationStore lanmanWorkstationConfigurationStore)
        {
            _lanmanWorkstationConfigurationStore = lanmanWorkstationConfigurationStore;
        }

        public void Disable()
        {
            ServiceHelper.StopService(LANMAN_WORKSTATION_SERVICE_NAME);
            ServiceHelper.SetStartupType(LANMAN_WORKSTATION_SERVICE_NAME, ServiceStartMode.Disabled);
            UpdateDetectedState(expectedState: false);
        }

        public void Enable()
        {
            // Automatic is the Windows 11 default for the SMB Workstation service.
            ServiceHelper.SetStartupType(LANMAN_WORKSTATION_SERVICE_NAME, ServiceStartMode.Automatic);
            ServiceHelper.StartService(LANMAN_WORKSTATION_SERVICE_NAME);
            UpdateDetectedState(expectedState: true);
        }

        public bool IsEnabled()
        {
            return ServiceHelper.GetStartupType(LANMAN_WORKSTATION_SERVICE_NAME) != ServiceStartMode.Disabled
                || !ServiceHelper.TryGetStatus(
                    LANMAN_WORKSTATION_SERVICE_NAME,
                    out ServiceControllerStatus status)
                || status != ServiceControllerStatus.Stopped;
        }

        private void UpdateDetectedState(bool expectedState)
        {
            bool detectedState = IsEnabled();
            _lanmanWorkstationConfigurationStore.CurrentSetting = detectedState;

            if (detectedState != expectedState)
            {
                throw new InvalidOperationException("Windows did not accept the requested SMB Workstation state.");
            }
        }
    }
}
