using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Microsoft.Win32;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class TroubleshootingConfigurationService : IConfigurationService
    {
        private const string DPS_SERVICE_NAME = "DPS";
        private const string WDI_SERVICE_HOST_SERVICE_NAME = "WdiServiceHost";
        private const string WDI_SYSTEM_HOST_SERVICE_NAME = "WdiSystemHost";

        private const string DIAG_LOG_KEY_NAME = @"HKLM\SYSTEM\CurrentControlSet\Control\WMI\Autologger\DiagLog";

        private const string START_VALUE_NAME = "Start";

        private readonly ConfigurationStore _troubleshootingConfigurationStore;

        public TroubleshootingConfigurationService(
            [FromKeyedServices("Troubleshooting")] ConfigurationStore troubleshootingConfigurationStore)
        {
            _troubleshootingConfigurationStore = troubleshootingConfigurationStore;
        }

        public void Disable()
        {
            ServiceHelper.SetStartupType(DPS_SERVICE_NAME, ServiceStartMode.Disabled);
            ServiceHelper.SetStartupType(WDI_SERVICE_HOST_SERVICE_NAME, ServiceStartMode.Disabled);
            ServiceHelper.SetStartupType(WDI_SYSTEM_HOST_SERVICE_NAME, ServiceStartMode.Disabled);

            RegistryHelper.SetValue(DIAG_LOG_KEY_NAME, START_VALUE_NAME, 0, RegistryValueKind.DWord);

            if (!IsDisabled())
            {
                throw new System.InvalidOperationException("Windows diagnostics were only partially disabled.");
            }

            _troubleshootingConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            ServiceHelper.SetStartupType(DPS_SERVICE_NAME, ServiceStartMode.Automatic);
            ServiceHelper.SetStartupType(WDI_SERVICE_HOST_SERVICE_NAME, ServiceStartMode.Manual);
            ServiceHelper.SetStartupType(WDI_SYSTEM_HOST_SERVICE_NAME, ServiceStartMode.Manual);

            RegistryHelper.SetValue(DIAG_LOG_KEY_NAME, START_VALUE_NAME, 1, RegistryValueKind.DWord);
            ServiceHelper.StartService(DPS_SERVICE_NAME);

            if (!IsEnabled())
            {
                throw new System.InvalidOperationException("Windows diagnostics were only partially enabled.");
            }

            _troubleshootingConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            bool[] checks =
            {
                ServiceHelper.IsStartupTypeMatch(DPS_SERVICE_NAME, ServiceStartMode.Automatic),
                ServiceHelper.IsStartupTypeMatch(WDI_SERVICE_HOST_SERVICE_NAME, ServiceStartMode.Manual),
                ServiceHelper.IsStartupTypeMatch(WDI_SYSTEM_HOST_SERVICE_NAME, ServiceStartMode.Manual),
                RegistryHelper.IsMatch(DIAG_LOG_KEY_NAME, START_VALUE_NAME, 1)
            };

            return checks.All(x => x);
        }

        private static bool IsDisabled()
        {
            bool[] checks =
            {
                ServiceHelper.IsStartupTypeMatch(DPS_SERVICE_NAME, ServiceStartMode.Disabled),
                ServiceHelper.IsStartupTypeMatch(WDI_SERVICE_HOST_SERVICE_NAME, ServiceStartMode.Disabled),
                ServiceHelper.IsStartupTypeMatch(WDI_SYSTEM_HOST_SERVICE_NAME, ServiceStartMode.Disabled),
                RegistryHelper.IsMatch(DIAG_LOG_KEY_NAME, START_VALUE_NAME, 0)
            };

            return checks.All(x => x);
        }
    }
}
