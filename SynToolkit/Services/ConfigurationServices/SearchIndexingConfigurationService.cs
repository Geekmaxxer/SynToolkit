using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class SearchIndexingConfigurationService : IConfigurationService
    {
        private const string WSEARCH_SERVICE_NAME = "WSearch";


        private readonly ConfigurationStore _searchIndexingConfigurationStore;

        public SearchIndexingConfigurationService(
            [FromKeyedServices("SearchIndexing")] ConfigurationStore searchIndexingConfigurationStore)
        {
            _searchIndexingConfigurationStore = searchIndexingConfigurationStore;
        }

        public void Disable()
        {
            ServiceHelper.StopService(WSEARCH_SERVICE_NAME);
            ServiceHelper.SetStartupType(WSEARCH_SERVICE_NAME, ServiceStartMode.Disabled);

            _searchIndexingConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            ServiceHelper.SetStartupType(WSEARCH_SERVICE_NAME, ServiceStartMode.Automatic);
            ServiceHelper.SetDelayedAutoStart(WSEARCH_SERVICE_NAME, true);
            ServiceHelper.StartService(WSEARCH_SERVICE_NAME);

            _searchIndexingConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return ServiceHelper.TryGetStartupType(WSEARCH_SERVICE_NAME, out ServiceStartMode startupType)
                && startupType != ServiceStartMode.Disabled
                && ServiceHelper.TryGetStatus(WSEARCH_SERVICE_NAME, out ServiceControllerStatus status)
                && status == ServiceControllerStatus.Running;
        }
    }
}
